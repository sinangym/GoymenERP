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

    public string Owner => OwnerId;

    public string ContractVersion => OwnerContractVersion;

    public IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations() =>
    [
        new ShellCapabilityDeclaration(
            ListCapabilityId,
            ListRouteId,
            ListRoutePath,
            ListScreenId,
            ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead,
            ShellCapabilityScopeRequirement.CompanyWide,
            [ErpPermissions.CounterpartyPartiesRead],
            ImplementationAvailable: false),
    ];
}
