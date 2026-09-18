using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// FIN-T04b (#320): the Finance owner's manual-receipt names are registered centrally with the same
/// strings, company-wide only and not Critical. Registration is not a grant: nothing here assigns them.
/// </summary>
public sealed class FinanceManualReceiptPermissionRegistrationTests
{
    [Theory]
    [InlineData(FinancePermissionNames.DraftSave, ErpPermissions.FinanceManualReceiptDraftSave)]
    [InlineData(FinancePermissionNames.DraftRead, ErpPermissions.FinanceManualReceiptDraftRead)]
    [InlineData(FinancePermissionNames.AmountRead, ErpPermissions.FinanceManualReceiptAmountRead)]
    public void The_owner_name_is_the_registered_central_name(string ownerName, string centralName)
    {
        Assert.Equal(ownerName, centralName);
        Assert.True(ErpPermissions.IsKnown(ownerName));
        Assert.Single(ErpPermissions.All, known => known == ownerName);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(ownerName));
        // A draft moves no money; Record and Reverse (OPEN-009) have no name yet.
        Assert.False(ErpPermissions.IsCritical(ownerName));
    }
}
