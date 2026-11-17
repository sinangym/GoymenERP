using GoymenERP.Modules.Logistics.Domain.WeighingDifferences;

namespace GoymenERP.Modules.Logistics.Application.Contracts;

/// <summary>The versioned command names the weighing-difference attempt record is keyed by (ADR-011 §2).</summary>
public static class LogisticsWeighingDifferenceCommandsV1
{
    public const string Record = "logistics.weighing-difference.record.v1";
    public const string Approve = "logistics.weighing-difference.approve.v1";
}

public static class LogisticsWeighingDifferenceCodesV1
{
    public const string NotFound = "logistics_weighing_difference_not_found";
    public const string DispatchNoteNotFound = "logistics_weighing_difference_dispatch_note_not_found";
    public const string DispatchNoteNotDraft = "logistics_weighing_difference_dispatch_note_not_draft";
    public const string AlreadyRecorded = "logistics_weighing_difference_already_recorded";
    public const string VersionConflict = WeighingDifferenceRuleCodes.VersionConflict;
    public const string StockUnavailable = "logistics_weighing_difference_stock_unavailable";
    public const string Unavailable = "logistics_weighing_difference_unavailable";
    public const string ValidationFailed = "validation_failed";
}

public sealed record LogisticsWeighingDifferenceBindingsV1(
    string RecordPermission,
    string ApprovePermission,
    string ReadPermission);

/// <summary>The scale kg of one product on the note. Every note product appears exactly once.</summary>
public sealed record WeighingDifferenceScaleEntryV1(Guid ProductId, decimal ScaleKg);

public sealed record RecordWeighingDifferenceV1(
    Guid RecordId,
    Guid DispatchNoteId,
    IReadOnlyList<WeighingDifferenceScaleEntryV1> Products,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    DispatchKilogramFinalizedDocumentV1? FinalizedDocument = null);

public sealed record ApproveWeighingDifferenceV1(int ExpectedVersion);

public sealed record WeighingDifferenceProductViewV1(
    Guid ProductId,
    decimal NoteKg,
    decimal ScaleKg,
    decimal DifferenceKg,
    decimal RemainingPhysicalKg,
    decimal PhysicallyCoveredKg,
    decimal ExcessKg,
    int? TargetLineNo);

public sealed record WeighingDifferenceViewV1(
    Guid CompanyId,
    Guid RecordId,
    Guid DispatchNoteId,
    Guid InstructionId,
    decimal TheoreticalKg,
    decimal ScaleKg,
    decimal DifferenceKg,
    string BandLevel,
    decimal? WarningBandPercent,
    decimal? ApprovalBandPercent,
    decimal? BlockBandPercent,
    decimal TotalExcessKg,
    decimal? ExcessRatioPercent,
    decimal? ApprovalRatePercent,
    string Decision,
    string Status,
    int Version,
    IReadOnlyList<WeighingDifferenceProductViewV1> Products,
    IReadOnlyList<string> EnteredBy,
    bool EnteredByComplete,
    string RecordedBy,
    DateTimeOffset RecordedAtUtc,
    string? ApprovedBy,
    DateTimeOffset? ApprovedAtUtc, DispatchKilogramSnapshot? KilogramAllocation = null);

public sealed record WeighingDifferenceResponseV1(
    WeighingDifferenceViewV1 Record,
    bool IsReplay,
    string OriginalCorrelationId);

/// <summary>
/// Owner service of the sales weighing-difference record; the central host exposes decimal-text V1 HTTP contracts.
/// </summary>
public interface ILogisticsWeighingDifferenceServiceV1
{
    Task<WeighingDifferenceResponseV1> RecordAsync(RecordWeighingDifferenceV1 request, LogisticsCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<WeighingDifferenceResponseV1> ApproveAsync(Guid recordId, ApproveWeighingDifferenceV1 request,
        LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default);

    Task<WeighingDifferenceViewV1> GetByDispatchNoteAsync(Guid dispatchNoteId, CancellationToken cancellationToken = default);
}

public sealed record LogisticsWeighingDifferenceScopeV1(
    Guid CompanyId,
    string ActorId,
    IReadOnlyCollection<string> RoleCodes);

public interface ILogisticsWeighingDifferenceScopeV1
{
    Task<LogisticsWeighingDifferenceScopeV1> RequireAsync(string permission, CancellationToken cancellationToken = default);

    Task RequireStillAsync(LogisticsWeighingDifferenceScopeV1 scope, string permission,
        CancellationToken cancellationToken = default);
}

/// <summary>One dispatch-note line as stored by Logistics (its own tables), with whether its unit is the kilogram.</summary>
public sealed record WeighingDifferenceSourceLineV1(int LineNo, Guid ShipmentId, Guid ProductId, decimal Quantity,
    bool IsKilogram);

/// <summary>
/// The dispatch note a record is evaluated against: its lines and the SAT-D321 (2) actors who entered its shipments and the
/// instruction's weighing. <see cref="EnteredByComplete"/> is false when one of them was stored without an actor.
/// </summary>
public sealed record WeighingDifferenceSourceV1(
    Guid DispatchNoteId,
    Guid InstructionId,
    bool IsDraft,
    IReadOnlyList<WeighingDifferenceSourceLineV1> Lines,
    IReadOnlyList<string> EnteredBy,
    bool EnteredByComplete);

public sealed record WeighingDifferenceStoreResultV1(
    WeighingDifferenceRecord Record,
    LogisticsWriteOutcome Outcome,
    string OriginalCorrelationId);

public interface ILogisticsWeighingDifferenceStoreV1
{
    /// <summary>The stored result of an earlier attempt with the same key, or null. A different payload or actor is refused.</summary>
    Task<WeighingDifferenceStoreResultV1?> FindReplayAsync(LogisticsWriteEnvelope envelope, string commandName,
        string requestHash, CancellationToken cancellationToken = default);

    Task<WeighingDifferenceSourceV1?> ReadSourceAsync(Guid dispatchNoteId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a new record in one serializable transaction. The source is re-read inside it and must still equal
    /// <paramref name="expectedSource"/> (note still Draft, same lines and actors), otherwise nothing is stored.
    /// </summary>
    Task<WeighingDifferenceStoreResultV1> StoreAsync(LogisticsWriteEnvelope envelope, string requestHash,
        WeighingDifferenceRecord record, WeighingDifferenceSourceV1 expectedSource, Func<CancellationToken, Task> beforeCommit,
        CancellationToken cancellationToken = default);

    Task<WeighingDifferenceStoreResultV1> MutateAsync(LogisticsWriteEnvelope envelope, string requestHash, string command,
        Guid recordId, Action<WeighingDifferenceRecord, DateTimeOffset> mutate, Func<CancellationToken, Task> beforeCommit,
        CancellationToken cancellationToken = default);

    Task<WeighingDifferenceRecord?> FindByDispatchNoteAsync(Guid dispatchNoteId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The settings the record is evaluated with: the single SAT-D344 approval rate and the SAT-D324 bands. Each value is null when
/// Settings could not be read. Consumer-defined port: Settings has no published read for these yet, so until its adapter is
/// registered the default answers "unreadable" (the record then waits for approval and leaves the band unevaluated).
/// </summary>
public sealed record LogisticsWeighingDifferencePolicyReadV1(
    decimal? ExcessApprovalRatePercent,
    decimal? WarningBandPercent,
    decimal? ApprovalBandPercent,
    decimal? BlockBandPercent)
{
    public static LogisticsWeighingDifferencePolicyReadV1 Unreadable { get; } = new(null, null, null, null);
}

public interface ILogisticsWeighingDifferencePolicyReadPortV1
{
    Task<LogisticsWeighingDifferencePolicyReadV1> ReadAsync(Guid companyId, CancellationToken cancellationToken = default);
}

/// <summary>Fail-closed default until the Settings adapter exists: every value unreadable, nothing guessed.</summary>
public sealed class UnavailableLogisticsWeighingDifferencePolicyReadPort : ILogisticsWeighingDifferencePolicyReadPortV1
{
    public Task<LogisticsWeighingDifferencePolicyReadV1> ReadAsync(Guid companyId,
        CancellationToken cancellationToken = default) => Task.FromResult(LogisticsWeighingDifferencePolicyReadV1.Unreadable);
}

/// <summary>
/// The free, company-owned and company-held physical kg Inventory still holds per product (after the note's own shipments were
/// issued). Not readable means the physical cover cannot be measured; the record is then refused, never guessed.
/// </summary>
public sealed record LogisticsWeighingDifferenceStockReadV1(bool IsReadable, IReadOnlyDictionary<Guid, decimal> FreeKgByProduct)
{
    public static LogisticsWeighingDifferenceStockReadV1 Unreadable { get; } = new(false, new Dictionary<Guid, decimal>());
}

public interface ILogisticsWeighingDifferenceStockReadPortV1
{
    Task<LogisticsWeighingDifferenceStockReadV1> ReadAsync(Guid companyId, IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken = default);
}

/// <summary>Fail-closed default until the host adapter over Inventory is registered.</summary>
public sealed class UnavailableLogisticsWeighingDifferenceStockReadPort : ILogisticsWeighingDifferenceStockReadPortV1
{
    public Task<LogisticsWeighingDifferenceStockReadV1> ReadAsync(Guid companyId, IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken = default) => Task.FromResult(LogisticsWeighingDifferenceStockReadV1.Unreadable);
}

/// <summary>
/// STK-S24 contract (provided by stok_lojistik, consumed by Sales): the weighing-difference record of one dispatch note, read
/// under the server-resolved company only. <see cref="SaleMayProceed"/> is false while the record waits for approval
/// (SAT-D319 (6)). Null means no record exists for the note in that company.
/// </summary>
public sealed record LogisticsWeighingDifferenceReadV1(
    Guid CompanyId,
    Guid DispatchNoteId,
    Guid RecordId,
    decimal TheoreticalKg,
    decimal ScaleKg,
    decimal DifferenceKg,
    string BandLevel,
    decimal TotalExcessKg,
    string Decision,
    string Status,
    bool SaleMayProceed,
    int Version, DispatchKilogramSnapshot? KilogramAllocation = null);

public interface ILogisticsWeighingDifferenceReadPortV1
{
    Task<LogisticsWeighingDifferenceReadV1?> FindByDispatchNoteAsync(Guid companyId, Guid dispatchNoteId,
        CancellationToken cancellationToken = default);
}

public sealed class LogisticsWeighingDifferenceServiceException(int status, string code) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
