using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public static class HppCalculatorApiEndpoints
{
    public static IEndpointRouteBuilder MapHppCalculatorApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/hpp-calculator", async (
            Guid? productId,
            Guid? batchId,
            DashboardPeriodPreset? preset,
            DateOnly? from,
            DateOnly? to,
            HppCalculatorService service,
            CancellationToken cancellationToken) =>
        {
            var query = new HppCalculatorQuery(productId, batchId, preset ?? DashboardPeriodPreset.ThisMonth, from, to);
            return Results.Ok(await service.GetSnapshotAsync(query, cancellationToken));
        });

        return endpoints;
    }
}
