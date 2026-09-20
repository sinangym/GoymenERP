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
    // FIN-T05: Record joins them. It DOES move money, and it is still not Critical — Critical governs
    // grant-time dual control (ADR-004 §6), and no source makes recording a receipt a segregated duty.
    [InlineData(FinancePermissionNames.Record, ErpPermissions.FinanceManualReceiptRecord)]
    public void The_owner_name_is_the_registered_central_name(string ownerName, string centralName)
    {
        Assert.Equal(ownerName, centralName);
        Assert.True(ErpPermissions.IsKnown(ownerName));
        Assert.Single(ErpPermissions.All, known => known == ownerName);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(ownerName));
        Assert.False(ErpPermissions.IsCritical(ownerName));
    }
}
