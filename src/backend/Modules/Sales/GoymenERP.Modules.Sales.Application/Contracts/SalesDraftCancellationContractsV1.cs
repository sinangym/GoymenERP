namespace GoymenERP.Modules.Sales.Application.Contracts;

// ---- DEC-SAL-005 / AT-007: cancellation of an approved order ------------------------------------------------------------
//
// DEC-SAL-005 (KILITLI, K1, verbatim): "Sipariş iptalinde aktif tahsis, bağlı satın alma/üretim, irsaliyesiz yükleme,
// faturalanmamış irsaliye ve eşleşmemiş avans/dekont etkisi gösterilir." — "Etki önizlemesi olmadan iptal yok."
// The preview reads what each OWNER holds for the order through that owner's port (never another module's tables), and a
// category no module on main records is shown as "no source" (never an assumed zero). What BLOCKS a cancellation is not in
// the decision's text: the fail-closed reading is implemented — any recorded effect, or an owner that cannot answer, refuses
// the cancellation and says why; nothing of another module is released or reversed by Sales (README "Cancellation").

/// <summary>
/// DEC-SAL-005 "aktif tahsis": the Sales-declared read the ALLOCATION OWNER answers (Inventory, which records physical
/// allocations against a Sales order line — INV-AL-1). Registered by that owner in its own module; Sales never reads
/// Inventory tables. It carries NO authority (the LOG-4 D6 / AT-024 shape): the Sales caller measures its own name before
/// and after asking. It reads under the caller's server-resolved company only and CREATES NOTHING. Until an owner registers
/// it, the preview shows the source as unreadable and the cancellation is refused (never an assumed "no allocation").
/// </summary>
public interface ISalesOrderAllocationReadPortV1
{
    /// <summary>
    /// The owner's ACTIVE allocations of every line of <paramref name="orderId"/> in the context company, per Sales order
    /// line: how many and the kilograms they still hold. A line the owner holds none for is absent. An unknown order
    /// answers an empty list.
    /// </summary>
    Task<SalesOrderAllocationReadAnswerV1> ReadOrderAllocationsAsync(Guid orderId, CancellationToken cancellationToken = default);
}

/// <summary>The allocation owner's answer for one Sales order in the context company.</summary>
public sealed record SalesOrderAllocationReadAnswerV1(Guid CompanyId, Guid OrderId, IReadOnlyList<SalesOrderLineAllocationV1> Lines);

/// <summary>One Sales order line's ACTIVE allocations: their count and the exact kilograms they still hold (Ö2, never rounded).</summary>
public sealed record SalesOrderLineAllocationV1(Guid OrderLineId, int ActiveCount, decimal ActiveRemainingKg);

/// <summary>
/// DEC-SAL-005 "irsaliyesiz yükleme": the Sales-declared read the LOADING OWNER answers (Logistics, whose loading-instruction
/// lines are bound to a Sales order line — LOG-4 D4). The AT-024 <see cref="ISalesOrderDeliveryReadPortV1"/> answers only
/// the definite shipped/delivered totals by design (an instructed or loaded line "adds nothing" there); a cancellation must
/// also see a line that is only instructed or loaded, so this read answers every instruction line's totals. Main has no
/// dispatch-note (irsaliye) record, so EVERY loading is "without a dispatch note". Same rules as the AT-024 port: no
/// authority, the caller's company only, creates nothing.
/// </summary>
public interface ISalesOrderLoadingReadPortV1
{
    /// <summary>
    /// The owner's loading-instruction lines of every line of <paramref name="orderId"/> in the context company, per Sales
    /// order line and unit: how many instruction lines, and their instructed, loaded, shipped and delivered totals. A line
    /// the owner holds nothing for is absent. An unknown order answers an empty list.
    /// </summary>
    Task<SalesOrderLoadingReadAnswerV1> ReadOrderLoadingAsync(Guid orderId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The Logistics owner's answer for one Sales order in the context company. Besides loading totals it carries the number
/// of approved, non-cancelled dispatch-note invoice-source candidates. Main has no accepted native invoice-consumption
/// owner yet, so those candidates remain blockers rather than being silently treated as invoiced.
/// </summary>
public sealed record SalesOrderLoadingReadAnswerV1(
    Guid CompanyId,
    Guid OrderId,
    IReadOnlyList<SalesOrderLineLoadingV1> Lines,
    int ActiveApprovedDispatchNoteCount = 0);

/// <summary>
/// One Sales order line's loading-instruction lines in one unit: their count and exact totals. A positive count has a
/// positive instructed total (Logistics rejects zero-quantity instruction lines); zero count has zero totals. The full
/// owner chain is delivered ≤ shipped ≤ loaded ≤ instructed ≤ the Sales-owned approved order quantity.
/// </summary>
public sealed record SalesOrderLineLoadingV1(Guid OrderLineId, Guid UnitId, int InstructionLineCount, decimal Instructed,
    decimal Loaded, decimal Shipped, decimal Delivered);

/// <summary>The six DEC-SAL-005 impact categories, one stable name each (the wire's <c>source</c>).</summary>
public static class SalesCancellationImpactSourcesV1
{
    /// <summary>"aktif tahsis" — Inventory's active physical allocations (owner port <see cref="ISalesOrderAllocationReadPortV1"/>).</summary>
    public const string ActiveAllocations = "inventory.active-allocations";

    /// <summary>"bağlı satın alma" — no module on main links a purchase order to a Sales order line: no source.</summary>
    public const string LinkedPurchasing = "purchasing.linked-orders";

    /// <summary>"bağlı üretim" — no module on main links a production plan/order to a Sales order line: no source.</summary>
    public const string LinkedProduction = "manufacturing.linked-orders";

    /// <summary>"irsaliyesiz yükleme" — Logistics's loading-instruction lines (owner port <see cref="ISalesOrderLoadingReadPortV1"/>).</summary>
    public const string LoadingWithoutDispatchNote = "logistics.loading-without-dispatch-note";

    /// <summary>
    /// "faturalanmamış irsaliye" — Logistics's active approved dispatch-note invoice-source candidates. Main has no
    /// accepted native invoice-consumption owner yet, so an approved active candidate is not silently treated as invoiced.
    /// </summary>
    public const string UninvoicedDispatchNotes = "invoicing.uninvoiced-dispatch-notes";

    /// <summary>"eşleşmemiş avans/dekont" — Finance records no link from an advance/receipt to a Sales order on main: no source.</summary>
    public const string UnmatchedAdvances = "finance.unmatched-advances";

    /// <summary>The preview's order of the categories: the decision's own order.</summary>
    public static IReadOnlyList<string> All { get; } =
        [ActiveAllocations, LinkedPurchasing, LinkedProduction, LoadingWithoutDispatchNote, UninvoicedDispatchNotes, UnmatchedAdvances];
}

/// <summary>What the preview knows of one category.</summary>
public static class SalesCancellationImpactStatusesV1
{
    /// <summary>The owner answered: nothing is recorded for this order.</summary>
    public const string None = "None";

    /// <summary>The owner answered: records exist for this order. Blocks the cancellation (fail-closed reading).</summary>
    public const string Present = "Present";

    /// <summary>The owner exists on main but is not composed, failed or answered a malformed value. Blocks; never assumed zero.</summary>
    public const string Unreadable = "Unreadable";

    /// <summary>
    /// No module on main records this kind of link to a Sales order, so there is no owner to ask. Shown as "no source" — not
    /// as zero. It does not block: no record of this kind can exist today. When an owner publishes such a link, its port
    /// joins the preview and this category is read like the others (README "Cancellation", open decision).
    /// </summary>
    public const string NoSource = "NoSource";
}

/// <summary>One category of the preview: its stable source name and its status.</summary>
public sealed record SalesDraftCancellationImpactSourceV1(string Source, string Status);

/// <summary>
/// One order line in the preview. The allocation members come from Inventory (<c>activeAllocations</c>, <c>allocatedKg</c>:
/// the kilograms the active allocations still hold), the loading members from Logistics (the number of instruction lines and
/// their instructed / loaded / shipped / delivered totals in the line's order unit). A member is NULL when its owner could
/// not be read — never zero in its place.
/// </summary>
public sealed record SalesDraftCancellationImpactLineV1(
    Guid LineId,
    int LineNo,
    Guid OrderUnitId,
    string Ordered,
    int? ActiveAllocations,
    string? AllocatedKg,
    int? LoadingInstructionLines,
    string? Instructed,
    string? Loaded,
    string? Shipped,
    string? Delivered);

/// <summary>
/// DEC-SAL-005: GET …/{id}/cancellation-impact — the impact preview under the cancel permission, read afresh from the owners on
/// every request. <c>cancellable</c> is true only when the order is approved and not cancelled and no category is
/// <see cref="SalesCancellationImpactStatusesV1.Present"/> or <see cref="SalesCancellationImpactStatusesV1.Unreadable"/>.
/// <c>impactToken</c> identifies exactly this preview (draft, version, every category and line); the cancel command must carry
/// it, so an order is never cancelled on a preview the user did not see.
/// </summary>
public sealed record SalesOrderDraftCancellationImpactResponse(
    string DraftVersion,
    string Revision,
    bool Cancellable,
    string ImpactToken,
    IReadOnlyList<SalesDraftCancellationImpactSourceV1> Sources,
    IReadOnlyList<SalesDraftCancellationImpactLineV1> Lines);

/// <summary>
/// DEC-SAL-005: POST …/{id}/cancellation — the expected version (text), the reason (required, ≤ 4000) and the token of the
/// preview the user confirmed.
/// </summary>
public sealed record SalesDraftCancelRequest(string ExpectedVersion, string Reason, string ImpactToken);

/// <summary>
/// AT-007: the recorded cancellation — the revision cancelled, who, when, why, and the preview it was decided on (its token and
/// each category's status then). Nothing else of the order changed: its approval facts, lines, revisions and schedules are read
/// as before.
/// </summary>
public sealed record SalesDraftCancellationV1(
    string Revision,
    string CancelledBy,
    DateTimeOffset CancelledAtUtc,
    string Reason,
    string ImpactToken,
    IReadOnlyList<SalesDraftCancellationImpactSourceV1> Sources);

/// <summary>GET …/{id}/cancellation — the recorded cancellation, or null while the order is not cancelled (under the draft read).</summary>
public sealed record SalesOrderDraftCancellationResponse(SalesDraftCancellationV1? Cancellation);
