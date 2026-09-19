using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Logistics.Application.Contracts;

namespace GoymenERP.Modules.Logistics.Infrastructure.Shell;

/// <summary>
/// Declares the three loading-instruction screens (L-01 list, L-03 new, L-04 detail) to the shell —
/// STAGED (<c>ImplementationAvailable: false</c>): LOG-4b brings the frontend, and the flip to published
/// precedes its browser suite (the S03-c lesson: a staged declaration is never published, so nothing opens
/// before the screens exist). A declaration grants nothing: the screens open only for an actor holding the
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
    public const string ListScreenId = "L-01";
    public const string CreateCapabilityId = "logistics.instructions.create";
    public const string CreateRoutePath = "/lojistik/yukleme-talimatlari/yeni";
    public const string CreateScreenId = "L-03";
    public const string DetailCapabilityId = "logistics.instructions.detail";
    public const string DetailRoutePath = "/lojistik/yukleme-talimatlari/:instructionId";
    public const string DetailScreenId = "L-04";

    public string Owner => OwnerId;

    public string ContractVersion => OwnerContractVersion;

    public IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations() =>
    [
        new ShellCapabilityDeclaration(ListCapabilityId, ListCapabilityId, ListRoutePath, ListScreenId,
            ShellCapabilityRoutePrecedence.Static, ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead,
            ShellCapabilityScopeRequirement.CompanyWide, [LogisticsPermissionManifestV1.InstructionsRead], ImplementationAvailable: false),
        new ShellCapabilityDeclaration(CreateCapabilityId, CreateCapabilityId, CreateRoutePath, CreateScreenId,
            ShellCapabilityRoutePrecedence.Static, ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.CompanyWide, [LogisticsPermissionManifestV1.InstructionsCreate], ImplementationAvailable: false),
        new ShellCapabilityDeclaration(DetailCapabilityId, DetailCapabilityId, DetailRoutePath, DetailScreenId,
            ShellCapabilityRoutePrecedence.ParameterizedAfterStatic, ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead,
            ShellCapabilityScopeRequirement.CompanyWide, [LogisticsPermissionManifestV1.InstructionsRead], ImplementationAvailable: false),
    ];
}
