namespace GoymenERP.Modules.AiKnowledge.Domain;

/// <summary>Opaque pipeline state; business commands remain owned and executed by their modules.</summary>
public sealed record AiCommandStoredRecordV1(Guid CompanyId, Guid PreviewId, string ActorId, int State,
    long Version, string BindingSha256, string RecordJson);

/// <summary>Content-free ledger event; never contains a question, command, password or model response.</summary>
public sealed record AiCommandStoredEventV1(Guid EventId, Guid CompanyId, string ActorId, int Kind,
    Guid? PreviewId, string? ToolId, string? ToolVersion, string? PreviewHash, Guid? ApprovalId,
    string? ReasonCode, string? ResultVersion, string CorrelationId, string? CausationId, DateTimeOffset OccurredAtUtc);

/// <summary>Company-scoped storage port. State CAS and its event are committed in one save.</summary>
public interface IAiCommandPersistenceV1
{
    bool IsAvailable { get; }
    Task<bool> TryAddAsync(AiCommandStoredRecordV1 record, IReadOnlyList<AiCommandStoredEventV1> events,
        CancellationToken cancellationToken = default);
    Task<AiCommandStoredRecordV1?> GetAsync(Guid companyId, Guid previewId, CancellationToken cancellationToken = default);
    Task<bool> TryReplaceAsync(AiCommandStoredRecordV1 next, long expectedVersion, AiCommandStoredEventV1 auditEvent,
        CancellationToken cancellationToken = default);
    Task<bool> AppendAsync(AiCommandStoredEventV1 auditEvent, CancellationToken cancellationToken = default);
}

public sealed class AiCommandPersistenceUnavailableException(Exception inner)
    : Exception("The AI command ledger is unavailable.", inner);
