using System.Collections.Frozen;

namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>
/// The Sales owner's technical permission names for the order draft (Business #254 5654539215:
/// "permission technical mapping is the backend owner's"). Names only: nothing here registers,
/// classifies or grants a permission. Registration in the central catalogue (company-wide only,
/// not critical) is the central writer's step; who holds which is a human gate. A draft grants no
/// Product authority: selecting a product still needs the narrow
/// <c>product.operational-selection.read</c> of the Product owner (#276), measured by that owner.
/// </summary>
public static class SalesPermissionManifestV1
{
    public const int Version = 10;
    public const string Subject = "Sales";

    /// <summary>POST /api/v1/sales/drafts, the reference reads of the draft form, and POST …/{id}/submission (SALES-A1: whoever may create a draft may send it for approval).</summary>
    public const string DraftsCreate = "sales.drafts.create";

    /// <summary>GET /api/v1/sales/drafts and GET /api/v1/sales/drafts/{id} (list and detail share one read).</summary>
    public const string DraftsRead = "sales.drafts.read";

    /// <summary>
    /// SALES-A1 (Business 5737989917): POST …/{id}/approval and …/{id}/rejection — the internal
    /// approval decision. Company-wide. Its criticality class is the central writer's registration
    /// decision; the grant is a human gate. No maker≠checker, threshold, role or second person is
    /// implied by the name.
    /// </summary>
    public const string OrdersApprove = "sales.orders.approve";

    /// <summary>
    /// Records customer acceptance of an internally approved order revision. Separate from internal approval,
    /// create and read; registration belongs to Central and the real grant remains a human boundary.
    /// </summary>
    public const string OrdersCustomerAcceptanceRecord = "sales.orders.customer-acceptance.record";

    /// <summary>
    /// K3 (Sinan 5748258299, closes 5745151414): writing a unit price or a discount on an order line —
    /// POST /api/v1/sales/drafts with a line carrying a commercial field. A SEPARATE name: the draft's
    /// create permission does NOT confer it, and this name confers no approval. Company-wide; the grant
    /// (to which persona, in which company) is a human gate, never implied here.
    /// </summary>
    public const string OrdersPriceWrite = "sales.orders.price.write";

    /// <summary>
    /// K3 P2 (Sinan 5748258299): READING a line's unit price, price unit and discount on the draft detail, on every
    /// command response and on the archived revisions — "Sipariş onayının fiyat/tutar bağlamı ilgili dar fiyat
    /// okumasıyla sağlanır; bu maliyet/marj/banka sınırsız okuması değildir." A SEPARATE, NARROW name: the draft
    /// read does NOT confer it (a holder of the draft read alone gets the WITHHELD marker, never the texts), and it
    /// confers nothing adjacent — no cost, margin, bank or Finance read. Company-wide; the grant is a human gate.
    /// </summary>
    public const string OrdersPriceRead = "sales.orders.price.read";

    /// <summary>
    /// DEC-SAL-003 (KILITLI): "termin güncellemesi planlama/sevkiyat rolündedir ve yeniden onay gerektirmez, ancak
    /// auditlidir" — POST …/{id}/delivery-schedule, moving the requested delivery dates of the header, the lines and
    /// their existing slices. A SEPARATE name because the decision gives the update to the planning/shipping role, not
    /// to the draft's preparer or approver: the create, approve and price names do NOT confer it, and it confers none of
    /// them (no approval, no price, no line or quantity change). The technical literal is the Sales owner's mapping
    /// (Business #66 5808295853 / 5811361336: the owner names the literal); company-wide; registration and class are the
    /// central writer's step; which persona holds it is a human gate.
    /// </summary>
    public const string OrdersDeliveryScheduleWrite = "sales.orders.delivery-schedule.write";

    /// <summary>
    /// DEC-SAL-005 (KILITLI): cancelling an APPROVED order — GET …/{id}/cancellation-impact (the impact preview) and POST
    /// …/{id}/cancellation. A SEPARATE name because no source says who may cancel: the create, approve, price and schedule
    /// names do NOT confer it and it confers none of them. Holding it grants no second-person approval, release or reversal
    /// of any other module's record. Company-wide; registration and its class (Critical = grant-time dual control, if Sinan
    /// wants a cancellation to need two people) are the central writer's step; which persona holds it is a human gate.
    /// </summary>
    public const string OrdersCancel = "sales.orders.cancel";

    /// <summary>
    /// DEC-SAL-002 (KILITLI): "Cari, ürün, miktar, fiyat, iskonto, para, vade ve risk istisnası değişirse yeni revizyon ve
    /// yeniden onay gerekir." — POST …/{id}/quantity-revision, replacing the ordered quantity of an order's lines (an approved
    /// order is archived and re-enters approval). A SEPARATE name because no source says who may change an approved order's
    /// quantity: the create, approve, price, schedule and cancel names do NOT confer it and it confers none of them (no
    /// approval — the new revision is approved through <see cref="OrdersApprove"/> as any other). Company-wide; registration
    /// and class are the central writer's step; which persona holds it is a human gate (NEEDS-SINAN).
    /// </summary>
    public const string OrdersQuantityWrite = "sales.orders.quantity.write";

    public static IReadOnlySet<string> Names { get; } =
        new[] { DraftsCreate, DraftsRead, OrdersApprove, OrdersCustomerAcceptanceRecord, OrdersPriceWrite, OrdersPriceRead, OrdersDeliveryScheduleWrite, OrdersCancel, OrdersQuantityWrite }
            .ToFrozenSet(StringComparer.Ordinal);
}
