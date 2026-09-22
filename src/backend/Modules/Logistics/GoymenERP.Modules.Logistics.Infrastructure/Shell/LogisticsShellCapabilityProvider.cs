using GoymenERP.BuildingBlocks.Shell;
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
/// </summary>
public sealed class LogisticsShellCapabilityProvider : IShellCapabilityProvider
{
    public const string OwnerId = "LOGISTICS";
    public const string OwnerContractVersion = "1.0";

    public const string ListCapabilityId = "logistics.instructions.list";
    public const string ListRoutePath = "/lojistik/yukleme-talimatlari";
    public const string ListScreenId = "L-02";
    public const string CreateCapabilityId = "logistics.instructions.create";
    public const string CreateRoutePath = "/lojistik/yukleme-talimatlari/yeni";
    public const string CreateScreenId = "L-03";
    public const string DetailCapabilityId = "logistics.instructions.detail";
    public const string DetailRoutePath = "/lojistik/yukleme-talimatlari/:instructionId";
    public const string DetailScreenId = "L-02-DETAIL";

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
    ];
}
