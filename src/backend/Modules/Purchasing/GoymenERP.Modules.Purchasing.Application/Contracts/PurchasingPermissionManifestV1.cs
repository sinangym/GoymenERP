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

    public static readonly IReadOnlyList<string> Names = [OrdersCreate, OrdersRead, OrdersApprove];
}
