using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Logistics.Domain.Loading;

namespace GoymenERP.Modules.Logistics.Application.Contracts;

/// <summary>
/// The ADR-009 audit envelope of one loading-instruction write (the Sales / Inventory shape): who
/// asked, under which permission, in which conversation, with which client key. Every value is the
/// caller's server-side context; the company is always the server-resolved one.
/// </summary>
public sealed record LogisticsWriteEnvelope(
    string ActorId,
    string PermissionCode,
    IReadOnlyCollection<string> RoleCodes,
    string CorrelationId,
    string? CausationId,
    string IdempotencyKey,
    string SourceChannel,
    string? ClientIpAddress = null,
    string? UserAgent = null);

public enum LogisticsWriteOutcome
{
    Recorded = 1,
    Replayed = 2,
}

/// <summary>The versioned command names the attempt record is keyed by (ADR-011 §2; LOG-4 D5).</summary>
public static class LogisticsCommands
{
    public const string CreateV1 = "logistics.loading-instruction.create.v1";
    public const string IssueV1 = "logistics.loading-instruction.issue.v1";
    public const string RecordLoadV1 = "logistics.loading-instruction.record-load.v1";
    public const string RecordShipmentV1 = "logistics.loading-instruction.record-shipment.v1";
    public const string RecordDeliveryV1 = "logistics.loading-instruction.record-delivery.v1";
    public const string RecordWeighingV1 = "logistics.loading-instruction.record-weighing.v1";

    /// <summary>AT-055: a dispatch-note draft from the instruction's definite shipments (one version step, no share).</summary>
    public const string CreateDispatchNoteV1 = "logistics.loading-instruction.create-dispatch-note.v1";

    /// <summary>AT-055 lifecycle (user decision 2026-09-30): approve (the system number), cancel (releases the shipments), e-İrsaliye number.</summary>
    public const string ApproveDispatchNoteV1 = "logistics.loading-instruction.approve-dispatch-note.v1";
    public const string CancelDispatchNoteV1 = "logistics.loading-instruction.cancel-dispatch-note.v1";
    public const string SetEDispatchNumberV1 = "logistics.loading-instruction.set-e-dispatch-number.v1";

    public static bool IsMutation(string commandName) =>
        commandName is IssueV1 or RecordLoadV1 or RecordShipmentV1 or RecordDeliveryV1 or RecordWeighingV1 or CreateDispatchNoteV1
            or ApproveDispatchNoteV1 or CancelDispatchNoteV1 or SetEDispatchNumberV1;

    public static bool IsKnown(string commandName) => commandName == CreateV1 || IsMutation(commandName);
}

/// <summary>The stable refusal codes of the store, beside the domain's rule codes.</summary>
public static class LogisticsStoreCodes
{
    public const string CompanyContextMismatch = "logistics_company_context_mismatch";
    public const string SameKeyPayloadMismatch = "logistics_same_key_payload_mismatch";
    public const string ConcurrentChange = "logistics_concurrent_change";
    public const string ValueDoesNotFit = "logistics_value_does_not_fit";
    public const string InvalidCursor = "logistics_invalid_cursor";
    public const string AggregateLimitExceeded = "logistics_order_line_aggregate_limit_exceeded";
}

public sealed class LogisticsStoreException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}

/// <summary>A stored instruction with the business date its unit profiles were read for (kept beside the domain record, not in it).</summary>
public sealed record StoredLoadingInstruction(LoadingInstruction Instruction, DateOnly EffectiveOn);

public sealed record LogisticsStoreResult(StoredLoadingInstruction Instruction, LogisticsWriteOutcome Outcome, string OriginalCorrelationId);

/// <summary>
/// A Sales-resolved OPEN-004 ceiling prepared for the Logistics store. The maximum is already in the
/// order unit; the store only aggregates instruction quantities and compares. The immutable source tuple
/// is written into the create audit snapshot.
/// </summary>
public sealed record LogisticsOrderLineAggregateLimit(
    Guid InstructionLineId,
    Guid CompanyId,
    Guid OrderId,
    Guid OrderLineId,
    Guid UnitId,
    long SalesOrderVersion,
    decimal MaximumAggregateQuantity,
    string MaximumAggregateQuantityText,
    string SourceOwner,
    string SourceId,
    string SourceVersion,
    DateOnly EffectiveOn);

/// <summary>
/// The fresh Sales cap and this load's quantity, checked by the Logistics store in the same serializable transaction as
/// the load mutation. The total is every instruction line linked to the same Sales line.
/// </summary>
public sealed record LogisticsOrderLineAggregateLoadLimit(
    Guid InstructionLineId,
    Guid CompanyId,
    Guid OrderId,
    Guid OrderLineId,
    Guid UnitId,
    long SalesOrderVersion,
    decimal MaximumAggregateQuantity,
    string MaximumAggregateQuantityText,
    decimal RequestedLoadQuantity,
    string RequestedLoadQuantityText,
    string SourceOwner,
    string SourceId,
    string SourceVersion,
    DateOnly EffectiveOn);

/// <summary>ADR-011 §7: a terminal refusal recorded under the attempt key (stale version, cap, block, state), answered again on the same key.</summary>
public sealed record LogisticsTerminalRefusal(int Status, string Code, IReadOnlyList<LogisticsFieldError> Errors);

/// <summary>What the key holds: the stored instruction or the recorded refusal, never both.</summary>
public sealed record LogisticsReplay(StoredLoadingInstruction? Instruction, LogisticsTerminalRefusal? Refusal, string OriginalCorrelationId);

public sealed record LogisticsInstructionPage(IReadOnlyList<StoredLoadingInstruction> Items, string? NextCursor);

/// <summary>The audit facts of one mutation (issue / record-load): the versioned event type, the operation and the reason.</summary>
public sealed record LogisticsMutationAudit(string EventType, string Operation, string Reason);

/// <summary>
/// The persistent instruction store of the server-resolved company (LOG-4a). One call is one save:
/// the instruction, its lines and shares, the attempt record and the audit event commit together or
/// not at all. Replay identity (ADR-011, R3): (company, command, SHA-256 of the trimmed key); the raw
/// key is never stored; the same key with the same content and actor answers the stored result, another
/// content or actor is one 409; a key of another company is invisible.
/// </summary>
public interface ILogisticsInstructionStore
{
    /// <summary>Stores a new instruction the caller built (Draft, ≥1 line), or answers the same attempt again.</summary>
    Task<LogisticsStoreResult> StoreInstructionAsync(LogisticsWriteEnvelope envelope, string requestHash, DateOnly effectiveOn,
        Func<LoadingInstruction> build, CancellationToken cancellationToken = default,
        IReadOnlyList<LogisticsOrderLineAggregateLimit>? aggregateLimits = null);

    Task<LogisticsReplay?> FindReplayAsync(LogisticsWriteEnvelope envelope, string requestHash, string commandName,
        CancellationToken cancellationToken = default);

    Task RecordRefusalAsync(LogisticsWriteEnvelope envelope, string requestHash, string commandName, LogisticsTerminalRefusal refusal,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One mutation (issue / record-load) of the company's stored instruction in ONE save: the domain
    /// transition, the attempt record and the audit event. The caller answers the domain's refusals
    /// (terminal ones through <see cref="RecordRefusalAsync"/>); another company's instruction, or none,
    /// is null (never 403); a same-key attempt already stored answers Replayed; a lost race is
    /// <see cref="LogisticsStoreCodes.ConcurrentChange"/> with nothing stored.
    /// </summary>
    Task<LogisticsStoreResult?> MutateInstructionAsync(LogisticsWriteEnvelope envelope, string requestHash, string commandName,
        Guid instructionId, Action<LoadingInstruction, DateTimeOffset> mutate, LogisticsMutationAudit audit,
        CancellationToken cancellationToken = default);

    Task<StoredLoadingInstruction?> FindInstructionAsync(Guid instructionId, CancellationToken cancellationToken = default);

    /// <summary>The company's instructions, newest first, by opaque company-bound cursor; <paramref name="status"/> narrows to one status.</summary>
    Task<LogisticsInstructionPage> ListInstructionsAsync(int pageSize, string? cursor, LoadingInstructionStatus? status = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// AT-055: the context company's dispatch notes (every state) with at least one line on a shipment of <paramref name="salesOrderId"/>'s
    /// lines, oldest first, at most <paramref name="limit"/> (the caller asks one more to know whether more exist). Untracked; creates
    /// nothing. Another company's drafts are invisible.
    /// </summary>
    Task<IReadOnlyList<StoredDispatchNoteReference>> ListSalesOrderDispatchNotesAsync(Guid salesOrderId, int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds one approved, non-cancelled dispatch note in the context company for the native invoice source reader.
    /// The read is untracked and creates no financial, stock or consumption record.
    /// </summary>
    Task<StoredApprovedDispatchInvoiceSource?> FindApprovedDispatchInvoiceSourceAsync(Guid dispatchNoteId,
        CancellationToken cancellationToken = default);
}

/// <summary>One stored dispatch note as the Sales order view needs it: the note, its state and numbers, its instruction and only this order's lines.</summary>
public sealed record StoredDispatchNoteReference(
    Guid DispatchNoteId,
    Guid InstructionId,
    DispatchNoteStatus Status,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<StoredDispatchNoteOrderLine> Lines,
    DispatchNoteNumber? Number = null,
    string? EDispatchNumber = null);

/// <summary>One line of a stored draft on the asked order: the order line, the shipment, the shipped quantity and its unit.</summary>
public sealed record StoredDispatchNoteOrderLine(int LineNo, Guid SourceOrderLineId, Guid ShipmentId, Guid UnitId, decimal Quantity);

/// <summary>The exact Logistics-owned facts of one approved, active dispatch note.</summary>
public sealed record StoredApprovedDispatchInvoiceSource(
    Guid CompanyId,
    Guid DispatchNoteId,
    Guid InstructionId,
    long InstructionVersion,
    Guid CustomerPartyId,
    Guid CompanyAccountId,
    DispatchNoteNumber? Number,
    string? EDispatchNumber,
    DateTimeOffset? ApprovedAtUtc,
    IReadOnlyList<StoredApprovedDispatchInvoiceSourceLine> Lines);

public sealed record StoredApprovedDispatchInvoiceSourceLine(
    int LineNo,
    Guid SourceOrderId,
    Guid SourceOrderLineId,
    Guid ShipmentId,
    Guid ProductId,
    Guid UnitId,
    decimal Quantity);

/// <summary>Optional atomic extension used only when Sales supplies a resolved loading cap.</summary>
public interface ILogisticsAggregateLoadStoreV1
{
    Task<LogisticsStoreResult?> MutateInstructionWithAggregateLoadLimitAsync(LogisticsWriteEnvelope envelope, string requestHash,
        string commandName, Guid instructionId, Action<LoadingInstruction, DateTimeOffset> mutate, LogisticsMutationAudit audit,
        LogisticsOrderLineAggregateLoadLimit aggregateLoadLimit, CancellationToken cancellationToken = default);
}

/// <summary>The canonical content of a command for the replay identity (R3): the strict wire shape, serialised and hashed.</summary>
public static class LogisticsRequestHash
{
    public static string Compute(CreateLoadingInstructionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Hash(JsonSerializer.Serialize(new { command = LogisticsCommands.CreateV1, request }, LogisticsJson.Options));
    }

    public static string Compute(string commandName, Guid instructionId, object request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        ArgumentNullException.ThrowIfNull(request);
        return Hash(JsonSerializer.Serialize(new { command = commandName, instructionId, request }, LogisticsJson.Options));
    }

    private static string Hash(string canonical) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
}
