using System.Data.Common;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingCostDifferenceSqlServerRelationalAcceptanceTests
{
    // Actual migrated SQL with the real scoped Audit writer/collector. Failures and authority are controlled;
    // injected 1205/completed-provider transactions are not a naturally occurring two-session deadlock proof.
    [SqlServerFact]
    public async Task Cost_difference_owned_approval_preserves_native_amounts_audit_wire_and_original_key_after_posting()
    {
        await using var run = await Run.StartAsync(); var id = await SafetyPrepare(run); var before = await SafetyCounts(run);
        await using var scope = new CostApprovalScope(run);
        var first = await scope.Approve(id, "cost-original"); var replay = await scope.Approve(id, "cost-original");
        Assert.Equal(AccountingApiStatus.Ok, first.Status); Assert.Equal("Posted", first.Resource?.Status); Assert.Equal(3L, first.Resource?.Version);
        Assert.Equal(Checker, first.Resource?.ApprovedBy); Assert.NotNull(first.Resource?.JournalId); Assert.True(replay.IsReplay);
        Assert.Equal(scope.CorrelationId, replay.OriginalCorrelationId); Assert.Equal(JsonSerializer.Serialize(first.Resource, Json), JsonSerializer.Serialize(replay.Resource, Json));
        Assert.Equal(8, JsonSerializer.SerializeToElement(first, Json).EnumerateObject().Count()); Assert.Equal(20, JsonSerializer.SerializeToElement(first.Resource, Json).EnumerateObject().Count());
        Assert.Equal(before with { Journals = before.Journals + 1, Deliveries = before.Deliveries + 1, Commands = before.Commands + 1, Audits = before.Audits + 2 }, await SafetyCounts(run));
        Assert.Equal(600m, await run.Scalar<decimal>("SELECT Debit FROM accounting.JournalLines WHERE Debit>0"));
        Assert.Equal(600m, await run.Scalar<decimal>("SELECT Credit FROM accounting.JournalLines WHERE Credit>0"));
        Assert.Equal(0m, await run.Scalar<decimal>("SELECT SUM(Debit-Credit) FROM accounting.JournalLines")); scope.AssertClean();
        var count = await SafetyCounts(run); scope.User.Actor = Maker;
        Assert.Equal(AccountingApiCodes.IdempotencyConflict, (await scope.Approve(id, "cost-original")).Code);
        scope.User.Actor = Checker;
        Assert.Equal(AccountingApiCodes.IdempotencyConflict, (await scope.Service.ApproveAsync(id, 3, "cost-original")).Code);
        Assert.Equal(count, await SafetyCounts(run)); scope.AssertClean();
    }

    [SqlServerFact]
    public async Task Cost_difference_entry_refusals_preserve_actual_caller_transaction_tracker_and_audit()
    {
        foreach (var fault in new[] { "borrowed", "ambient", "dirty", "pending", "await-dirty", "await-pending", "missing-writer", "other-context", "other-collector" })
        {
            await using var run = await Run.StartAsync(); var id = await SafetyPrepare(run); var before = await SafetyCounts(run);
            await using var scope = new CostApprovalScope(run, entryFault: fault);
            await using var borrowed = fault == "borrowed" ? await scope.Db.Database.BeginTransactionAsync() : null;
            using var ambient = fault == "ambient" ? new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled) : null;
            var caller = CostSafetyCaller();
            if (fault == "dirty") scope.Db.Commands.Add(caller);
            if (fault == "pending") scope.Collector.Enqueue(CostSafetyAudit());
            scope.Authority.Read = () =>
            {
                if (fault == "await-dirty" && !scope.Db.ChangeTracker.HasChanges()) scope.Db.Commands.Add(caller);
                if (fault == "await-pending" && !scope.Writer.HasPendingDrafts) scope.Collector.Enqueue(CostSafetyAudit());
                return true;
            };
            Assert.Equal(AccountingApiStatus.Unavailable, (await scope.Approve(id, "entry")).Status);
            Assert.Equal(0, scope.Writer.Writes); Assert.Equal(0, scope.Writer.Discards);
            Assert.Equal(fault is "dirty" or "await-dirty", scope.Db.ChangeTracker.HasChanges());
            Assert.Equal(fault is "pending" or "await-pending", scope.Writer.HasPendingDrafts);
            if (scope.Db.ChangeTracker.HasChanges()) Assert.Same(caller, Assert.Single(scope.Db.ChangeTracker.Entries()).Entity);
            Assert.Same(borrowed, scope.Db.Database.CurrentTransaction);
            // No database connection is opened under the synthetic ambient entry refusal.
            if (ambient is null) Assert.Equal(before, await SafetyCounts(run));
        }
    }

    [SqlServerFact]
    public async Task Cost_difference_audit_save_precommit_and_release_fences_roll_back_or_suppress_exactly_one_effect()
    {
        foreach (var fault in new[] { "before-audit", "after-audit", "after-journal-save", "after-decision-save", "precommit", "company", "actor", "member", "delegation", "cancel", "postcommit" })
        {
            await using var run = await Run.StartAsync(); var id = await SafetyPrepare(run); var before = await SafetyCounts(run);
            var save = new CostApprovalSave(id); var commits = new CostApprovalCommits();
            await using var scope = new CostApprovalScope(run, save, commits); var pendingSeen = false; var deny = true; var observed = false; var savedFences = 0;
            using var cancellation = new CancellationTokenSource();
            scope.Authority.Read = () =>
            {
                if (scope.Writer.HasPendingDrafts) pendingSeen = true;
                if (save.Saved) savedFences++;
                var reached = fault == "before-audit" ? scope.Writer.HasPendingDrafts && !save.Saved
                    : fault == "after-audit" ? pendingSeen && !scope.Writer.HasPendingDrafts && !save.Saved
                    : fault == "after-decision-save" ? save.Saves == 2 : fault == "precommit" ? savedFences == 10
                    : fault == "postcommit" ? commits.Commits > 0 : save.Saved;
                if (!deny || !reached) return true; observed = true;
                if (fault == "company") { scope.Company.Set(TestCompanyIds.CompanyB); return true; }
                if (fault == "actor") { scope.User.Actor = Maker; return true; }
                if (fault == "member") { scope.User.Member = false; return true; }
                if (fault == "cancel") { cancellation.Cancel(); return true; }
                return false; // permission/delegation loss is controlled through the same fresh native authority seam.
            };
            if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.Approve(id, "fenced", cancellation.Token));
            else Assert.Equal(AccountingApiStatus.Denied, (await scope.Approve(id, "fenced")).Status);
            Assert.True(observed); scope.AssertClean();
            // No lineage branch here: journal Save1/Receive2/store3/decision preSave4/postAudit5/decision Save6/
            // store7/service8/owner9/precommit10. This is the actual final commit boundary, not an earlier saved read.
            if (fault == "precommit") Assert.Equal(10, savedFences);
            var committed = before with { Journals = before.Journals + 1, Deliveries = before.Deliveries + 1, Commands = before.Commands + 1, Audits = before.Audits + 2 };
            Assert.Equal(fault == "postcommit" ? committed : before, await SafetyCounts(run));
            deny = false; scope.Company.Set(Company); scope.User.Actor = Checker; scope.User.Member = true;
            var first = await scope.Approve(id, "fenced"); var replay = await scope.Approve(id, "fenced");
            Assert.Equal(AccountingApiStatus.Ok, first.Status); Assert.True(replay.IsReplay); Assert.Equal(first.Resource?.JournalId, replay.Resource?.JournalId);
            Assert.Equal(committed, await SafetyCounts(run)); scope.AssertClean();
        }
    }

    [SqlServerFact]
    public async Task Cost_difference_failed_owned_attempt_gets_one_readonly_original_key_winner_without_a_mutation_retry()
    {
        foreach (var fault in new[] { "concurrency", "direct", "wrapped", "completed-provider", "audit" })
        foreach (var winner in new[] { false, true })
        {
            await using var run = await Run.StartAsync(); var id = await SafetyPrepare(run); var before = await SafetyCounts(run);
            var save = new CostApprovalSave(id) { Fault = fault == "audit" ? null : fault };
            await using var scope = new CostApprovalScope(run, save, auditDeadlock: fault == "audit");
            var transactions = new HashSet<Guid>(); var winnerSaved = false; string? winnerCorrelation = null;
            scope.Authority.BeforeRead = async () =>
            {
                if (scope.Db.Database.CurrentTransaction is { } tx) transactions.Add(tx.TransactionId);
                if (!(save.Injected || scope.Writer.Injected) || scope.Db.Database.CurrentTransaction is not null || winnerSaved || !winner) return;
                scope.AssertClean(); winnerSaved = true;
                await using var other = new CostApprovalScope(run);
                var won = await other.Approve(id, "winner"); Assert.Equal(AccountingApiStatus.Ok, won.Status); other.AssertClean();
                winnerCorrelation = other.CorrelationId;
            };
            var result = await scope.Approve(id, "winner");
            Assert.Equal(winner ? AccountingApiStatus.Ok : AccountingApiStatus.Conflict, result.Status); Assert.Equal(winner, result.IsReplay);
            if (winner) { Assert.Equal(winnerCorrelation, result.OriginalCorrelationId); Assert.Equal("Posted", result.Resource?.Status); Assert.Equal(3L, result.Resource?.Version); }
            else { Assert.Equal(AccountingStoreCodes.ConcurrentChange, result.Code); Assert.Null(result.Resource); }
            Assert.Equal(fault == "audit" ? 0 : 1, save.Attempts); Assert.Equal(2, transactions.Count); scope.AssertClean();
            Assert.Equal(winner ? before with { Journals = before.Journals + 1, Deliveries = before.Deliveries + 1, Commands = before.Commands + 1, Audits = before.Audits + 2 } : before, await SafetyCounts(run));
        }
    }

    [SqlServerFact]
    public async Task Cost_difference_cleanup_fault_never_skips_tracker_or_collector_cleanup_or_reads_a_winner()
    {
        foreach (var dispose in new[] { false, true })
        {
            await using var run = await Run.StartAsync(); var id = await SafetyPrepare(run);
            var save = new CostApprovalSave(id) { Fault = "concurrency" }; var cleanup = new CostApprovalCleanup(dispose);
            await using var scope = new CostApprovalScope(run, save, cleanup); var transactions = new HashSet<Guid>();
            scope.Authority.Read = () => { if (scope.Db.Database.CurrentTransaction is { } tx) transactions.Add(tx.TransactionId); return true; };
            var error = await Assert.ThrowsAsync<AggregateException>(() => scope.Approve(id, "cleanup"));
            Assert.Contains("cleanup failed", error.Message, StringComparison.Ordinal); Assert.True(cleanup.Failed);
            Assert.Single(transactions); Assert.Equal(1, save.Attempts); Assert.False(scope.Db.ChangeTracker.HasChanges()); Assert.False(scope.Writer.HasPendingDrafts);
            Assert.Equal(1, scope.Writer.Discards);
        }
    }

    [SqlServerFact]
    public async Task Cost_difference_exact_read_boundaries_stop_the_next_owner_read_after_normally_completed_revocation()
    {
        foreach (var boundary in new[] { "row", "period", "plan", "currency" })
        {
            await using var run = await Run.StartAsync(); var id = await SafetyPrepare(run); var before = await SafetyCounts(run); var allowed = true; var reached = false;
            var probe = new CostApprovalReadProbe(command =>
            {
                if (boundary == "row" && command.Contains("[ManufacturingCostDifferences]", StringComparison.Ordinal)
                    || boundary == "period" && command.Contains("[Periods]", StringComparison.Ordinal)) { reached = true; allowed = false; }
            });
            await using var scope = new CostApprovalScope(run, extra: probe); scope.Authority.Read = () => allowed;
            scope.CurrentPlan.AfterRead = () => { if (boundary == "plan") { reached = true; allowed = false; } };
            scope.Currencies.AfterRead = () => { if (boundary == "currency") { reached = true; allowed = false; } };
            Assert.Equal(AccountingApiStatus.Denied, (await scope.Approve(id, "read-boundary")).Status);
            Assert.True(reached); Assert.Equal(boundary is "row" or "period" ? 0 : 1, scope.CurrentPlan.Reads);
            Assert.Equal(boundary == "currency" ? 1 : 0, scope.Currencies.Reads);
            Assert.Equal(before, await SafetyCounts(run)); scope.AssertClean();
        }
    }

    private static async Task<Guid> SafetyPrepare(Run run)
    {
        var period = await run.OpenPeriod(); var source = Source(); var id = source.CostDifferenceEventId!.Value;
        await using var scope = run.Scope(); await scope.Store.AcceptAsync(Envelope(source), source);
        Assert.Equal(AccountingApiStatus.Ok, (await scope.Service(Maker).PrepareAsync(Preparation(id, period), "SENTETIK-safety-prepare")).Status);
        return id;
    }
    private sealed record CostCounts(int Journals, int Deliveries, int Commands, int Audits);
    private static async Task<CostCounts> SafetyCounts(Run run) => new(await run.Count("accounting.Journals"), await run.Count("accounting.Deliveries"), await run.Count("accounting.Commands"), await run.Count("audit.AuditEvents"));
    private sealed class CostApprovalScope : IAsyncDisposable
    {
        private readonly IServiceScope auditScope; private readonly AccountingDbContext other;
        public CostApprovalCompany Company { get; } = new(); public CostApprovalUser User { get; } = new(); public CostApprovalAuthority Authority { get; } = new();
        public CostApprovalPlan CurrentPlan { get; } = new(); public CostApprovalCurrencies Currencies { get; } = new();
        public string CorrelationId { get; } = Guid.NewGuid().ToString("N"); public AccountingDbContext Db { get; }
        public AuditEventCollector Collector { get; } public CostApprovalWriter Writer { get; } public ManufacturingCostDifferenceServiceV1 Service { get; }
        public CostApprovalScope(Run run, CostApprovalSave? save = null, IInterceptor? extra = null, string? entryFault = null, bool auditDeadlock = false)
        {
            auditScope = run.Factory.Services.CreateScope(); auditScope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(ManufacturingCostDifferenceSqlServerRelationalAcceptanceTests.Company);
            Collector = auditScope.ServiceProvider.GetRequiredService<AuditEventCollector>();
            Writer = new(auditScope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>()) { AuditDeadlock = auditDeadlock };
            var options = new DbContextOptionsBuilder<AccountingDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", AccountingDbContext.Schema)).AddInterceptors(new AuditSaveChangesInterceptor(Collector, Company));
            if (save is not null) options.AddInterceptors(save); if (extra is not null) options.AddInterceptors(extra);
            if (extra is CostApprovalCleanup cleanup) cleanup.Configure(options);
            Db = new(options.Options, Company); other = new(options.Options, Company);
            var core = new AccountingStore(entryFault == "other-context" ? other : Db, Company,
                entryFault == "other-collector" ? new AuditEventCollector() : Collector, TimeProvider.System,
                new AccountingAmountScaleVerifier(new AccountingSqlServerRelationalAcceptanceTests.SyntheticMoneyProfiles(ManufacturingCostDifferenceSqlServerRelationalAcceptanceTests.Company)),
                normalApprovalAuditWriter: entryFault == "missing-writer" ? null : Writer);
            var store = new ManufacturingCostDifferenceStoreV1(Db, core, Company, CurrentPlan, Currencies, Collector, TimeProvider.System);
            var correlation = new CorrelationContext(); correlation.Set(CorrelationId, null, "Web", null, null);
            Service = new(store, core, Authority, Company, User, correlation);
        }
        public Task<AccountingApiResult<ManufacturingCostDifferenceViewV1>> Approve(Guid id, string key, CancellationToken token = default) => Service.ApproveAsync(id, 2, key, token);
        public void AssertClean() { Assert.False(Db.ChangeTracker.HasChanges()); Assert.False(Writer.HasPendingDrafts); Assert.Null(Db.Database.CurrentTransaction); }
        public async ValueTask DisposeAsync() { try { await Db.DisposeAsync(); await other.DisposeAsync(); } finally { auditScope.Dispose(); } }
    }
    private sealed class CostApprovalCompany : ICompanyContext
    { public Guid CompanyId { get; private set; } = Company; public bool IsResolved => CompanyId != Guid.Empty; public void Set(Guid id) => CompanyId = id; }
    private sealed class CostApprovalUser : ICurrentUser
    { public Guid Actor = Checker; public bool Member = true; public bool IsAuthenticated => true; public string UserId => Actor.ToString("D"); public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company }; public bool CanAccessCompany(Guid id) => Member && id == Company; }
    private sealed class CostApprovalAuthority : IErpAuthorityRevalidationService
    {
        public Func<bool>? Read; public Func<Task>? BeforeRead;
        public async Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken token = default)
        { if (BeforeRead is not null) await BeforeRead(); return new(Read?.Invoke() ?? true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()); }
    }
    private sealed class CostApprovalPlan : IActiveAccountPlanReaderV1
    {
        public int Reads; public Action? AfterRead;
        public async Task<ApprovedAccountingSourceCatalogRecordV1?> ReadActiveAsync(CancellationToken token = default)
        { Reads++; var answer = await new Plan().ReadActiveAsync(token); AfterRead?.Invoke(); return answer; }
    }
    private sealed class CostApprovalCurrencies : GoymenERP.Modules.Settings.Application.Contracts.V1.ISettingsMoneyCandidateReader
    {
        public int Reads; public Action? AfterRead;
        public async Task<IReadOnlyList<GoymenERP.Modules.Settings.Application.Contracts.V1.MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(CancellationToken token = default)
        { Reads++; var answer = await new Currencies().GetMoneyCurrencyCandidatesAsync(token); AfterRead?.Invoke(); return answer; }
    }
    private sealed class CostApprovalWriter(IAuditTransactionWriterV1 actual) : IAuditTransactionWriterV1
    {
        public int Writes, Discards; public bool AuditDeadlock, Injected; public bool HasPendingDrafts => actual.HasPendingDrafts;
        public Task WritePendingAsync(System.Data.IDbTransaction transaction, CancellationToken token = default)
        { Writes++; if (AuditDeadlock && !Injected) { Injected = true; throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205); } return actual.WritePendingAsync(transaction, token); }
        public void DiscardPending() { Discards++; actual.DiscardPending(); }
    }
    private sealed class CostApprovalSave(Guid proposalId) : SaveChangesInterceptor
    {
        public string? Fault; public bool Injected, Saved; public int Attempts, Saves;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default)
        {
            if (data.Context is AccountingDbContext db && (db.ChangeTracker.Entries<JournalRow>().Any(e => e.State == EntityState.Added && e.Entity.SourceId == proposalId)
                || db.ChangeTracker.Entries<ManufacturingCostDifferenceRow>().Any(e => e.State == EntityState.Modified && e.Entity.Id == proposalId && e.Entity.Version == 3)))
            {
                Attempts++;
                if (Fault is not null && !Injected)
                {
                    Injected = true;
                    if (Fault == "concurrency") throw new DbUpdateConcurrencyException("SENTETIK cost approval race");
                    if (Fault == "completed-provider") db.Database.CurrentTransaction!.GetDbTransaction().Rollback();
                    var error = SqlConcurrencyWrapperPolicyTests.SqlFailure(1205);
                    if (Fault == "wrapped") throw new DbUpdateException("SENTETIK cost approval 1205", error);
                    throw error;
                }
            }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default)
        { if (Attempts > Saves) { Saves++; Saved = true; } return ValueTask.FromResult(result); }
    }
    private sealed class CostApprovalCommits : DbTransactionInterceptor
    { public int Commits; public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData data, CancellationToken token = default) { Commits++; return Task.CompletedTask; } }
    private sealed class CostApprovalCleanup(bool dispose) : DbTransactionInterceptor
    {
        public bool Failed;
        public void Configure(DbContextOptionsBuilder<AccountingDbContext> options) => options.LogTo((id, _) => id == RelationalEventId.TransactionDisposed,
            _ => { if (dispose && !Failed) { Failed = true; throw new InvalidOperationException("SENTETIK dispose fault"); } });
        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction transaction, TransactionEventData data, InterceptionResult result, CancellationToken token = default)
        { if (!dispose && !Failed) { Failed = true; throw new InvalidOperationException("SENTETIK rollback fault"); } return ValueTask.FromResult(result); }
    }
    private sealed class CostApprovalReadProbe(Action<string> after) : DbCommandInterceptor
    {
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data, DbDataReader reader, CancellationToken token = default)
        { after(command.CommandText); return ValueTask.FromResult(reader); }
    }
    private static AccountingCommandRow CostSafetyCaller() => new() { CompanyId = Company, CommandName = "SENTETIK caller", IdempotencyKeyHash = "caller", ActorId = "caller", PayloadHash = "hash", ResultJson = "{}", CorrelationId = "corr" };
    private static AuditEventDraft CostSafetyAudit() => new(Company, "SENTETIK caller", "Accounting.ManufacturingCostDifference", "caller", "Approve", 2, 3,
        "User", Checker.ToString("D"), AccountingJournalReversalPermissionNamesV1.Approve, [], "{}", "{}", "SENTETIK", null, AuditAiInvolvement.None,
        null, "SQLSource", null, null, "SENTETIK-caller", null, "SENTETIK-key");
}
