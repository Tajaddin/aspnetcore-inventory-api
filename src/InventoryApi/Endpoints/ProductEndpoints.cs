using InventoryApi.Common;
using InventoryApi.Domain;
using InventoryApi.Dtos;
using InventoryApi.Services;

namespace InventoryApi.Endpoints;

public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/products").WithTags("Products");

        group.MapGet("/", async (Category? category, int? page, int? size, ProductService svc, CancellationToken ct) =>
        {
            var result = await svc.ListAsync(category, page ?? 0, size ?? 20, ct);
            return Results.Ok(result);
        });

        group.MapGet("/{id:int}", async (int id, ProductService svc, CancellationToken ct) =>
        {
            var product = await svc.GetAsync(id, ct);
            return product is null
                ? Results.Problem(statusCode: 404, title: "Product not found", detail: $"No product with id {id}")
                : Results.Ok(product);
        });

        group.MapPost("/", async (CreateProductRequest req, ProductService svc, CancellationToken ct) =>
        {
            var (ok, errors) = Validation.Validate(req);
            if (!ok)
            {
                return Results.ValidationProblem(errors);
            }
            if (await svc.SkuExistsAsync(req.Sku, ct))
            {
                return DuplicateSku(req.Sku);
            }
            try
            {
                var created = await svc.CreateAsync(req, ct);
                return Results.Created($"/api/products/{created.Id}", created);
            }
            catch (DuplicateSkuException)
            {
                return DuplicateSku(req.Sku);
            }
        });

        group.MapPut("/{id:int}", async (int id, UpdateProductRequest req, ProductService svc, CancellationToken ct) =>
        {
            var (ok, errors) = Validation.Validate(req);
            if (!ok)
            {
                return Results.ValidationProblem(errors);
            }
            var updated = await svc.UpdateAsync(id, req, ct);
            return updated is null
                ? Results.Problem(statusCode: 404, title: "Product not found", detail: $"No product with id {id}")
                : Results.Ok(updated);
        });

        group.MapDelete("/{id:int}", async (int id, ProductService svc, CancellationToken ct) =>
        {
            var deleted = await svc.DeleteAsync(id, ct);
            return deleted
                ? Results.NoContent()
                : Results.Problem(statusCode: 404, title: "Product not found", detail: $"No product with id {id}");
        });

        group.MapPost("/{id:int}/reserve", async (int id, ReserveRequest req, ProductService svc, CancellationToken ct) =>
        {
            var (ok, errors) = Validation.Validate(req);
            if (!ok)
            {
                return Results.ValidationProblem(errors);
            }
            var (outcome, product) = await svc.ReserveAsync(id, req.Quantity, ct);
            return outcome switch
            {
                ReserveOutcome.Reserved => Results.Ok(product),
                ReserveOutcome.NotFound => Results.Problem(statusCode: 404, title: "Product not found", detail: $"No product with id {id}"),
                ReserveOutcome.InsufficientStock => Results.Problem(statusCode: 409, title: "Insufficient stock", detail: $"Cannot reserve {req.Quantity} units"),
                _ => Results.Problem(statusCode: 500, title: "Unknown outcome")
            };
        });

        return app;
    }

    private static IResult DuplicateSku(string sku) =>
        Results.Problem(statusCode: 409, title: "Duplicate SKU", detail: $"SKU '{sku}' already exists");
}
