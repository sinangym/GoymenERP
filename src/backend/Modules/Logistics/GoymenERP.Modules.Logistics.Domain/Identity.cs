namespace GoymenERP.Modules.Logistics.Domain;

internal static class Identity
{
    public static Guid Require(Guid value, string parameterName) =>
        value == Guid.Empty ? throw new ArgumentException("Kimlik boş olamaz.", parameterName) : value;
}

/// <summary>The caller's expected version is not the current one; the record changed underneath it.</summary>
public sealed class LogisticsVersionConflictException(long expected, long current)
    : Exception("Sevkiyat kaydının sürümü değişti.")
{
    public long ExpectedVersion { get; } = expected;
    public long CurrentVersion { get; } = current;
}

/// <summary>
/// A logistics rule refused the operation. <see cref="Code"/> is stable and machine-readable so a
/// test or a later application layer can tell WHICH rule held, never only that some rule did.
/// Nothing is partly applied when this is thrown.
/// </summary>
public sealed class LogisticsRuleException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

/// <summary>The stable refusal codes of the Logistics domain.</summary>
public static class LogisticsRuleCodes
{
    public const string InstructionNotDraft = "logistics_instruction_not_draft";
    public const string InstructionNotIssued = "logistics_instruction_not_issued";

    // F-020 instruction cancel: a cancelled instruction takes no further command; a recorded shipment makes the cancel final-refused.
    public const string InstructionCancelled = "logistics_instruction_cancelled";
    public const string InstructionShipped = "logistics_instruction_shipped";
    public const string InstructionWithoutLines = "logistics_instruction_without_lines";
    public const string DuplicateLineId = "logistics_duplicate_line_id";
    public const string LineNotFound = "logistics_line_not_found";
    public const string LineBlocked = "logistics_line_blocked";
    public const string DuplicateShareId = "logistics_duplicate_share_id";
    public const string MeasurementRequired = "logistics_measurement_required";
    public const string LoadExceedsInstructed = "logistics_load_exceeds_instructed";
    public const string ShipmentExceedsLoaded = "logistics_shipment_exceeds_loaded";
    public const string ShipmentNotFound = "logistics_shipment_not_found";
    public const string ShipmentOfAnotherLine = "logistics_shipment_of_another_line";
    public const string DeliveryExceedsShipment = "logistics_delivery_exceeds_shipment";
    public const string UnitMismatch = "logistics_unit_mismatch";
    public const string QuantityBelowZero = "logistics_quantity_below_zero";

    // AT-053 manual weighbridge record (one per instruction; its correction rule is not decided, so a second is refused).
    public const string WeighingAlreadyRecorded = "logistics_weighing_already_recorded";
    public const string WeighingDirectionRequired = "logistics_weighing_direction_required";
    public const string WeighingDirectionInconsistent = "logistics_weighing_direction_inconsistent";
    public const string WeighingUnitUnsupported = "logistics_weighing_unit_unsupported";

    // LOJ-D1/D12: shipment kind of an instruction and the weighing order of a subcontract ("fason teslim") delivery.
    public const string ShipmentKindRequired = "logistics_shipment_kind_required";
    public const string ContractProcessingShipmentWithoutWeighing = "logistics_contract_processing_shipment_without_weighing";
    public const string ContractProcessingLoadAfterWeighing = "logistics_contract_processing_load_after_weighing";

    // LOJ-D13 reasoned weighing correction after the vehicle left (append-only; the original stays for the audit trail).
    public const string WeighingNotRecorded = "logistics_weighing_not_recorded";
    public const string WeighingCorrectionBeforeShipment = "logistics_weighing_correction_before_shipment";
    public const string WeighingCorrectionReasonRequired = "logistics_weighing_correction_reason_required";
    public const string DuplicateWeighingCorrectionId = "logistics_duplicate_weighing_correction_id";

    // LOJ-D17 unload weighing after an instruction cancel: only a cancelled instruction with loaded goods takes one, once.
    public const string InstructionNotCancelled = "logistics_instruction_not_cancelled";
    public const string UnloadNothingLoaded = "logistics_unload_nothing_loaded";
    public const string UnloadWeighingAlreadyRecorded = "logistics_unload_weighing_already_recorded";

    // AT-055 dispatch note: a definite shipment is on at most one non-cancelled dispatch note; approve / cancel / e-İrsaliye number.
    public const string ShipmentAlreadyDispatched = "logistics_shipment_already_dispatched";
    public const string DuplicateDispatchNoteId = "logistics_duplicate_dispatch_note_id";
    public const string DispatchNoteNotFound = "logistics_dispatch_note_not_found";
    public const string DispatchNoteState = "logistics_dispatch_note_state";
    public const string EDispatchNumberInUse = "logistics_e_dispatch_number_in_use";

    /// <summary>
    /// A line-level guard fired although the aggregate had already checked the same bound. The
    /// aggregate is the single authority for every share bound; this code marks the second,
    /// defensive check so a test can tell the two apart.
    /// </summary>
    public const string InternalInvariant = "logistics_internal_invariant";
}
