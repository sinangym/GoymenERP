using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// ACC-V0a: the Accounting owner's permission names are registered centrally with the same strings (company-wide
/// only, not Critical, no grant; #66 5743488059), and nothing else is registered under <c>accounting.</c>.
/// </summary>
public sealed class AccountingPermissionRegistrationTests
{
    [Theory]
    [InlineData(AccountingPermissionManifestV1.PeriodsRead, ErpPermissions.AccountingPeriodsRead)]
    [InlineData(AccountingPermissionManifestV1.PeriodsPropose, ErpPermissions.AccountingPeriodsPropose)]
    [InlineData(AccountingPermissionManifestV1.PeriodsApprove, ErpPermissions.AccountingPeriodsApprove)]
    [InlineData(AccountingPermissionManifestV1.PeriodsClose, ErpPermissions.AccountingPeriodsClose)]
    [InlineData(AccountingPermissionManifestV1.JournalsRead, ErpPermissions.AccountingJournalsRead)]
    [InlineData(AccountingPermissionManifestV1.PostingQueuesRead, ErpPermissions.AccountingPostingQueuesRead)]
    [InlineData(NormalJournalPermissionNamesV1.DraftCreate, ErpPermissions.AccountingNormalJournalDraftCreate)]
    [InlineData(NormalJournalPermissionNamesV1.DraftApprove, ErpPermissions.AccountingNormalJournalDraftApprove)]
    [InlineData(AccountingJournalReversalPermissionNamesV1.Propose, ErpPermissions.AccountingJournalReversePropose)]
    [InlineData(AccountingJournalReversalPermissionNamesV1.Approve, ErpPermissions.AccountingJournalReverseApprove)]
    public void The_owner_name_is_the_registered_central_name(string ownerName, string centralName)
    {
        Assert.Equal(ownerName, centralName);
        Assert.True(ErpPermissions.IsKnown(ownerName));
        Assert.Single(ErpPermissions.All, known => known == ownerName);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(ownerName));
        Assert.False(ErpPermissions.IsCritical(ownerName));
    }

    [Fact]
    public void Every_owner_name_is_registered_and_nothing_else_under_accounting()
    {
        Assert.Equal(AccountingPermissionManifestV1.All
                .Concat(NormalJournalPermissionNamesV1.All)
                .Concat([
                    AccountingJournalReversalPermissionNamesV1.Propose,
                    AccountingJournalReversalPermissionNamesV1.Approve,
                ])
                .Order(StringComparer.Ordinal),
            ErpPermissions.All.Where(name => name.StartsWith("accounting.", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
    }
}
