using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using InventoryApi.Data;
using InventoryApi.Domain;
using InventoryApi.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace InventoryApi.Tests;

/// <summary>
/// The app host on a temp-file SQLite database with a connection per request,
/// so concurrent requests really run concurrently, as they do in production.
/// </summary>
public class FileSqliteWebAppFactory(IInterceptor interceptor) : CustomWebAppFactory
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"inventory-api-{Guid.NewGuid():N}.db");

    protected override void AddDatabase(IServiceCollection services) =>
        services.AddDbContext<AppDbContext>(options => options
            .UseSqlite($"Data Source={_path};Pooling=False")
            .AddInterceptors(interceptor));

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            File.Delete(_path);
        }
    }
}

/// <summary>
/// Holds each SKU-exists query, once it has read, until every caller has run
/// one. Every create then passes the endpoint's pre-check, so all but one
/// must lose at the unique index instead of at the pre-check.
/// </summary>
public class SkuCheckBarrier(int callers) : DbCommandInterceptor
{
    private readonly TaskCompletionSource _allChecked = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _checks;

    public int Checks => _checks;

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        if (command.CommandText.StartsWith("SELECT EXISTS", StringComparison.Ordinal)
            && command.CommandText.Contains("\"Sku\"", StringComparison.Ordinal))
        {
            if (Interlocked.Increment(ref _checks) == callers)
            {
                _allChecked.SetResult();
            }
            await _allChecked.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }
        return result;
    }
}

public class ConcurrentCreateTests
{
    [Fact]
    public async Task Concurrent_creates_of_one_sku_return_one_201_and_409s()
    {
        const int callers = 20;
        var barrier = new SkuCheckBarrier(callers);
        await using var factory = new FileSqliteWebAppFactory(barrier);
        var client = factory.CreateClient();

        var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var posts = Enumerable.Range(0, callers).Select(_ => Task.Run(async () =>
        {
            await go.Task;
            var resp = await client.PostAsJsonAsync("/api/products",
                new CreateProductRequest("Gadget", "RACE-SKU", 1m, 1, Category.Other));
            return resp.StatusCode;
        })).ToList();
        go.SetResult();
        var statuses = await Task.WhenAll(posts);

        barrier.Checks.Should().Be(callers);
        statuses.Should().ContainSingle(s => s == HttpStatusCode.Created);
        statuses.Count(s => s == HttpStatusCode.Conflict).Should().Be(callers - 1);
    }
}
