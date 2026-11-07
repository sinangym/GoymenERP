using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Shell;

/// <summary>
/// Declares the purchase-request and purchase-order screens to the shell, on master v3 §27.3's table: P-01
/// "Satın Alma Talepleri / İhtiyaç Havuzu" is the request list/create/detail workspace; P-03 "Satın Alma
/// Siparişleri" is the list, P-04 "Yeni/Düzenle Satın Alma Siparişi" the new-order workspace, and the detail
/// is the read view of one P-03 row ("P-03-DETAIL", the F-02-DETAIL precedent; the master has no separate
/// detail id). Submitting a request, deciding a request (approve / reject, A1-1) and approving an order are
/// server-measured controls on their details.
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
/// <para>
/// 1.5 (A1-1, plan API-01): the request decision control (<see cref="RequestApproveCapabilityId"/>) is a child of the
/// request detail, measured with its own name <see cref="PurchasingPermissionManifestV1.RequestsApprove"/>; one control
/// carries both the approval and the rejection, the same manager decision (ALS-D2, No.161 §1).
/// </para>
/// <para>
/// 1.6 (A1-1b, plan API-01b; ALS-D46): the request withdrawal control (<see cref="RequestWithdrawCapabilityId"/>) is a
/// child of the request detail, measured with its own name <see cref="PurchasingPermissionManifestV1.RequestsWithdraw"/>;
/// "only the preparer or the submitter" (ALS-D47) is the service's rule on the command (the screen shows it only to them).
/// </para>
/// <para>
/// 1.7 (A1-3, plan API-10/UI-05; No.161 §2): the order remainder cancellation control (<see cref="CancelCapabilityId"/>) is a
/// child of the order detail, measured with its own name <see cref="PurchasingPermissionManifestV1.OrdersCancel"/>; it carries
/// the impact preview and the cancellation of the whole unaccepted remainder.
/// </para>
/// </summary>
public sealed class PurchasingShellCapabilityProvider : IShellCapabilityProvider
{
    public const string OwnerId = "PURCHASING";
    public const string OwnerContractVersion = "1.7";

    public const string RequestListCapabilityId = "purchasing.requests.list";
    public const string RequestListRoutePath = "/satinalma/talepler";
    public const string RequestListScreenId = "P-01";
    public const string RequestCreateCapabilityId = "purchasing.requests.create";
    public const string RequestCreateRoutePath = "/satinalma/talepler/yeni";
    public const string RequestCreateScreenId = "P-01-NEW";
    public const string RequestDetailCapabilityId = "purchasing.requests.detail";
    public const string RequestSubmitCapabilityId = "purchasing.requests.submit";
    public const string RequestApproveCapabilityId = "purchasing.requests.approve";
    public const string RequestWithdrawCapabilityId = "purchasing.requests.withdraw";
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
    public const string CancelCapabilityId = "purchasing.orders.cancel";
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
            requestDetail with
            {
                CapabilityId = RequestApproveCapabilityId,
                ParentCapabilityId = RequestDetailCapabilityId,
                RequiredPermissions = [PurchasingPermissionManifestV1.OrdersRead, PurchasingPermissionManifestV1.RequestsApprove],
            },
            requestDetail with
            {
                CapabilityId = RequestWithdrawCapabilityId,
                ParentCapabilityId = RequestDetailCapabilityId,
                RequiredPermissions = [PurchasingPermissionManifestV1.OrdersRead, PurchasingPermissionManifestV1.RequestsWithdraw],
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
            detail with
            {
                CapabilityId = CancelCapabilityId,
                ParentCapabilityId = DetailCapabilityId,
                RequiredPermissions = [PurchasingPermissionManifestV1.OrdersRead, PurchasingPermissionManifestV1.OrdersCancel],
            },
        ];
    }
}
