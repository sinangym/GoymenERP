using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceCreditLimitSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Work_credit_limit_Set_and_Remove_decisions_use_actual_native_commands_receipts_and_original_replay_after_later_direct_change()
    {
        await using var run = await Run.StartAsync();
        foreach (var remove in new[] { false, true })
        foreach (var approve in new[] { true, false })
        {
            var id = remove ? await LimitWorkRemoveProposal(run) : await LimitSafetyPrepare(run);
            await using var scope = new LimitWorkScope(run);
            var preview = await scope.Host.PreviewAsync(WorkDecisionHostV1.PartyCreditLimit, id, default);
            Assert.True(preview.Pending); Assert.Equal("1", preview.SourceVersion);
            Assert.Equal(remove ? "Remove" : "Set", preview.PartyCreditLimit!.Kind);
            Assert.Equal(remove ? null : "1234.50", preview.PartyCreditLimit.Amount);
            var key = "limit-work-" + id;
            var result = await scope.Host.DecideAsync(WorkDecisionHostV1.PartyCreditLimit, id, new("1", approve, approve ? null : Reason), key, default);
            Assert.Equal(approve ? "Approved" : "Rejected", result.State); Assert.False(result.Replayed);
            await LimitSafetyDurable(run, id, 2, approve ? 2 : 3, 1, 1);
            await LimitWorkDirectSet(run);
            // A fresh Work instance re-reads current terminal/superseded metadata but forwards the original body/key.
            await using var retry = new LimitWorkScope(run);
            var terminal = await retry.Host.PreviewAsync(WorkDecisionHostV1.PartyCreditLimit, id, default);
            Assert.False(terminal.Pending); Assert.Equal(approve ? "3" : "2", terminal.SourceVersion);
            var replay = await retry.Host.DecideAsync(WorkDecisionHostV1.PartyCreditLimit, id, new("1", approve, approve ? null : Reason), key, default);
            Assert.True(replay.Replayed); Assert.Equal(result.State, replay.State); Assert.Equal(result.OriginalCorrelationId, replay.OriginalCorrelationId);
            Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PartyCreditLimitCommands WHERE CompanyId=@company AND PartyCreditLimitId=@id AND CommandName=@command",
                ("@company", CompanyA), ("@id", id), ("@command", approve ? FinancePartyCreditLimitService.ApproveCommand : FinancePartyCreditLimitService.RejectCommand)));
            Assert.False(scope.Writer.HasPendingDrafts); Assert.False(retry.Writer.HasPendingDrafts);
            Assert.Null(scope.Database.Database.CurrentTransaction); Assert.Null(retry.Database.Database.CurrentTransaction);
        }
    }

    [SqlServerFact]
    public async Task Work_credit_limit_source_refuses_a_mixed_projection_if_a_native_direct_change_supersedes_the_proposal_mid_read()
    {
        await using var run = await Run.StartAsync(); var id = await LimitSafetyPrepare(run);
        await using var scope = new LimitWorkScope(run); var changed = false;
        scope.Authority.BeforeRead = async count =>
        {
            if (count != 4) return;
            await LimitWorkDirectSet(run); changed = true;
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Source.ReadDecisionAsync(new(CompanyA, Approver, "SENTETIK", null), id, default));
        Assert.True(changed); Assert.False(scope.Writer.HasPendingDrafts); Assert.False(scope.Database.ChangeTracker.HasChanges());
        Assert.Equal(4, await run.ScalarAsync<int>("SELECT Status FROM finance.PartyCreditLimits WHERE Id=@id", ("@id", id)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PartyCreditLimitCommands WHERE PartyCreditLimitId=@id AND CommandName IN (N'finance.credit-limit.approve',N'finance.credit-limit.reject')", ("@id", id)));
    }

    private static async Task LimitWorkDirectSet(Run run)
    {
        var revision = await run.ScalarAsync<long>("SELECT COALESCE(MAX(AccountRevision),CONVERT(bigint,0)) FROM finance.PartyCreditLimits WHERE CompanyId=@company AND CompanyAccountId=@account", ("@company", CompanyA), ("@account", Account));
        await using var direct = run.Open(CompanyA, Approver);
        var result = await direct.Limits.SetAsync(direct.Context, Key("limit-work-direct-" + Guid.NewGuid().ToString("N")),
            new(Party, Account, Currency, "2222.50", revision.ToString(System.Globalization.CultureInfo.InvariantCulture)), default);
        Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
    }
    private static async Task<Guid> LimitWorkRemoveProposal(Run run)
    {
        await LimitWorkDirectSet(run);
        var revision = await run.ScalarAsync<long>("SELECT MAX(AccountRevision) FROM finance.PartyCreditLimits WHERE CompanyId=@company AND CompanyAccountId=@account", ("@company", CompanyA), ("@account", Account));
        await using var proposer = run.Open(CompanyA, Proposer);
        var result = await proposer.Limits.ProposeAsync(proposer.Context, Key("limit-work-remove-" + Guid.NewGuid().ToString("N")),
            new(Party, Account, PartyCreditLimitKind.Remove, null, null, revision.ToString(System.Globalization.CultureInfo.InvariantCulture)), default);
        Assert.Equal(CommandOutcome.Succeeded, result.Outcome); return result.Resource!.Id;
    }

    // Same disposable migrated SQL fixture and real production scoped Audit writer/collector. Synthetic authority,
    // Settings and Counterparty ports only; this is not actual Identity, simultaneous concurrency or HTTP acceptance.
    private sealed class LimitWorkScope : IAsyncDisposable
    {
        private readonly IServiceScope owner;
        public FinanceDbContext Database { get; } public IAuditTransactionWriterV1 Writer { get; }
        public readonly LimitWorkAuthority Authority = new(); public FinancePartyCreditLimitWorkInboxSourceV1 Source { get; }
        public WorkDecisionHostV1 Host { get; }
        public LimitWorkScope(Run run)
        {
            owner = run.Factory.Services.CreateScope(); var company = owner.ServiceProvider.GetRequiredService<ICompanyContext>(); company.Set(CompanyA);
            var user = new FixedCurrentUser(Approver, CompanyA); var execution = new WorkDecisionExecutionFenceV1();
            var authority = new WorkDecisionAuthorityV1(Authority, execution); var permissions = new FinancePermissionPort(authority, company, user);
            var collector = owner.ServiceProvider.GetRequiredService<AuditEventCollector>(); Writer = owner.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
            var options = new DbContextOptionsBuilder<FinanceDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema)).AddInterceptors(new AuditSaveChangesInterceptor(collector, company));
            Database = new(options.Options, company);
            var core = new FinancePartyCreditLimitService(Database, company, user, permissions, new SyntheticCandidates(), new SyntheticParty(), collector,
                new(new(ErpPermissions.FinanceCreditLimitsPropose, [], "Web"), new(ErpPermissions.FinanceCreditLimitsApprove, [], "Web")), TimeProvider.System);
            Source = new(Database, new FinanceWorkInboxFence(company, user, authority));
            var correlation = new CorrelationContext(); correlation.Set("limit-work-" + Guid.NewGuid().ToString("N"), null, "Web", null, null);
            Host = new(company, user, correlation, authority, [Source], new NoLimitWorkDelegations(), execution, null!, null!,
                partyCreditLimits: new GuardedFinancePartyCreditLimitService(core, Writer));
        }
        public async ValueTask DisposeAsync() { try { await Database.DisposeAsync(); } finally { owner.Dispose(); } }
    }
    private sealed class LimitWorkAuthority : IErpAuthorityRevalidationService
    {
        public int Reads; public Func<int, Task>? BeforeRead;
        public async Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken token = default)
        {
            Assert.Equal(ErpPermissions.FinanceCreditLimitsApprove, permission); Assert.Equal(CompanyA, company);
            Reads++; if (BeforeRead is not null) await BeforeRead(Reads);
            return new(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
        }
    }
    private sealed class NoLimitWorkDelegations : IWorkDelegationReaderV1
    { public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken t = default) => throw new InvalidOperationException("No delegation was requested."); }
}
