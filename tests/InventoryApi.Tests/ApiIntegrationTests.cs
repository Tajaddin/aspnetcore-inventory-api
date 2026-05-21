using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using InventoryApi.Domain;
using InventoryApi.Dtos;

namespace InventoryApi.Tests;

/// <summary>
/// End-to-end HTTP tests through the real minimal-API pipeline backed by
/// in-memory SQLite. Exercises validation, problem-details, and the reserve
/// flow over the wire.
/// </summary>
public class ApiIntegrationTests : IClassFixture<CustomWebAppFactory>
{
    private readonly HttpClient _client;

    public ApiIntegrationTests(CustomWebAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    private static CreateProductRequest NewProduct(string sku) =>
        new("Gadget", sku, 19.99m, 25, Category.Electronics);

    [Fact]
    public async Task Healthz_returns_ok()
    {
        var resp = await _client.GetAsync("/healthz");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Create_get_and_list_roundtrip()
    {
        var create = await _client.PostAsJsonAsync("/api/products", NewProduct("INT-1"));
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<ProductResponse>();
        created!.Sku.Should().Be("INT-1");

        var get = await _client.GetAsync($"/api/products/{created.Id}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await _client.GetFromJsonAsync<PagedResponse<ProductResponse>>("/api/products?category=Electronics");
        list!.Items.Should().Contain(p => p.Sku == "INT-1");
    }

    [Fact]
    public async Task Get_missing_returns_404_problem()
    {
        var resp = await _client.GetAsync("/api/products/999999");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Create_with_blank_name_returns_validation_problem()
    {
        var bad = new CreateProductRequest("", "BAD-1", 1m, 1, Category.Other);
        var resp = await _client.PostAsJsonAsync("/api/products", bad);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Duplicate_sku_returns_409()
    {
        await _client.PostAsJsonAsync("/api/products", NewProduct("DUP-API"));
        var second = await _client.PostAsJsonAsync("/api/products", NewProduct("DUP-API"));
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Reserve_flow_returns_409_when_over_stock()
    {
        var create = await _client.PostAsJsonAsync("/api/products", new CreateProductRequest(
            "Limited", "RES-1", 5m, 3, Category.Other));
        var created = await create.Content.ReadFromJsonAsync<ProductResponse>();

        var ok = await _client.PostAsJsonAsync($"/api/products/{created!.Id}/reserve", new ReserveRequest(2));
        ok.StatusCode.Should().Be(HttpStatusCode.OK);

        var over = await _client.PostAsJsonAsync($"/api/products/{created.Id}/reserve", new ReserveRequest(5));
        over.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Delete_returns_204_then_404()
    {
        var create = await _client.PostAsJsonAsync("/api/products", NewProduct("DEL-1"));
        var created = await create.Content.ReadFromJsonAsync<ProductResponse>();

        var del = await _client.DeleteAsync($"/api/products/{created!.Id}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var again = await _client.DeleteAsync($"/api/products/{created.Id}");
        again.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
