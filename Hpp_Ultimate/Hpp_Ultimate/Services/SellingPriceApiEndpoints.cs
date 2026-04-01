using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public static class SellingPriceApiEndpoints
{
    public static IEndpointRouteBuilder MapSellingPriceApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/selling-price", async (
            Guid? productId,
            Guid? batchId,
            DashboardPeriodPreset? preset,
            DateOnly? from,
            DateOnly? to,
            SellingPriceService service,
            CancellationToken cancellationToken) =>
        {
            var query = new SellingPriceQuery(productId, batchId, preset ?? DashboardPeriodPreset.ThisMonth, from, to);
            return Results.Ok(await service.GetSnapshotAsync(query, cancellationToken));
        });

        return endpoints;
    }
}
