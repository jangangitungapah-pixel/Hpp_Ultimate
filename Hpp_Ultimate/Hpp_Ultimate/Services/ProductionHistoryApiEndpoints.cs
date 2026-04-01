using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public static class ProductionHistoryApiEndpoints
{
    public static IEndpointRouteBuilder MapProductionHistoryApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/production-history", async (
            string? search,
            Guid? productId,
            DashboardPeriodPreset? preset,
            DateOnly? from,
            DateOnly? to,
            string? sortBy,
            bool? desc,
            ProductionHistoryService service,
            CancellationToken cancellationToken) =>
        {
            var query = new ProductionHistoryQuery(search, productId, preset ?? DashboardPeriodPreset.ThisMonth, from, to, sortBy ?? "date", desc ?? true);
            return Results.Ok(await service.QueryAsync(query, cancellationToken));
        });

        endpoints.MapGet("/api/production-history/{batchId:guid}", async (
            Guid batchId,
            Guid? compareBatchId,
            ProductionHistoryService service,
            CancellationToken cancellationToken) =>
        {
            var detail = await service.GetDetailAsync(batchId, compareBatchId, cancellationToken);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });

        return endpoints;
    }
}
