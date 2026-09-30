using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// DEC-FIN-016: the five customer cheque / promissory-note names are the Finance owner's strings, registered
/// once, company-wide and not Critical. Registration grants nothing; only the disposable fin01 actor holds them
/// (user decision 2026-09-27).
/// </summary>
public sealed class FinanceCustomerInstrumentPermissionRegistrationTests
{
    public static TheoryData<string, string> Permissions => new()
    {
        { FinancePermissionNames.CustomerInstrumentRead, ErpPermissions.FinanceCustomerInstrumentRead },
        { FinancePermissionNames.CustomerInstrumentAmountRead, ErpPermissions.FinanceCustomerInstrumentAmountRead },
        { FinancePermissionNames.CustomerInstrumentReceive, ErpPermissions.FinanceCustomerInstrumentReceive },
        { FinancePermissionNames.CustomerInstrumentStatusChange, ErpPermissions.FinanceCustomerInstrumentStatusChange },
        { FinancePermissionNames.CustomerInstrumentCollect, ErpPermissions.FinanceCustomerInstrumentCollect },
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
