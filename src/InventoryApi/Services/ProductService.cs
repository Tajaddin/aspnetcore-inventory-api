using InventoryApi.Data;
using InventoryApi.Domain;
using InventoryApi.Dtos;
using Microsoft.EntityFrameworkCore;

namespace InventoryApi.Services;

/// <summary>
/// Outcome of a stock reservation. Distinguishes "not found" from
/// "insufficient stock" so the endpoint can map each to the right status code.
/// </summary>
public enum ReserveOutcome
{
    Reserved,
    NotFound,
    InsufficientStock
}

public class ProductService(AppDbContext db)
{
    private const int MaxPageSize = 100;

    public async Task<PagedResponse<ProductResponse>> ListAsync(
        Category? category, int page, int size, CancellationToken ct = default)
    {
        page = Math.Max(page, 0);
        size = Math.Clamp(size, 1, MaxPageSize);

        IQueryable<Product> query = db.Products.AsNoTracking();
        if (category is not null)
        {
            query = query.Where(p => p.Category == category);
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(p => p.CreatedAt)
            .ThenBy(p => p.Id)
            .Skip(page * size)
            .Take(size)
            .Select(p => ProductResponse.From(p))
            .ToListAsync(ct);

        return new PagedResponse<ProductResponse>(items, page, size, total);
    }

    public async Task<ProductResponse?> GetAsync(int id, CancellationToken ct = default)
    {
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        return product is null ? null : ProductResponse.From(product);
    }

    public async Task<bool> SkuExistsAsync(string sku, CancellationToken ct = default)
        => await db.Products.AnyAsync(p => p.Sku == sku, ct);

    public async Task<ProductResponse> CreateAsync(CreateProductRequest req, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var product = new Product
        {
            Name = req.Name,
            Sku = req.Sku,
            Price = req.Price,
            StockQuantity = req.StockQuantity,
            Category = req.Category,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        return ProductResponse.From(product);
    }

    public async Task<ProductResponse?> UpdateAsync(int id, UpdateProductRequest req, CancellationToken ct = default)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null)
        {
            return null;
        }
        if (req.Name is not null) product.Name = req.Name;
        if (req.Price is not null) product.Price = req.Price.Value;
        if (req.StockQuantity is not null) product.StockQuantity = req.StockQuantity.Value;
        if (req.Category is not null) product.Category = req.Category.Value;
        product.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return ProductResponse.From(product);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var rows = await db.Products.Where(p => p.Id == id).ExecuteDeleteAsync(ct);
        return rows > 0;
    }

    /// <summary>
    /// Atomically decrement stock by quantity with one conditional UPDATE, so
    /// concurrent reservations can never take more than the stock on hand.
    /// Returns the outcome plus the new product state when reserved.
    /// </summary>
    public async Task<(ReserveOutcome Outcome, ProductResponse? Product)> ReserveAsync(
        int id, int quantity, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow;
        var reserved = await db.Products
            .Where(p => p.Id == id && p.StockQuantity >= quantity)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.StockQuantity, p => p.StockQuantity - quantity)
                .SetProperty(p => p.UpdatedAt, now), ct);
        if (reserved == 0)
        {
            var exists = await db.Products.AnyAsync(p => p.Id == id, ct);
            return (exists ? ReserveOutcome.InsufficientStock : ReserveOutcome.NotFound, null);
        }

        // The UPDATE holds the row lock until commit, so this reads exactly
        // the state it left, not a later reservation's.
        var product = await db.Products.AsNoTracking().SingleAsync(p => p.Id == id, ct);
        await tx.CommitAsync(ct);
        return (ReserveOutcome.Reserved, ProductResponse.From(product));
    }
}
