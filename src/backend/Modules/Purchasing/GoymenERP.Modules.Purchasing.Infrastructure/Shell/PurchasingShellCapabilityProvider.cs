using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Shell;

/// <summary>
/// Declares the purchase-request and purchase-order screens to the shell, on master v3 §27.3's table: P-01
/// "Satın Alma Talepleri / İhtiyaç Havuzu" is the request list/create/detail workspace; P-03 "Satın Alma
/// Siparişleri" is the list, P-04 "Yeni/Düzenle Satın Alma Siparişi" the new-order workspace, and the detail
/// is the read view of one P-03 row ("P-03-DETAIL", the F-02-DETAIL precedent; the master has no separate
/// detail id). Submitting a request and approving an order are server-measured controls on their details.
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
    public const string OwnerContractVersion = "1.4";

    public const string RequestListCapabilityId = "purchasing.requests.list";
    public const string RequestListRoutePath = "/satinalma/talepler";
    public const string RequestListScreenId = "P-01";
    public const string RequestCreateCapabilityId = "purchasing.requests.create";
    public const string RequestCreateRoutePath = "/satinalma/talepler/yeni";
    public const string RequestCreateScreenId = "P-01-NEW";
    public const string RequestDetailCapabilityId = "purchasing.requests.detail";
    public const string RequestSubmitCapabilityId = "purchasing.requests.submit";
    public const string RequestDetailRoutePath = "/satinalma/talepler/:requestId";
    public const string RequestDetailScreenId = "P-01-DETAIL";

    public const string ListCapabilityId = "purchasing.orders.list";
    public const string ListRoutePath = "/satinalma/siparisler";
    public const string ListScreenId = "P-03";
    public const string CreateCapabilityId = "purchasing.orders.create";
    public const string CreateRoutePath = "/satinalma/siparisler/yeni";
    public const string CreateScreenId = "P-04";
    public const string DetailCapabilityId = "purchasing.orders.detail";
    public const string ApproveCapabilityId = "purchasing.orders.approve";
    public const string GoodsReceiptCapabilityId = "purchasing.goods-receipts.record";
    public const string DetailRoutePath = "/satinalma/siparisler/:orderId";
    public const string DetailScreenId = "P-03-DETAIL";

    public string Owner => OwnerId;

    public string ContractVersion => OwnerContractVersion;

    public IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations()
    {
        var requestDetail = new ShellCapabilityDeclaration(RequestDetailCapabilityId, RequestDetailCapabilityId,
            RequestDetailRoutePath, RequestDetailScreenId, ShellCapabilityRoutePrecedence.ParameterizedAfterStatic,
            ShellCapabilityContextRequirement.SingleCompany, ShellCapabilityScopeRequirement.CompanyWide,
            [PurchasingPermissionManifestV1.OrdersRead], ImplementationAvailable: true);
        var detail = new ShellCapabilityDeclaration(DetailCapabilityId, DetailCapabilityId, DetailRoutePath, DetailScreenId,
            ShellCapabilityRoutePrecedence.ParameterizedAfterStatic, ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.CompanyWide, [PurchasingPermissionManifestV1.OrdersRead], ImplementationAvailable: true);

        return
        [
            new ShellCapabilityDeclaration(RequestListCapabilityId, RequestListCapabilityId, RequestListRoutePath, RequestListScreenId,
                ShellCapabilityRoutePrecedence.Static, ShellCapabilityContextRequirement.SingleCompany,
                ShellCapabilityScopeRequirement.CompanyWide, [PurchasingPermissionManifestV1.OrdersRead], ImplementationAvailable: true),
            new ShellCapabilityDeclaration(RequestCreateCapabilityId, RequestCreateCapabilityId, RequestCreateRoutePath, RequestCreateScreenId,
                ShellCapabilityRoutePrecedence.Static, ShellCapabilityContextRequirement.SingleCompany,
                ShellCapabilityScopeRequirement.CompanyWide, [PurchasingPermissionManifestV1.OrdersCreate], ImplementationAvailable: true),
            requestDetail,
            requestDetail with
            {
                CapabilityId = RequestSubmitCapabilityId,
                ParentCapabilityId = RequestDetailCapabilityId,
                RequiredPermissions = [PurchasingPermissionManifestV1.OrdersRead, PurchasingPermissionManifestV1.OrdersCreate],
            },
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
            detail with
            {
                CapabilityId = GoodsReceiptCapabilityId,
                ParentCapabilityId = DetailCapabilityId,
                RequiredPermissions = [PurchasingPermissionManifestV1.OrdersRead, PurchasingPermissionManifestV1.GoodsReceiptsRecord],
            },
        ];
    }
}
