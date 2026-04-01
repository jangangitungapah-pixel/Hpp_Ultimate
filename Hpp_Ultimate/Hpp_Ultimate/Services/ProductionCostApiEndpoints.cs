using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public static class ProductionCostApiEndpoints
{
    public static IEndpointRouteBuilder MapProductionCostApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/production-costs", async (
            string? search,
            Guid? productId,
            DashboardPeriodPreset? preset,
            DateOnly? from,
            DateOnly? to,
            string? sortBy,
            bool? desc,
            ProductionCostService service,
            CancellationToken cancellationToken) =>
        {
            var query = new ProductionCostQuery(search, productId, preset ?? DashboardPeriodPreset.ThisMonth, from, to, sortBy ?? "date", desc ?? true);
            return Results.Ok(await service.QueryAsync(query, cancellationToken));
        });

        endpoints.MapGet("/api/production-costs/batches/{batchId:guid}", async (
            Guid batchId,
            ProductionCostService service,
            CancellationToken cancellationToken) =>
        {
            var detail = await service.GetDetailAsync(batchId, cancellationToken);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });

        endpoints.MapPost("/api/production-costs/labor", async (
            ProductionCostEntryRequest request,
            ProductionCostService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.SaveEntryAsync(ProductionCostEntryType.Labor, request, cancellationToken);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        endpoints.MapPut("/api/production-costs/labor/{entryId:guid}", async (
            Guid entryId,
            ProductionCostEntryRequest request,
            ProductionCostService service,
            CancellationToken cancellationToken) =>
        {
            request.Id = entryId;
            var result = await service.SaveEntryAsync(ProductionCostEntryType.Labor, request, cancellationToken);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        endpoints.MapDelete("/api/production-costs/labor/{entryId:guid}", async (
            Guid entryId,
            ProductionCostService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DeleteEntryAsync(ProductionCostEntryType.Labor, entryId, cancellationToken);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        endpoints.MapPost("/api/production-costs/overhead", async (
            ProductionCostEntryRequest request,
            ProductionCostService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.SaveEntryAsync(ProductionCostEntryType.Overhead, request, cancellationToken);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        endpoints.MapPut("/api/production-costs/overhead/{entryId:guid}", async (
            Guid entryId,
            ProductionCostEntryRequest request,
            ProductionCostService service,
            CancellationToken cancellationToken) =>
        {
            request.Id = entryId;
            var result = await service.SaveEntryAsync(ProductionCostEntryType.Overhead, request, cancellationToken);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        endpoints.MapDelete("/api/production-costs/overhead/{entryId:guid}", async (
            Guid entryId,
            ProductionCostService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DeleteEntryAsync(ProductionCostEntryType.Overhead, entryId, cancellationToken);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        return endpoints;
    }
}
