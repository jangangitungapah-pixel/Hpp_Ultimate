using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public static class SettingsApiEndpoints
{
    public static IEndpointRouteBuilder MapSettingsApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/settings")
            .RequireAuthenticatedSession();

        group.MapGet("", async (SettingsService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetSnapshotAsync(cancellationToken)));

        group.MapPut("", async (BusinessSettingsRequest request, SettingsService service, CancellationToken cancellationToken) =>
        {
            var result = await service.SaveAsync(request, cancellationToken);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        })
        .RequireAdminSession();

        group.MapPost("/clear-data", async (SettingsService service, CancellationToken cancellationToken) =>
        {
            var result = await service.ClearOperationalDataAsync(cancellationToken);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        })
        .RequireAdminSession();

        return endpoints;
    }
}
