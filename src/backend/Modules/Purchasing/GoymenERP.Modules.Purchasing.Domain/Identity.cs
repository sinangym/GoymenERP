namespace GoymenERP.Modules.Purchasing.Domain;

internal static class Identity
{
    public static Guid Require(Guid value, string parameterName) =>
        value == Guid.Empty ? throw new ArgumentException("Kimlik boş olamaz.", parameterName) : value;
}

/// <summary>The caller's expected version is not the current one; the record changed underneath it.</summary>
public sealed class PurchasingVersionConflictException(long expected, long current)
    : Exception("Satınalma kaydının sürümü değişti.")
{
    public long ExpectedVersion { get; } = expected;
    public long CurrentVersion { get; } = current;
}

/// <summary>
/// A purchasing rule refused the operation. <see cref="Code"/> is stable and machine-readable so
/// a test or a later application layer can tell WHICH rule held, never only that some rule did.
/// Nothing is partly applied when this is thrown.
/// </summary>
public sealed class PurchasingRuleException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

/// <summary>The stable refusal codes of the Purchasing domain.</summary>
public static class PurchasingRuleCodes
{
    public const string RequestNotDraft = "purchasing_request_not_draft";
    public const string RequestNotSubmitted = "purchasing_request_not_submitted";
    public const string RequestWithoutLines = "purchasing_request_without_lines";

    /// <summary>
    /// A1-2b (SAT-D219, SAT-D220; fail-closed): a new request line cannot record a Sales source until MOD-03 validates it through
    /// the Sales order line reference port and wires the linked-supply read adaptor in the same delivery.
    /// </summary>
    public const string RequestSourceSalesUnavailable = "purchasing_request_source_sales_unavailable";

    /// <summary>A1-1 (ALS-D2, ALS-D16): an order is opened only for an APPROVED request.</summary>
    public const string RequestNotApproved = "purchasing_request_not_approved";

    /// <summary>A1-1 (No.161 §1): only a Submitted or Approved request is rejected.</summary>
    public const string RequestNotRejectable = "purchasing_request_not_rejectable";

    /// <summary>A1-1 (ALS-D2): the preparer or the submitter cannot approve or reject the same request (hazırlayan ≠ onaylayan).</summary>
    public const string RequestSameActor = "purchasing_request_same_actor";

    /// <summary>A1-1 (fail-closed): the request's preparer or submitter is not recorded, so maker ≠ checker cannot be measured.</summary>
    public const string RequestMakerUnknown = "purchasing_request_maker_unknown";

    /// <summary>A1-1 (No.161 §1): a rejection needs a reason.</summary>
    public const string RequestRejectionReasonRequired = "purchasing_request_rejection_reason_required";

    /// <summary>A1-1: a rejection reason longer than the stored bound is refused, never cut.</summary>
    public const string RequestRejectionReasonTooLong = "purchasing_request_rejection_reason_too_long";

    /// <summary>A1-1 (No.161 §1): a request with an APPROVED bound order cannot be rejected; the order is cancelled first.</summary>
    public const string RequestHasApprovedOrder = "purchasing_request_has_approved_order";

    /// <summary>A1-1b (ALS-D46): only a Submitted request (no decision yet) is withdrawn.</summary>
    public const string RequestNotWithdrawable = "purchasing_request_not_withdrawable";

    /// <summary>A1-1b (ALS-D46, ALS-D47): only the request's preparer (creator) or submitter withdraws it.</summary>
    public const string RequestNotPreparer = "purchasing_request_not_preparer";

    /// <summary>A1-1b (ALS-D46): a withdrawal needs a reason.</summary>
    public const string RequestWithdrawalReasonRequired = "purchasing_request_withdrawal_reason_required";

    /// <summary>A1-1b: a withdrawal reason longer than the stored bound is refused, never cut.</summary>
    public const string RequestWithdrawalReasonTooLong = "purchasing_request_withdrawal_reason_too_long";

    public const string OrderNotDraft = "purchasing_order_not_draft";
    public const string OrderNotApproved = "purchasing_order_not_approved";
    public const string OrderWithoutLines = "purchasing_order_without_lines";

    /// <summary>A1-3 (No.161 §2): only an APPROVED order's unaccepted remainder is cancelled (a Draft or an already cancelled order is not).</summary>
    public const string OrderNotCancellable = "purchasing_order_not_cancellable";

    /// <summary>A1-3 (fail-closed): every line is fully accepted; a cancellation that would cancel nothing is not recorded.</summary>
    public const string OrderNothingToCancel = "purchasing_order_nothing_to_cancel";

    /// <summary>A1-3 (No.161 §2): a cancellation needs a reason.</summary>
    public const string OrderCancellationReasonRequired = "purchasing_order_cancellation_reason_required";

    /// <summary>A1-3: a cancellation reason longer than the stored bound is refused, never cut.</summary>
    public const string OrderCancellationReasonTooLong = "purchasing_order_cancellation_reason_too_long";

    /// <summary>A1-3: the order's remainder was cancelled; no acceptance share or breakdown is added to it.</summary>
    public const string OrderCancelled = "purchasing_order_cancelled";
    public const string DuplicateLineId = "purchasing_duplicate_line_id";
    public const string LineNotFound = "purchasing_line_not_found";
    public const string TechnicalLineNotBreakable = "purchasing_technical_line_not_breakable";
    public const string SliceExceedsRemaining = "purchasing_slice_exceeds_remaining";
    public const string DuplicateAcceptanceId = "purchasing_duplicate_acceptance_id";
    public const string AcceptanceExceedsRemaining = "purchasing_acceptance_exceeds_remaining";
    public const string UnitMismatch = "purchasing_unit_mismatch";
    public const string QuantityBelowZero = "purchasing_quantity_below_zero";

    /// <summary>PUR-4a: a line bound to a request line on an order that was not opened for a request.</summary>
    public const string SourceLineWithoutRequest = "purchasing_source_line_without_request";

    /// <summary>PUR-4a: the same request line bound twice in one order.</summary>
    public const string DuplicateSourceLine = "purchasing_duplicate_source_line";

    /// <summary>PUR-4a (store, at approval): the approved shares of a request line across orders would exceed its need (Sinan 5738113623 §1).</summary>
    public const string RequestLineOverSourced = "purchasing_request_line_over_sourced";

    /// <summary>
    /// A line-level guard fired although the aggregate had already checked the same bound. The
    /// aggregate is the single authority for every share bound; this code marks the second,
    /// defensive check so a test can tell the two apart.
    /// </summary>
    public const string InternalInvariant = "purchasing_internal_invariant";
}
