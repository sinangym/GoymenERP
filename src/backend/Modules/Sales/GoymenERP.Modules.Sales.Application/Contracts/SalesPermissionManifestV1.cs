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
    public const int Version = 2;
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

    public static IReadOnlySet<string> Names { get; } =
        new[] { DraftsCreate, DraftsRead, OrdersApprove }.ToFrozenSet(StringComparer.Ordinal);
}
