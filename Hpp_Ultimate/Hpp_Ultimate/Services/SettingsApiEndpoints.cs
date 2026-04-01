using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public static class SettingsApiEndpoints
{
    public static IEndpointRouteBuilder MapSettingsApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/settings", async (SettingsService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetSnapshotAsync(cancellationToken)));

        endpoints.MapPut("/api/settings", async (BusinessSettingsRequest request, SettingsService service, CancellationToken cancellationToken) =>
        {
            var result = await service.SaveAsync(request, cancellationToken);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        return endpoints;
    }
}
