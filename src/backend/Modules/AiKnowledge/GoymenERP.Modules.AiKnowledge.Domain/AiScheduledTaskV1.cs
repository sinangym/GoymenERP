namespace GoymenERP.Modules.AiKnowledge.Domain;

// AI-D33 separates effects; this increment dispatches only the existing morning-summary read.
public enum AiScheduledTaskEffectV1 { Read = 1, ProduceSummaryDraftOrNotification = 2, ChangeBusinessRecord = 3, ExternalSend = 4 }
public enum AiScheduledTaskStateV1 { Active = 1, Paused = 2, Closed = 3, StoppedForAuthority = 4 }
public enum AiScheduledTaskActionV1 { ManageOwn = 1, ReadOwn = 2, Oversee = 3, ExecuteCurrent = 4 }
public enum AiScheduledTaskAuthorityOutcomeV1 { Unavailable = 0, Allowed = 1, Denied = 2 }
public static class AiScheduledTaskPurposesV1 { public const string MorningSummary = "work.morning-summary.v1"; }

public sealed record AiScheduledTaskViewV1(Guid CompanyId, Guid TaskId, string OwnerActorId, string Purpose,
    AiScheduledTaskEffectV1 Effect, AiScheduledTaskStateV1 State, long Version, string ScheduleVersion,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record AiScheduledTaskDefinitionV1(string Purpose, AiScheduledTaskEffectV1 Effect, string ScheduleVersion);
public sealed record AiScheduledTaskMetadataV1(string IdempotencyKey, string CorrelationId, string? CausationId = null);
public sealed record AiScheduledTaskRunRequestV1(long ExpectedVersion, DateTimeOffset SlotUtc, string ScheduleVersion);
// Content-free evidence: no questions, ERP facts, summary counts, money, recipient lists or model output.
public sealed record AiScheduledTaskRunReceiptV1(Guid CompanyId, Guid TaskId, string OwnerActorId, long TaskVersion,
    DateTimeOffset SlotUtc, string ScheduleVersion, string Outcome, Guid? SourceReceiptId, DateTimeOffset RecordedAtUtc);
public sealed record AiScheduledTaskResponseV1(int Status, string? Code, AiScheduledTaskViewV1? Task,
    AiScheduledTaskRunReceiptV1? Run, bool IsReplay, string OriginalCorrelationId);
public sealed record AiScheduledTaskAuthorityV1(AiScheduledTaskAuthorityOutcomeV1 Outcome, string? PermissionCode);
// The root Settings adapter supplies real values. Null never means unlimited or free.
public sealed record AiScheduledTaskSettingsV1(Guid CompanyId, string ScheduleVersion, DateTimeOffset SlotUtc,
    int? MaximumActiveTasks, bool? ExecutionBudgetAvailable);
public enum AiScheduledTaskMorningReadOutcomeV1 { Unavailable = 0, Available = 1, Denied = 2 }
public sealed record AiScheduledTaskMorningReadV1(Guid CompanyId, string ScheduleVersion, DateTimeOffset SlotUtc, Guid? SourceReceiptId,
    AiScheduledTaskMorningReadOutcomeV1 Outcome);

public interface IAiScheduledTaskAuthorityV1
{
    Task<AiScheduledTaskAuthorityV1> CheckAsync(Guid companyId, string actualActorId, string ownerActorId,
        AiScheduledTaskActionV1 action, CancellationToken cancellationToken = default);
}
public interface IAiScheduledTaskSettingsV1
{
    Task<AiScheduledTaskSettingsV1?> ReadAsync(Guid companyId, string ownerActorId, CancellationToken cancellationToken = default);
}
/// <summary>API-owned adapter calls the existing public Work read under the actual current user. Never impersonates an owner.</summary>
public interface IAiScheduledTaskMorningReadV1
{
    Task<AiScheduledTaskMorningReadV1?> ReadCurrentAsync(CancellationToken cancellationToken = default);
}
public sealed record AiScheduledTaskCommandV1(Guid CompanyId, Guid TaskId, string ActorId, string OwnerActorId,
    string Command, string KeyHash, string PayloadHash, AiScheduledTaskMetadataV1 Metadata, DateTimeOffset RecordedAtUtc,
    DateTimeOffset? RunSlotUtc = null);
public sealed record AiScheduledTaskStoredSourceV1(AiScheduledTaskViewV1? Task, int ActiveTaskCount, bool RunSlotAlreadyRecorded);
public sealed record AiScheduledTaskMutationV1(int Status, string? Code, AiScheduledTaskViewV1? Task,
    AiScheduledTaskRunReceiptV1? Run, string EventKind, string PermissionCode);
/// <summary>One owned transaction binds task CAS, exact command/run receipts and the real Audit batch.</summary>
public interface IAiScheduledTaskPersistenceV1
{
    bool IsAvailable { get; }
    bool IsCleanEntry { get; }
    Task<AiScheduledTaskViewV1?> ReadAsync(Guid companyId, Guid taskId, Func<CancellationToken, Task> freshFence,
        CancellationToken cancellationToken = default);
    Task<AiScheduledTaskResponseV1> ExecuteAsync(AiScheduledTaskCommandV1 command,
        Func<AiScheduledTaskStoredSourceV1, CancellationToken, Task<AiScheduledTaskMutationV1>> decide,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default);
}
public interface IAiScheduledTaskServiceV1
{
    Task<AiScheduledTaskResponseV1> CreateAsync(Guid taskId, AiScheduledTaskDefinitionV1 definition, AiScheduledTaskMetadataV1 metadata, CancellationToken token = default);
    Task<AiScheduledTaskResponseV1> UpdateAsync(Guid taskId, long expectedVersion, AiScheduledTaskDefinitionV1 definition, bool active, AiScheduledTaskMetadataV1 metadata, CancellationToken token = default);
    Task<AiScheduledTaskResponseV1> PauseAsync(Guid taskId, long expectedVersion, AiScheduledTaskMetadataV1 metadata, CancellationToken token = default);
    Task<AiScheduledTaskResponseV1> CloseAsync(Guid taskId, long expectedVersion, AiScheduledTaskMetadataV1 metadata, CancellationToken token = default);
    Task<AiScheduledTaskViewV1?> ReadOwnAsync(Guid taskId, CancellationToken token = default);
    Task<AiScheduledTaskResponseV1> ViewForManagerAsync(Guid taskId, string ownerActorId, AiScheduledTaskMetadataV1 metadata, CancellationToken token = default);
    Task<AiScheduledTaskResponseV1> CloseForManagerAsync(Guid taskId, string ownerActorId, long expectedVersion, AiScheduledTaskMetadataV1 metadata, CancellationToken token = default);
    Task<AiScheduledTaskResponseV1> RunCurrentAsync(Guid taskId, AiScheduledTaskRunRequestV1 request, AiScheduledTaskMetadataV1 metadata, CancellationToken token = default);
}
public sealed class AiScheduledTaskException(int status, string code, Exception? inner = null) : Exception(code, inner)
{ public int Status { get; } = status; public string Code { get; } = code; }
