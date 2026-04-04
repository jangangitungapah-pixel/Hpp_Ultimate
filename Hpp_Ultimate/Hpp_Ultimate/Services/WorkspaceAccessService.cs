using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public sealed class WorkspaceAccessService(SeededBusinessDataStore store)
{
    public AccessDecision RequireAuthenticated(string missingSessionMessage = "Sesi login tidak ditemukan. Silakan masuk ulang.")
    {
        var actor = GetActiveUser(clearInvalidSession: true);
        return actor is null
            ? new AccessDecision(false, true, missingSessionMessage, null)
            : new AccessDecision(true, false, string.Empty, actor);
    }

    public AccessDecision RequireAdmin(
        string missingSessionMessage = "Sesi admin tidak ditemukan. Silakan login ulang.",
        string forbiddenMessage = "Aksi ini hanya tersedia untuk admin.")
    {
        var actor = GetActiveUser(clearInvalidSession: true);
        if (actor is null)
        {
            return new AccessDecision(false, true, missingSessionMessage, null);
        }

        return actor.Role == UserRole.Admin
            ? new AccessDecision(true, false, string.Empty, actor)
            : new AccessDecision(false, false, forbiddenMessage, actor);
    }

    public BusinessUser? GetActiveUser(bool clearInvalidSession = false)
    {
        var session = store.AuthSession;
        if (session is null)
        {
            return null;
        }

        var user = store.FindUser(session.UserId);
        if (user is not { Status: UserStatus.Active })
        {
            if (clearInvalidSession)
            {
                store.SetSession(null);
            }

            return null;
        }

        return user;
    }
}

public sealed record AccessDecision(
    bool Allowed,
    bool RequiresAuthentication,
    string Message,
    BusinessUser? Actor)
{
    public IResult ToHttpResult()
        => RequiresAuthentication
            ? Results.Json(new { message = Message }, statusCode: StatusCodes.Status401Unauthorized)
            : Results.Json(new { message = Message }, statusCode: StatusCodes.Status403Forbidden);
}
