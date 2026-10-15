using GoymenERP.Modules.Logistics.Domain.Transfers;

namespace GoymenERP.Modules.Logistics.Application.Contracts;

public static class LogisticsTransferOutgoingDocumentCommandsV1
{
    public const string CreateDraft = "logistics.transfer-outgoing-document.create.v1";
    public const string Finalize = "logistics.transfer-outgoing-document.finalize.v1";
    public const string Cancel = "logistics.transfer-outgoing-document.cancel.v1";
}

public static class LogisticsTransferOutgoingDocumentCodesV1
{
    public const string NotFound = "logistics_transfer_outgoing_document_not_found";
    public const string State = "logistics_transfer_outgoing_document_state";
    public const string VersionConflict = "logistics_transfer_outgoing_document_version_conflict";
    public const string TransferAlreadyBound = "logistics_transfer_outgoing_document_transfer_already_bound";
}

public sealed record LogisticsTransferOutgoingDocumentBindingsV1(
    string CreatePermission,
    string FinalizePermission,
    string CancelPermission);

public sealed record CreateTransferOutgoingDocumentV1(
    Guid DocumentId,
    Guid TransferId,
    IReadOnlyList<Guid> OrderedTransferLineIds,
    string? EDispatchNumber = null);

public sealed record FinalizeTransferOutgoingDocumentV1(int ExpectedVersion);

public sealed record CancelTransferOutgoingDocumentV1(int ExpectedVersion, string Reason);

public sealed record TransferOutgoingDocumentViewV1(
    Guid CompanyId,
    Guid DocumentId,
    Guid TransferId,
    IReadOnlyList<Guid> OrderedTransferLineIds,
    string? EDispatchNumber,
    string Status,
    int Version,
    string CreatedBy,
    DateTimeOffset CreatedAtUtc,
    string? EffectiveBy,
    DateTimeOffset? EffectiveAtUtc,
    string? CancelledBy,
    DateTimeOffset? CancelledAtUtc,
    string? CancellationReason);

public sealed record TransferOutgoingDocumentResponseV1(
    TransferOutgoingDocumentViewV1 Document,
    bool IsReplay,
    string OriginalCorrelationId);

public interface ILogisticsTransferOutgoingDocumentServiceV1
{
    Task<TransferOutgoingDocumentResponseV1> CreateDraftAsync(CreateTransferOutgoingDocumentV1 request,
        LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default);

    Task<TransferOutgoingDocumentResponseV1> FinalizeAsync(Guid documentId,
        FinalizeTransferOutgoingDocumentV1 request, LogisticsCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<TransferOutgoingDocumentResponseV1> CancelAsync(Guid documentId,
        CancelTransferOutgoingDocumentV1 request, LogisticsCommandMetadata metadata,
        CancellationToken cancellationToken = default);
}

public sealed record LogisticsTransferOutgoingDocumentScopeV1(
    Guid CompanyId,
    string ActorId,
    IReadOnlyCollection<string> RoleCodes);

public interface ILogisticsTransferOutgoingDocumentScopeV1
{
    Task<LogisticsTransferOutgoingDocumentScopeV1> RequireAsync(string permission,
        CancellationToken cancellationToken = default);

    Task RequireStillAsync(LogisticsTransferOutgoingDocumentScopeV1 scope, string permission,
        CancellationToken cancellationToken = default);
}

public sealed record TransferOutgoingDocumentStoreResultV1(
    TransferOutgoingDocument Document,
    LogisticsWriteOutcome Outcome,
    string OriginalCorrelationId);

public interface ILogisticsTransferOutgoingDocumentStoreV1
{
    Task<TransferOutgoingDocumentStoreResultV1> StoreDraftAsync(LogisticsWriteEnvelope envelope,
        string requestHash, TransferOutgoingDocument document, Func<CancellationToken, Task> beforeCommit,
        CancellationToken cancellationToken = default);

    Task<TransferOutgoingDocumentStoreResultV1> MutateAsync(LogisticsWriteEnvelope envelope,
        string requestHash, string command, Guid documentId, Action<TransferOutgoingDocument, DateTimeOffset> mutate,
        Func<CancellationToken, Task> beforeCommit, CancellationToken cancellationToken = default);

    Task<LogisticsTransferOutgoingDocumentOwnerSnapshotV1?> FindProofSnapshotAsync(Guid documentId,
        CancellationToken cancellationToken = default);
}

public enum LogisticsTransferOutgoingDocumentProofStateV1
{
    Draft = 1,
    CurrentEffective = 2,
    Cancelled = 3,
}

public sealed record LogisticsTransferOutgoingDocumentOwnerSnapshotV1(
    Guid CompanyId,
    Guid TransferId,
    Guid DocumentId,
    int DocumentVersion,
    IReadOnlyList<Guid> OrderedTransferLineIds,
    LogisticsTransferOutgoingDocumentProofStateV1 State);

public interface ILogisticsTransferOutgoingDocumentProofSourceV1
{
    Task<LogisticsTransferOutgoingDocumentOwnerSnapshotV1?> FindAsync(Guid documentId,
        CancellationToken cancellationToken = default);
}

public sealed class LogisticsTransferOutgoingDocumentServiceException(int status, string code)
    : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
