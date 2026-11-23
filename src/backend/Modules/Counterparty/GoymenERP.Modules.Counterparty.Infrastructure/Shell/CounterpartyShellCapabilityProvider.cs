using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Shell;

public sealed class CounterpartyShellCapabilityProvider : IShellCapabilityProvider
{
    public const string OwnerId = "CHATGPT-BUSINESS/CARI";
    public const string OwnerContractVersion = "1.6";
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

    /// <summary>
    /// FNS-D121 (Counterparty 1.6): controls inside the C-03 detail. <c>cari.party-ibans.read</c> shows the IBAN list panel,
    /// <c>cari.party-ibans.manage</c> its add/deactivate buttons. A declaration grants nothing; the API re-checks each
    /// permission per request.
    /// </summary>
    public const string PartyIbansReadCapabilityId = "cari.party-ibans.read";
    public const string PartyIbansManageCapabilityId = "cari.party-ibans.manage";

    public string Owner => OwnerId;

    public string ContractVersion => OwnerContractVersion;

    public IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations()
    {
        var detail = new ShellCapabilityDeclaration(
            DetailCapabilityId,
            DetailRouteId,
            DetailRoutePath,
            DetailScreenId,
            ShellCapabilityRoutePrecedence.ParameterizedAfterStatic,
            ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead,
            ShellCapabilityScopeRequirement.CompanyWide,
            [ErpPermissions.CounterpartyPartiesRead],
            ImplementationAvailable: true);
        return
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
                ImplementationAvailable: true),
            new ShellCapabilityDeclaration(
                CreateCapabilityId,
                CreateRouteId,
                CreateRoutePath,
                CreateScreenId,
                ShellCapabilityRoutePrecedence.Static,
                ShellCapabilityContextRequirement.SingleCompany,
                ShellCapabilityScopeRequirement.CompanyWide,
                [ErpPermissions.CounterpartyPartiesCreate],
                ImplementationAvailable: true),
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
            detail,
            detail with
            {
                CapabilityId = PartyIbansReadCapabilityId,
                ParentCapabilityId = DetailCapabilityId,
                RequiredPermissions = [ErpPermissions.CounterpartyPartiesRead, ErpPermissions.CounterpartyPartyIbansRead],
            },
            detail with
            {
                CapabilityId = PartyIbansManageCapabilityId,
                ParentCapabilityId = DetailCapabilityId,
                RequiredPermissions =
                [
                    ErpPermissions.CounterpartyPartiesRead,
                    ErpPermissions.CounterpartyPartyIbansRead,
                    ErpPermissions.CounterpartyPartyIbansManage,
                ],
            },
        ];
    }
}
