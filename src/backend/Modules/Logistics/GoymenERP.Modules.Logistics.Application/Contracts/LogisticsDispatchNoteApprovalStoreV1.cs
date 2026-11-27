using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Logistics.Application.Contracts;

/// <summary>Private owner receipt persisted in the existing immutable command ResultJson, never the mutable current view.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LogisticsDispatchNoteApprovalReceiptV1(Guid CompanyId, string CommandName, Guid InstructionId,
    Guid DispatchNoteId, long ExpectedVersion, long ApprovedVersion, string ApprovedBy, DateTimeOffset ApprovedAtUtc,
    string OriginalCorrelationId, string RequestHash);

/// <summary>Only dispatch APPROVE owns this boundary; the other instruction participant commands retain their contract.</summary>
public interface ILogisticsDispatchNoteApprovalStoreV1
{
    Task<LogisticsReplay?> FindDispatchNoteApprovalReplayAsync(LogisticsWriteEnvelope envelope, string requestHash,
        Guid instructionId, Guid dispatchNoteId, long expectedVersion, Func<CancellationToken, Task> freshAuthority,
        CancellationToken cancellationToken = default);
    Task<LogisticsStoreResult?> ApproveDispatchNoteOwnedAsync(LogisticsWriteEnvelope envelope, string requestHash,
        Guid instructionId, Guid dispatchNoteId, long expectedVersion, LogisticsMutationAudit audit,
        Func<CancellationToken, Task> freshAuthority, CancellationToken cancellationToken = default);
    Task RecordDispatchNoteApprovalRefusalAsync(LogisticsWriteEnvelope envelope, string requestHash,
        LogisticsTerminalRefusal refusal, Func<CancellationToken, Task> freshAuthority, CancellationToken cancellationToken = default);
}
