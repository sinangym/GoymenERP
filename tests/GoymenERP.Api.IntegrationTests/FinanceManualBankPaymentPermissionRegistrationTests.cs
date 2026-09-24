using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed class FinanceManualBankPaymentPermissionRegistrationTests
{
    public static TheoryData<string, string> Permissions => new()
    {
        { FinancePermissionNames.ManualBankPaymentDraftSave, ErpPermissions.FinanceManualBankPaymentDraftSave },
        { FinancePermissionNames.ManualBankPaymentDraftRead, ErpPermissions.FinanceManualBankPaymentDraftRead },
        { FinancePermissionNames.ManualBankPaymentApprove, ErpPermissions.FinanceManualBankPaymentApprove },
        { FinancePermissionNames.ManualBankPaymentRecord, ErpPermissions.FinanceManualBankPaymentRecord },
        { FinancePermissionNames.AllocationWrite, ErpPermissions.FinanceAllocationWrite },
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
