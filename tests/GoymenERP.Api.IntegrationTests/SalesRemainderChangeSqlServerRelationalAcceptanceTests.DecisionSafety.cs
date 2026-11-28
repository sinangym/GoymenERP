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
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesRemainderChangeSqlServerRelationalAcceptanceTests
{
    // Real SQL ownership/audit; injected faults are controlled failures, not a real deadlock reproduction.
    // These sources require the root-owned ChangeRemainder/RecordRefusal/Restore marker relays before execution.
    [SqlServerFact]
    public async Task Owned_remainder_approve_and_reject_keep_native_floor_versions_raw_hash_and_original_key_once()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        {
            var id = await SeedOwnedRemainder(run); var key = "owned-remainder-" + id.ToString("N"); var before = await run.EffectsAsync(id);
            await using var safe = RemainderScope(run);
            var first = await DecideOwnedRemainder(safe, id, key, approve); var replay = await DecideOwnedRemainder(safe, id, key, approve);
            Assert.Equal("6", first.Detail.Draft.Version); Assert.False(first.IsReplay); Assert.True(replay.IsReplay);
            Assert.Equal(first.OriginalCorrelationId, replay.OriginalCorrelationId);
            Assert.Equal(before with { SalesAttempts = before.SalesAttempts + 1, AuditEvents = before.AuditEvents + 1, Decisions = 1, DraftVersion = 6 }, await run.EffectsAsync(id));
            Assert.Equal(approve ? "8" : "10", (await run.HeaderAsync(id)).QuantityText);
            Assert.Equal(1, await run.AuditAsync(approve ? ApprovedEvent : RejectedEvent, id, 5, 6));
            var raw = new SalesDraftRemainderDecisionRequest("05", approve ? null : "  SENTETIK ret  ");
            var command = approve ? SalesDraftCommands.ApproveRemainderChangeV1 : SalesDraftCommands.RejectRemainderChangeV1;
            Assert.Equal(SalesDraftRequestHash.Compute(command, id, 1, raw), await run.ScalarAsync<string>(
                "SELECT [RequestHash] FROM [sales].[DraftAttempts] WHERE [IdempotencyKeyHash]=@key", ("@key", SalesStore.IdempotencyKeyHash(key))));
            AssertOwnedRemainderClean(safe);
        }
    }

    [SqlServerFact]
    public async Task Owned_remainder_native_stale_version_is_one_durable_terminal_refusal_not_a_decision_or_a_retried_mutation()
    {
        await using var run = await Run.StartAsync(); var id = await SeedOwnedRemainder(run); var key = "stale-remainder-" + id.ToString("N"); var before = await run.EffectsAsync(id);
        await using var safe = RemainderScope(run);
        Task<SalesOrderDraftResponse> Stale() => safe.Service.RejectRemainderChangeAsync(id, 1, new("4", "SENTETIK ret"), RemainderMetadata(key));
        var first = await Assert.ThrowsAsync<SalesDraftServiceException>(Stale); var replay = await Assert.ThrowsAsync<SalesDraftServiceException>(Stale);
        Assert.Equal(409, first.Status); Assert.Equal(first.Code, replay.Code); Assert.False(first.IsReplay); Assert.True(replay.IsReplay);
        Assert.Equal(first.OriginalCorrelationId, replay.OriginalCorrelationId); Assert.Equal(1, await run.AttemptsAsync(key));
        Assert.Equal(before with { SalesAttempts = before.SalesAttempts + 1 }, await run.EffectsAsync(id)); AssertOwnedRemainderClean(safe);
    }

    [SqlServerFact]
    public async Task Owned_remainder_saved_and_precommit_authority_loss_or_completed_cancellation_roll_back_then_same_key_can_execute_once()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { false, true })
        {
          // Observe the actual final approval read before provider Commit on an otherwise identical native command.
          // Receipt attachment now performs more fenced reads; the old pre-receipt literal 3 is no longer that phase.
          var precommitFence = await RemainderPrecommitFenceNumber(run, approve);
          foreach (var phase in new[] { 1, precommitFence }) foreach (var cancel in new[] { false, true })
          {
            var id = await SeedOwnedRemainder(run); var key = "abort-remainder-" + id.ToString("N"); var before = await run.EffectsAsync(id);
            var save = new RemainderSaveProbe(); await using var safe = RemainderScope(run, save); using var cts = new CancellationTokenSource(); var savedFences = 0; var fired = false;
            safe.Authority.AfterRead = permission =>
            {
                // Save-release=1; the real precommit ordinal was captured at provider TransactionCommitting above.
                // Optional PriceRead calls are deliberately not counted as approval fences.
                if (save.Saved && permission == SalesPermissionManifestV1.OrdersApprove && ++savedFences == phase)
                { fired = true; if (cancel) cts.Cancel(); else safe.Authority.Granted = false; }
            };
            if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DecideOwnedRemainder(safe, id, key, approve, cts.Token));
            else Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => DecideOwnedRemainder(safe, id, key, approve))).Status);
            Assert.True(fired); Assert.True(save.Saved); Assert.Equal(before, await run.EffectsAsync(id)); Assert.Equal(0, await run.AttemptsAsync(key)); AssertOwnedRemainderClean(safe);
            await using var retry = RemainderScope(run); Assert.False((await DecideOwnedRemainder(retry, id, key, approve)).IsReplay);
            Assert.True((await DecideOwnedRemainder(retry, id, key, approve)).IsReplay); Assert.Equal(1, await run.AttemptsAsync(key));
          }
        }
    }

    [SqlServerFact]
    public async Task Owned_remainder_postcommit_release_denial_keeps_durable_native_effect_and_original_key_replay()
    {
        await using var run = await Run.StartAsync(); var id = await SeedOwnedRemainder(run); var key = "committed-remainder-" + id.ToString("N");
        var committed = new RemainderCommitProbe(); await using var safe = RemainderScope(run, committed);
        committed.AfterCommit = () => safe.Authority.Granted = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => DecideOwnedRemainder(safe, id, key, true))).Status);
        Assert.Equal(1, committed.Commits); Assert.Equal(1, await run.AttemptsAsync(key)); var durable = await run.EffectsAsync(id); Assert.Equal((1, 6L), (durable.Decisions, durable.DraftVersion));
        await using var retry = RemainderScope(run); var answer = await DecideOwnedRemainder(retry, id, key, true); Assert.True(answer.IsReplay);
        Assert.Equal("SENTETIK-remainder-original", answer.OriginalCorrelationId); Assert.Equal(durable, await run.EffectsAsync(id));
    }

    [SqlServerFact]
    public async Task Owned_remainder_preserves_borrowed_dirty_and_pending_caller_state_including_initial_state_cleaned_during_authority()
    {
        await using var run = await Run.StartAsync(); var id = await SeedOwnedRemainder(run); var before = await run.EffectsAsync(id);
        foreach (var kind in new[] { "dirty", "audit", "borrowed", "initial-dirty-cleaned", "initial-audit-cleaned" })
        {
            await using var safe = RemainderScope(run); var row = await safe.Db.Drafts.SingleAsync(x => x.Id == id); var key = "caller-" + Guid.NewGuid().ToString("N");
            if (kind.Contains("dirty", StringComparison.Ordinal)) safe.Db.Entry(row).State = EntityState.Modified;
            if (kind.Contains("audit", StringComparison.Ordinal)) safe.Collector.Enqueue(RemainderCallerAudit());
            await using var borrowed = kind == "borrowed" ? await safe.Db.Database.BeginTransactionAsync() : null;
            var cleared = false;
            safe.Authority.AfterRead = _ => { if (!cleared && kind.StartsWith("initial-", StringComparison.Ordinal)) { cleared = true; safe.Db.ChangeTracker.Clear(); safe.Writer.DiscardPending(); } };
            Assert.Equal(503, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => DecideOwnedRemainder(safe, id, key, false))).Status);
            if (kind == "dirty") Assert.Equal(EntityState.Modified, safe.Db.Entry(row).State);
            if (kind == "audit") Assert.True(safe.Writer.HasPendingDrafts);
            if (borrowed is not null) Assert.Same(borrowed, safe.Db.Database.CurrentTransaction);
            if (kind.StartsWith("initial-", StringComparison.Ordinal)) Assert.True(cleared);
            Assert.Equal(before, await run.EffectsAsync(id)); Assert.Equal(0, await run.AttemptsAsync(key));
            safe.Writer.DiscardPending(); // Explicit caller cleanup after its preservation assertions.
        }
    }

    [SqlServerFact]
    public async Task Owned_remainder_controlled_1205_and_1222_abort_once_without_winner_never_write_a_terminal_attempt_and_retry_once()
    {
        await using var run = await Run.StartAsync();
        foreach (var number in new[] { 1205, 1222 }) foreach (var wrapped in new[] { false, true })
        {
            var id = await SeedOwnedRemainder(run); var key = "race-remainder-" + id.ToString("N"); var before = await run.EffectsAsync(id);
            var save = new RemainderSaveProbe { Failure = wrapped ? new DbUpdateException("SENTETIK wrapped", SqlConcurrencyWrapperPolicyTests.SqlFailure(number)) : SqlConcurrencyWrapperPolicyTests.SqlFailure(number) };
            await using var safe = RemainderScope(run, save);
            Assert.Equal(503, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => DecideOwnedRemainder(safe, id, key, true))).Status);
            Assert.Equal(1, save.Saves); Assert.Equal(before, await run.EffectsAsync(id)); Assert.Equal(0, await run.AttemptsAsync(key)); AssertOwnedRemainderClean(safe);
            await using var retry = RemainderScope(run); Assert.False((await DecideOwnedRemainder(retry, id, key, true)).IsReplay); Assert.True((await DecideOwnedRemainder(retry, id, key, true)).IsReplay); Assert.Equal(1, await run.AttemptsAsync(key));
        }
    }

    [SqlServerFact]
    public async Task Owned_remainder_controlled_race_returns_only_the_genuine_original_key_winner_without_a_second_mutation()
    {
        await using var run = await Run.StartAsync();
        foreach (var number in new[] { 1205, 1222 })
        {
            var id = await SeedOwnedRemainder(run); var key = "winner-remainder-" + id.ToString("N");
            var read = new RemainderKeyProbe(); await using var loser = RemainderScope(run, read); SalesOrderDraftResponse? original = null;
            read.BeforeFirst = async () =>
            {
                // A separate actual owner commits the identical key before this transaction's first attempt query executes.
                await using var winner = RemainderScope(run); original = await DecideOwnedRemainder(winner, id, key, true);
                throw SqlConcurrencyWrapperPolicyTests.SqlFailure(number);
            };
            var replay = await DecideOwnedRemainder(loser, id, key, true);
            Assert.NotNull(original); Assert.True(replay.IsReplay); Assert.Equal(original.OriginalCorrelationId, replay.OriginalCorrelationId);
            Assert.Equal("6", replay.Detail.Draft.Version); Assert.Equal(2, read.Reads); // Failed first lookup, then exactly one readonly original-key lookup.
            Assert.Equal(1, await run.AttemptsAsync(key)); Assert.Equal(1, (await run.EffectsAsync(id)).Decisions); AssertOwnedRemainderClean(loser);
        }
    }

    [SqlServerFact]
    public async Task Owned_remainder_cleanup_fault_is_not_native_success_or_readonly_reconciliation()
    {
        await using var run = await Run.StartAsync(); var id = await SeedOwnedRemainder(run); var key = "cleanup-remainder-" + id.ToString("N");
        var before = await run.EffectsAsync(id); var save = new RemainderSaveProbe { Failure = SqlConcurrencyWrapperPolicyTests.SqlFailure(1222) }; var rollback = new RemainderRollbackFault();
        await using var safe = RemainderScope(run, save, rollback);
        await Assert.ThrowsAsync<AggregateException>(() => DecideOwnedRemainder(safe, id, key, false)); Assert.Equal(1, save.Saves); Assert.Equal(1, rollback.Calls);
        Assert.Equal(before, await run.EffectsAsync(id)); Assert.Equal(0, await run.AttemptsAsync(key));
    }

    private static async Task<Guid> SeedOwnedRemainder(Run run, Guid? companyId = null)
    {
        var company = companyId ?? CompanyA;
        var draft = await run.ApprovedDraftAsync(company, "safety-" + Guid.NewGuid().ToString("N"), "10");
        await run.PartlyDeliveredAsync(company, draft, "floor-" + Guid.NewGuid().ToString("N"), 6m, 4m, 2.5m);
        await using var scope = run.SalesStore(company);
        await scope.Store.ChangeRemainderAsync(run.SalesEnvelope("request-" + Guid.NewGuid().ToString("N")), Hash("SENTETIK-" + draft.Id), SalesDraftCommands.RequestRemainderChangeV1, draft.Id,
            (order, now) => order.RequestRemainderChange(4, Assert.Single(order.Lines).Id, GoymenERP.Modules.Sales.Domain.Drafts.RemainderChangeKind.Revise, "8", 8m, "SENTETIK kalan", new(6m, 4m, 2.5m), run.Actor, now),
            new("sales.order-draft.remainder-change-requested.v1", "RequestRemainderChange", "SENTETIK kalan"), (_, _) => Task.CompletedTask);
        return draft.Id;
    }
    private static Task<SalesOrderDraftResponse> DecideOwnedRemainder(RemainderSafetyScope safe, Guid id, string key, bool approve, CancellationToken token = default) => approve
        ? safe.Service.ApproveRemainderChangeAsync(id, 1, new("05"), RemainderMetadata(key), token)
        : safe.Service.RejectRemainderChangeAsync(id, 1, new("05", "  SENTETIK ret  "), RemainderMetadata(key), token);
    private static SalesCommandMetadata RemainderMetadata(string key) => new(key, "SENTETIK-remainder-original", null, T0);
    private static async Task<int> RemainderPrecommitFenceNumber(Run run, bool approve)
    {
        var id = await SeedOwnedRemainder(run); var saved = new RemainderSaveProbe(); var commit = new RemainderCommitProbe();
        await using var safe = RemainderScope(run, saved, commit); var savedApprovalReads = 0; var measured = 0;
        safe.Authority.AfterRead = permission => { if (saved.Saved && permission == SalesPermissionManifestV1.OrdersApprove) savedApprovalReads++; };
        commit.BeforeCommit = () => measured = savedApprovalReads;
        var answer = await DecideOwnedRemainder(safe, id, "phase-" + id.ToString("N"), approve);
        Assert.False(answer.IsReplay); Assert.Equal(1, commit.Commits); Assert.True(measured > 1); return measured;
    }
    private static void AssertOwnedRemainderClean(RemainderSafetyScope safe)
    { Assert.False(safe.Db.ChangeTracker.HasChanges()); Assert.False(safe.Writer.HasPendingDrafts); Assert.Null(safe.Db.Database.CurrentTransaction); }
    private static RemainderSafetyScope RemainderScope(Run run, params IInterceptor[] interceptors)
    {
        var scope = run.Factory.Services.CreateAsyncScope(); var p = scope.ServiceProvider; var company = p.GetRequiredService<ICompanyContext>(); company.Set(CompanyA);
        var collector = p.GetRequiredService<AuditEventCollector>(); Assert.Same(collector, p.GetRequiredService<IAuditEventCollector>()); var writer = p.GetRequiredService<IAuditTransactionWriterV1>();
        var db = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString, x => x.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
            .AddInterceptors(new AuditSaveChangesInterceptor(collector, company)).AddInterceptors(interceptors).Options, company);
        var store = new SalesStore(db, company, collector, TimeProvider.System, officialCancellationAuditWriter: writer); var authority = new RemainderAuthority(); var roles = new RemainderRoles(); var user = new RemainderUser();
        var correlation = new CorrelationContext(); correlation.Set("SENTETIK-remainder-original", null, "Web", null, null);
        var service = new SalesDraftService(company, user, authority, roles, correlation,
            p.GetRequiredService<ISalesPartyAccountPortV1>(), p.GetRequiredService<ISettingsPrerequisiteProvider>(), p.GetService<IProductSalesEligibilityV1>(), store, new(), TimeProvider.System,
            allocations: p.GetRequiredService<ISalesOrderAllocationReadPortV1>(), loadings: p.GetRequiredService<ISalesOrderLoadingReadPortV1>(), requests: store).WithRemainderDecisionSafety();
        return new(scope, db, service, authority, writer, collector, roles, company, user);
    }
    private sealed class RemainderSafetyScope(AsyncServiceScope scope, SalesDbContext db, SalesDraftService service, RemainderAuthority authority, IAuditTransactionWriterV1 writer, AuditEventCollector collector, RemainderRoles roles, ICompanyContext company, RemainderUser user) : IAsyncDisposable
    {
        internal SalesDbContext Db => db; internal SalesDraftService Service => service; internal RemainderAuthority Authority => authority; internal IAuditTransactionWriterV1 Writer => writer; internal AuditEventCollector Collector => collector; internal RemainderRoles Roles => roles; internal ICompanyContext Company => company; internal RemainderUser User => user;
        public async ValueTask DisposeAsync() { await db.DisposeAsync(); await scope.DisposeAsync(); }
    }
    private sealed class RemainderAuthority : IErpAuthorityRevalidationService
    {
        internal bool Granted = true; internal Action<string>? AfterRead;
        public async Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        { Assert.Equal(CompanyA, companyId); await Task.Yield(); AfterRead?.Invoke(permission); return new(Granted, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()); }
    }
    private sealed class RemainderUser : ICurrentUser
    { public bool IsAuthenticated => true; public string UserId => "SENTETIK-remainder-independent"; public IReadOnlySet<Guid> AllowedCompanyIds => new HashSet<Guid>([CompanyA]); public bool CanAccessCompany(Guid id) => id == CompanyA; }
    private sealed class RemainderRoles : IActiveRoleCodeReader
    { internal bool Support; public Task<IReadOnlySet<string>> GetActiveRoleCodesAsync(Guid companyId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<string>>(Support ? new HashSet<string>([ErpPersonaRoleCodes.SalesSupport]) : new HashSet<string>()); }
    private sealed class RemainderSaveProbe : SaveChangesInterceptor
    {
        internal bool Saved; internal int Saves; internal Exception? Failure;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default)
        { Saves++; if (Failure is not null) throw Failure; return ValueTask.FromResult(result); }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default)
        { Saved = true; return ValueTask.FromResult(result); }
    }
    private sealed class RemainderCommitProbe : DbTransactionInterceptor
    {
        internal int Commits; internal Action? BeforeCommit; internal Action? AfterCommit;
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData data, InterceptionResult result, CancellationToken token = default) { BeforeCommit?.Invoke(); return ValueTask.FromResult(result); }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData data, CancellationToken token = default) { Commits++; AfterCommit?.Invoke(); return Task.CompletedTask; }
    }
    private sealed class RemainderRollbackFault : DbTransactionInterceptor
    { internal int Calls; public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction transaction, TransactionEventData data, InterceptionResult result, CancellationToken token = default) { Calls++; throw new InvalidOperationException("SENTETIK rollback fault"); } }
    private sealed class RemainderKeyProbe : DbCommandInterceptor
    {
        internal int Reads; internal Func<Task>? BeforeFirst;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken token = default)
        { if (command.CommandText.Contains("DraftAttempts", StringComparison.Ordinal) && ++Reads == 1 && BeforeFirst is not null) await BeforeFirst(); return result; }
    }
    private static AuditEventDraft RemainderCallerAudit() => new(CompanyA, "SENTETIK.remainder.caller.v1", "Sales.Draft", "SENTETIK", "Caller", null, null,
        AuditActorTypes.User, "SENTETIK", SalesPermissionManifestV1.OrdersApprove, [], "{}", "{}", "SENTETIK caller", null, AuditAiInvolvement.None, null, "Web", null, null, "SENTETIK", null, "SENTETIK-remainder-caller");
}
