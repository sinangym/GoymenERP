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
    /// MFG-PLAN-EDIT: a plan's process never changes; a revision may move it only to a work center of its own process.
    /// Process chains are open (OPEN-005/007), so no cross-process move is invented.
    /// </summary>
    public const string PlanProcessFixed = "manufacturing_plan_process_fixed";

    /// <summary>
    /// MFG-PLAN-EDIT: a plan that carries a slitting spec is not revised: how the spec balances against the parent
    /// coil is an open Business question (AT-044, #66 NEEDS-BUSINESS 5741547443).
    /// </summary>
    public const string PlanSlittingSpecFixed = "manufacturing_plan_slitting_spec_fixed";

    /// <summary>MFG-PLAN-EDIT: the revision names exactly the plan's current content; nothing is recorded.</summary>
    public const string PlanRevisionNoChange = "manufacturing_plan_revision_no_change";

    /// <summary>
    /// Üretim U2-a (URT-D1, "tek plan = tek emir"): the approved plan already opened its one order; a second order for the
    /// same plan is refused. The order it opened stands.
    /// </summary>
    public const string PlanOrderExists = "manufacturing_plan_order_exists";

    /// <summary>
    /// Üretim U2-a: a plan that opened an order is not cancelled here; its order carries the downstream effect (an order's
    /// own cancellation / reversal chain is a later slice).
    /// </summary>
    public const string PlanHasOrder = "manufacturing_plan_has_order";

    /// <summary>Üretim U2-a: the plan is cancelled; it is kept (never deleted) and takes no further decision.</summary>
    public const string PlanCancelled = "manufacturing_plan_cancelled";

    /// <summary>
    /// Üretim U2-b / API-16 (SAT-D219): the plan's sales order link moved since the caller read it (its link revision
    /// differs); the caller reads the plan again. Answered as a conflict.
    /// </summary>
    public const string PlanSalesOrderLinkChanged = "manufacturing_plan_sales_order_link_changed";

    /// <summary>Üretim U2-b / API-16: the plan's order is closed; its sales order link no longer changes.</summary>
    public const string PlanSalesOrderLinkOrderClosed = "manufacturing_plan_sales_order_link_order_closed";

    /// <summary>Üretim U2-b / API-16: the request links the line the plan is already linked to (or unlinks an unlinked plan).</summary>
    public const string PlanSalesOrderLinkNoChange = "manufacturing_plan_sales_order_link_no_change";

    /// <summary>
    /// Üretim F-010a (URT-D29): the plan's termin moved since the caller read it (its termin revision differs); the caller
    /// reads the plan again. Answered as a conflict.
    /// </summary>
    public const string PlanDueDateChanged = "manufacturing_plan_due_date_changed";

    /// <summary>Üretim F-010a (URT-D29): the plan's order is closed; its termin no longer changes.</summary>
    public const string PlanDueDateOrderClosed = "manufacturing_plan_due_date_order_closed";

    /// <summary>Üretim F-010a (URT-D29): the request sets the termin the plan already has (or removes an absent one).</summary>
    public const string PlanDueDateNoChange = "manufacturing_plan_due_date_no_change";

    /// <summary>
    /// URT-D66 (1): the termin is earlier than the company's business day (the Europe/Istanbul date of the server's clock).
    /// Held on the plan and on the production order alike.
    /// </summary>
    public const string DueDateInPast = "manufacturing_due_date_in_past";

    /// <summary>
    /// URT-D66 (2): a plan without a termin is not approved; an approved plan's termin is changed but not removed.
    /// </summary>
    public const string PlanDueDateRequired = "manufacturing_plan_due_date_required";

    /// <summary>
    /// URT-D66 (3): the order's termin moved since the caller read it (its termin revision differs); the caller reads the
    /// order again. Answered as a conflict.
    /// </summary>
    public const string OrderDueDateChanged = "manufacturing_order_due_date_changed";

    /// <summary>URT-D66 (3): the order is closed; its termin no longer changes.</summary>
    public const string OrderDueDateOrderClosed = "manufacturing_order_due_date_order_closed";

    /// <summary>URT-D66 (3): the request sets the termin the order already has.</summary>
    public const string OrderDueDateNoChange = "manufacturing_order_due_date_no_change";

    /// <summary>
    /// Üretim U2-b / API-16: the plan's customer-owned inputs belong to a different party than the sales order line's
    /// customer; the link (or a revision under a link) is refused.
    /// </summary>
    public const string PlanSalesOrderPartyMismatch = "manufacturing_plan_sales_order_party_mismatch";

    /// <summary>Üretim U2-b / API-16: the sales order line exists but is not approved (Sales owns the approval).</summary>
    public const string PlanSalesOrderLineNotApproved = "manufacturing_plan_sales_order_line_not_approved";

    /// <summary>Üretim U2-b / API-16: Sales has no such order line for this company.</summary>
    public const string PlanSalesOrderLineNotFound = "manufacturing_plan_sales_order_line_not_found";

    /// <summary>Üretim U2-b / API-16: Sales could not answer for the order line now; nothing was changed (503).</summary>
    public const string PlanSalesOrderLineUnavailable = "manufacturing_plan_sales_order_line_unavailable";

    /// <summary>
    /// Üretim U5-b / API-36 (URT-D16, URT-D22): the plan processes a customer's goods, so it is linked only to an open,
    /// approved subcontract service order of that same customer (party and account) whose usable basis carries the line.
    /// A normal order, an unapproved or closed order, another customer or a line outside the basis is refused (422).
    /// </summary>
    public const string PlanCustomerGoodsRequireSubcontractOrder =
        "manufacturing_plan_customer_goods_require_subcontract_order";

    /// <summary>
    /// Üretim U2-d (URT-D2): the user who prepared the plan's current version (saved it, or recorded its last revision)
    /// may not approve or reject it; another user decides.
    /// </summary>
    public const string PlanSelfApproval = "manufacturing_plan_self_approval";

    /// <summary>
    /// Üretim U2-a: the net usable width entered on the slitting spec is wider than a planned parent coil's width (the
    /// Product snapshot captured for that roll). Refused, never cut to the coil.
    /// </summary>
    public const string PlanNetWidthExceedsParent = "manufacturing_plan_net_width_exceeds_parent";

    /// <summary>F-009 D2: the output a reversal names was never declared on this order.</summary>
    public const string OutputNotDeclared = "manufacturing_output_not_declared";

    /// <summary>
    /// F-009 D2 (URT-D30, analysis §3): the output declaration carries no plan output line ("satırsız", stored before
    /// D2), so the line bound of its correction cannot be derived. Refused fail-closed; no line is assigned to it here.
    /// </summary>
    public const string OutputLineMissing = "manufacturing_output_line_missing";

    /// <summary>
    /// F-009 D2 (URT-D30, ASSUMPTION-URT-F009-1): the plan output line a declaration names is not one of the order's
    /// plan output lines, or does not fit the output class (only a good product binds to a plan line; every other
    /// class, and a good product of a plan without output lines, binds to its class line).
    /// </summary>
    public const string OutputLineNotInPlan = "manufacturing_output_line_not_in_plan";

    /// <summary>F-009 D2: this output reversal id was already recorded.</summary>
    public const string OutputReversalAlreadyRecorded = "manufacturing_output_reversal_already_recorded";

    /// <summary>
    /// F-009 D2 (analysis §4.3): the reversals of one output declaration would add up to more than the declared mass;
    /// its net output never goes below zero.
    /// </summary>
    public const string OutputReversalExceedsDeclared = "manufacturing_output_reversal_exceeds_declared";

    /// <summary>
    /// URT-S13 (URT-D47, URT-D65): the company already has a scrap price for this month. A recorded month is never changed
    /// or replaced here (correcting it is not decided); the answer is a conflict.
    /// </summary>
    public const string ScrapPriceAlreadyRecorded = "manufacturing_scrap_price_already_recorded";

    /// <summary>URT-S13: a scrap price carries at most four decimals (its decimal(18,4) column); a fifth is refused, not rounded.</summary>
    public const string ScrapPricePrecisionExceeded = "manufacturing_scrap_price_precision_exceeded";

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
