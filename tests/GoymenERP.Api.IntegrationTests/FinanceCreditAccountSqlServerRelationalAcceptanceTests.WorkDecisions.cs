using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceCreditAccountSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Work_credit_purpose_uses_native_immutable_receipt_after_completion_without_a_money_event_or_a_browse_grant()
    {
        await using var run = await Run.StartAsync();
        Guid id;
        await using (var maker = run.Open(CompanyA, Maker))
            id = (await maker.Credits.ProposeAsync(maker.Context, Key("work-credit-propose"), Request(), default)).Resource!.Id;
        var authority = new CreditWorkAuthority();
        await using (var owner = OpenCreditDecision(run, CompanyA, Checker, authority))
        {
            var preview = await owner.Host.PreviewAsync(WorkDecisionHostV1.CreditAccount, id, default);
            Assert.Equal((true, "1"), (preview.Pending, preview.SourceVersion));
            var first = await owner.Host.DecideAsync(preview.Kind, id, new("1", true, null), "work-credit-approve", default);
            Assert.Equal("Approved", first.State); Assert.False(first.Replayed);
        }
        await using (var owner = OpenCreditDecision(run, CompanyA, Checker, authority, correlationId: "SENTETIK-retry-correlation"))
        {
            var preview = await owner.Host.PreviewAsync(WorkDecisionHostV1.CreditAccount, id, default);
            Assert.Equal((false, "2"), (preview.Pending, preview.SourceVersion));
            var replay = await owner.Host.DecideAsync(preview.Kind, id, new("1", true, null), "work-credit-approve", default);
            Assert.True(replay.Replayed); Assert.Equal(run.Correlation, replay.OriginalCorrelationId);
            Assert.Equal("Approved", replay.State);
        }
        await using (var maker = OpenCreditDecision(run, CompanyA, Maker, authority))
            Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => maker.Host.PreviewAsync(WorkDecisionHostV1.CreditAccount, id, default))).Status);
        await using (var foreign = OpenCreditDecision(run, CompanyB, Checker, authority))
            Assert.Equal(404, (await Assert.ThrowsAsync<WorkDecisionException>(() => foreign.Host.PreviewAsync(WorkDecisionHostV1.CreditAccount, id, default))).Status);
        Assert.All(authority.Asked, name => Assert.Equal(ErpPermissions.FinanceCreditsApprove, name));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CreditAccountCommands"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'finance.credit-account.approved.v1'"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
    }

    [SqlServerFact]
    public async Task Lost_credit_authority_or_delegation_after_save_rolls_back_native_decisions_and_terminal_receipts_before_commit()
    {
        await using var run = await Run.StartAsync();
        Guid id;
        await using (var maker = run.Open(CompanyA, Maker))
            id = (await maker.Credits.ProposeAsync(maker.Context, Key("work-credit-propose"), Request(), default)).Resource!.Id;
        foreach (var approve in new[] { true, false })
        foreach (var loss in new[] { "authority", "delegation", "terminal-refusal" })
        {
            var authority = new CreditWorkAuthority();
            var delegation = new CreditWorkDelegation();
            var flush = new CreditWorkAfterFlush(() => { if (loss == "authority") authority.Allowed = false; else delegation.Allowed = false; });
            await using var owner = OpenCreditDecision(run, CompanyA, Checker, authority, delegation, flush);
            var key = $"work-credit-{approve}-{loss}";
            var error = await Assert.ThrowsAsync<WorkDecisionException>(() => owner.Host.DecideAsync(WorkDecisionHostV1.CreditAccount, id,
                new(loss == "terminal-refusal" ? "9" : "1", approve, approve ? null : "SENTETIK ret", Guid.NewGuid()), key, default));
            Assert.Equal(403, error.Status); Assert.True(flush.Flushed);
            Assert.False(owner.Database.ChangeTracker.HasChanges());
            Assert.Equal(1L, await run.ScalarAsync<long>("SELECT Version FROM finance.CreditAccounts WHERE Id=@id", ("@id", id)));
            Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CreditAccountCommands"));
            Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType IN (N'finance.credit-account.approved.v1',N'finance.credit-account.rejected.v1')"));
            Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
        }
    }

    [SqlServerFact]
    public async Task Native_credit_decisions_refuse_ambient_borrowed_and_dirty_entry_without_taking_the_callers_handles()
    {
        await using var run = await Run.StartAsync();
        await using var owner = OpenCreditDecision(run, CompanyA, Checker, new CreditWorkAuthority());
        var id = Guid.NewGuid();
        using (var ambient = new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
            Assert.Equal(CommandOutcome.Unavailable, (await owner.Credits.ApproveAsync(owner.Context, Key("credit-ambient"), new(id, "1"), default)).Outcome);
        await using (var borrowed = await owner.Database.Database.BeginTransactionAsync())
        {
            Assert.Equal(CommandOutcome.Unavailable, (await owner.Credits.RejectAsync(owner.Context, Key("credit-borrowed"), new(id, "1", "SENTETIK ret"), default)).Outcome);
            Assert.Same(borrowed, owner.Database.Database.CurrentTransaction);
            await borrowed.RollbackAsync();
        }
        var staged = new CreditAccountRow { Id = id, CompanyId = CompanyA };
        owner.Database.CreditAccounts.Add(staged);
        Assert.Equal(CommandOutcome.Unavailable, (await owner.Credits.ApproveAsync(owner.Context, Key("credit-dirty"), new(id, "1"), default)).Outcome);
        Assert.Equal(EntityState.Added, owner.Database.Entry(staged).State);
        owner.Database.ChangeTracker.Clear();
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CreditAccountCommands"));
    }

    // The existing Run owns SQL lifecycle/migrations and its proposal fixture. This narrow scope uses the REAL
    // AuditSaveChangesInterceptor in the SAME Finance transaction, explicitly with no AuditTransactionWriter fake.
    // Production DI additionally supplies the optional Audit owner for pending-batch entry/discard protection.
    private static CreditDecisionScope OpenCreditDecision(Run run, Guid companyId, string actor, CreditWorkAuthority raw,
        CreditWorkDelegation? delegation = null, CreditWorkAfterFlush? flush = null, string? correlationId = null)
    {
        var company = new CompanyContext(); company.Set(companyId);
        var user = new FixedCurrentUser(actor, companyId);
        var collector = new AuditEventCollector();
        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
            .AddInterceptors(new AuditSaveChangesInterceptor(collector, company));
        if (flush is not null) options.AddInterceptors(flush);
        var database = new FinanceDbContext(options.Options, company);
        var execution = new WorkDecisionExecutionFenceV1();
        var authority = new WorkDecisionAuthorityV1(raw, execution);
        var context = new FinanceContext(companyId, actor, correlationId ?? run.Correlation, null);
        var credits = new FinanceCreditAccountService(database, company, user, new FinancePermissionPort(authority, company, user),
            new SyntheticCandidates(), collector, new(new(ErpPermissions.FinanceCreditsPropose, [], "Web"), new(ErpPermissions.FinanceCreditsApprove, [], "Web")),
            TimeProvider.System, new SyntheticCreditSources(companyId), new SyntheticCreditSources(companyId));
        var correlation = new CorrelationContext(); correlation.Set(context.CorrelationId, null, "api", null, null);
        var source = new FinanceCreditAccountWorkInboxSourceV1(database, new FinanceWorkInboxFence(company, user, authority));
        var host = new WorkDecisionHostV1(company, user, correlation, authority, [source], delegation ?? new CreditWorkDelegation(), execution,
            null!, null!, credits: credits);
        return new(database, credits, context, host);
    }
    private sealed record CreditDecisionScope(FinanceDbContext Database, FinanceCreditAccountService Credits, FinanceContext Context, WorkDecisionHostV1 Host) : IAsyncDisposable
    { public ValueTask DisposeAsync() => Database.DisposeAsync(); }
    private sealed class CreditWorkAuthority : IErpAuthorityRevalidationService
    {
        public bool Allowed = true;
        public List<string> Asked = [];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        { Asked.Add(permission); return Task.FromResult(Allowed ? new PermissionScope(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()) : PermissionScope.Denied); }
    }
    private sealed class CreditWorkDelegation : IWorkDelegationReaderV1
    {
        public bool Allowed = true;
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkDelegationViewV1?>(Allowed ? new(id, CompanyA, item.Kind, ErpPermissions.FinanceCreditsApprove, Guid.NewGuid(), Guid.NewGuid(),
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), "1", Guid.NewGuid(), DateTimeOffset.UtcNow, "SENTETIK", null, null, null) : null);
    }
    private sealed class CreditWorkAfterFlush(Action revoke) : SaveChangesInterceptor
    {
        public bool Flushed;
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            Assert.NotNull(eventData.Context!.Database.CurrentTransaction);
            Assert.False(eventData.Context.ChangeTracker.HasChanges());
            Flushed = true; revoke(); return ValueTask.FromResult(result);
        }
    }
}
