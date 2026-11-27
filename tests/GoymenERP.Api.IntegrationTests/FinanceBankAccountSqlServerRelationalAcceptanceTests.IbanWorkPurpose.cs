using System.Reflection;
using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Work.Infrastructure.Delegations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceBankAccountSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Iban_work_purpose_calls_real_native_approval_and_old_key_replay_uses_original_maker_after_a_new_self_prepared_request()
    {
        await using var run = await Run.StartAsync(); var id = await IbanSafetyPending(run);
        var moneyBefore = await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents");
        await using var scope = new BankSafetyScope(run);
        var authority = new IbanPurposeAuthority(); var execution = new WorkDecisionExecutionFenceV1();
        var source = new FinanceBankAccountIbanChangeWorkInboxSourceV1(scope.Database, new(scope.Company, scope.User, authority));
        var companies = DispatchProxy.Create<IOrganizationService, WorkBankAccountIbanDecisionHostTests.OrganizationOwner>();
        ((WorkBankAccountIbanDecisionHostTests.OrganizationOwner)(object)companies).Values = [new(CompanyA, "SENTETIK", "SENTETIK actual-company owner", false)];
        var correlation = new CorrelationContext(); correlation.Set("SENTETIK-iban-work-original", null, "Web", null, null);
        var host = new WorkDecisionHostV1(scope.Company, scope.User, correlation, new WorkDecisionAuthorityV1(authority, execution), [source],
            new NoIbanPurposeDelegation(), execution, null!, null!, bankAccounts: scope.Service, bankAccountCompanies: companies);
        var preview = await host.PreviewAsync(source.Kind, id, default);
        Assert.Equal(("3", IbanSafetyValue, Proposer), (preview.SourceVersion, preview.BankAccountIbanChange!.ProposedIban, preview.BankAccountIbanChange.ProposedBy));
        Assert.Equal("SENTETIK actual-company owner", preview.BankAccountIbanChange.CompanyLegalName);
        Assert.False(preview.RejectionSupported); Assert.False(preview.ApprovalReasonRequired);
        var snapshot = await source.ReadDecisionAsync(new(CompanyA, Approver, "SENTETIK", null), id, default);
        Assert.DoesNotContain(IbanSafetyValue, JsonSerializer.Serialize(snapshot), StringComparison.Ordinal);
        var first = await host.DecideAsync(source.Kind, id, new("3", true, null), "iban-work-original", default);
        Assert.Equal(("Approved", false), (first.State, first.Replayed));
        await AssertIbanSafetyDurable(run, id, version: 4, commandCount: 1, auditCount: 1);
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => host.PreviewAsync(source.Kind, id, default))).Status);
        await using (var newMaker = run.Open(CompanyA, Approver))
            Assert.Equal(CommandOutcome.Succeeded, (await newMaker.Accounts.ProposeIbanChangeAsync(newMaker.Context, Key("iban-work-new-request"),
                new(id, "4", "TR330006100519786457841326", "SENTETIK new current maker"), default)).Outcome);
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => host.PreviewAsync(source.Kind, id, default))).Status);
        var original = await host.DecideAsync(source.Kind, id, new("3", true, null), "iban-work-original", default);
        Assert.True(original.Replayed); Assert.Equal(first.OriginalCorrelationId, original.OriginalCorrelationId);
        var nativeReplay = await scope.Service.ApproveIbanChangeAsync(new(CompanyA, Approver, "SENTETIK-retry", null), Key("iban-work-original"), new(id, "3"), default);
        Assert.True(nativeReplay.IsReplay); Assert.NotNull(nativeReplay.IbanApprovalReceipt);
        Assert.Equal(Proposer, nativeReplay.IbanApprovalReceipt.ChangeProposedBy);
        Assert.DoesNotContain("ibanApprovalReceipt", JsonSerializer.Serialize(nativeReplay, FinanceJson.CreateOptions()), StringComparison.Ordinal);
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => host.DecideAsync(source.Kind, id, new("5", true, null), "new-self", default))).Status);
        await AssertIbanSafetyDurable(run, id, version: 5, commandCount: 1, auditCount: 1);
        authority.Granted = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => host.DecideAsync(source.Kind, id, new("3", true, null), "iban-work-original", default))).Status);
        Assert.Equal(moneyBefore, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
        // Real Finance SQL, commands, audit and version selection; permission/Organization/delegation ports are controlled.
    }

    private sealed class IbanPurposeAuthority : IErpAuthorityRevalidationService
    {
        public bool Granted = true;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken token = default)
        { Assert.Equal(ErpPermissions.FinanceBankAccountApprove, permission); return Task.FromResult(new PermissionScope(Granted, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); }
    }
    private sealed class NoIbanPurposeDelegation : IWorkDelegationReaderV1
    { public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken token = default) => Task.FromResult<WorkDelegationViewV1?>(null); }
}
