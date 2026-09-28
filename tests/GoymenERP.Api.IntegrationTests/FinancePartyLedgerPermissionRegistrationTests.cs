using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Finance party ledger: the two read names are the Finance owner's strings, registered once, company-wide and not
/// Critical. Registration grants nothing; only the disposable fin01 actor holds them (user decision 2026-09-28).
/// </summary>
public sealed class FinancePartyLedgerPermissionRegistrationTests
{
    public static TheoryData<string, string> Permissions => new()
    {
        { FinancePermissionNames.PartyLedgerRead, ErpPermissions.FinancePartyLedgerRead },
        { FinancePermissionNames.PartyLedgerAmountRead, ErpPermissions.FinancePartyLedgerAmountRead },
    };

    [Theory]
    [MemberData(nameof(Permissions))]
    public void Owner_names_are_registered_once_company_wide_and_not_critical(
        string ownerName,
        string centralName)
    {
        Assert.Equal(ownerName, centralName);
        Assert.Single(ErpPermissions.All, permission => permission == ownerName);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(ownerName));
        Assert.False(ErpPermissions.IsCritical(ownerName));
    }
}
