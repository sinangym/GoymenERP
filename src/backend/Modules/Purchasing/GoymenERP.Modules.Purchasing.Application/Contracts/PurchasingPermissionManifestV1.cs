namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>
/// The Purchasing permission names of PUR-4a. Company-wide, not critical; the central catalogue
/// (<c>ErpPermissions</c>) registers them with the central writer's step, and any grant is a human gate.
/// Two personas (Sinan §4): the one who enters requests and orders, and the one who approves an order;
/// approval is a separate name so the two are never one grant. The service measures them afresh at every
/// fence; an unregistered name is a 503 setup problem, never a grant.
/// <para>
/// ASSUMPTION-PUR4-1: purchase requests (the demand side) are created, submitted and read under the SAME
/// names as orders (<see cref="OrdersCreate"/> / <see cref="OrdersRead"/>) — the same persona enters both;
/// no fourth name is invented. Reversible: a later slice may split them.
/// </para>
/// </summary>
public static class PurchasingPermissionManifestV1
{
    /// <summary>Open a purchase request (Draft with lines) and submit it; open a purchase order (Draft with lines).</summary>
    public const string OrdersCreate = "purchasing.orders.create";

    /// <summary>List and read purchase requests and orders of the context company.</summary>
    public const string OrdersRead = "purchasing.orders.read";

    /// <summary>Approve a Draft purchase order (Draft → Approved); the commitment becomes expected supply.</summary>
    public const string OrdersApprove = "purchasing.orders.approve";

    /// <summary>Record one physical goods receipt against an approved purchase-order line.</summary>
    public const string GoodsReceiptsRecord = "purchasing.goods-receipts.record";

    /// <summary>
    /// A1-1 (ALS-D2, plan API-01): approve a Submitted purchase request or reject a Submitted/Approved one (No.161 §1) —
    /// the purchase manager's decision, apart from entering (<see cref="OrdersCreate"/>) and from order approval
    /// (<see cref="OrdersApprove"/>); neither confers the other. Company-wide, not critical; maker ≠ checker is the
    /// service's rule on the command, not this name's. Registered only: the grant is a human gate.
    /// </summary>
    public const string RequestsApprove = "purchasing.requests.approve";

    /// <summary>
    /// A1-1b (ALS-D46, ALS-D47): the preparer or submitter withdraws their Submitted purchase request with a reason — apart from entering
    /// (<see cref="OrdersCreate"/>) and from the manager's decision (<see cref="RequestsApprove"/>); neither confers the
    /// other. "Only the preparer or the submitter" is the service's rule on the command, not this name's. Company-wide, not critical.
    /// Registered only: the grant is a human gate (only SENTETIK browser-suite personas in disposable stacks hold it).
    /// </summary>
    public const string RequestsWithdraw = "purchasing.requests.withdraw";

    /// <summary>
    /// A1-3 (plan API-10; No.161 §2): cancel the whole unaccepted remainder of an APPROVED order with a reason, and read the
    /// cancellation's impact preview — apart from entering, approving and receiving; none confers another. Company-wide, not
    /// critical. Registered only: the grant is a human gate (only SENTETIK browser-suite personas in disposable stacks hold it).
    /// ALS-D66 (EK10): it is proposed to the approvers of ALS-D53 (Satın Alma Müdürü, Genel Müdür, Admin), not to the Satın Alma
    /// Uzmanı; ALS-D67: a single step with a reason and an audit record, no second approval.
    /// </summary>
    public const string OrdersCancel = "purchasing.orders.cancel";

    /// <summary>
    /// A2a-1 (PRS-02; plan §2.5 "Fiyatsız görünüm: Depo/kabul personeli"): see an order's prices — unit price, price unit, discount,
    /// net unit price and line amount. It governs DISCLOSURE, not reachability: <see cref="OrdersRead"/> still shows the order, and
    /// without this name every price field is null beside <c>PriceDisclosure: Withheld</c> (the Sales
    /// <c>sales.orders.price.read</c> pattern; persona card: the Sevkiyatçı "fiyatları göremez"). Writing a price needs no separate
    /// name: it is measured with <see cref="OrdersCreate"/> (ALS-D60 yorum). Holding create does not confer this name. Company-wide,
    /// not critical. Registered only. Sinan (#66 6704170096) named its holders: Satın Alma Uzmanı, Satın Alma Müdürü, Genel Müdür,
    /// Admin, Muhasebe, Muhasebe Müdürü, Satış Destek and Satış Uzmanı — not the Sevkiyatçı. That is a record for the persona card;
    /// the grant stays a human gate, so no role, seed or user receives it here.
    /// </summary>
    public const string OrdersPriceRead = "purchasing.orders.price.read";

    public static readonly IReadOnlyList<string> Names =
        [OrdersCreate, OrdersRead, OrdersApprove, GoodsReceiptsRecord, RequestsApprove, RequestsWithdraw, OrdersCancel, OrdersPriceRead];
}
