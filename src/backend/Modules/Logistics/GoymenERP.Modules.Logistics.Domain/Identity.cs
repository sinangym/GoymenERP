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

    // AT-055 dispatch-note draft: a definite shipment is linked to at most one dispatch note.
    public const string ShipmentAlreadyDispatched = "logistics_shipment_already_dispatched";
    public const string DuplicateDispatchNoteId = "logistics_duplicate_dispatch_note_id";

    /// <summary>
    /// A line-level guard fired although the aggregate had already checked the same bound. The
    /// aggregate is the single authority for every share bound; this code marks the second,
    /// defensive check so a test can tell the two apart.
    /// </summary>
    public const string InternalInvariant = "logistics_internal_invariant";
}
