using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesOrderOverDeliveryApprovalSqlServerRelationalAcceptanceTests
{
    // Actual SQL owner/writer/transaction, controlled authority/interceptor faults. Not actual deadlock reproduction.
    [SqlServerFact]
    public async Task Owned_over_delivery_request_decisions_preserve_versions_raw_reason_seen_total_and_original_key_once()
    {
        await using var run = await Run.StartAsync(owner: null);
        foreach (var approve in new[] { true, false })
        {
            var id = await SeedOwnedOverRequest(run); var key = "over-owned-" + id.ToString("N");
            await using var safe = OverRequestScope(run);
            var first = await OverRequestDecide(safe, run, id, key, approve);
            Assert.Equal((approve ? "Approved" : "Rejected", "2", safe.User.Actor),
                (first.Request.Status, first.Request.Version, first.Request.DecidedBy));
            Assert.Equal("102.5", first.Request.RequestedTotal); Assert.Equal("100", first.Request.Ordered);
            Assert.Equal(approve ? null : "  SENTETIK ret  ", first.Request.DecisionReason);
            var replay = await OverRequestDecide(safe, run, id, key, approve);
            Assert.True(replay.IsReplay); Assert.Equal(first.Request, replay.Request); Assert.Equal(first.CorrelationId, replay.CorrelationId);
            AssertOverRequestClean(safe);
            Assert.Equal((1, approve ? 1 : 0, approve ? 5L : 4L),
                (await OverRequestAttemptCount(run, id), await OverRequestApprovalCount(run, id), await OverRequestVersion(run, id)));
            Assert.Equal(approve ? 2 : 1, await OverRequestAuditCount(run, id));
        }
    }

    [SqlServerFact]
    public async Task Owned_over_delivery_request_late_authority_abort_rolls_back_audit_attempt_and_approval_then_same_key_decides_once()
    {
        await using var run = await Run.StartAsync(owner: null);
        foreach (var approve in new[] { true, false }) foreach (var phase in new[] { "after-audit", "post-save", "precommit" })
        {
            var id = await SeedOwnedOverRequest(run); var key = $"over-late-{id:N}"; var save = new OverRequestSaveProbe();
            await using (var safe = OverRequestScope(run, save))
            {
                var pendingSeen = false; var savedFences = 0; var fired = false;
                safe.Authority.AfterAwait = () =>
                {
                    if (safe.Writer.HasPendingDrafts) pendingSeen = true;
                    // Native Save post-fence1; whole-command post-execute2; final precommit3. No receipt IO in this increment.
                    var lateSave = save.Saved && ++savedFences == (phase == "precommit" ? 3 : 1);
                    if (phase == "after-audit" ? pendingSeen && !safe.Writer.HasPendingDrafts && !save.Saved : lateSave)
                    { fired = true; safe.Authority.Granted = false; }
                    return Task.CompletedTask;
                };
                var denial = await Assert.ThrowsAsync<SalesDraftServiceException>(() => OverRequestDecide(safe, run, id, key, approve));
                Assert.Equal(403, denial.Status); Assert.True(fired); Assert.Equal(phase != "after-audit", save.Saved); AssertOverRequestClean(safe);
            }
            Assert.Equal((0, 0, 0, 4L), (await OverRequestAttemptCount(run, id), await OverRequestAuditCount(run, id),
                await OverRequestApprovalCount(run, id), await OverRequestVersion(run, id)));
            await using var retry = OverRequestScope(run); Assert.False((await OverRequestDecide(retry, run, id, key, approve)).IsReplay);
            Assert.Equal(1, await OverRequestAttemptCount(run, id)); Assert.Equal(approve ? 1 : 0, await OverRequestApprovalCount(run, id));
        }
    }

    [SqlServerFact]
    public async Task Owned_over_delivery_request_postcommit_denial_keeps_durable_original_key_without_second_effect()
    {
        await using var run = await Run.StartAsync(owner: null);
        foreach (var approve in new[] { true, false })
        {
            var id = await SeedOwnedOverRequest(run); var key = $"over-release-{id:N}"; var commit = new OverRequestCommitProbe();
            await using (var safe = OverRequestScope(run, commit))
            {
                commit.AfterCommit = () => safe.User.Member = false;
                Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => OverRequestDecide(safe, run, id, key, approve))).Status);
                Assert.Equal(1, commit.Commits); AssertOverRequestClean(safe);
            }
            Assert.Equal(1, await OverRequestAttemptCount(run, id));
            await using var retry = OverRequestScope(run); var replay = await OverRequestDecide(retry, run, id, key, approve);
            Assert.True(replay.IsReplay); Assert.Equal("SENTETIK-over-original", replay.CorrelationId);
            Assert.Equal((1, approve ? 1 : 0), (await OverRequestAttemptCount(run, id), await OverRequestApprovalCount(run, id)));
        }
    }

    [SqlServerFact]
    public async Task Owned_over_delivery_request_native_terminal_self_or_version_refusal_remains_one_original_409_without_audit()
    {
        await using var run = await Run.StartAsync(owner: null);
        foreach (var approve in new[] { true, false }) foreach (var self in new[] { true, false })
        {
            var id = await SeedOwnedOverRequest(run); var key = $"over-terminal-{id:N}";
            await using var safe = OverRequestScope(run); if (self) safe.User.Actor = Shipper;
            var request = new SalesDraftStateCommandRequest(self ? "1" : "9", approve ? null : "SENTETIK ret");
            Task<SalesApprovalRequestCommandResult> Execute() => approve
                ? safe.Service.ApproveOverDeliveryRequestAsync(id, 1, request, OverRequestMetadata(key))
                : safe.Service.RejectOverDeliveryRequestAsync(id, 1, request, OverRequestMetadata(key));
            var first = await Assert.ThrowsAsync<SalesDraftServiceException>(Execute);
            var replay = await Assert.ThrowsAsync<SalesDraftServiceException>(Execute);
            Assert.Equal((409, self ? SalesApprovalRequestProblemCodesV1.SelfDecision : SalesDraftProblemCodesV1.VersionConflict), (first.Status, first.Code));
            Assert.Equal((first.Status, first.Code, true, "SENTETIK-over-original"), (replay.Status, replay.Code, replay.IsReplay, replay.OriginalCorrelationId));
            Assert.Equal((1, 0, 0, 4L), (await OverRequestAttemptCount(run, id), await OverRequestAuditCount(run, id), await OverRequestApprovalCount(run, id), await OverRequestVersion(run, id)));
            AssertOverRequestClean(safe);
        }
    }

    [SqlServerFact]
    public async Task Owned_over_delivery_request_entry_preserves_initial_and_during_await_caller_state_even_when_it_becomes_clean()
    {
        await using var run = await Run.StartAsync(owner: null); var id = await SeedOwnedOverRequest(run);
        foreach (var fault in new[] { "dirty", "pending", "borrowed", "ambient", "late-dirty", "initial-pending-cleaned", "initial-dirty-cleaned" })
        {
            var save = new OverRequestSaveProbe(); await using var safe = OverRequestScope(run, save);
            var caller = new DocumentCounterRow { CompanyId = CompanyA, Series = "SENTETIK-caller", Next = 1, Version = 1, UpdatedAtUtc = T0 };
            if (fault is "dirty" or "initial-dirty-cleaned") safe.Db.DocumentCounters.Add(caller);
            if (fault is "pending" or "initial-pending-cleaned") safe.Collector.Enqueue(OverRequestCallerAudit());
            await using var borrowed = fault == "borrowed" ? await safe.Db.Database.BeginTransactionAsync() : null;
            using var ambient = fault == "ambient" ? new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled) : null;
            safe.Authority.AfterAwait = () =>
            {
                if (safe.Authority.Reads == 1)
                {
                    if (fault == "initial-pending-cleaned") safe.Writer.DiscardPending(); // Caller explicitly clears its own work.
                    if (fault == "initial-dirty-cleaned") safe.Db.Entry(caller).State = EntityState.Detached;
                    if (fault == "late-dirty") safe.Db.DocumentCounters.Add(caller);
                }
                return Task.CompletedTask;
            };
            Assert.Equal(503, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => OverRequestDecide(safe, run, id, "over-entry-" + fault, true))).Status);
            Assert.Equal(0, save.Saves);
            if (fault is "dirty" or "late-dirty") Assert.Same(caller, Assert.Single(safe.Db.ChangeTracker.Entries()).Entity);
            if (fault == "pending") Assert.True(safe.Writer.HasPendingDrafts);
            if (fault == "borrowed") Assert.Same(borrowed, safe.Db.Database.CurrentTransaction);
        }
        Assert.Equal(0, await OverRequestAttemptCount(run, id)); Assert.Equal(0, await OverRequestApprovalCount(run, id));
    }

    [SqlServerFact]
    public async Task Owned_over_delivery_request_failed_save_has_no_standalone_terminal_attempt_and_retry_can_decide_once()
    {
        await using var run = await Run.StartAsync(owner: null);
        foreach (var approve in new[] { true, false }) foreach (var after in new[] { true, false })
        {
            var id = await SeedOwnedOverRequest(run); var key = $"over-save-fault-{id:N}"; var save = new OverRequestSaveProbe();
            Action fail = () => throw new GoymenERP.Modules.Sales.Domain.SalesRuleException(GoymenERP.Modules.Sales.Domain.SalesRuleCodes.StateConflict, "SENTETIK intercepted save");
            if (after) save.AfterSaved = fail; else save.BeforeSave = fail;
            await using (var safe = OverRequestScope(run, save))
            {
                Assert.Equal(503, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => OverRequestDecide(safe, run, id, key, approve))).Status);
                Assert.Equal(1, save.Saves); AssertOverRequestClean(safe);
            }
            Assert.Equal((0, 0, 0), (await OverRequestAttemptCount(run, id), await OverRequestAuditCount(run, id), await OverRequestApprovalCount(run, id)));
            await using var retry = OverRequestScope(run); Assert.False((await OverRequestDecide(retry, run, id, key, approve)).IsReplay);
            Assert.Equal(1, await OverRequestAttemptCount(run, id));
        }
    }

    [SqlServerFact]
    public async Task Owned_over_delivery_request_controlled_1205_1222_only_reads_the_genuine_original_key_after_abort()
    {
        await using var run = await Run.StartAsync(owner: null);
        foreach (var approve in new[] { true, false }) foreach (var number in new[] { 1205, 1222 }) foreach (var wrapped in new[] { true, false })
        {
            var id = await SeedOwnedOverRequest(run); var key = $"over-race-{id:N}"; var reads = new OverRequestKeyProbe(); var save = new OverRequestSaveProbe();
            reads.BeforeFirst = async () =>
            {
                await using var winner = OverRequestScope(run); await OverRequestDecide(winner, run, id, key, approve);
                var error = SqlConcurrencyWrapperPolicyTests.SqlFailure(number);
                if (wrapped) throw new DbUpdateException("SENTETIK controlled provider race", error); throw error;
            };
            await using var loser = OverRequestScope(run, reads, save);
            var answer = await OverRequestDecide(loser, run, id, key, approve);
            Assert.True(answer.IsReplay); Assert.Equal("SENTETIK-over-original", answer.CorrelationId);
            Assert.Equal(2, reads.Reads); Assert.Equal(0, save.Saves); AssertOverRequestClean(loser);
            Assert.Equal((1, approve ? 2 : 1), (await OverRequestAttemptCount(run, id), await OverRequestAuditCount(run, id)));
        }
    }

    [SqlServerFact]
    public async Task Owned_over_delivery_request_completed_provider_deadlock_without_winner_is_unknown_without_mutating_retry()
    {
        await using var run = await Run.StartAsync(owner: null); var id = await SeedOwnedOverRequest(run);
        var reads = new OverRequestKeyProbe(); var save = new OverRequestSaveProbe(); await using var safe = OverRequestScope(run, reads, save);
        reads.BeforeFirst = async () =>
        {
            var transaction = safe.Db.Database.CurrentTransaction!.GetDbTransaction(); await transaction.RollbackAsync();
            Assert.Null(transaction.Connection); throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205);
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => OverRequestDecide(safe, run, id, "over-no-winner", true))).Status);
        Assert.Equal(2, reads.Reads); Assert.Equal(0, save.Saves); AssertOverRequestClean(safe);
        Assert.Equal((0, 0, 0), (await OverRequestAttemptCount(run, id), await OverRequestAuditCount(run, id), await OverRequestApprovalCount(run, id)));
    }

    [SqlServerFact]
    public async Task Owned_over_delivery_request_postsave_completed_cancellation_or_identity_loss_rolls_back_without_effects()
    {
        await using var run = await Run.StartAsync(owner: null);
        foreach (var fault in new[] { "cancel", "actor", "membership" })
        {
            var id = await SeedOwnedOverRequest(run); var save = new OverRequestSaveProbe(); using var cancellation = new CancellationTokenSource();
            await using var safe = OverRequestScope(run, save); var fired = false;
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
            Task<SalesApprovalRequestCommandResult> Execute() => safe.Service.ApproveOverDeliveryRequestAsync(id, 1, new("1"), OverRequestMetadata("over-drift-" + id.ToString("N")), cancellation.Token);
            if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(Execute);
            else Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(Execute)).Status);
            Assert.True(fired); Assert.True(save.Saved); AssertOverRequestClean(safe);
            Assert.Equal((0, 0, 0), (await OverRequestAttemptCount(run, id), await OverRequestAuditCount(run, id), await OverRequestApprovalCount(run, id)));
        }
    }

    [SqlServerFact]
    public async Task Owned_over_delivery_request_cleanup_fault_never_reconciles_and_cleanup_release_keeps_fresh_native_422_or_denial()
    {
        await using var run = await Run.StartAsync(owner: null);
        var id = await SeedOwnedOverRequest(run); var save = new OverRequestSaveProbe { BeforeSave = () => throw new InvalidOperationException("SENTETIK save fault") };
        var cleanup = new OverRequestRollbackProbe { Fail = true }; var reads = new OverRequestKeyProbe();
        await using (var safe = OverRequestScope(run, save, cleanup, reads))
        {
            var failure = await Assert.ThrowsAsync<AggregateException>(() => OverRequestDecide(safe, run, id, "over-cleanup", true));
            Assert.Contains(failure.InnerExceptions, x => x.Message == "SENTETIK rollback fault");
            Assert.Equal(1, cleanup.Rollbacks); Assert.Equal(2, reads.Reads); AssertOverRequestClean(safe);
        }
        Assert.Equal((0, 0, 0), (await OverRequestAttemptCount(run, id), await OverRequestAuditCount(run, id), await OverRequestApprovalCount(run, id)));
        foreach (var fault in new[] { "unchanged", "permission", "cancel" })
        {
            var invalid = await SeedOwnedOverRequest(run); using var cancellation = new CancellationTokenSource();
            var release = new OverRequestRollbackProbe(); await using var safe = OverRequestScope(run, release);
            release.AfterRollback = () => { if (fault == "permission") safe.Authority.Granted = false; if (fault == "cancel") cancellation.Cancel(); };
            Task<SalesApprovalRequestCommandResult> Reject() => safe.Service.RejectOverDeliveryRequestAsync(invalid, 1, new("1", null), OverRequestMetadata("over-invalid-" + invalid.ToString("N")), cancellation.Token);
            if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(Reject);
            else
            {
                var failure = await Assert.ThrowsAsync<SalesDraftServiceException>(Reject);
                Assert.Equal(fault == "unchanged" ? 422 : 403, failure.Status);
                if (fault == "unchanged") Assert.Equal(SalesApprovalRequestProblemCodesV1.ReasonRequired, failure.Code);
            }
            Assert.Equal(1, release.Rollbacks); AssertOverRequestClean(safe);
            Assert.Equal((0, 0, 0), (await OverRequestAttemptCount(run, invalid), await OverRequestAuditCount(run, invalid), await OverRequestApprovalCount(run, invalid)));
        }
    }

    [SqlServerFact]
    public async Task Owned_over_delivery_request_final_save_race_without_winner_keeps_native_409_and_never_writes_loser_terminal()
    {
        await using var run = await Run.StartAsync(owner: null);
        foreach (var fault in new[] { 0, 2601, 2627 })
        {
            var id = await SeedOwnedOverRequest(run); var key = "over-final-race-" + id.ToString("N"); var reads = new OverRequestKeyProbe();
            var save = new OverRequestSaveProbe { BeforeSave = () =>
            {
                if (fault == 0) throw new DbUpdateConcurrencyException("SENTETIK final concurrency");
                throw new DbUpdateException("SENTETIK final unique", SqlConcurrencyWrapperPolicyTests.SqlFailure(fault));
            } };
            await using (var safe = OverRequestScope(run, save, reads))
            {
                var failure = await Assert.ThrowsAsync<SalesDraftServiceException>(() => OverRequestDecide(safe, run, id, key, true));
                Assert.Equal((409, SalesDraftProblemCodesV1.ConcurrentChange), (failure.Status, failure.Code));
                Assert.Equal(1, save.Saves); Assert.Equal(3, reads.Reads); AssertOverRequestClean(safe);
            }
            Assert.Equal((0, 0, 0), (await OverRequestAttemptCount(run, id), await OverRequestAuditCount(run, id), await OverRequestApprovalCount(run, id)));
            await using var retry = OverRequestScope(run); Assert.False((await OverRequestDecide(retry, run, id, key, true)).IsReplay);
            Assert.Equal((1, 1), (await OverRequestAttemptCount(run, id), await OverRequestApprovalCount(run, id)));
        }
    }

    private static async Task<Guid> SeedOwnedOverRequest(Run run)
    {
        var draft = await run.ApprovedDraftAsync(CompanyA, "over-owned-seed-" + Guid.NewGuid().ToString("N"), "100");
        Assert.Equal((SalesOverDeliveryGateResultV1.Held, (int?)1), ((await GateAsync(run, draft.Id, Assert.Single(draft.Lines).Id, 100m, 2.5m)) is var result ? (result.Result, result.RequestNo) : default));
        return draft.Id;
    }
    private static SalesCommandMetadata OverRequestMetadata(string key) => new(key, "SENTETIK-over-original", null, T0);
    private static Task<SalesApprovalRequestCommandResult> OverRequestDecide(OverRequestScopeState safe, Run run, Guid id, string key, bool approve) => approve
        ? safe.Service.ApproveOverDeliveryRequestAsync(id, 1, new("1"), OverRequestMetadata(key))
        : safe.Service.RejectOverDeliveryRequestAsync(id, 1, new("1", "  SENTETIK ret  "), OverRequestMetadata(key));
    private static Task<int> OverRequestAttemptCount(Run run, Guid id) => run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.ApprovalRequestAttempts WHERE SubjectId=@id", ("@id", id));
    private static Task<int> OverRequestApprovalCount(Run run, Guid id) => run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftOverDeliveryApprovals WHERE DraftId=@id", ("@id", id));
    private static Task<long> OverRequestVersion(Run run, Guid id) => run.ScalarAsync<long>("SELECT Version FROM sales.Drafts WHERE Id=@id", ("@id", id));
    private static Task<int> OverRequestAuditCount(Run run, Guid id) => run.ScalarAsync<int>(
        "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType IN (N'sales.order.over-delivery-request-approved.v1',N'sales.order.over-delivery-request-rejected.v1',N'sales.order-draft.over-delivery-approved.v1') AND (EntityId=@text OR EntityId IN (SELECT CONVERT(nvarchar(36),Id) FROM sales.ApprovalRequests WHERE SubjectId=@id))", ("@text", id.ToString("D")), ("@id", id));
    private static void AssertOverRequestClean(OverRequestScopeState safe)
    { Assert.Empty(safe.Db.ChangeTracker.Entries().Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)); Assert.False(safe.Writer.HasPendingDrafts); Assert.Null(safe.Db.Database.CurrentTransaction); }

    private static OverRequestScopeState OverRequestScope(Run run, params IInterceptor[] interceptors)
    {
        var scope = run.Factory.Services.CreateAsyncScope(); var p = scope.ServiceProvider;
        var company = p.GetRequiredService<ICompanyContext>(); company.Set(CompanyA);
        var collector = p.GetRequiredService<AuditEventCollector>(); Assert.Same(collector, p.GetRequiredService<IAuditEventCollector>());
        var writer = p.GetRequiredService<IAuditTransactionWriterV1>();
        var options = new DbContextOptionsBuilder<SalesDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
            .AddInterceptors(new AuditSaveChangesInterceptor(collector, company)).AddInterceptors(interceptors);
        var db = new SalesDbContext(options.Options, company); var store = new SalesStore(db, company, collector, TimeProvider.System, officialCancellationAuditWriter: writer);
        var authority = new OverRequestAuthority(); var user = new OverRequestUser();
        var service = new SalesDraftService(company, user, authority, new OverRequestNoRoles(), new OverRequestCorrelation(),
            p.GetRequiredService<ISalesPartyAccountPortV1>(), p.GetRequiredService<ISettingsPrerequisiteProvider>(), p.GetService<IProductSalesEligibilityV1>(),
            store, new SalesDraftServiceBindings(), TimeProvider.System, requests: store).WithOverDeliveryRequestApprovalSafety();
        return new(scope, db, service, authority, user, writer, collector);
    }
    private sealed class OverRequestScopeState(AsyncServiceScope scope, SalesDbContext db, SalesDraftService service, OverRequestAuthority authority, OverRequestUser user, IAuditTransactionWriterV1 writer, AuditEventCollector collector) : IAsyncDisposable
    {
        internal SalesDbContext Db => db; internal SalesDraftService Service => service; internal OverRequestAuthority Authority => authority;
        internal OverRequestUser User => user; internal IAuditTransactionWriterV1 Writer => writer; internal AuditEventCollector Collector => collector;
        public async ValueTask DisposeAsync() { await db.DisposeAsync(); await scope.DisposeAsync(); }
    }
    private sealed class OverRequestNoRoles : IActiveRoleCodeReader
    {
        public Task<IReadOnlySet<string>> GetActiveRoleCodesAsync(Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
    }
    private sealed class OverRequestCorrelation : ICorrelationContext
    {
        public bool IsResolved => true; public string CorrelationId => "SENTETIK-over-original"; public string? CausationId => null;
        public string SourceChannel => "test"; public string? ClientIpAddress => null; public string? UserAgent => null;
        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress, string? userAgent) =>
            throw new NotSupportedException();
    }
    private sealed class OverRequestUser : ICurrentUser
    {
        internal string Actor = "SENTETIK-over-independent-checker"; internal bool Member = true;
        public bool IsAuthenticated => true; public string UserId => Actor;
        public IReadOnlySet<Guid> AllowedCompanyIds => Member ? new HashSet<Guid>([CompanyA]) : new HashSet<Guid>();
        public bool CanAccessCompany(Guid id) => Member && id == CompanyA;
    }
    private sealed class OverRequestAuthority : IErpAuthorityRevalidationService
    {
        internal bool Granted = true; internal int Reads; internal Func<Task>? AfterAwait;
        public async Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            Assert.Equal(CompanyA, companyId); Reads++; await Task.Yield(); if (AfterAwait is not null) await AfterAwait();
            return new(Granted, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
        }
    }
    private sealed class OverRequestSaveProbe : SaveChangesInterceptor
    {
        internal bool Saved; internal int Saves; internal Action? BeforeSave; internal Action? AfterSaved;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Saves++; BeforeSave?.Invoke(); return ValueTask.FromResult(result); }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken cancellationToken = default)
        { Saved = true; AfterSaved?.Invoke(); return ValueTask.FromResult(result); }
    }
    private sealed class OverRequestCommitProbe : DbTransactionInterceptor
    {
        internal int Commits; internal Action? AfterCommit;
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData data, CancellationToken cancellationToken = default)
        { Commits++; AfterCommit?.Invoke(); return Task.CompletedTask; }
    }
    private sealed class OverRequestRollbackProbe : DbTransactionInterceptor
    {
        internal bool Fail; internal int Rollbacks; internal Action? AfterRollback;
        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction transaction, TransactionEventData data, InterceptionResult result, CancellationToken cancellationToken = default)
        { Rollbacks++; if (Fail) throw new InvalidOperationException("SENTETIK rollback fault"); return ValueTask.FromResult(result); }
        public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData data, CancellationToken cancellationToken = default)
        { AfterRollback?.Invoke(); return Task.CompletedTask; }
    }
    private sealed class OverRequestKeyProbe : DbCommandInterceptor
    {
        internal int Reads; internal Func<Task>? BeforeFirst;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { if (command.CommandText.Contains("ApprovalRequestAttempts", StringComparison.Ordinal) && ++Reads == 1 && BeforeFirst is not null) await BeforeFirst(); return result; }
    }
    private static AuditEventDraft OverRequestCallerAudit() => new(CompanyA, "SENTETIK.over.caller.v1", "Sales.ApprovalRequest", "SENTETIK", "Caller", null, null,
        AuditActorTypes.User, "SENTETIK", SalesPermissionManifestV1.OrdersOverDeliveryApprove, [], "{}", "{}", "SENTETIK caller", null, AuditAiInvolvement.None, null, "test", null, null, "SENTETIK", null, "SENTETIK-over-caller");
}
