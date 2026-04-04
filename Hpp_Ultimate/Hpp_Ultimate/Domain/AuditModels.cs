namespace Hpp_Ultimate.Domain;

public sealed record AuditLogEntry(
    Guid Id,
    string Area,
    string Action,
    string ActorName,
    string? ActorEmail,
    Guid? ActorUserId,
    string Subject,
    Guid? SubjectId,
    string Detail,
    DateTime OccurredAt);
