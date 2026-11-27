using System.Transactions;
using System.Data.Common;
using GoymenERP.Api.LogisticsRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Logistics.Application;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class LogisticsCustomerReturnSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Native_customer_return_request_reject_and_original_request_replay_preserve_real_audit_and_immutable_snapshots()
    {
        await using var run = await Run.StartAsync(); var source = await run.CreateDeliveredSourceAsync(100);
        await using var frame = run.DecisionFrame();
        var request = new RequestCustomerReturnV1(Guid.NewGuid(), source.DeliveryId, "6", "25", "SYNTHETIC native return", T0);
        var requested = await frame.Service.RequestAsync(source.InstructionId, source.LineId, request, ReturnMetadata("native-request"));
        Assert.Equal(("Pending", "1"), (requested.Return.Status, requested.Return.Version));
        frame.User.Actor = "different-decider";
        var decision = await frame.Service.RejectAsync(request.CustomerReturnId, new("1", "SYNTHETIC note"), ReturnMetadata("native-reject"));
        var replay = await frame.Service.RejectAsync(request.CustomerReturnId, new("1", "SYNTHETIC note"), ReturnMetadata("native-reject") with { CorrelationId = "later-correlation" });
        Assert.Equal(("Rejected", "2", false), (decision.Return.Status, decision.Return.Version, decision.IsReplay));
        Assert.True(replay.IsReplay); Assert.Equal(decision.Return, replay.Return); Assert.Equal(decision.OriginalCorrelationId, replay.OriginalCorrelationId);
        frame.User.Actor = "safety-decider";
        var original = await frame.Service.RequestAsync(source.InstructionId, source.LineId, request, ReturnMetadata("native-request"));
        Assert.True(original.IsReplay); Assert.Equal(("Pending", "1"), (original.Return.Status, original.Return.Version));
        Assert.Equal((1, 2), await run.CountsAsync()); Assert.Equal(2, await run.DecisionAuditCount());
        Assert.False(frame.Writer.HasPendingDrafts); Assert.False(frame.Db.ChangeTracker.HasChanges());
    }

    [SqlServerFact]
    public async Task Customer_return_shared_entry_refuses_dirty_borrowed_ambient_and_pending_caller_without_cleanup_or_replay_reads()
    {
        await using var run = await Run.StartAsync(); var source = await run.CreateDeliveredSourceAsync(100);
        foreach (var fault in new[] { "dirty", "borrowed", "ambient", "pending-audit" })
        {
            await using var frame = run.DecisionFrame();
            var row = await frame.Db.Instructions.SingleAsync(value => value.Id == source.InstructionId);
            if (fault == "dirty") row.Version++;
            if (fault == "pending-audit") frame.Collector.Enqueue(ReturnCallerAudit(source.InstructionId));
            await using var borrowed = fault == "borrowed" ? await frame.Db.Database.BeginTransactionAsync() : null;
            using var ambient = fault == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
            var calls = 0;
            foreach (var approving in new[] { false, true })
            {
                Task<CustomerReturnReplayV1?> Read(CancellationToken token)
                { calls++; return frame.Store.FindReplayAsync(run.Envelope("entry-replay", frame.User.Actor), Hash("entry"),
                    approving ? LogisticsCustomerReturnCommandsV1.Approve : LogisticsCustomerReturnCommandsV1.Reject, token); }
                Assert.Equal(503, (await Assert.ThrowsAsync<LogisticsCustomerReturnServiceException>(() => approving
                    ? frame.Runner.ExecuteApprovalAsync(Company, (_, token) => Read(token), (_, _) => { calls++; return Task.CompletedTask; })
                    : frame.Runner.ExecuteLogisticsAsync(Company, Read, _ => { calls++; return Task.CompletedTask; }))).Status);
            }
            Assert.Equal(0, calls);
            Assert.Same(row, Assert.Single(frame.Db.ChangeTracker.Entries()).Entity);
            Assert.Equal(fault == "dirty", frame.Db.ChangeTracker.HasChanges());
            Assert.Equal(fault == "pending-audit", frame.Writer.HasPendingDrafts);
            if (borrowed is not null) Assert.Same(borrowed, frame.Db.Database.CurrentTransaction);
        }
        Assert.Equal((0, 0), await run.CountsAsync());
    }

    [SqlServerFact]
    public async Task Native_customer_return_post_save_permission_company_or_actor_loss_rolls_back_decision_and_real_audit()
    {
        await using var run = await Run.StartAsync(); var source = await run.CreateDeliveredSourceAsync(100);
        foreach (var fault in new[] { "permission", "company", "actor" })
        {
            var probe = new ReturnDecisionProbe(); await using var frame = run.DecisionFrame(probe);
            var id = Guid.NewGuid();
            await frame.Service.RequestAsync(source.InstructionId, source.LineId, new(id, source.DeliveryId, "6", "5", "SYNTHETIC return", T0), ReturnMetadata("request-" + id));
            frame.User.Actor = "other-decider";
            var before = await run.CountsAsync(); var audits = await run.DecisionAuditCount();
            probe.AfterSaved = () =>
            {
                if (fault == "permission") frame.Authority.Allowed = false;
                else if (fault == "company") frame.Company.Set(TestCompanyIds.CompanyB);
                else frame.User.Actor = "changed-actor";
            };
            Assert.Equal(403, (await Assert.ThrowsAsync<LogisticsCustomerReturnServiceException>(() => frame.Service.RejectAsync(id, new("1"), ReturnMetadata("reject-" + id)))).Status);
            Assert.Equal(1, probe.SavedDecisions); Assert.Equal(before, await run.CountsAsync()); Assert.Equal(audits, await run.DecisionAuditCount());
            Assert.Empty(frame.Db.ChangeTracker.Entries()); Assert.False(frame.Writer.HasPendingDrafts);
            frame.Authority.Allowed = true; frame.Company.Set(Company); frame.User.Actor = "other-decider"; probe.AfterSaved = null;
            var retry = await frame.Service.RejectAsync(id, new("1"), ReturnMetadata("reject-" + id));
            Assert.False(retry.IsReplay); Assert.Equal("Rejected", retry.Return.Status);
        }
    }

    [SqlServerFact]
    public async Task Customer_return_first_save_concurrency_has_only_one_mutation_and_no_synthetic_winner_receipt_or_audit()
    {
        await using var run = await Run.StartAsync(); var source = await run.CreateDeliveredSourceAsync(100);
        var probe = new ReturnDecisionProbe(); await using var frame = run.DecisionFrame(probe);
        var id = Guid.NewGuid(); await frame.Service.RequestAsync(source.InstructionId, source.LineId,
            new(id, source.DeliveryId, "6", "5", "SYNTHETIC return", T0), ReturnMetadata("race-request"));
        frame.User.Actor = "other-decider"; probe.Concurrency = true;
        var before = await run.CountsAsync(); var audits = await run.DecisionAuditCount();
        Assert.Equal(LogisticsStoreCodes.ConcurrentChange, (await Assert.ThrowsAsync<LogisticsStoreException>(() => frame.Service.RejectAsync(id, new("1"), ReturnMetadata("no-winner")))).Code);
        Assert.Equal(1, probe.SaveAttempts); Assert.Equal(before, await run.CountsAsync()); Assert.Equal(audits, await run.DecisionAuditCount());
        Assert.Empty(frame.Db.ChangeTracker.Entries()); Assert.False(frame.Writer.HasPendingDrafts);
    }

    [SqlServerFact]
    public async Task Customer_return_actual_save_failure_with_rollback_or_dispose_fault_still_clears_only_owned_tracker_and_audit()
    {
        await using var run = await Run.StartAsync(); var source = await run.CreateDeliveredSourceAsync(100);
        foreach (var disposal in new[] { false, true })
        {
            var cleanup = new ReturnCleanupFault(disposal); var probe = new ReturnDecisionProbe();
            await using var frame = run.DecisionFrame(probe, cleanup);
            var id = Guid.NewGuid(); await frame.Service.RequestAsync(source.InstructionId, source.LineId,
                new(id, source.DeliveryId, "6", "5", "SYNTHETIC return", T0), ReturnMetadata("cleanup-request-" + id));
            var before = await run.CountsAsync(); var audits = await run.DecisionAuditCount(); frame.User.Actor = "other-decider";
            probe.ThrowSaving = () => { frame.Collector.Enqueue(ReturnCallerAudit(id)); cleanup.Armed = true; };
            Assert.Equal(503, (await Assert.ThrowsAsync<LogisticsCustomerReturnServiceException>(() => frame.Service.RejectAsync(id, new("1"), ReturnMetadata("cleanup-reject-" + id)))).Status);
            Assert.Equal(1, probe.SaveAttempts); Assert.True(cleanup.Failed);
            Assert.Empty(frame.Db.ChangeTracker.Entries()); Assert.False(frame.Writer.HasPendingDrafts);
            Assert.Equal(before, await run.CountsAsync()); Assert.Equal(audits, await run.DecisionAuditCount());
        }
    }

    private static LogisticsCommandMetadata ReturnMetadata(string key) => new(key, "return-safety-correlation", null, DateTimeOffset.UtcNow);
    private static AuditEventDraft ReturnCallerAudit(Guid id) => new(Company, "logistics.synthetic.caller.v1", "Logistics.CustomerReturn", id.ToString("D"),
        "Caller", 1, 1, AuditActorTypes.User, "caller", "synthetic.permission", [], "{}", "{}", "SYNTHETIC caller-owned draft", null,
        AuditAiInvolvement.None, null, "Web", null, null, "return-safety-correlation", null, "caller-key");

    private sealed class ReturnDecisionProbe : SaveChangesInterceptor
    {
        public bool Concurrency; public int SaveAttempts, SavedDecisions; public Action? AfterSaved, ThrowSaving;
        private static bool Decision(DbContext? db) => db is LogisticsDbContext context
            && context.ChangeTracker.Entries<CustomerReturnRow>().Any(row => row.Entity.Version == 2);
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default)
        {
            if (Decision(data.Context))
            {
                SaveAttempts++;
                if (Concurrency) throw new DbUpdateConcurrencyException("SYNTHETIC actual first decision Save concurrency");
                if (ThrowSaving is { } failure) { failure(); throw new InvalidOperationException("SYNTHETIC actual decision SavingChanges failure"); }
            }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default)
        { if (Decision(data.Context)) { SavedDecisions++; AfterSaved?.Invoke(); } return ValueTask.FromResult(result); }
    }

    // Existing SQL cleanup-fault pattern: diagnostics inject a one-shot failure after real disposal;
    // EF has a rollback interceptor but no transaction-dispose interceptor override.
    private sealed class ReturnCleanupFault(bool dispose) : DbTransactionInterceptor
    {
        public bool Armed, Failed;
        public void Configure(DbContextOptionsBuilder<LogisticsDbContext> options) => options.LogTo(_ =>
        {
            if (Armed && dispose && !Failed) { Failed = true; throw new InvalidOperationException("SYNTHETIC real transaction disposed fault"); }
        }, (id, _) => id == RelationalEventId.TransactionDisposed);
        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction transaction, TransactionEventData data,
            InterceptionResult result, CancellationToken token = default)
        {
            if (Armed && !dispose && !Failed) { Failed = true; throw new InvalidOperationException("SYNTHETIC real transaction rollback fault"); }
            return ValueTask.FromResult(result);
        }
    }

    private sealed record ReturnDecisionFrame(LogisticsDbContext Db, LogisticsCustomerReturnStore Store, ICompanyContext Company,
        AuditEventCollector Collector, IAuditTransactionWriterV1 Writer, LogisticsCustomerReturnDecisionSafetyTests.SafetyUser User,
        LogisticsCustomerReturnDecisionSafetyTests.SafetyAuthority Authority, LogisticsCustomerReturnTransactionRunner Runner,
        LogisticsCustomerReturnService Service, IServiceScope Scope) : IAsyncDisposable
    { public async ValueTask DisposeAsync() { await Db.DisposeAsync(); Scope.Dispose(); } }

    private sealed partial class Run
    {
        public Task<int> DecisionAuditCount() => ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType LIKE N'logistics.customer-return.%'");
        public ReturnDecisionFrame DecisionFrame(IInterceptor? extra = null, ReturnCleanupFault? cleanup = null,
            Func<IErpAuthorityRevalidationService, IErpAuthorityRevalidationService>? decorate = null)
        {
            var scope = factory.Services.CreateScope(); var company = scope.ServiceProvider.GetRequiredService<ICompanyContext>(); company.Set(Company);
            var collector = scope.ServiceProvider.GetRequiredService<AuditEventCollector>(); var writer = scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
            var options = new DbContextOptionsBuilder<LogisticsDbContext>().UseSqlServer(factory.DatabaseConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, company));
            if (extra is not null) options.AddInterceptors(extra);
            if (cleanup is not null) { options.AddInterceptors(cleanup); cleanup.Configure(options); }
            var db = new LogisticsDbContext(options.Options, company); var user = new LogisticsCustomerReturnDecisionSafetyTests.SafetyUser();
            var authority = new LogisticsCustomerReturnDecisionSafetyTests.SafetyAuthority();
            var nativeAuthority = decorate?.Invoke(authority) ?? authority;
            var runner = new LogisticsCustomerReturnTransactionRunner(db, scope.ServiceProvider, company, user, nativeAuthority, TimeProvider.System);
            var store = new LogisticsCustomerReturnStore(db, company, collector, TimeProvider.System);
            var correlation = new CorrelationContext(); correlation.Set("return-safety-correlation", null, "Web", null, null);
            var service = new LogisticsCustomerReturnService(new LogisticsCustomerReturnScope(company, user, nativeAuthority),
                new LogisticsCustomerReturnSourceReader(db, company), store, runner,
                new(LogisticsPermissionManifestV1.CustomerReturnsRequest, LogisticsPermissionManifestV1.CustomerReturnsDecision,
                    LogisticsPermissionManifestV1.CustomerReturnsRead, ErpPermissions.InventoryPhysicalAllocationWrite), correlation, TimeProvider.System);
            return new(db, store, company, collector, writer, user, authority, runner, service, scope);
        }
    }
}
