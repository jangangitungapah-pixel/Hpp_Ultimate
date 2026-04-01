using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public static class BomApiEndpoints
{
    public static IEndpointRouteBuilder MapBomApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/bom", async (
            string? search,
            BomCoverageFilter? coverage,
            string? sortBy,
            bool? desc,
            BomCatalogService service,
            CancellationToken cancellationToken) =>
        {
            var query = new BomQuery(search, coverage ?? BomCoverageFilter.All, sortBy ?? "updated", desc ?? true);
            return Results.Ok(await service.QueryAsync(query, cancellationToken));
        });

        endpoints.MapGet("/api/bom/{productId:guid}", async (Guid productId, BomCatalogService service, CancellationToken cancellationToken) =>
        {
            var detail = await service.GetDetailAsync(productId, cancellationToken);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });

        endpoints.MapPut("/api/bom/{productId:guid}/recipe", async (Guid productId, RecipeMetaRequest request, BomCatalogService service, CancellationToken cancellationToken) =>
        {
            var result = await service.SaveRecipeMetaAsync(productId, request, cancellationToken);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        endpoints.MapPost("/api/bom/{productId:guid}/items", async (Guid productId, BomItemRequest request, BomCatalogService service, CancellationToken cancellationToken) =>
        {
            var result = await service.UpsertItemAsync(productId, request, cancellationToken);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        endpoints.MapDelete("/api/bom/{productId:guid}/items/{materialId:guid}", async (Guid productId, Guid materialId, BomCatalogService service, CancellationToken cancellationToken) =>
        {
            var result = await service.RemoveItemAsync(productId, materialId, cancellationToken);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        return endpoints;
    }
}
