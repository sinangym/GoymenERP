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

    // Realization (DURUM_MAKINELERI "Gerçekleşme: Başlamadı / Kısmi / Tamamlandı" + the two cases it does not name).
    public const string NotStarted = "NotStarted";
    public const string Partial = "Partial";
    public const string Completed = "Completed";
    public const string OutsideTolerance = "OutsideTolerance";
    public const string ClosedWithRemainder = "ClosedWithRemainder";
}

/// <summary>An axis that carries only its state (lifecycle, internal approval, and the three "NoSource" axes).</summary>
public sealed record SalesDraftStatusAxisV1(string State);

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
/// One order line on the realization axis, in the line's order unit, exact (Ö2). <c>shipped</c> / <c>delivered</c> are the
/// delivery owner's recorded totals (Logistics, AT-024 port), null when the owner is unreadable. <c>direction</c>
/// (<c>None</c>/<c>Under</c>/<c>Over</c>) and <c>deviation</c> (|shipped − ordered|) are null when unreadable; <c>band</c>
/// (<c>Within</c>/<c>Warn</c>/<c>Approval</c>/<c>Block</c>) is null when not measured (nothing shipped on an open line, an open
/// under-delivered line outside the tolerance — SAT-D16 — or no tolerance). <c>closedRemainder</c> is the recorded force-closure
/// remainder (No 30), null otherwise. <c>warning</c> is true exactly when the line counts <c>Completed</c> in spite of a deviation
/// in the warn band (SAT-D24: an over-delivery between the warn and the approval percent is "Tamamlandı" with a warning).
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
    string? ClosedRemainder);

/// <summary>
/// The realization axis: its state, whether any line carries the SAT-D24 warning (a <c>Completed</c> line in the warn band —
/// the axis may then read <c>Completed</c> with <c>warning</c> true), the tolerance it was judged with (null when none was needed
/// or none is available) and every order line once, in line order.
/// </summary>
public sealed record SalesDraftRealizationAxisV1(
    string State,
    bool Warning,
    SalesDraftDeliveryToleranceV1? Tolerance,
    IReadOnlyList<SalesDraftLineRealizationV1> Lines);

/// <summary>
/// DEC-SAL-001 / API-08: GET …/{id}/status — the seven axes, read afresh on every request under the draft read; nothing is
/// written or cached. <c>draftVersion</c> is the concurrency version the axes were read against; <c>revision</c> is the
/// commercial revision ("Rev.N").
/// </summary>
public sealed record SalesOrderDraftStatusResponse(
    string DraftVersion,
    string Revision,
    SalesDraftStatusAxisV1 Lifecycle,
    SalesDraftStatusAxisV1 InternalApproval,
    SalesDraftCustomerAcceptanceAxisV1 CustomerAcceptance,
    SalesDraftStatusAxisV1 Source,
    SalesDraftRealizationAxisV1 Realization,
    SalesDraftStatusAxisV1 Invoicing,
    SalesDraftStatusAxisV1 FinancialClosure);
