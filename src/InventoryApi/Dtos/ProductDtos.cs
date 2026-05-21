using System.ComponentModel.DataAnnotations;
using InventoryApi.Domain;

namespace InventoryApi.Dtos;

// Validation attributes use the [property: ...] target so they land on the
// generated properties (which Validator.TryValidateObject inspects), not on
// the positional constructor parameters.
public record CreateProductRequest(
    [property: Required, StringLength(200, MinimumLength = 1)] string Name,
    [property: Required, StringLength(64, MinimumLength = 1)] string Sku,
    [property: Range(0, 1_000_000)] decimal Price,
    [property: Range(0, int.MaxValue)] int StockQuantity,
    Category Category);

public record UpdateProductRequest(
    [property: StringLength(200, MinimumLength = 1)] string? Name,
    [property: Range(0, 1_000_000)] decimal? Price,
    [property: Range(0, int.MaxValue)] int? StockQuantity,
    Category? Category);

public record ReserveRequest([property: Range(1, int.MaxValue)] int Quantity);

public record ProductResponse(
    int Id,
    string Name,
    string Sku,
    decimal Price,
    int StockQuantity,
    Category Category,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static ProductResponse From(Product p) => new(
        p.Id, p.Name, p.Sku, p.Price, p.StockQuantity, p.Category, p.CreatedAt, p.UpdatedAt);
}

public record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int Size, int Total);
