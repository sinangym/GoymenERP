using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Logistics.Application.Contracts;

public enum LogisticsTransferEDispatchAssociationV1 { Unknown = 0, NotLinked = 1, Linked = 2 }
public enum LogisticsTransferDepartureEventKindV1 { ExitWeighing = 1, VehicleExit = 2 }
public static class LogisticsTransferDepartureV1
{
    public const string RecordPermission = "logistics.transfer-departures.record";
    public const string SourceVersion = "logistics.transfer-departures.v1";
}

/// <summary>The Inventory owner supplies the genuine inert plan or recorded dispatch, never client-entered status.</summary>
public sealed record LogisticsTransferReferenceV1(Guid CompanyId, Guid TransferId, Guid SourceLocationId,
    string Status, long Version, DateTimeOffset? DispatchedAtUtc, IReadOnlyList<Guid> OrderedLineIds);
public interface ILogisticsTransferReferenceSourceV1
{
    Task<LogisticsTransferReferenceV1?> ReadAsync(Guid companyId, Guid transferId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Actual official-document owner evidence only. No implementation is inferred from an e-İrsaliye number or a setting.
/// Linked associations remain unavailable until a genuine importer/transport owner supplies this versioned proof.
/// </summary>
public sealed record LogisticsTransferEDispatchEvidenceV1(Guid CompanyId, Guid TransferId, Guid OfficialDocumentId,
    long Version, bool IsLinked, DateTimeOffset? LinkedAtUtc, bool? IsSentToGib, DateTimeOffset? SentAtUtc,
    long? CancelConfirmationVersion);
public interface ILogisticsTransferEDispatchEvidenceSourceV1
{
    Task<LogisticsTransferEDispatchEvidenceV1?> ReadAsync(Guid companyId, Guid transferId, CancellationToken cancellationToken = default);
    Task<LogisticsTransferEDispatchEvidenceV1?> ConfirmCancellationAsync(Guid companyId, Guid transferId,
        LogisticsWriteEnvelope envelope, Func<CancellationToken, Task> freshAuthority, CancellationToken cancellationToken = default);
}

public sealed record LogisticsTransferDepartureQueryV1(Guid CompanyId, Guid TransferId, Guid SourceLocationId,
    DateTimeOffset DispatchedAtUtc);
public sealed record LogisticsTransferDepartureFactsV1(Guid CompanyId, Guid TransferId, Guid SourceLocationId,
    LogisticsTransferEDispatchAssociationV1 Association, long? Version, DateTimeOffset? ExitWeighingAtUtc,
    DateTimeOffset? VehicleExitAtUtc, bool NoFurtherExit, LogisticsTransferEDispatchEvidenceV1? OfficialDocument,
    string SourceVersion);
public sealed record LogisticsTransferCancelConfirmationV1(LogisticsTransferDepartureFactsV1 Facts,
    long? NoExitConfirmationVersion, long? EDispatchCancellationVersion);
public sealed record LogisticsTransferDepartureRecordedV1(LogisticsTransferDepartureFactsV1 Facts, bool IsReplay,
    string OriginalCorrelationId);
public sealed record LogisticsTransferCancelIntentV1(LogisticsTransferDepartureQueryV1 Dispatch,
    string ExpectedTransferVersion, DateTimeOffset CancelledAtUtc, string Reason,
    bool RequireNoExit, bool RequireEDispatchCancellation);

public interface ILogisticsTransferDepartureOwnerV1
{
    Task<LogisticsTransferDepartureFactsV1> ReadAsync(LogisticsTransferDepartureQueryV1 query, CancellationToken cancellationToken = default);
    Task<bool> SupportsWarehouseAsync(Guid companyId, Guid locationId, CancellationToken cancellationToken = default);
    Task<LogisticsTransferCancelConfirmationV1> ConfirmAsync(LogisticsTransferCancelIntentV1 intent,
        LogisticsWriteEnvelope envelope, Func<CancellationToken, Task> freshAuthority, CancellationToken cancellationToken = default);
    Task<LogisticsTransferDepartureRecordedV1> RecordAsync(Guid transferId, RecordTransferDepartureV1 request,
        LogisticsWriteEnvelope envelope, Func<CancellationToken, Task> freshAuthority, CancellationToken cancellationToken = default);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RecordTransferDepartureV1(string? ExpectedVersion, LogisticsTransferDepartureEventKindV1 Kind,
    DateTimeOffset OccurredAtUtc, string? Reason, TransferDepartureWeighingV1? Weighing = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TransferDepartureWeighingV1(int Direction, string FirstKg, string SecondKg);

/// <summary>
/// Initial association belongs to the real new outgoing-document transaction. Omitted legacy metadata remains Unknown.
/// Explicit NotLinked is its own versioned owner fact; it is never reconstructed from a missing document number.
/// </summary>
public interface ILogisticsTransferOutgoingDocumentStoreV2 : ILogisticsTransferOutgoingDocumentStoreV1
{
    Task<TransferOutgoingDocumentStoreResultV1> StoreDraftWithAssociationAsync(LogisticsWriteEnvelope envelope,
        string requestHash, GoymenERP.Modules.Logistics.Domain.Transfers.TransferOutgoingDocument document,
        LogisticsTransferEDispatchAssociationV1 association,
        Func<CancellationToken, Task> beforeCommit, CancellationToken cancellationToken = default);
}

public sealed class LogisticsTransferDepartureException(int status, string code) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
