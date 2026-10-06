using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Logistics.Application.Contracts;

namespace GoymenERP.Modules.Logistics.Infrastructure.Shell;

/// <summary>
/// Declares the three loading-instruction screens to the shell, on master v3 §27.8's table: L-02 "Yükleme
/// Talimatları" is the list, L-03 "Yeni/Düzenle Yükleme Talimatı" the new-instruction workspace, and the
/// detail is the read view of one L-02 row ("L-02-DETAIL", the F-02-DETAIL precedent; the master has no
/// separate detail id). L-01 (Sevk Edilebilir Siparişler) and L-04 (Araç / Sürücü / Kapı) are other screens
/// and are not declared here; recording a load is L-05's act (Fiilî Yükleme) and will be declared with that
/// screen, in LOG-4a it is a server-measured command on the detail.
/// PUBLISHED (<c>ImplementationAvailable: true</c>) after LOG-4b's exact-head staged-profile browser acceptance
/// on PR #358 (<c>af2072ce1f5ca17b8124fadc81cd83ea7c32fd92</c>, run 35484271018, 5/5). A declaration grants
/// nothing: the screens open only for an actor holding the
/// permission (D6), and the grant is a human gate. The list and the detail share
/// <see cref="LogisticsPermissionManifestV1.InstructionsRead"/>; the new-instruction screen needs
/// <see cref="LogisticsPermissionManifestV1.InstructionsCreate"/>; recording a load is a server-measured
/// command on the detail, not a screen. The host registers this provider only after the central catalogue
/// knows the names, together with its accepted revision.
/// <para>
/// 1.2 (AT-025): the operations work list's two SEPARATE sections, one screen each on master v3 §27.8 — L-01 "Sevk Edilebilir
/// Siparişler" (the sales section: approved sales order lines to load, ship and deliver) and L-09 "Beklenen Alış / Kapı
/// Kabulü" (the purchase section: approved purchase order lines waiting for goods receipt). Each has its own route and its
/// own read name, so an actor sees a section only with that section's grant; neither implies the other.
/// </para>
/// </summary>
public sealed class LogisticsShellCapabilityProvider : IShellCapabilityProvider
{
    public const string OwnerId = "LOGISTICS";
    public const string OwnerContractVersion = "1.3";

    public const string ListCapabilityId = "logistics.instructions.list";
    public const string ListRoutePath = "/lojistik/yukleme-talimatlari";
    public const string ListScreenId = "L-02";
    public const string CreateCapabilityId = "logistics.instructions.create";
    public const string CreateRoutePath = "/lojistik/yukleme-talimatlari/yeni";
    public const string CreateScreenId = "L-03";
    public const string DetailCapabilityId = "logistics.instructions.detail";
    public const string DetailRoutePath = "/lojistik/yukleme-talimatlari/:instructionId";
    public const string DetailScreenId = "L-02-DETAIL";
    public const string ShipmentCapabilityId = "logistics.shipments.record";
    public const string DeliveryCapabilityId = "logistics.deliveries.record";
    public const string CustomerReturnsReadCapabilityId = "logistics.customer-returns.read";
    public const string CustomerReturnsRequestCapabilityId = "logistics.customer-returns.request";
    public const string CustomerReturnsApproveCapabilityId = "logistics.customer-returns.approve";
    public const string CustomerReturnsRejectCapabilityId = "logistics.customer-returns.reject";
    public const string SalesWorkCapabilityId = "logistics.operations-work.sales";
    public const string SalesWorkRoutePath = "/lojistik/operasyon-isleri/satis";
    public const string SalesWorkScreenId = "L-01";
    public const string PurchaseWorkCapabilityId = "logistics.operations-work.purchase";
    public const string PurchaseWorkRoutePath = "/lojistik/operasyon-isleri/satinalma";
    public const string PurchaseWorkScreenId = "L-09";

    public string Owner => OwnerId;

    public string ContractVersion => OwnerContractVersion;

    public IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations() =>
    [
        new ShellCapabilityDeclaration(ListCapabilityId, ListCapabilityId, ListRoutePath, ListScreenId,
            ShellCapabilityRoutePrecedence.Static, ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead,
            ShellCapabilityScopeRequirement.CompanyWide, [LogisticsPermissionManifestV1.InstructionsRead], ImplementationAvailable: true),
        new ShellCapabilityDeclaration(CreateCapabilityId, CreateCapabilityId, CreateRoutePath, CreateScreenId,
            ShellCapabilityRoutePrecedence.Static, ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.CompanyWide, [LogisticsPermissionManifestV1.InstructionsCreate], ImplementationAvailable: true),
        new ShellCapabilityDeclaration(DetailCapabilityId, DetailCapabilityId, DetailRoutePath, DetailScreenId,
            ShellCapabilityRoutePrecedence.ParameterizedAfterStatic, ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead,
            ShellCapabilityScopeRequirement.CompanyWide, [LogisticsPermissionManifestV1.InstructionsRead], ImplementationAvailable: true),
        new ShellCapabilityDeclaration(ShipmentCapabilityId, DetailCapabilityId, DetailRoutePath, DetailScreenId,
            ShellCapabilityRoutePrecedence.ParameterizedAfterStatic, ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead,
            ShellCapabilityScopeRequirement.CompanyWide,
            [LogisticsPermissionManifestV1.InstructionsRead, LogisticsPermissionManifestV1.ShipmentsRecord,
                ErpPermissions.InventoryPhysicalAllocationWrite], ImplementationAvailable: true,
            ParentCapabilityId: DetailCapabilityId),
        new ShellCapabilityDeclaration(DeliveryCapabilityId, DetailCapabilityId, DetailRoutePath, DetailScreenId,
            ShellCapabilityRoutePrecedence.ParameterizedAfterStatic, ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead,
            ShellCapabilityScopeRequirement.CompanyWide,
            [LogisticsPermissionManifestV1.InstructionsRead, LogisticsPermissionManifestV1.DeliveriesRecord],
            ImplementationAvailable: true, ParentCapabilityId: DetailCapabilityId),
        new ShellCapabilityDeclaration(CustomerReturnsReadCapabilityId, DetailCapabilityId, DetailRoutePath, DetailScreenId,
            ShellCapabilityRoutePrecedence.ParameterizedAfterStatic, ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead,
            ShellCapabilityScopeRequirement.CompanyWide,
            [LogisticsPermissionManifestV1.InstructionsRead, LogisticsPermissionManifestV1.CustomerReturnsRead],
            ImplementationAvailable: true, ParentCapabilityId: DetailCapabilityId),
        new ShellCapabilityDeclaration(CustomerReturnsRequestCapabilityId, DetailCapabilityId, DetailRoutePath, DetailScreenId,
            ShellCapabilityRoutePrecedence.ParameterizedAfterStatic, ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead,
            ShellCapabilityScopeRequirement.CompanyWide,
            [LogisticsPermissionManifestV1.InstructionsRead, LogisticsPermissionManifestV1.CustomerReturnsRead,
                LogisticsPermissionManifestV1.CustomerReturnsRequest],
            ImplementationAvailable: true, ParentCapabilityId: DetailCapabilityId),
        new ShellCapabilityDeclaration(CustomerReturnsApproveCapabilityId, DetailCapabilityId, DetailRoutePath, DetailScreenId,
            ShellCapabilityRoutePrecedence.ParameterizedAfterStatic, ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead,
            ShellCapabilityScopeRequirement.CompanyWide,
            [LogisticsPermissionManifestV1.InstructionsRead, LogisticsPermissionManifestV1.CustomerReturnsRead,
                LogisticsPermissionManifestV1.CustomerReturnsDecision, ErpPermissions.InventoryPhysicalAllocationWrite],
            ImplementationAvailable: true, ParentCapabilityId: DetailCapabilityId),
        new ShellCapabilityDeclaration(CustomerReturnsRejectCapabilityId, DetailCapabilityId, DetailRoutePath, DetailScreenId,
            ShellCapabilityRoutePrecedence.ParameterizedAfterStatic, ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead,
            ShellCapabilityScopeRequirement.CompanyWide,
            [LogisticsPermissionManifestV1.InstructionsRead, LogisticsPermissionManifestV1.CustomerReturnsRead,
                LogisticsPermissionManifestV1.CustomerReturnsDecision],
            ImplementationAvailable: true, ParentCapabilityId: DetailCapabilityId),
        new ShellCapabilityDeclaration(SalesWorkCapabilityId, SalesWorkCapabilityId, SalesWorkRoutePath, SalesWorkScreenId,
            ShellCapabilityRoutePrecedence.Static, ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.CompanyWide, [LogisticsPermissionManifestV1.OperationsSalesWorkRead], ImplementationAvailable: true),
        new ShellCapabilityDeclaration(PurchaseWorkCapabilityId, PurchaseWorkCapabilityId, PurchaseWorkRoutePath, PurchaseWorkScreenId,
            ShellCapabilityRoutePrecedence.Static, ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.CompanyWide, [LogisticsPermissionManifestV1.OperationsPurchaseWorkRead], ImplementationAvailable: true),
    ];
}
