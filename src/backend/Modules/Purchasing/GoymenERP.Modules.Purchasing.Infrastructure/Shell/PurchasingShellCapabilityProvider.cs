using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Shell;

/// <summary>
/// Declares the purchase-order screens to the shell, on master v3 §27.3's table: P-03 "Satın Alma
/// Siparişleri" is the list, P-04 "Yeni/Düzenle Satın Alma Siparişi" the new-order workspace, and the detail
/// is the read view of one P-03 row ("P-03-DETAIL", the F-02-DETAIL precedent; the master has no separate
/// detail id). P-01 (Talep Havuzu) is not declared: requests have no screen in PUR-4a. Approving an order is a
/// server-measured command on the detail, not a screen; its control is declared with the frontend slice.
/// The list and detail are published by PUR-4a-FE; creation is published with P-04 after the Product owner
/// supplied the purchase-eligibility candidate source and the form bound the existing Cari supplier read.
/// Approval is a child control of the detail, measured with
/// its own existing permission. A declaration grants nothing: the screens open only for an actor holding the
/// permission, and the grant is a human gate. The current HTTP reads require an explicit company id; therefore the
/// published list, detail and approval control are single-company capabilities. Authorized group reads need their
/// own backend predecessor before this metadata can widen. The list and the detail share
/// <see cref="PurchasingPermissionManifestV1.OrdersRead"/>; the new-order screen needs
/// <see cref="PurchasingPermissionManifestV1.OrdersCreate"/>. The host registers this provider only after the
/// central catalogue knows the names, together with its accepted revision.
/// </summary>
public sealed class PurchasingShellCapabilityProvider : IShellCapabilityProvider
{
    public const string OwnerId = "PURCHASING";
    public const string OwnerContractVersion = "1.2";

    public const string ListCapabilityId = "purchasing.orders.list";
    public const string ListRoutePath = "/satinalma/siparisler";
    public const string ListScreenId = "P-03";
    public const string CreateCapabilityId = "purchasing.orders.create";
    public const string CreateRoutePath = "/satinalma/siparisler/yeni";
    public const string CreateScreenId = "P-04";
    public const string DetailCapabilityId = "purchasing.orders.detail";
    public const string ApproveCapabilityId = "purchasing.orders.approve";
    public const string DetailRoutePath = "/satinalma/siparisler/:orderId";
    public const string DetailScreenId = "P-03-DETAIL";

    public string Owner => OwnerId;

    public string ContractVersion => OwnerContractVersion;

    public IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations()
    {
        var detail = new ShellCapabilityDeclaration(DetailCapabilityId, DetailCapabilityId, DetailRoutePath, DetailScreenId,
            ShellCapabilityRoutePrecedence.ParameterizedAfterStatic, ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.CompanyWide, [PurchasingPermissionManifestV1.OrdersRead], ImplementationAvailable: true);

        return
        [
            new ShellCapabilityDeclaration(ListCapabilityId, ListCapabilityId, ListRoutePath, ListScreenId,
                ShellCapabilityRoutePrecedence.Static, ShellCapabilityContextRequirement.SingleCompany,
                ShellCapabilityScopeRequirement.CompanyWide, [PurchasingPermissionManifestV1.OrdersRead], ImplementationAvailable: true),
            new ShellCapabilityDeclaration(CreateCapabilityId, CreateCapabilityId, CreateRoutePath, CreateScreenId,
                ShellCapabilityRoutePrecedence.Static, ShellCapabilityContextRequirement.SingleCompany,
                ShellCapabilityScopeRequirement.CompanyWide, [PurchasingPermissionManifestV1.OrdersCreate], ImplementationAvailable: true),
            detail,
            detail with
            {
                CapabilityId = ApproveCapabilityId,
                ParentCapabilityId = DetailCapabilityId,
                RequiredPermissions = [PurchasingPermissionManifestV1.OrdersRead, PurchasingPermissionManifestV1.OrdersApprove],
            },
        ];
    }
}
