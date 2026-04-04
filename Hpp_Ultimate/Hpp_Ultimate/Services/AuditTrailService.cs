using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public sealed class AuditTrailService(SeededBusinessDataStore store)
{
    public void Record(
        string area,
        string action,
        string actorName,
        string? actorEmail,
        Guid? actorUserId,
        string subject,
        Guid? subjectId,
        string detail)
    {
        store.AddAuditLog(new AuditLogEntry(
            Guid.NewGuid(),
            area,
            action,
            actorName,
            actorEmail,
            actorUserId,
            subject,
            subjectId,
            detail,
            DateTime.Now));
    }

    public void Record(BusinessUser? actor, string area, string action, string subject, Guid? subjectId, string detail)
        => Record(
            area,
            action,
            actor?.FullName ?? "System",
            actor?.Email,
            actor?.Id,
            subject,
            subjectId,
            detail);

    public IReadOnlyList<AuditLogEntry> GetRecent(int take)
        => store.AuditLogEntries.Take(Math.Max(0, take)).ToArray();
}
