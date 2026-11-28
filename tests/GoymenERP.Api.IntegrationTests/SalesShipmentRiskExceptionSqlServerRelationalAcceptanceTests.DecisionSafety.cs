using System.Data.Common;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesShipmentRiskExceptionSqlServerRelationalAcceptanceTests
{
    // Actual SQL owner/context/collector/writer; authority and provider errors are controlled test probes.
    // These cases do not claim simultaneous deadlock reproduction or production identity acceptance.
    [SqlServerFact]
    public async Task Owned_risk_decisions_keep_native_day_reason_order_version_and_one_original_key()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        {
            var id = await SeedOwnedRisk(run); var key = $"risk-owned-{id:N}";
            await using var safe = RiskDecisionScope(run);
            var first = await RiskDecide(safe, id, key, approve);
            Assert.Equal((approve ? "Approved" : "Rejected", "2", safe.User.Actor, approve ? (DateOnly?)Day : null),
                (first.Exception.State, first.Exception.Version, first.Exception.DecidedBy, first.Exception.ValidOn));
            Assert.Equal(approve ? null : "  SENTETIK ret  ", first.Exception.RejectionReason);
            Assert.Equal(approve, first.Exception.IsValidToday);
            var replay = await RiskDecide(safe, id, key, approve);
            Assert.True(replay.IsReplay); Assert.Equal(first.Exception, replay.Exception); Assert.Equal(first.OriginalCorrelationId, replay.OriginalCorrelationId);
            AssertRiskClean(safe); Assert.All(safe.Authority.Permissions, x => Assert.Equal(SalesPermissionManifestV1.OrdersRiskExceptionApprove, x));
            Assert.Equal((1, 1, 2L, 4L), (await RiskAttemptCount(run, id), await RiskDecisionAuditCount(run, id), await RiskVersion(run, id), await RiskOrderVersion(run, id)));
        }
    }

    [SqlServerFact]
    public async Task Owned_risk_late_authority_abort_rolls_back_decision_attempt_actual_audit_then_same_key_records_once()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false }) foreach (var phase in new[] { "after-audit", "post-save", "precommit" })
        {
            var id = await SeedOwnedRisk(run); var key = $"risk-late-{id:N}"; var save = new RiskDecisionSaveProbe();
            await using (var safe = RiskDecisionScope(run, save))
            {
                var pendingSeen = false; var savedFences = 0; var fired = false;
                safe.Authority.AfterAwait = () =>
                {
                    if (safe.Writer.HasPendingDrafts) pendingSeen = true;
                    // Native Save post-fence1; whole-command post-execute2; final precommit3. No receipt read in this increment.
                    var lateSave = save.Saved && ++savedFences == (phase == "precommit" ? 3 : 1);
                    if (phase == "after-audit" ? pendingSeen && !safe.Writer.HasPendingDrafts && !save.Saved : lateSave)
                    { fired = true; safe.Authority.Granted = false; }
                    return Task.CompletedTask;
                };
                Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => RiskDecide(safe, id, key, approve))).Status);
                Assert.True(fired); Assert.Equal(phase != "after-audit", save.Saved); AssertRiskClean(safe);
            }
            Assert.Equal((0, 0, 1L, 4L), (await RiskAttemptCount(run, id), await RiskDecisionAuditCount(run, id), await RiskVersion(run, id), await RiskOrderVersion(run, id)));
            await using var retry = RiskDecisionScope(run); Assert.False((await RiskDecide(retry, id, key, approve)).IsReplay);
            Assert.Equal((1, 1, 2L), (await RiskAttemptCount(run, id), await RiskDecisionAuditCount(run, id), await RiskVersion(run, id)));
        }
    }

    [SqlServerFact]
    public async Task Owned_risk_postcommit_denial_suppresses_response_but_keeps_original_decision_and_replay_without_second_effect()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        {
            var id = await SeedOwnedRisk(run); var key = $"risk-release-{id:N}"; var commit = new RiskDecisionCommitProbe();
            await using (var safe = RiskDecisionScope(run, commit))
            {
                commit.AfterCommit = () => safe.User.Member = false;
                Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => RiskDecide(safe, id, key, approve))).Status);
                Assert.Equal(1, commit.Commits); AssertRiskClean(safe);
            }
            Assert.Equal((1, 1, 2L), (await RiskAttemptCount(run, id), await RiskDecisionAuditCount(run, id), await RiskVersion(run, id)));
            await using var retry = RiskDecisionScope(run); var replay = await RiskDecide(retry, id, key, approve);
            Assert.True(replay.IsReplay); Assert.Equal("SENTETIK-risk-original", replay.OriginalCorrelationId);
            Assert.Equal((1, 1, 4L), (await RiskAttemptCount(run, id), await RiskDecisionAuditCount(run, id), await RiskOrderVersion(run, id)));
        }
    }

    [SqlServerFact]
    public async Task Owned_risk_terminal_self_or_version_refusal_keeps_native_original_409_once_without_decision_audit()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false }) foreach (var self in new[] { true, false })
        {
            var id = await SeedOwnedRisk(run); var key = $"risk-terminal-{id:N}"; await using var safe = RiskDecisionScope(run);
            if (self) safe.User.Actor = Shipper;
            var request = new SalesDraftStateCommandRequest(self ? "1" : "9", approve ? null : "SENTETIK ret");
            Task<SalesRiskExceptionCommandResult> Execute() => approve
                ? safe.Service.ApproveRiskExceptionAsync(id, 1, request, RiskDecisionMetadata(key))
                : safe.Service.RejectRiskExceptionAsync(id, 1, request, RiskDecisionMetadata(key));
            var first = await Assert.ThrowsAsync<SalesDraftServiceException>(Execute);
            var replay = await Assert.ThrowsAsync<SalesDraftServiceException>(Execute);
            Assert.Equal((409, self ? SalesRiskExceptionProblemCodesV1.SelfDecision : SalesDraftProblemCodesV1.VersionConflict), (first.Status, first.Code));
            Assert.Equal((first.Status, first.Code, true, "SENTETIK-risk-original"), (replay.Status, replay.Code, replay.IsReplay, replay.OriginalCorrelationId));
            Assert.Equal((1, 0, 1L, 4L), (await RiskAttemptCount(run, id), await RiskDecisionAuditCount(run, id), await RiskVersion(run, id), await RiskOrderVersion(run, id)));
            AssertRiskClean(safe);
        }
    }

    [SqlServerFact]
    public async Task Owned_risk_entry_preserves_borrowed_ambient_dirty_and_pending_state_before_and_after_first_authority_await()
    {
        await using var run = await Run.StartAsync(); var id = await SeedOwnedRisk(run);
        foreach (var fault in new[] { "dirty", "pending", "borrowed", "ambient", "late-dirty", "initial-pending-cleaned", "initial-dirty-cleaned" })
        {
            var save = new RiskDecisionSaveProbe(); await using var safe = RiskDecisionScope(run, save);
            var caller = new DocumentCounterRow { CompanyId = CompanyA, Series = "SENTETIK-caller", Next = 1, Version = 1, UpdatedAtUtc = T0 };
            if (fault is "dirty" or "initial-dirty-cleaned") safe.Db.DocumentCounters.Add(caller);
            if (fault is "pending" or "initial-pending-cleaned") safe.Collector.Enqueue(RiskDecisionCallerAudit());
            await using var borrowed = fault == "borrowed" ? await safe.Db.Database.BeginTransactionAsync() : null;
            using var ambient = fault == "ambient" ? new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled) : null;
            safe.Authority.AfterAwait = () =>
            {
                if (safe.Authority.Reads == 1)
                {
                    if (fault == "initial-pending-cleaned") safe.Writer.DiscardPending();
                    if (fault == "initial-dirty-cleaned") safe.Db.Entry(caller).State = EntityState.Detached;
                    if (fault == "late-dirty") safe.Db.DocumentCounters.Add(caller);
                }
                return Task.CompletedTask;
            };
            Assert.Equal(503, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => RiskDecide(safe, id, $"risk-entry-{fault}", true))).Status);
            Assert.Equal(0, save.Saves); Assert.Same(borrowed, safe.Db.Database.CurrentTransaction);
            Assert.Equal(fault is "dirty" or "late-dirty", safe.Db.Entry(caller).State == EntityState.Added);
            Assert.Equal(fault == "pending", safe.Writer.HasPendingDrafts);
            Assert.Equal(0, await RiskAttemptCount(run, id)); Assert.Equal(0, await RiskDecisionAuditCount(run, id));
        }
    }

    [SqlServerFact]
    public async Task Owned_risk_controlled_provider_race_recovers_only_one_readonly_original_key_winner_without_mutation_retry()
    {
        await using var run = await Run.StartAsync();
        foreach (var number in new[] { 1205, 1222, 2601, 2627 }) foreach (var winner in new[] { true, false })
        {
            var id = await SeedOwnedRisk(run); var key = $"risk-race-{id:N}";
            if (winner) { await using var seed = RiskDecisionScope(run); await RiskDecide(seed, id, key, true); }
            var reads = new RiskDecisionKeyProbe(); var save = new RiskDecisionSaveProbe(); await using var safe = RiskDecisionScope(run, reads, save);
            reads.BeforeFirst = () => throw new DbUpdateException("SENTETIK controlled provider error", SqlConcurrencyWrapperPolicyTests.SqlFailure(number));
            if (winner)
            {
                var replay = await RiskDecide(safe, id, key, true); Assert.True(replay.IsReplay); Assert.Equal("SENTETIK-risk-original", replay.OriginalCorrelationId);
            }
            else
            {
                var refusal = await Assert.ThrowsAsync<SalesDraftServiceException>(() => RiskDecide(safe, id, key, true));
                Assert.Equal(number is 2601 or 2627 ? 409 : 503, refusal.Status);
            }
            Assert.Equal(2, reads.Reads); Assert.Equal(0, save.Saves); AssertRiskClean(safe);
            Assert.Equal((winner ? 1 : 0, winner ? 1 : 0, winner ? 2L : 1L), (await RiskAttemptCount(run, id), await RiskDecisionAuditCount(run, id), await RiskVersion(run, id)));
        }
    }

    [SqlServerFact]
    public async Task Owned_risk_controlled_completed_provider_1205_skips_only_redundant_rollback_and_remains_readonly_without_winner()
    {
        await using var run = await Run.StartAsync(); var id = await SeedOwnedRisk(run);
        var reads = new RiskDecisionKeyProbe(); var save = new RiskDecisionSaveProbe(); await using var safe = RiskDecisionScope(run, reads, save);
        reads.BeforeFirst = async () =>
        {
            var transaction = safe.Db.Database.CurrentTransaction!.GetDbTransaction(); await transaction.RollbackAsync();
            Assert.Null(transaction.Connection); throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205);
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => RiskDecide(safe, id, "risk-completed-1205", true))).Status);
        Assert.Equal(2, reads.Reads); Assert.Equal(0, save.Saves); AssertRiskClean(safe);
        Assert.Equal((0, 0, 1L), (await RiskAttemptCount(run, id), await RiskDecisionAuditCount(run, id), await RiskVersion(run, id)));
    }

    [SqlServerFact]
    public async Task Owned_risk_postsave_completed_cancellation_actor_or_membership_loss_rolls_back_without_release()
    {
        await using var run = await Run.StartAsync();
        foreach (var fault in new[] { "cancel", "actor", "membership" })
        {
            var id = await SeedOwnedRisk(run); var save = new RiskDecisionSaveProbe(); using var cancellation = new CancellationTokenSource();
            await using var safe = RiskDecisionScope(run, save); var fired = false;
            safe.Authority.AfterAwait = () =>
            {
                if (save.Saved)
                {
                    fired = true;
                    if (fault == "cancel") cancellation.Cancel();
                    if (fault == "actor") safe.User.Actor = "SENTETIK-other";
                    if (fault == "membership") safe.User.Member = false;
                }
                return Task.CompletedTask;
            };
            Task<SalesRiskExceptionCommandResult> Execute() => safe.Service.ApproveRiskExceptionAsync(id, 1, new("1"), RiskDecisionMetadata($"risk-drift-{id:N}"), cancellation.Token);
            if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(Execute);
            else Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(Execute)).Status);
            Assert.True(fired); Assert.True(save.Saved); AssertRiskClean(safe);
            Assert.Equal((0, 0, 1L, 4L), (await RiskAttemptCount(run, id), await RiskDecisionAuditCount(run, id), await RiskVersion(run, id), await RiskOrderVersion(run, id)));
        }
    }

    [SqlServerFact]
    public async Task Owned_risk_cleanup_fault_blocks_reconciliation_and_successful_abort_rechecks_authority_and_cancellation()
    {
        await using var run = await Run.StartAsync(); var id = await SeedOwnedRisk(run);
        var save = new RiskDecisionSaveProbe { BeforeSave = () => throw new InvalidOperationException("SENTETIK save fault") };
        var cleanup = new RiskDecisionRollbackProbe { Fail = true }; var reads = new RiskDecisionKeyProbe();
        await using (var safe = RiskDecisionScope(run, save, cleanup, reads))
        {
            var failure = await Assert.ThrowsAsync<AggregateException>(() => RiskDecide(safe, id, "risk-cleanup", true));
            Assert.Contains(failure.InnerExceptions, x => x.Message == "SENTETIK rollback fault");
            Assert.Equal(1, cleanup.Rollbacks); Assert.Equal(2, reads.Reads); AssertRiskClean(safe);
        }
        Assert.Equal((0, 0, 1L), (await RiskAttemptCount(run, id), await RiskDecisionAuditCount(run, id), await RiskVersion(run, id)));
        foreach (var fault in new[] { "unchanged", "permission", "cancel" })
        {
            var invalid = await SeedOwnedRisk(run); using var cancellation = new CancellationTokenSource();
            var release = new RiskDecisionRollbackProbe(); await using var safe = RiskDecisionScope(run, release);
            release.AfterRollback = () => { if (fault == "permission") safe.Authority.Granted = false; if (fault == "cancel") cancellation.Cancel(); };
            Task<SalesRiskExceptionCommandResult> Reject() => safe.Service.RejectRiskExceptionAsync(invalid, 1, new("1", null), RiskDecisionMetadata($"risk-invalid-{invalid:N}"), cancellation.Token);
            if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(Reject);
            else Assert.Equal(fault == "unchanged" ? 422 : 403, (await Assert.ThrowsAsync<SalesDraftServiceException>(Reject)).Status);
            Assert.Equal(1, release.Rollbacks); AssertRiskClean(safe);
            Assert.Equal((0, 0, 1L), (await RiskAttemptCount(run, invalid), await RiskDecisionAuditCount(run, invalid), await RiskVersion(run, invalid)));
        }
    }

    [SqlServerFact]
    public async Task Owned_risk_final_save_race_without_winner_keeps_native_409_and_original_key_available()
    {
        await using var run = await Run.StartAsync();
        foreach (var terminal in new[] { true, false }) foreach (var number in new[] { 0, 2601, 2627 })
        {
            var id = await SeedOwnedRisk(run); var key = $"risk-final-race-{id:N}"; var reads = new RiskDecisionKeyProbe();
            var save = new RiskDecisionSaveProbe { BeforeSave = () =>
            {
                if (number == 0) throw new DbUpdateConcurrencyException("SENTETIK final concurrency");
                throw new DbUpdateException("SENTETIK final unique", SqlConcurrencyWrapperPolicyTests.SqlFailure(number));
            } };
            await using (var safe = RiskDecisionScope(run, save, reads))
            {
                if (terminal) safe.User.Actor = Shipper;
                var refusal = await Assert.ThrowsAsync<SalesDraftServiceException>(() => RiskDecide(safe, id, key, true));
                Assert.Equal((409, SalesDraftProblemCodesV1.ConcurrentChange), (refusal.Status, refusal.Code));
                Assert.Equal(1, save.Saves); Assert.Equal(terminal ? 4 : 3, reads.Reads); AssertRiskClean(safe);
            }
            Assert.Equal((0, 0, 1L), (await RiskAttemptCount(run, id), await RiskDecisionAuditCount(run, id), await RiskVersion(run, id)));
            await using var retry = RiskDecisionScope(run); Assert.False((await RiskDecide(retry, id, key, true)).IsReplay);
            Assert.Equal((1, 1, 2L), (await RiskAttemptCount(run, id), await RiskDecisionAuditCount(run, id), await RiskVersion(run, id)));
        }
    }

    [SqlServerFact]
    public async Task Owned_risk_savedchanges_domain_failure_after_acceptallchanges_rolls_back_instead_of_recording_a_false_terminal()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        {
            var id = await SeedOwnedRisk(run); var key = $"risk-accepted-then-fault-{id:N}";
            var save = new RiskDecisionSaveProbe { AfterSaved = () => throw new SalesRuleException(SalesRuleCodes.StateConflict, "SENTETIK after EF accepted") };
            await using (var safe = RiskDecisionScope(run, save))
            {
                Assert.Equal(503, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => RiskDecide(safe, id, key, approve))).Status);
                Assert.Equal(1, save.Saves); Assert.True(save.Saved); AssertRiskClean(safe);
            }
            Assert.Equal((0, 0, 1L), (await RiskAttemptCount(run, id), await RiskDecisionAuditCount(run, id), await RiskVersion(run, id)));
            await using var retry = RiskDecisionScope(run); var first = await RiskDecide(retry, id, key, approve);
            Assert.False(first.IsReplay); Assert.True((await RiskDecide(retry, id, key, approve)).IsReplay);
            Assert.Equal((1, 1, 2L), (await RiskAttemptCount(run, id), await RiskDecisionAuditCount(run, id), await RiskVersion(run, id)));
        }
    }

    [SqlServerFact]
    public async Task Owned_risk_missing_actual_writer_refuses_before_attempt_or_order_IO_without_adopting_caller_state()
    {
        await using var run = await Run.StartAsync(); var id = await SeedOwnedRisk(run);
        var reads = new RiskDecisionKeyProbe(); var save = new RiskDecisionSaveProbe(); await using var safe = RiskDecisionScope(run, reads, save);
        var company = run.Company(CompanyA);
        var raw = new SalesStore(safe.Db, company, safe.Collector, new Clock(T0));
        var service = new SalesRiskExceptionService(company, safe.User, safe.Authority, new RiskDecisionCorrelation(), raw, raw, new Clock(T0)).WithRiskExceptionDecisionSafety();
        Assert.Equal(503, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => service.ApproveRiskExceptionAsync(id, 1, new("1"), RiskDecisionMetadata("risk-no-writer")))).Status);
        Assert.Equal(0, reads.Reads); Assert.Equal(0, reads.OrderReads); Assert.Equal(0, save.Saves); AssertRiskClean(safe);
        Assert.Equal((0, 0, 1L, 4L), (await RiskAttemptCount(run, id), await RiskDecisionAuditCount(run, id), await RiskVersion(run, id), await RiskOrderVersion(run, id)));
    }

    private static async Task<Guid> SeedOwnedRisk(Run run)
    {
        var id = (await run.ApprovedDraftAsync(CompanyA, "risk-owned-seed-" + Guid.NewGuid().ToString("N"))).Id;
        var clock = new Clock(T0); await using var scope = run.SalesStore(CompanyA, clock);
        var requested = await run.Service(scope.Store, CompanyA, Shipper, clock).RequestRiskExceptionAsync(id,
            new(Shipment, "SENTETIK risk kabul olgusu"), Metadata("risk-request-" + id.ToString("N")));
        Assert.Equal(("Requested", "1"), (requested.Exception.State, requested.Exception.Version));
        return id;
    }
    private static SalesCommandMetadata RiskDecisionMetadata(string key) => new(key, "SENTETIK-risk-original", null, T0);
    private static Task<SalesRiskExceptionCommandResult> RiskDecide(RiskDecisionScopeState safe, Guid id, string key, bool approve) => approve
        ? safe.Service.ApproveRiskExceptionAsync(id, 1, new("1"), RiskDecisionMetadata(key))
        : safe.Service.RejectRiskExceptionAsync(id, 1, new("1", "  SENTETIK ret  "), RiskDecisionMetadata(key));
    private static Task<int> RiskAttemptCount(Run run, Guid id) => run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.ShipmentRiskExceptionAttempts WHERE OrderId=@id AND CommandName IN (N'sales.order.risk-exception.approve.v1',N'sales.order.risk-exception.reject.v1')", ("@id", id));
    private static Task<long> RiskVersion(Run run, Guid id) => run.ScalarAsync<long>("SELECT Version FROM sales.ShipmentRiskExceptions WHERE OrderId=@id AND ExceptionNo=1", ("@id", id));
    private static Task<long> RiskOrderVersion(Run run, Guid id) => run.ScalarAsync<long>("SELECT Version FROM sales.Drafts WHERE Id=@id", ("@id", id));
    private static Task<int> RiskDecisionAuditCount(Run run, Guid id) => run.ScalarAsync<int>(
        "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType IN (N'sales.order.risk-exception.approved.v1',N'sales.order.risk-exception.rejected.v1') AND EntityId IN (SELECT CONVERT(nvarchar(36),Id) FROM sales.ShipmentRiskExceptions WHERE OrderId=@id)", ("@id", id));
    private static void AssertRiskClean(RiskDecisionScopeState safe)
    { Assert.DoesNotContain(safe.Db.ChangeTracker.Entries(), x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted); Assert.False(safe.Writer.HasPendingDrafts); Assert.Null(safe.Db.Database.CurrentTransaction); }

    private static RiskDecisionScopeState RiskDecisionScope(Run run, params IInterceptor[] interceptors)
    {
        var scope = run.Factory.Services.CreateAsyncScope(); var p = scope.ServiceProvider;
        var company = p.GetRequiredService<ICompanyContext>(); company.Set(CompanyA);
        var collector = p.GetRequiredService<AuditEventCollector>(); Assert.Same(collector, p.GetRequiredService<IAuditEventCollector>());
        var writer = p.GetRequiredService<IAuditTransactionWriterV1>();
        var options = new DbContextOptionsBuilder<SalesDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
            .AddInterceptors(new AuditSaveChangesInterceptor(collector, company)).AddInterceptors(interceptors);
        var db = new SalesDbContext(options.Options, company); var store = new SalesStore(db, company, collector, new Clock(T0), officialCancellationAuditWriter: writer);
        var authority = new RiskDecisionAuthority(); var user = new RiskDecisionUser();
        var service = new SalesRiskExceptionService(company, user, authority, new RiskDecisionCorrelation(), store, store, new Clock(T0)).WithRiskExceptionDecisionSafety();
        return new(scope, db, service, authority, user, writer, collector);
    }
    private sealed class RiskDecisionScopeState(AsyncServiceScope scope, SalesDbContext db, SalesRiskExceptionService service, RiskDecisionAuthority authority, RiskDecisionUser user, IAuditTransactionWriterV1 writer, AuditEventCollector collector) : IAsyncDisposable
    {
        internal SalesDbContext Db => db; internal SalesRiskExceptionService Service => service; internal RiskDecisionAuthority Authority => authority;
        internal RiskDecisionUser User => user; internal IAuditTransactionWriterV1 Writer => writer; internal AuditEventCollector Collector => collector;
        public async ValueTask DisposeAsync() { await db.DisposeAsync(); await scope.DisposeAsync(); }
    }
    private sealed class RiskDecisionCorrelation : ICorrelationContext
    {
        public bool IsResolved => true; public string CorrelationId => "SENTETIK-risk-original"; public string? CausationId => null;
        public string SourceChannel => "test"; public string? ClientIpAddress => null; public string? UserAgent => null;
        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress, string? userAgent) => throw new NotSupportedException();
    }
    private sealed class RiskDecisionUser : ICurrentUser
    {
        internal string Actor = "SENTETIK-risk-independent-checker"; internal bool Member = true;
        public bool IsAuthenticated => true; public string UserId => Actor;
        public IReadOnlySet<Guid> AllowedCompanyIds => Member ? new HashSet<Guid>([CompanyA]) : new HashSet<Guid>();
        public bool CanAccessCompany(Guid id) => Member && id == CompanyA;
    }
    private sealed class RiskDecisionAuthority : IErpAuthorityRevalidationService
    {
        internal bool Granted = true; internal int Reads; internal Func<Task>? AfterAwait; internal List<string> Permissions = [];
        public async Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            Assert.Equal(CompanyA, companyId); Reads++; Permissions.Add(permission); await Task.Yield(); if (AfterAwait is not null) await AfterAwait();
            return new(Granted, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
        }
    }
    private sealed class RiskDecisionSaveProbe : SaveChangesInterceptor
    {
        internal bool Saved; internal int Saves; internal Action? BeforeSave; internal Action? AfterSaved;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Saves++; BeforeSave?.Invoke(); return ValueTask.FromResult(result); }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken cancellationToken = default)
        { Saved = true; AfterSaved?.Invoke(); return ValueTask.FromResult(result); }
    }
    private sealed class RiskDecisionCommitProbe : DbTransactionInterceptor
    {
        internal int Commits; internal Action? AfterCommit;
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData data, CancellationToken cancellationToken = default)
        { Commits++; AfterCommit?.Invoke(); return Task.CompletedTask; }
    }
    private sealed class RiskDecisionRollbackProbe : DbTransactionInterceptor
    {
        internal bool Fail; internal int Rollbacks; internal Action? AfterRollback;
        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction transaction, TransactionEventData data, InterceptionResult result, CancellationToken cancellationToken = default)
        { Rollbacks++; if (Fail) throw new InvalidOperationException("SENTETIK rollback fault"); return ValueTask.FromResult(result); }
        public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData data, CancellationToken cancellationToken = default)
        { AfterRollback?.Invoke(); return Task.CompletedTask; }
    }
    private sealed class RiskDecisionKeyProbe : DbCommandInterceptor
    {
        internal int Reads; internal int OrderReads; internal Func<Task>? BeforeFirst;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM [sales].[Drafts]", StringComparison.Ordinal)) OrderReads++;
            if (command.CommandText.Contains("ShipmentRiskExceptionAttempts", StringComparison.Ordinal) && ++Reads == 1 && BeforeFirst is not null) await BeforeFirst();
            return result;
        }
    }
    private static AuditEventDraft RiskDecisionCallerAudit() => new(CompanyA, "SENTETIK.risk.caller.v1", "Sales.ShipmentRiskException", "SENTETIK", "Caller", null, null,
        AuditActorTypes.User, "SENTETIK", SalesPermissionManifestV1.OrdersRiskExceptionApprove, [], "{}", "{}", "SENTETIK caller", null, AuditAiInvolvement.None, null, "test", null, null, "SENTETIK", null, "SENTETIK-risk-caller");
}
