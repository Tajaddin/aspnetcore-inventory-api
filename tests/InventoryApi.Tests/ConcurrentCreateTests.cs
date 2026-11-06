using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using InventoryApi.Data;
using InventoryApi.Domain;
using InventoryApi.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InventoryApi.Tests;

/// <summary>
/// The app host on a temp-file SQLite database with a connection per request,
/// so concurrent requests really run concurrently, as they do in production.
/// </summary>
public class FileSqliteWebAppFactory : CustomWebAppFactory
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"inventory-api-{Guid.NewGuid():N}.db");

    protected override void AddDatabase(IServiceCollection services) =>
        services.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={_path};Pooling=False"));

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            File.Delete(_path);
        }
    }
}

public class ConcurrentCreateTests : IClassFixture<FileSqliteWebAppFactory>
{
    private readonly HttpClient _client;

    public ConcurrentCreateTests(FileSqliteWebAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Concurrent_creates_of_one_sku_return_one_201_and_409s()
    {
        const int callers = 20;

        var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var posts = Enumerable.Range(0, callers).Select(_ => Task.Run(async () =>
        {
            await go.Task;
            var resp = await _client.PostAsJsonAsync("/api/products",
                new CreateProductRequest("Gadget", "RACE-SKU", 1m, 1, Category.Other));
            return resp.StatusCode;
        })).ToList();
        go.SetResult();
        var statuses = await Task.WhenAll(posts);

        statuses.Should().ContainSingle(s => s == HttpStatusCode.Created);
        statuses.Count(s => s == HttpStatusCode.Conflict).Should().Be(callers - 1);
    }
}
