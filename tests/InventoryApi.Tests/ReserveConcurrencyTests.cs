using FluentAssertions;
using InventoryApi.Data;
using InventoryApi.Domain;
using InventoryApi.Services;
using Microsoft.EntityFrameworkCore;

namespace InventoryApi.Tests;

/// <summary>
/// Races many reservations against one product. Each caller gets its own
/// DbContext and connection to a shared SQLite file, the way concurrent
/// requests do in the running API.
/// </summary>
public class ReserveConcurrencyTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"inventory-race-{Guid.NewGuid():N}.db");

    private AppDbContext NewContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite($"Data Source={_dbPath};Pooling=False")
        .Options);

    public void Dispose()
    {
        File.Delete(_dbPath);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Concurrent_reservations_never_oversell()
    {
        const int stock = 10;
        const int callers = 20;

        int productId;
        await using (var db = NewContext())
        {
            await db.Database.EnsureCreatedAsync();
            var created = await new ProductService(db).CreateAsync(
                new("Last units", "RACE-1", 1m, stock, Category.Other));
            productId = created.Id;
        }

        var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = Enumerable.Range(0, callers).Select(_ => Task.Run(async () =>
        {
            await go.Task;
            await using var db = NewContext();
            var (outcome, _) = await new ProductService(db).ReserveAsync(productId, 1);
            return outcome;
        })).ToList();
        go.SetResult();
        var outcomes = await Task.WhenAll(attempts);

        outcomes.Count(o => o == ReserveOutcome.Reserved).Should().Be(stock);
        outcomes.Count(o => o == ReserveOutcome.InsufficientStock).Should().Be(callers - stock);
        await using var check = NewContext();
        (await check.Products.SingleAsync(p => p.Id == productId)).StockQuantity.Should().Be(0);
    }
}
