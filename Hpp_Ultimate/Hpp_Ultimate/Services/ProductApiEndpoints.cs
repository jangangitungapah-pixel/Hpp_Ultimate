using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public static class ProductApiEndpoints
{
    public static IEndpointRouteBuilder MapProductApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/products", async (
            string? search,
            string? category,
            ProductStatus? status,
            string? sortBy,
            bool? desc,
            int? page,
            int? pageSize,
            ProductCatalogService catalogService,
            CancellationToken cancellationToken) =>
        {
            var query = new ProductQuery(search, category, status, sortBy ?? "updated", desc ?? true, page ?? 1, pageSize ?? 10);
            var result = await catalogService.QueryAsync(query, cancellationToken);
            return Results.Ok(result);
        });

        endpoints.MapGet("/api/products/{id:guid}", async (Guid id, ProductCatalogService catalogService, CancellationToken cancellationToken) =>
        {
            var detail = await catalogService.GetDetailAsync(id, cancellationToken);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });

        endpoints.MapPost("/api/products", async (ProductUpsertRequest request, ProductCatalogService catalogService, CancellationToken cancellationToken) =>
        {
            var result = await catalogService.CreateAsync(request, cancellationToken);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        endpoints.MapPut("/api/products/{id:guid}", async (Guid id, ProductUpsertRequest request, ProductCatalogService catalogService, CancellationToken cancellationToken) =>
        {
            var result = await catalogService.UpdateAsync(id, request, cancellationToken);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        endpoints.MapDelete("/api/products/{id:guid}", async (Guid id, ProductCatalogService catalogService, CancellationToken cancellationToken) =>
        {
            var result = await catalogService.DeactivateAsync(id, cancellationToken);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        endpoints.MapPost("/api/products/{id:guid}/duplicate", async (Guid id, ProductCatalogService catalogService, CancellationToken cancellationToken) =>
        {
            var result = await catalogService.DuplicateAsync(id, cancellationToken);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        endpoints.MapGet("/api/products-next-code", async (ProductCatalogService catalogService, CancellationToken cancellationToken) =>
            Results.Ok(new { code = await catalogService.GenerateNextCodeAsync(cancellationToken) }));

        return endpoints;
    }
}
