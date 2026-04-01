using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public static class ReportApiEndpoints
{
    public static IEndpointRouteBuilder MapReportApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/reports", async (
            Guid? productId,
            DashboardPeriodPreset? preset,
            DateOnly? from,
            DateOnly? to,
            ReportKind? kind,
            ReportService service,
            CancellationToken cancellationToken) =>
        {
            var query = new ReportsQuery(productId, preset ?? DashboardPeriodPreset.ThisMonth, from, to, kind ?? ReportKind.Hpp);
            return Results.Ok(await service.GetSnapshotAsync(query, cancellationToken));
        });

        return endpoints;
    }
}
