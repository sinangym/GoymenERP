using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed class FinanceOfficialInvoiceImportPermissionRegistrationTests
{
    [Theory]
    [InlineData(FinanceOfficialInvoiceImportPermissionNamesV1.Upload,
        ErpPermissions.FinanceOfficialInvoiceImportUpload)]
    [InlineData(FinanceOfficialInvoiceImportPermissionNamesV1.Approve,
        ErpPermissions.FinanceOfficialInvoiceImportApprove)]
    public void Owner_command_name_is_known_company_wide_and_never_a_grant(
        string ownerName, string centralName)
    {
        Assert.Equal(ownerName, centralName);
        Assert.True(ErpPermissions.IsKnown(ownerName));
        Assert.Single(ErpPermissions.All, known => known == ownerName);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(ownerName));
        Assert.False(ErpPermissions.IsCritical(ownerName));
    }
}
