using FluentAssertions;
using InventoryApi.Data;
using InventoryApi.Domain;
using InventoryApi.Dtos;
using InventoryApi.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace InventoryApi.Tests;

/// <summary>
/// Unit tests for ProductService against a fresh in-memory SQLite database
/// per test. Each test owns its connection so they are fully isolated.
/// </summary>
public class ProductServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly ProductService _service;

    public ProductServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _service = new ProductService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private CreateProductRequest SampleCreate(string sku = "SKU-1", int stock = 10) =>
        new("Widget", sku, 9.99m, stock, Category.Electronics);

    [Fact]
    public async Task Create_then_Get_returns_product()
    {
        var created = await _service.CreateAsync(SampleCreate());
        var fetched = await _service.GetAsync(created.Id);

        fetched.Should().NotBeNull();
        fetched!.Name.Should().Be("Widget");
        fetched.Sku.Should().Be("SKU-1");
        fetched.StockQuantity.Should().Be(10);
    }

    [Fact]
    public async Task Get_missing_returns_null()
    {
        (await _service.GetAsync(999)).Should().BeNull();
    }

    [Fact]
    public async Task List_filters_by_category_and_paginates()
    {
        await _service.CreateAsync(new("A", "S1", 1m, 1, Category.Electronics));
        await _service.CreateAsync(new("B", "S2", 1m, 1, Category.Electronics));
        await _service.CreateAsync(new("C", "S3", 1m, 1, Category.Food));

        var electronics = await _service.ListAsync(Category.Electronics, 0, 20);
        electronics.Total.Should().Be(2);
        electronics.Items.Should().HaveCount(2);

        var all = await _service.ListAsync(null, 0, 20);
        all.Total.Should().Be(3);
    }

    [Fact]
    public async Task Update_applies_only_provided_fields()
    {
        var created = await _service.CreateAsync(SampleCreate());
        var updated = await _service.UpdateAsync(created.Id,
            new UpdateProductRequest(Name: "Renamed", Price: null, StockQuantity: null, Category: null));

        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Renamed");
        updated.Price.Should().Be(9.99m); // unchanged
        updated.StockQuantity.Should().Be(10); // unchanged
    }

    [Fact]
    public async Task Delete_removes_product()
    {
        var created = await _service.CreateAsync(SampleCreate());
        (await _service.DeleteAsync(created.Id)).Should().BeTrue();
        (await _service.DeleteAsync(created.Id)).Should().BeFalse();
        (await _service.GetAsync(created.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Reserve_decrements_stock_when_available()
    {
        var created = await _service.CreateAsync(SampleCreate(stock: 10));
        var (outcome, product) = await _service.ReserveAsync(created.Id, 3);

        outcome.Should().Be(ReserveOutcome.Reserved);
        product!.StockQuantity.Should().Be(7);
    }

    [Fact]
    public async Task Reserve_rejects_when_insufficient_stock()
    {
        var created = await _service.CreateAsync(SampleCreate(stock: 2));
        var (outcome, product) = await _service.ReserveAsync(created.Id, 5);

        outcome.Should().Be(ReserveOutcome.InsufficientStock);
        product.Should().BeNull();
        // Stock unchanged.
        (await _service.GetAsync(created.Id))!.StockQuantity.Should().Be(2);
    }

    [Fact]
    public async Task Reserve_on_missing_product_returns_not_found()
    {
        var (outcome, _) = await _service.ReserveAsync(404, 1);
        outcome.Should().Be(ReserveOutcome.NotFound);
    }

    [Fact]
    public async Task SkuExists_detects_duplicates()
    {
        await _service.CreateAsync(SampleCreate(sku: "DUP"));
        (await _service.SkuExistsAsync("DUP")).Should().BeTrue();
        (await _service.SkuExistsAsync("OTHER")).Should().BeFalse();
    }
}
