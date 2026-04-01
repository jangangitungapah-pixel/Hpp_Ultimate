using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public static class DashboardApiEndpoints
{
    public static IEndpointRouteBuilder MapDashboardApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/dashboard", async (
            string? preset,
            DateOnly? from,
            DateOnly? to,
            Guid? productId,
            DashboardService dashboardService,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse<DashboardPeriodPreset>(preset, true, out var parsedPreset))
            {
                parsedPreset = DashboardPeriodPreset.ThisMonth;
            }

            var filter = new DashboardFilter(parsedPreset, from, to, productId);
            var snapshot = await dashboardService.GetSnapshotAsync(filter, cancellationToken);
            return Results.Ok(snapshot);
        });

        endpoints.MapGet("/api/reference/products", (IBusinessDataStore store) =>
            Results.Ok(store.Products.Where(product => product.IsActive)
                .OrderBy(product => product.Name)
                .Select(product => new ProductFilterOption(product.Id, product.Name))));

        return endpoints;
    }
}
