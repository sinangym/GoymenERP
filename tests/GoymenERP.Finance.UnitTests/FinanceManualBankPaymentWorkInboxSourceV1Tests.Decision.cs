using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;
using FixedUser = GoymenERP.Finance.UnitTests.FinancePartyCreditLimitWorkInboxSourceV1Tests.FixedUser;

namespace GoymenERP.Finance.UnitTests;

public sealed partial class FinanceManualBankPaymentWorkInboxSourceV1Tests
{
    [Theory]
    [InlineData(ManualBankPaymentStatuses.Draft)]
    [InlineData(ManualBankPaymentStatuses.Approved)]
    [InlineData(ManualBankPaymentStatuses.Recorded)]
    public async Task Decision_source_keeps_the_original_maker_and_revisers_for_terminal_replay_without_mutation(int status)
    {
        var row=Payment("125.00",Start,status:status);row.Version=status+1;
        await using var run=await Run.CreateAsync([row],[],[SaveCommand(row.Id,"SENTETIK-reviser",CommandOutcome.Succeeded)]);
        var source=run.Source();var answer=await source.ReadDecisionAsync(Context(),row.Id,default);Assert.NotNull(answer);
        Assert.Equal(status==ManualBankPaymentStatuses.Draft,answer.Pending);Assert.Equal((status+1).ToString(),answer.SourceVersion);
        Assert.Equal(Preparer,answer.Item.PreparedBy);Assert.Equal(["SENTETIK-reviser"],answer.Item.ExcludedDeciders);
        Assert.Equal("/onaylarim/finance.manual-bank-payment.v1/"+row.Id.ToString("D"),answer.Item.Link);
        Assert.Null(await source.ReadDecisionAsync(Context(OtherCompany),row.Id,default));Assert.False(run.Db.ChangeTracker.HasChanges());
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Decision_source_refuses_invalid_version_unknown_state_or_unreadable_reviser_history(int fault)
    {
        var row=Payment("125",Start);if(fault==0)row.Version=0;if(fault==1)row.Status=99;
        var command=SaveCommand(row.Id,"SENTETIK-reviser",CommandOutcome.Succeeded);if(fault==2)command.ResultJson="{}";
        await using var run=await Run.CreateAsync([row],[],[command]);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>run.Source().ReadDecisionAsync(Context(),row.Id,default));
    }
    [Theory]
    [InlineData(2)] [InlineData(3)]
    public async Task Decision_source_rechecks_approval_authority_after_owner_row_and_reviser_reads(int revokeAt)
    {
        var row=Payment("125",Start);await using var run=await Run.CreateAsync([row],[]);_=run.Source();
        var company=new CompanyContext();company.Set(Company);var authority=new RevokedPurposeAuthority(revokeAt);
        var source=new FinanceManualBankPaymentWorkInboxSourceV1(run.Db,new FinanceWorkInboxFence(company,new FixedUser(Caller,[Company]),authority));
        Assert.Null(await source.ReadDecisionAsync(Context(),row.Id,default));Assert.Equal(revokeAt,authority.Calls);Assert.False(run.Db.ChangeTracker.HasChanges());
    }
    private sealed class RevokedPurposeAuthority(int revokeAt):IErpAuthorityRevalidationService
    {
        public int Calls;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission,Guid companyId,CancellationToken cancellationToken=default)
        {
            Assert.Equal(FinancePermissionNames.ManualBankPaymentApprove,permission);Assert.Equal(Company,companyId);
            return Task.FromResult(++Calls>=revokeAt?PermissionScope.Denied:new PermissionScope(true,true,new HashSet<string>(),new HashSet<Guid>(),new HashSet<Guid>(),new HashSet<Guid>()));
        }
    }
}
