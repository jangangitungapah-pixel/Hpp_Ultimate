namespace Hpp_Ultimate.Services;

public static class EndpointAccessControl
{
    public static RouteHandlerBuilder RequireAuthenticatedSession(this RouteHandlerBuilder builder)
    {
        builder.AddEndpointFilter(async (context, next) =>
        {
            var access = context.HttpContext.RequestServices.GetRequiredService<WorkspaceAccessService>()
                .RequireAuthenticated();

            return access.Allowed
                ? await next(context)
                : access.ToHttpResult();
        });

        return builder;
    }

    public static RouteGroupBuilder RequireAuthenticatedSession(this RouteGroupBuilder group)
    {
        group.AddEndpointFilter(async (context, next) =>
        {
            var access = context.HttpContext.RequestServices.GetRequiredService<WorkspaceAccessService>()
                .RequireAuthenticated();

            return access.Allowed
                ? await next(context)
                : access.ToHttpResult();
        });

        return group;
    }

    public static RouteHandlerBuilder RequireAdminSession(this RouteHandlerBuilder builder)
    {
        builder.AddEndpointFilter(async (context, next) =>
        {
            var access = context.HttpContext.RequestServices.GetRequiredService<WorkspaceAccessService>()
                .RequireAdmin();

            return access.Allowed
                ? await next(context)
                : access.ToHttpResult();
        });

        return builder;
    }
}
