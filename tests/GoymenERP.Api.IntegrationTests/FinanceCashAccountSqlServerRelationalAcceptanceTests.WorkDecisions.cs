using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceCashAccountSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Work_cash_decision_reuses_the_real_owner_transaction_receipt_and_audit_without_a_second_money_event()
    {
        await using var run = await Run.StartAsync();
        Guid id;
        await using (var maker = run.Open(CompanyA, Proposer))
            id = (await maker.Accounts.ProposeAsync(maker.Context, Key("wp8-propose"), new("SENTETIK WP8", Currency), default)).Resource!.Id;
        var execution = new WorkDecisionExecutionFenceV1(); var authority = new WorkDecisionAuthorityV1(new WorkAllowedAuthority(), execution);
        var reader = new WorkDelegationStub();
        await using (var owner = run.OpenWithPermissions(CompanyA, Approver, new WorkAuthorityBridge(authority), new SyntheticCandidates()))
        {
            var host = WorkHost(owner, authority, execution, reader);
            var preview = await host.PreviewAsync("finance.cash-account.v1", id, default);
            Assert.Equal((true, "1"), (preview.Pending, preview.SourceVersion));
            var first = await host.DecideAsync(preview.Kind, id, new("1", true, null), "wp8-approve", default);
            Assert.False(first.Replayed); Assert.Equal("Approved", first.State);
        }
        // A fresh request / owner context reaches its durable receipt after the pending item disappeared.
        await using (var owner = run.OpenWithPermissions(CompanyA, Approver, new WorkAuthorityBridge(authority), new SyntheticCandidates()))
        {
            var host = WorkHost(owner, authority, execution, reader);
            Assert.False((await host.PreviewAsync("finance.cash-account.v1", id, default)).Pending);
            var replay = await host.DecideAsync("finance.cash-account.v1", id, new("1", true, null), "wp8-approve", default);
            Assert.True(replay.Replayed); Assert.Equal(run.Correlation, replay.OriginalCorrelationId);
        }
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashAccountCommands"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'finance.cash-account.approved.v1'"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
        await using var foreign = run.Open(CompanyB, Approver);
        var error = await Assert.ThrowsAsync<WorkDecisionException>(() => WorkHost(foreign, authority, execution, reader)
            .PreviewAsync("finance.cash-account.v1", id, default));
        Assert.Equal(404, error.Status);
    }

    [SqlServerFact]
    public async Task Work_delegation_revoked_after_owner_flush_rolls_back_the_real_cash_decision_and_audit()
    {
        await using var run = await Run.StartAsync(); Guid id;
        await using (var maker = run.Open(CompanyA, Proposer))
            id = (await maker.Accounts.ProposeAsync(maker.Context, Key("wp8-propose"), new("SENTETIK WP8", Currency), default)).Resource!.Id;
        var reader = new WorkDelegationStub(); var execution = new WorkDecisionExecutionFenceV1();
        var authority = new WorkDecisionAuthorityV1(new WorkAllowedAuthority(), execution);
        var revoke = new WorkRevokeAfterFlush(reader);
        await using (var owner = run.OpenWithPermissions(CompanyA, Approver, new WorkAuthorityBridge(authority), new SyntheticCandidates(), revoke))
        {
            var host = WorkHost(owner, authority, execution, reader);
            var error = await Assert.ThrowsAsync<WorkDecisionException>(() => host.DecideAsync("finance.cash-account.v1", id,
                new("1", true, null, Guid.NewGuid()), "wp8-revoked", default));
            Assert.Equal(403, error.Status); Assert.True(revoke.Flushed); Assert.False(reader.Allowed);
        }
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashAccountCommands"));
        Assert.Equal(1L, await run.ScalarAsync<long>("SELECT Version FROM finance.CashAccounts WHERE Id=@id", ("@id", id)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'finance.cash-account.approved.v1'"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
    }

    private static WorkDecisionHostV1 WorkHost(Scope owner, IErpAuthorityRevalidationService authority,
        WorkDecisionExecutionFenceV1 execution, IWorkDelegationReaderV1 reader)
    {
        var company = new CompanyContext(); company.Set(owner.Context.CompanyId);
        var user = new FixedCurrentUser(owner.Context.ActorId, owner.Context.CompanyId);
        var correlation = new CorrelationContext(); correlation.Set(owner.Context.CorrelationId, null, "api", null, null);
        var source = new FinanceCashAccountWorkInboxSourceV1(owner.Database, new FinanceWorkInboxFence(company, user, authority));
        return new(company, user, correlation, authority, [source], reader, execution, owner.Accounts, new WorkUnusedInventory());
    }
    /// <summary>SENTETIK authority and delegation seams; not actual user/role or WP3 persistence acceptance.</summary>
    private sealed class WorkAllowedAuthority : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
            => Task.FromResult(new PermissionScope(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
    }
    private sealed class WorkAuthorityBridge(IErpAuthorityRevalidationService authority) : IFinancePermissionPort
    {
        public async Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId, CancellationToken cancellationToken)
            => (await authority.RevalidatePermissionScopeAsync(ErpPermissions.FinanceCashAccountApprove, context.CompanyId, cancellationToken)).IsGranted
                ? PermissionDecision.Allowed : PermissionDecision.Denied;
    }
    private sealed class WorkDelegationStub : IWorkDelegationReaderV1
    {
        public bool Allowed = true;
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken cancellationToken = default)
            => Task.FromResult<WorkDelegationViewV1?>(Allowed ? new(id, CompanyA, item.Kind, ErpPermissions.FinanceCashAccountApprove,
                Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), "1", Guid.NewGuid(),
                DateTimeOffset.UtcNow, "SENTETIK", null, null, null) : null);
    }
    private sealed class WorkRevokeAfterFlush(WorkDelegationStub reader) : SaveChangesInterceptor
    {
        public bool Flushed;
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        { Flushed = true; reader.Allowed = false; return ValueTask.FromResult(result); }
    }
    private sealed class WorkUnusedInventory : IInventoryOpeningCostCommandV1
    {
        public Task<InventoryOpeningCostResultV1> SubmitAsync(SubmitInventoryOpeningCostV1 request, InventoryCommandMetadata metadata, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InventoryOpeningCostResultV1> DecideAsync(Guid id, DecideInventoryOpeningCostV1 request, InventoryCommandMetadata metadata, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
