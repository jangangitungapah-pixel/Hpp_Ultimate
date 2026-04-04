using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public static class AuthApiEndpoints
{
    public static IEndpointRouteBuilder MapAuthApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/auth", async (AuthService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetSnapshotAsync(cancellationToken)));

        endpoints.MapPost("/api/auth/login", async (LoginRequest request, AuthService service, CancellationToken cancellationToken) =>
        {
            var result = await service.LoginAsync(request, cancellationToken);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        endpoints.MapPost("/api/auth/logout", async (AuthService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.LogoutAsync(cancellationToken)))
            .RequireAuthenticatedSession();

        endpoints.MapPost("/api/auth/users", async (UserUpsertRequest request, AuthService service, CancellationToken cancellationToken) =>
        {
            var result = await service.SaveUserAsync(request, cancellationToken);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        })
        .RequireAdminSession();

        endpoints.MapPut("/api/auth/users/{id:guid}", async (Guid id, UserUpsertRequest request, AuthService service, CancellationToken cancellationToken) =>
        {
            request.Id = id;
            var result = await service.SaveUserAsync(request, cancellationToken);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        })
        .RequireAdminSession();

        endpoints.MapDelete("/api/auth/users/{id:guid}", async (Guid id, AuthService service, CancellationToken cancellationToken) =>
        {
            var result = await service.DeactivateUserAsync(id, cancellationToken);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        })
        .RequireAdminSession();

        return endpoints;
    }
}
