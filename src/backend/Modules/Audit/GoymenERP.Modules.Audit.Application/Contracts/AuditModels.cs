namespace GoymenERP.Modules.Audit.Application.Contracts;

public static class AuditActorTypes
{
    public const string User = "User";

    public const string System = "System";

    public const string Integration = "Integration";

    public const string Ai = "Ai";
}

public static class AuditAiInvolvement
{
    public const string None = "None";

    public const string Assisted = "Assisted";

    public const string Proposed = "Proposed";

    public const string Executed = "Executed";
}

public static class AuditOutcomes
{
    public const string Succeeded = "Succeeded";
}

public sealed record AuditEventDraft(
    Guid CompanyId,
    string EventType,
    string EntityType,
    string EntityId,
    string Operation,
    long? EntityVersionBefore,
    long? EntityVersionAfter,
    string ActorType,
    string ActorId,
    string PermissionCode,
    IReadOnlyCollection<string> RoleCodes,
    string BeforeJson,
    string AfterJson,
    string Reason,
    string? ApprovalContextJson,
    string AiInvolvement,
    string? AiContextJson,
    string SourceChannel,
    string? ClientIpAddress,
    string? UserAgent,
    string CorrelationId,
    string? CausationId,
    string IdempotencyKey,
    string Outcome = AuditOutcomes.Succeeded);

public sealed record AuditEventItem(
    Guid Id,
    Guid CompanyId,
    DateTimeOffset OccurredAtUtc,
    string EventType,
    string EntityType,
    string EntityId,
    string Operation,
    long? EntityVersionBefore,
    long? EntityVersionAfter,
    string ActorType,
    string ActorId,
    string PermissionCode,
    IReadOnlyList<string> RoleCodes,
    string BeforeJson,
    string AfterJson,
    string Reason,
    string? ApprovalContextJson,
    string AiInvolvement,
    string? AiContextJson,
    string SourceChannel,
    string? ClientIpAddress,
    string? UserAgent,
    string CorrelationId,
    string? CausationId,
    string Outcome,
    int SchemaVersion,
    string IntegrityHash);

public sealed record AuditEventPage(
    IReadOnlyList<AuditEventItem> Items,
    int PageSize,
    string? NextCursor);
