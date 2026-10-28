namespace GoymenERP.Modules.Sales.Application.Contracts;

// ---- DEC-SAL-001 / API-08: the order's seven status axes -----------------------------------------------------------------
//
// DEC-SAL-001 (KILITLI, K0, verbatim): "Sipariş yaşam döngüsü, iç onay, kaynak/tahsis, gerçekleşme, faturalama, finansal
// kapanış ve müşteri onayı ayrı durum eksenleridir." — "UI tek özet durum türetebilir." Every axis is answered separately;
// no summary state is derived here. An axis whose owner module does not exist on main is "NoSource" (never complete, never
// zero); an owner that cannot answer is "Unreadable"; a realization without an approved Settings tolerance is
// "PolicyUnavailable" (SAT-D5; the percents are never a code default).

/// <summary>The stable wire names of the axis states (the <c>state</c> member of every axis).</summary>
public static class SalesDraftStatusAxisStatesV1
{
    /// <summary>The axis's owner module does not exist on main yet (source MOD-05/API-11, invoicing S5, financial closure MOD-04).</summary>
    public const string NoSource = "NoSource";

    /// <summary>The owner exists but is not composed, failed or answered a malformed value: no number and no state is shown.</summary>
    public const string Unreadable = "Unreadable";

    /// <summary>SAT-D5: no approved, active Settings delivery tolerance for the company on the order's business date.</summary>
    public const string PolicyUnavailable = "PolicyUnavailable";

    // Customer acceptance (DURUM_MAKINELERI "Müşteri onayı: Bekleniyor / Alındı / Vazgeçildi"; the domain records no
    // declination, so "Vazgeçildi" is never emitted). "NotRequired": an order of before the Q9 cutover.
    public const string NotRequired = "NotRequired";
    public const string Pending = "Pending";
    public const string Accepted = "Accepted";

    // Lifecycle (SAT-D32): the stored "Draft" / "Cancelled" / "Closed", and "Active" derived once the order was approved
    // (DURUM_MAKINELERI "Taslak / Aktif / Kapalı / İptal"). The order is never closed automatically.
    public const string Draft = "Draft";
    public const string Active = "Active";

    // Realization (DURUM_MAKINELERI "Gerçekleşme: Başlamadı / Kısmi / Tamamlandı" + the two cases it does not name).
    public const string NotStarted = "NotStarted";
    public const string Partial = "Partial";
    public const string Completed = "Completed";
    public const string OutsideTolerance = "OutsideTolerance";
    public const string ClosedWithRemainder = "ClosedWithRemainder";

    /// <summary>SAT-D30: an approval-band over-delivery without a recorded over-delivery approval ("onay bekliyor").</summary>
    public const string AwaitingApproval = "AwaitingApproval";

    /// <summary>
    /// A revision awaits re-approval and the last approved revision's quantities cannot be taken exactly (a line it does not
    /// hold, another unit): the realization is not judged against unapproved quantities (fail-closed), no owner is asked.
    /// </summary>
    public const string PendingReapproval = "PendingReapproval";
}

/// <summary>An axis that carries only its state (lifecycle, internal approval, source and financial closure).</summary>
public sealed record SalesDraftStatusAxisV1(string State);

/// <summary>
/// SAT-D103 (EK13): the Sales-owned gate of the order's invoicing — the <c>gate</c> member of the invoicing axis. Invoicing
/// itself (S5 API-32) does not exist yet; the gate is what it must obey.
/// </summary>
public static class SalesDraftInvoicingGatesV1
{
    /// <summary>No approved revision exists (never approved, or withdrawn): nothing may be invoiced.</summary>
    public const string NotApproved = "NotApproved";

    /// <summary>
    /// An approved (and, when required, customer-accepted — SAT-D114) revision is the price basis (<c>priceBasisRevision</c>):
    /// the current revision once approved, or — after the newer revision was REJECTED — the last approved revision ("red
    /// gelirse son onaylı fiyatla faturalanır"). The gate is the approval gate only: the lifecycle (a cancelled or closed
    /// order) and the shipments are the invoicing owner's own preconditions (S5 API-32).
    /// </summary>
    public const string Open = "Open";

    /// <summary>
    /// A newer revision (price, discount, rate or quantity) waits for re-approval: physical shipping goes on under the last
    /// approved revision, but the order's invoicing WAITS until the decision ("faturası onay gelene kadar bekler").
    /// </summary>
    public const string AwaitingReapproval = "AwaitingReapproval";

    /// <summary>
    /// SAT-D114 (EK14, PR #858): customer acceptance is required and the revision invoicing would use is internally approved
    /// but not accepted by the customer — invoicing WAITS (already shipped goods included; "approval" in SAT-D103 means the
    /// internal approval AND the customer acceptance when it is required). No price basis.
    /// </summary>
    public const string AwaitingCustomerAcceptance = "AwaitingCustomerAcceptance";
}

/// <summary>
/// The invoicing axis: <c>state</c> as every owner-less axis (<c>NoSource</c> until the S5 invoicing owner exists — never
/// complete or zero), plus the SAT-D103 <c>gate</c> Sales owns (<see cref="SalesDraftInvoicingGatesV1"/>) and the revision
/// whose approved prices an invoice uses (<c>priceBasisRevision</c>, "Rev.N"; null unless the gate is <c>Open</c>). Contract
/// delta (EK13): the axis was <c>{state}</c>.
/// </summary>
public sealed record SalesDraftInvoicingAxisV1(string State, string Gate, string? PriceBasisRevision);

/// <summary>
/// The customer-acceptance axis: <c>NotRequired</c>, <c>Pending</c> or <c>Accepted</c> (of the CURRENT commercial revision).
/// <c>acceptedRevision</c> is the revision the latest recorded acceptance belongs to ("Rev.N"), null when none was recorded —
/// an acceptance of an earlier revision leaves the axis <c>Pending</c>.
/// </summary>
public sealed record SalesDraftCustomerAcceptanceAxisV1(string State, string? AcceptedRevision);

/// <summary>
/// SAT-D5: the Settings delivery tolerance the realization was judged with — the exact approved profile identity, version,
/// percents (invariant decimal text, trailing zeros dropped) and window, asked for the order's business date
/// (<c>evaluatedOn</c>).
/// </summary>
public sealed record SalesDraftDeliveryToleranceV1(
    Guid ProfileId,
    long ProfileVersion,
    string WarnPercent,
    string ApprovalPercent,
    string BlockPercent,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough,
    DateOnly EvaluatedOn);

/// <summary>
/// One order line on the realization axis, in the line's order unit, exact (Ö2). <c>ordered</c> is the quantity the line is
/// measured against — the axis's <c>basisRevision</c>: the current quantity, or while a revision awaits re-approval the last
/// APPROVED revision's quantity (never the new unapproved one). <c>shipped</c> / <c>delivered</c> are the
/// delivery owner's recorded totals (Logistics, AT-024 port), null when the owner is unreadable. <c>direction</c>
/// (<c>None</c>/<c>Under</c>/<c>Over</c>) and <c>deviation</c> (|shipped − ordered|) are null when unreadable; <c>band</c>
/// (<c>Within</c>/<c>Warn</c>/<c>Approval</c>/<c>Block</c>) is null when not measured. One shape per state: a <c>NotStarted</c>
/// line always carries <c>direction</c> "Under", <c>deviation</c> = <c>ordered</c> and no band; a <c>PolicyUnavailable</c>,
/// <c>Unreadable</c> or <c>PendingReapproval</c> line carries no direction, deviation or band (nothing shipped on an open line, an open
/// under-delivered line outside the tolerance — SAT-D16 — or no tolerance). <c>closedRemainder</c> is the recorded force-closure
/// remainder (No 30), null otherwise. <c>warning</c> is true exactly when the line counts <c>Completed</c> in spite of a deviation
/// in the warn band (SAT-D29: an over-delivery between the warn and the approval percent; SAT-D31: a force-closed shortfall in
/// the warn band). <c>approvedOverDelivery</c> is true when an approval-band over-delivery was approved (SAT-D30: "Tamamlandı +
/// onaylı fazla teslim"); no over-delivery approval record exists yet, so it is false and such a line is
/// <c>AwaitingApproval</c>. <c>approvedReturned</c> is the line's APPROVED customer-return total from the API-10 Logistics
/// port, shown separately and never netted against the shipped total (SAT-D33); null when that owner is not composed or
/// unreadable, "0" only after it answered.
/// </summary>
public sealed record SalesDraftLineRealizationV1(
    Guid LineId,
    int LineNo,
    Guid OrderUnitId,
    string Ordered,
    string? Shipped,
    string? Delivered,
    string State,
    string? Direction,
    string? Deviation,
    string? Band,
    bool Warning,
    bool ApprovedOverDelivery,
    string? ClosedRemainder,
    string? ApprovedReturned);

/// <summary>
/// The realization axis: its state, whether any line carries the SAT-D29 warning (a <c>Completed</c> line in the warn band —
/// the axis may then read <c>Completed</c> with <c>warning</c> true), the revision whose approved quantities the lines were
/// measured against ("Rev.N"; null only for <c>PendingReapproval</c>), the tolerance it was judged with (null when none was
/// needed or none is available) and every order line once, in line order.
/// </summary>
public sealed record SalesDraftRealizationAxisV1(
    string State,
    bool Warning,
    string? BasisRevision,
    SalesDraftDeliveryToleranceV1? Tolerance,
    IReadOnlyList<SalesDraftLineRealizationV1> Lines);

/// <summary>
/// DEC-SAL-001 / API-08: GET …/{id}/status — the seven axes, read afresh on every request under the draft read; nothing is
/// written or cached. <c>draftVersion</c> is the concurrency version the axes were read against; <c>revision</c> is the
/// commercial revision ("Rev.N"). The invoicing axis carries the SAT-D103 gate (<see cref="SalesDraftInvoicingAxisV1"/>). <c>readyToClose</c> (SAT-D32, "kapanmaya hazır") is true only when ALL seven axes are
/// complete: lifecycle <c>Active</c>, internal approval <c>Approved</c>, customer acceptance <c>Accepted</c> or
/// <c>NotRequired</c>, realization <c>Completed</c>, and source, invoicing and financial closure complete. An axis that is
/// <c>NoSource</c>, <c>Unreadable</c> or <c>PolicyUnavailable</c> is never complete, so while those three owners do not exist
/// the flag is always false. It is a flag only: closing stays a user action and nothing closes automatically.
/// </summary>
public sealed record SalesOrderDraftStatusResponse(
    string DraftVersion,
    string Revision,
    SalesDraftStatusAxisV1 Lifecycle,
    SalesDraftStatusAxisV1 InternalApproval,
    SalesDraftCustomerAcceptanceAxisV1 CustomerAcceptance,
    SalesDraftStatusAxisV1 Source,
    SalesDraftRealizationAxisV1 Realization,
    SalesDraftInvoicingAxisV1 Invoicing,
    SalesDraftStatusAxisV1 FinancialClosure,
    bool ReadyToClose);
