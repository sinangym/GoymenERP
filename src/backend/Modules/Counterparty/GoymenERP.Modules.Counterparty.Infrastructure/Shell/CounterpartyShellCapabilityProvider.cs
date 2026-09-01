using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Shell;

public sealed class CounterpartyShellCapabilityProvider : IShellCapabilityProvider
{
    public const string OwnerId = "CHATGPT-BUSINESS/CARI";
    public const string OwnerContractVersion = "1.5";
    public const string ListCapabilityId = "cari.parties.list";
    public const string ListRouteId = "cari.list";
    public const string ListRoutePath = "/cari";
    public const string ListScreenId = "C-01";
    public const string CreateCapabilityId = "cari.parties.create";
    public const string CreateRouteId = "cari.create";
    public const string CreateRoutePath = "/cari/yeni";
    public const string CreateScreenId = "C-02";
    public const string LinkCapabilityId = "cari.accounts.link";
    public const string LinkRouteId = "cari.company-link";
    public const string LinkRoutePath = "/cari/sirkete-bagla";
    public const string LinkScreenId = "CARI-LINK-WORKSPACE-001";
    public const string DetailCapabilityId = "cari.parties.detail";
    public const string DetailRouteId = "cari.detail";
    public const string DetailRoutePath = "/cari/:partyId";
    public const string DetailScreenId = "C-03";

    public string Owner => OwnerId;

    public string ContractVersion => OwnerContractVersion;

    public IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations() =>
    [
        new ShellCapabilityDeclaration(
            ListCapabilityId,
            ListRouteId,
            ListRoutePath,
            ListScreenId,
            ShellCapabilityRoutePrecedence.Static,
            ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead,
            ShellCapabilityScopeRequirement.CompanyWide,
            [ErpPermissions.CounterpartyPartiesRead],
            ImplementationAvailable: false),
        new ShellCapabilityDeclaration(
            CreateCapabilityId,
            CreateRouteId,
            CreateRoutePath,
            CreateScreenId,
            ShellCapabilityRoutePrecedence.Static,
            ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.CompanyWide,
            [ErpPermissions.CounterpartyPartiesCreate],
            ImplementationAvailable: false),
        new ShellCapabilityDeclaration(
            LinkCapabilityId,
            LinkRouteId,
            LinkRoutePath,
            LinkScreenId,
            ShellCapabilityRoutePrecedence.Static,
            ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.CompanyWide,
            [
                ErpPermissions.CounterpartyAccountsCreate,
                ErpPermissions.CounterpartyPartiesRead,
            ],
            ImplementationAvailable: false),
        new ShellCapabilityDeclaration(
            DetailCapabilityId,
            DetailRouteId,
            DetailRoutePath,
            DetailScreenId,
            ShellCapabilityRoutePrecedence.ParameterizedAfterStatic,
            ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead,
            ShellCapabilityScopeRequirement.CompanyWide,
            [ErpPermissions.CounterpartyPartiesRead],
            ImplementationAvailable: false),
    ];
}
