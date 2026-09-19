namespace GoymenERP.Modules.Manufacturing.Domain.Common;

/// <summary>
/// A manufacturing rule was broken. <see cref="Code"/> is one of <see cref="ManufacturingRuleCodes"/>:
/// stable and machine-readable, so a test or a later application layer can tell WHICH rule held
/// without parsing the message. Nothing is partly applied when this is thrown. <see cref="Parameters"/>
/// names what the rule found missing or wrong (MFG-6: the unavailable engine parameters), empty otherwise.
/// </summary>
public sealed class ManufacturingRuleException(string code, string message, IReadOnlyList<string>? parameters = null)
    : InvalidOperationException(message)
{
    public string Code { get; } = code;

    public IReadOnlyList<string> Parameters { get; } = parameters ?? [];
}

/// <summary>
/// The stable refusal codes of the Manufacturing domain. Invalid arguments (an empty id, an
/// undefined enum value, a zero or negative mass where a positive one is required) stay
/// <see cref="ArgumentException"/>s: they are caller errors, not rules.
/// </summary>
public static class ManufacturingRuleCodes
{
    public const string KilogramPrecisionExceeded = "manufacturing_kilogram_precision_exceeded";
    public const string PlanWithoutInputs = "manufacturing_plan_without_inputs";
    public const string DuplicateRollInPlan = "manufacturing_duplicate_roll_in_plan";
    public const string PlanNotDraft = "manufacturing_plan_not_draft";
    public const string PlanNotApproved = "manufacturing_plan_not_approved";
    public const string OrderClosed = "manufacturing_order_closed";
    public const string RollNotInOrder = "manufacturing_roll_not_in_order";
    public const string RollNotIssued = "manufacturing_roll_not_issued";
    public const string InputAlreadyIssued = "manufacturing_input_already_issued";
    public const string ReturnAlreadyRecorded = "manufacturing_return_already_recorded";
    public const string ReturnExceedsIssued = "manufacturing_return_exceeds_issued";
    public const string OutputAlreadyDeclared = "manufacturing_output_already_declared";
    public const string WipAlreadyMeasured = "manufacturing_wip_already_measured";
    public const string WipMeasurementNotLatest = "manufacturing_wip_measurement_not_latest";

    public const string UnexplainedDifference = "manufacturing_unexplained_difference";

    /// <summary>A nominal millimetre carries more than three decimals (Ö2): refused, never rounded.</summary>
    public const string MillimetrePrecisionExceeded = "manufacturing_millimetre_precision_exceeded";

    /// <summary>A slitting spec names no slit line.</summary>
    public const string SlittingSpecWithoutLines = "manufacturing_slitting_spec_without_lines";

    /// <summary>
    /// MFG-6: a plan with a slitting spec is approved only when every engine input is known. Some are not;
    /// <see cref="ManufacturingRuleException.Parameters"/> names each one. Nothing is approved with gaps.
    /// </summary>
    public const string PlanParametersUnavailable = "manufacturing_plan_parameters_unavailable";

    /// <summary>
    /// MFG-6: an engine refused the plan's inputs (e.g. the slit lines use more width than the coil has).
    /// <see cref="ManufacturingRuleException.Parameters"/> holds the engine's code and the parameter it named.
    /// </summary>
    public const string PlanEngineRefused = "manufacturing_plan_engine_refused";

    /// <summary>
    /// MFG-6: the winding fit computed and failed (WND-008 the band cannot reach its target length, or
    /// WND-009 the coil exceeds the line's maximum outer diameter). <see cref="ManufacturingRuleException.Parameters"/>
    /// names the failed check. Fail-closed: an authorized exception path, if any, is a Business decision.
    /// </summary>
    public const string PlanWindingDoesNotFit = "manufacturing_plan_winding_does_not_fit";

    /// <summary>MFG-6: the label source says a planned parent roll's label cannot be used for this plan.</summary>
    public const string PlanLabelNotUsable = "manufacturing_plan_label_not_usable";

    /// <summary>
    /// A stored plan or order does not rebuild under the rules that wrote it (MFG-5): a close trace
    /// with only one of its two parts, a version that is not the one its facts add up to, or a
    /// stored cost the domain cannot have produced. The record is refused, never repaired.
    /// </summary>
    public const string StoredRecordInvalid = "manufacturing_stored_record_invalid";

    /// <summary>Raised as <see cref="ManufacturingVersionConflictException"/>, not as a rule refusal.</summary>
    public const string VersionConflict = "manufacturing_version_conflict";
}

/// <summary>
/// The caller's expected version is not the record's current one: it changed underneath the
/// caller (MFG-3, SİPARİŞ #292 P3-a; the Purchasing and Logistics pattern). Nothing is applied.
/// <see cref="Code"/> is <see cref="ManufacturingRuleCodes.VersionConflict"/>.
/// </summary>
public sealed class ManufacturingVersionConflictException(long expected, long current)
    : Exception("Üretim kaydının sürümü değişti.")
{
    public string Code => ManufacturingRuleCodes.VersionConflict;

    public long ExpectedVersion { get; } = expected;

    public long CurrentVersion { get; } = current;
}
