using FluentAssertions;
using InventoryApi.Data;
using InventoryApi.Domain;
using InventoryApi.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace InventoryApi.Tests;

/// <summary>
/// Races many reservations against one product. Each caller gets its own
/// DbContext and connection, the way concurrent requests do in the running API.
/// Runs on a shared SQLite file by default. Set TEST_POSTGRES_CONNECTION to a
/// PostgreSQL connection string to run it on a throwaway database built by the
/// real EF migrations instead.
/// </summary>
public class ReserveConcurrencyTests : IDisposable
{
    private static readonly string? Postgres = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION");

    private readonly string _database = $"inventory_race_{Guid.NewGuid():N}";

    private AppDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>();
        if (Postgres is null)
        {
            var path = Path.Combine(Path.GetTempPath(), $"{_database}.db");
            options.UseSqlite($"Data Source={path};Pooling=False");
        }
        else
        {
            options.UseNpgsql(new NpgsqlConnectionStringBuilder(Postgres) { Database = _database }.ConnectionString);
        }
        return new AppDbContext(options.Options);
    }

    public void Dispose()
    {
        using var db = NewContext();
        db.Database.EnsureDeleted();
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
            if (Postgres is null)
            {
                await db.Database.EnsureCreatedAsync();
            }
            else
            {
                await db.Database.MigrateAsync();
            }
            var created = await new ProductService(db).CreateAsync(
                new("Last units", "RACE-1", 1m, stock, Category.Other));
            productId = created.Id;
        }

        var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = Enumerable.Range(0, callers).Select(_ => Task.Run(async () =>
        {
            await go.Task;
            await using var db = NewContext();
            return await new ProductService(db).ReserveAsync(productId, 1);
        })).ToList();
        go.SetResult();
        var results = await Task.WhenAll(attempts);

        var reserved = results.Where(r => r.Outcome == ReserveOutcome.Reserved).ToList();
        reserved.Should().HaveCount(stock);
        results.Count(r => r.Outcome == ReserveOutcome.InsufficientStock).Should().Be(callers - stock);
        // Each success reports the stock its own update left behind, so the
        // reported values are exactly 9, 8, ..., 0 with no repeats.
        reserved.Select(r => r.Product!.StockQuantity).Should().BeEquivalentTo(Enumerable.Range(0, stock));
        await using var check = NewContext();
        (await check.Products.SingleAsync(p => p.Id == productId)).StockQuantity.Should().Be(0);
    }
}
