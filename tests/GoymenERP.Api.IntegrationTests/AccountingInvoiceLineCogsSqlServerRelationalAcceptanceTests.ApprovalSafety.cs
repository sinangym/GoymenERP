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

public sealed partial class AccountingInvoiceLineCogsSqlServerRelationalAcceptanceTests
{
    // Actual migrated Accounting + scoped Audit. Only the commercial source/plan and injected faults are synthetic.
    // Controlled 1205/completed-provider transactions are not a naturally occurring simultaneous deadlock proof.
    [SqlServerFact]
    public async Task Cogs_owned_approval_preserves_native_component_amounts_audit_hash_and_original_receipt_with_or_without_interceptor()
    {
        foreach (var interceptor in new[] { false, true })
        {
            await using var run = await Run.Start(); var id = await CogsSafetyPrepare(run); var before = await CogsCountsOf(run);
            await using var scope = new CogsApprovalScope(run, auditInterceptor: interceptor);
            var first = await scope.Approve(id, "original"); run.Source.Unavailable = true;
            var sourceReads = run.Source.Calls; var replay = await scope.Approve(id, "original");
            Assert.Equal(AccountingApiStatus.Ok, first.Status); Assert.Equal("Approved", first.Resource?.Status); Assert.Equal(2L, first.Resource?.Version);
            Assert.True(replay.IsReplay); Assert.Equal(scope.CorrelationId, replay.OriginalCorrelationId); Assert.Equal(sourceReads, run.Source.Calls);
            Assert.Equal(JsonSerializer.Serialize(first.Resource, Json), JsonSerializer.Serialize(replay.Resource, Json));
            Assert.Equal(22, JsonSerializer.SerializeToElement(first.Resource, Json).EnumerateObject().Count());
            Assert.Equal(before with { Journals = before.Journals + 2, Revisions = before.Revisions + 1, Commands = before.Commands + 1, Audits = before.Audits + 1 }, await CogsCountsOf(run));
            var replacement = await scope.Db.JournalLines.AsNoTracking().SingleAsync(x => x.JournalId == first.Resource!.ReplacementCorrectionJournalId && x.LineNumber == 1);
            Assert.Equal(100.01m, replacement.Debit); Assert.Equal(Next, replacement.AccountId);
            scope.AssertClean(); scope.User.Actor = Maker;
            Assert.Equal(AccountingApiCodes.IdempotencyConflict, (await scope.Approve(id, "original")).Code); scope.User.Actor = Checker;
            Assert.Equal(AccountingApiCodes.IdempotencyConflict, (await scope.Service.ApproveAsync(run.Source.Facts.InvoiceId, run.Source.Facts.InvoiceLineId, id, new(2), "original")).Code);
            scope.AssertClean();
        }
    }

    [SqlServerFact]
    public async Task Cogs_owned_entry_refusals_preserve_actual_caller_work_before_any_command_or_source_query()
    {
        foreach (var fault in new[] { "borrowed", "ambient", "dirty", "pending", "await-dirty", "await-pending", "missing-owner", "missing-writer", "other-context", "other-collector", "other-writer" })
        {
            await using var run = await Run.Start(); var id = await CogsSafetyPrepare(run); var before = await CogsCountsOf(run); var reads = 0;
            var probe = new CogsApprovalReadProbe(_ => reads++); await using var scope = new CogsApprovalScope(run, extra: probe, entryFault: fault);
            await using var borrowed = fault == "borrowed" ? await scope.Db.Database.BeginTransactionAsync() : null;
            using var ambient = fault == "ambient" ? new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled) : null;
            var caller = CogsCaller(); if (fault == "dirty") scope.Db.Commands.Add(caller); if (fault == "pending") scope.Collector.Enqueue(CogsCallerAudit());
            scope.Authority.Read = () => { if (fault == "await-dirty" && !scope.Db.ChangeTracker.HasChanges()) scope.Db.Commands.Add(caller); if (fault == "await-pending" && !scope.Writer.HasPendingDrafts) scope.Collector.Enqueue(CogsCallerAudit()); return true; };
            Assert.Equal(AccountingApiStatus.Unavailable, (await scope.Approve(id, "entry")).Status); Assert.Equal(0, reads);
            Assert.Equal(0, scope.Writer.Writes); Assert.Equal(0, scope.Writer.Discards); Assert.Same(borrowed, scope.Db.Database.CurrentTransaction);
            Assert.Equal(fault is "dirty" or "await-dirty", scope.Db.ChangeTracker.HasChanges()); Assert.Equal(fault is "pending" or "await-pending", scope.Writer.HasPendingDrafts);
            if (scope.Db.ChangeTracker.HasChanges()) Assert.Same(caller, Assert.Single(scope.Db.ChangeTracker.Entries()).Entity);
            if (ambient is null) Assert.Equal(before, await CogsCountsOf(run));
        }
    }

    [SqlServerFact]
    public async Task Cogs_owned_audit_save_precommit_and_release_fences_abort_or_suppress_exactly_the_native_effect()
    {
        foreach (var fault in new[] { "before-audit", "after-audit", "after-save", "precommit", "postcommit", "company", "actor", "member", "cancel" })
        {
            await using var run = await Run.Start(); var id = await CogsSafetyPrepare(run); var before = await CogsCountsOf(run);
            var save = new CogsApprovalSave(); var commits = new CogsApprovalCommits(); await using var scope = new CogsApprovalScope(run, save, commits);
            using var cancellation = new CancellationTokenSource(); var deny = true; var observed = false; var seenPending = false; var savedFences = 0;
            scope.Authority.Read = () =>
            {
                if (scope.Writer.HasPendingDrafts) seenPending = true; if (save.Saved) savedFences++;
                // After Save: owned Save fence=1, service result fence=2, runner result=3, precommit=4, release=5.
                var reached = fault == "before-audit" ? scope.Writer.HasPendingDrafts : fault == "after-audit" ? seenPending && !scope.Writer.HasPendingDrafts && !save.Saved
                    : fault == "precommit" ? savedFences == 4 : fault == "postcommit" ? commits.Commits > 0 : save.Saved;
                if (!deny || !reached) return true; observed = true;
                if (fault == "company") { scope.Company.Set(TestCompanyIds.CompanyB); return true; }
                if (fault == "actor") { scope.User.Actor = Maker; return true; } if (fault == "member") { scope.User.Member = false; return true; }
                if (fault == "cancel") { cancellation.Cancel(); return true; } return false;
            };
            if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.Approve(id, "late", cancellation.Token));
            else Assert.Equal(AccountingApiStatus.Denied, (await scope.Approve(id, "late")).Status);
            Assert.True(observed); scope.AssertClean();
            Assert.Equal(fault == "postcommit" ? before with { Journals = before.Journals + 2, Revisions = before.Revisions + 1, Commands = before.Commands + 1, Audits = before.Audits + 1 } : before, await CogsCountsOf(run));
            deny = false; scope.Company.Set(Company); scope.User.Actor = Checker; scope.User.Member = true;
            var recovered = await scope.Approve(id, "late"); Assert.Equal(AccountingApiStatus.Ok, recovered.Status); Assert.Equal(fault == "postcommit", recovered.IsReplay);
            scope.AssertClean(); Assert.Equal(before.Journals + 2, (await CogsCountsOf(run)).Journals);
        }
    }

    [SqlServerFact]
    public async Task Cogs_original_key_race_aborts_before_one_readonly_winner_pass_and_never_retries_mutation()
    {
        foreach (var fault in new[] { "concurrency", "direct", "wrapped", "completed-provider", "audit" })
        foreach (var winner in new[] { false, true })
        {
            await using var run = await Run.Start(); var id = await CogsSafetyPrepare(run); var before = await CogsCountsOf(run);
            var save = new CogsApprovalSave { Fault = fault == "audit" ? null : fault }; await using var scope = new CogsApprovalScope(run, save, auditDeadlock: fault == "audit");
            var transactions = new HashSet<Guid>(); var winnerSaved = false; string? winnerCorrelation = null;
            scope.Authority.BeforeRead = async () =>
            {
                if (scope.Db.Database.CurrentTransaction is { } tx) transactions.Add(tx.TransactionId);
                if (!(save.Injected || scope.Writer.Injected) || scope.Db.Database.CurrentTransaction is not null || !winner || winnerSaved) return;
                scope.AssertClean(); winnerSaved = true; await using var other = new CogsApprovalScope(run);
                Assert.Equal(AccountingApiStatus.Ok, (await other.Approve(id, "race")).Status); other.AssertClean(); winnerCorrelation = other.CorrelationId;
            };
            var answer = await scope.Approve(id, "race"); Assert.Equal(winner ? AccountingApiStatus.Ok : AccountingApiStatus.Conflict, answer.Status); Assert.Equal(winner, answer.IsReplay);
            Assert.Equal(fault == "audit" ? 0 : 1, save.Attempts); Assert.Equal(2, transactions.Count); scope.AssertClean();
            if (winner) { Assert.Equal(winnerCorrelation, answer.OriginalCorrelationId); Assert.Equal("Approved", answer.Resource?.Status); Assert.Equal(2L, answer.Resource?.Version); }
            else { Assert.Equal(AccountingStoreCodes.ConcurrentChange, answer.Code); Assert.Null(answer.Resource); }
            Assert.Equal(winner ? before with { Journals = before.Journals + 2, Revisions = before.Revisions + 1, Commands = before.Commands + 1, Audits = before.Audits + 1 } : before, await CogsCountsOf(run));
        }
    }

    [SqlServerFact]
    public async Task Cogs_replay_only_second_provider_failure_and_cleanup_faults_never_start_a_third_attempt()
    {
        foreach (var fault in new[] { "second-1205", "rollback", "dispose" })
        {
            await using var run = await Run.Start(); var id = await CogsSafetyPrepare(run); var before = await CogsCountsOf(run);
            var save = new CogsApprovalSave { Fault = "concurrency" }; var cleanup = fault == "second-1205" ? null : new CogsApprovalCleanup(fault == "dispose");
            var commandReads = 0; var probe = new CogsApprovalReadProbe(sql => { if (sql.Contains("[Commands]", StringComparison.Ordinal) && ++commandReads == 2 && fault == "second-1205") throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205); });
            await using var scope = new CogsApprovalScope(run, save, cleanup, probe); var transactions = new HashSet<Guid>();
            scope.Authority.Read = () => { if (scope.Db.Database.CurrentTransaction is { } tx) transactions.Add(tx.TransactionId); return true; };
            if (cleanup is null) { Assert.Equal(AccountingApiStatus.Unavailable, (await scope.Approve(id, "second")).Status); Assert.Equal(2, transactions.Count); Assert.Equal(2, commandReads); }
            else { await Assert.ThrowsAsync<AggregateException>(() => scope.Approve(id, "cleanup")); Assert.True(cleanup.Failed); Assert.Single(transactions); Assert.Equal(1, scope.Writer.Discards); }
            Assert.Equal(1, save.Attempts); Assert.False(scope.Db.ChangeTracker.HasChanges()); Assert.False(scope.Writer.HasPendingDrafts); Assert.Equal(before, await CogsCountsOf(run));
        }
    }

    [SqlServerFact]
    public async Task Cogs_each_owner_read_fences_normally_completed_revocation_and_token_ignoring_source_cancellation()
    {
        foreach (var boundary in new[] { "source", "source-cancel", "row", "period", "plan", "attribution" })
        {
            await using var run = await Run.Start(); var id = await CogsSafetyPrepare(run); var before = await CogsCountsOf(run); var allowed = true; var observed = false;
            var probe = new CogsApprovalReadProbe(sql =>
            {
                if (boundary == "row" && sql.Contains("[InvoiceLineCogsProposals]", StringComparison.Ordinal)
                    || boundary == "period" && sql.Contains("[Periods]", StringComparison.Ordinal)
                    || boundary == "attribution" && sql.Contains("[InvoiceCogsEconomicClaims]", StringComparison.Ordinal)) { observed = true; allowed = false; }
            });
            await using var scope = new CogsApprovalScope(run, extra: probe); using var cancellation = new CancellationTokenSource();
            scope.Authority.Read = () => allowed;
            scope.Source.AfterRead = () => { if (boundary is "source" or "source-cancel") { observed = true; if (boundary == "source-cancel") cancellation.Cancel(); else allowed = false; } };
            scope.CurrentPlan.AfterRead = () => { if (boundary == "plan") { observed = true; allowed = false; } };
            if (boundary == "source-cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.Approve(id, "read", cancellation.Token));
            else Assert.Equal(AccountingApiStatus.Denied, (await scope.Approve(id, "read")).Status);
            Assert.True(observed); Assert.Equal(boundary is "source" or "source-cancel" or "row" or "period" ? 0 : 1, scope.CurrentPlan.Reads);
            Assert.Equal(before, await CogsCountsOf(run)); scope.AssertClean();
        }
    }

    [SqlServerFact]
    public async Task Cogs_native_maker_denial_and_terminal_refusal_receipts_keep_their_original_semantics()
    {
        await using var run = await Run.Start(); var id = await CogsSafetyPrepare(run); var before = await CogsCountsOf(run);
        await using var scope = new CogsApprovalScope(run); scope.User.Actor = Maker;
        var maker = await scope.Approve(id, "maker"); Assert.Equal(AccountingApiStatus.Denied, maker.Status); Assert.Equal(InvoiceLineCogsCorrectionCodesV1.SelfApproval, maker.Code);
        Assert.Equal(before, await CogsCountsOf(run)); scope.User.Actor = Checker;
        var missingId = Guid.NewGuid(); var missing = await scope.Approve(missingId, "missing"); var replay = await scope.Approve(missingId, "missing");
        Assert.Equal(AccountingApiStatus.NotFound, missing.Status); Assert.True(replay.IsReplay); Assert.Equal(missing.Code, replay.Code);
        Assert.Equal(before with { Commands = before.Commands + 1 }, await CogsCountsOf(run)); scope.AssertClean();
        var conflict = await scope.Service.ApproveAsync(run.Source.Facts.InvoiceId, run.Source.Facts.InvoiceLineId, id, new(2), "version");
        Assert.Equal(AccountingApiStatus.Conflict, conflict.Status); Assert.Equal(AccountingApiCodes.VersionConflict, conflict.Code);
        Assert.True((await scope.Service.ApproveAsync(run.Source.Facts.InvoiceId, run.Source.Facts.InvoiceLineId, id, new(2), "version")).IsReplay);
        Assert.Equal(before with { Commands = before.Commands + 2 }, await CogsCountsOf(run)); scope.AssertClean();
    }

    private static async Task<Guid> CogsSafetyPrepare(Run run)
    {
        var facts = run.Source.Facts; var period = await run.Period(); await run.PostComponent(facts, period);
        await using var scope = run.Open(); return (await scope.Store.PrepareAsync(facts, Request(facts, period), Maker, Fence, Metadata(Maker))).ProposalId;
    }
    private sealed record CogsCounts(int Journals, int Revisions, int Commands, int Audits);
    private static async Task<CogsCounts> CogsCountsOf(Run run) => new(await run.Count("accounting.Journals"), await run.Count("accounting.InvoiceLineCogsRevisions"), await run.Count("accounting.Commands"), await run.Count("audit.AuditEvents"));
    private sealed class CogsApprovalScope : IAsyncDisposable
    {
        private readonly IServiceScope auditScope; private readonly AccountingDbContext other;
        public MutableCompany Company { get; } = new(AccountingInvoiceLineCogsSqlServerRelationalAcceptanceTests.Company);
        public CogsApprovalUser User { get; } = new(); public CogsApprovalAuthority Authority { get; } = new(); public CogsApprovalPlan CurrentPlan { get; } = new();
        public CogsApprovalSource Source { get; } public string CorrelationId { get; } = Guid.NewGuid().ToString("N");
        public AccountingDbContext Db { get; } public AuditEventCollector Collector { get; } public CogsApprovalWriter Writer { get; } public InvoiceLineCogsCorrectionServiceV1 Service { get; }
        public CogsApprovalScope(Run run, CogsApprovalSave? save = null, IInterceptor? extra = null, IInterceptor? second = null, string? entryFault = null, bool auditDeadlock = false, bool auditInterceptor = true)
        {
            auditScope = run.Factory.Services.CreateScope(); auditScope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(AccountingInvoiceLineCogsSqlServerRelationalAcceptanceTests.Company);
            Collector = auditScope.ServiceProvider.GetRequiredService<AuditEventCollector>(); Writer = new(auditScope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>()) { AuditDeadlock = auditDeadlock };
            var options = new DbContextOptionsBuilder<AccountingDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", AccountingDbContext.Schema));
            if (auditInterceptor) options.AddInterceptors(new AuditSaveChangesInterceptor(Collector, Company));
            if (save is not null) options.AddInterceptors(save); if (extra is not null) options.AddInterceptors(extra); if (second is not null) options.AddInterceptors(second); if (extra is CogsApprovalCleanup cleanup) cleanup.Configure(options);
            Db = new(options.Options, Company); other = new(options.Options, Company);
            var core = new AccountingStore(entryFault == "other-context" ? other : Db, Company, entryFault == "other-collector" ? new AuditEventCollector() : Collector,
                TimeProvider.System, new AccountingAmountScaleVerifier(new AccountingSqlServerRelationalAcceptanceTests.SyntheticMoneyProfiles(AccountingInvoiceLineCogsSqlServerRelationalAcceptanceTests.Company)),
                normalApprovalAuditWriter: entryFault == "missing-writer" ? null : Writer);
            var store = new InvoiceLineCogsCorrectionStoreV1(Db, Company, CurrentPlan, Collector, entryFault == "other-writer" ? new CogsApprovalWriter(Writer) : Writer, TimeProvider.System,
                approvalSafetyOwner: entryFault == "missing-owner" ? null : core);
            Source = new(run.Source); var correlation = new CorrelationContext(); correlation.Set(CorrelationId, null, "Web", null, null);
            Service = new(store, Source, run.Source, core, Authority, Company, User, correlation);
        }
        public Task<AccountingApiResult<InvoiceLineCogsCorrectionViewV1>> Approve(Guid id, string key, CancellationToken token = default) => Service.ApproveAsync(Source.Actual.Facts.InvoiceId, Source.Actual.Facts.InvoiceLineId, id, new(1), key, token);
        public void AssertClean() { Assert.False(Db.ChangeTracker.HasChanges()); Assert.False(Writer.HasPendingDrafts); Assert.Null(Db.Database.CurrentTransaction); }
        public async ValueTask DisposeAsync() { try { await Db.DisposeAsync(); await other.DisposeAsync(); } finally { auditScope.Dispose(); } }
    }
    private sealed class CogsApprovalUser : ICurrentUser
    { public Guid Actor = Checker; public bool Member = true; public bool IsAuthenticated => true; public string UserId => Actor.ToString("D"); public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { AccountingInvoiceLineCogsSqlServerRelationalAcceptanceTests.Company }; public bool CanAccessCompany(Guid id) => Member && id == AccountingInvoiceLineCogsSqlServerRelationalAcceptanceTests.Company; }
    private sealed class CogsApprovalAuthority : IErpAuthorityRevalidationService
    { public Func<bool>? Read; public Func<Task>? BeforeRead; public async Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid id, CancellationToken token = default) { if (BeforeRead is not null) await BeforeRead(); return new(Read?.Invoke() ?? true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()); } }
    private sealed class CogsApprovalPlan : IActiveAccountPlanReaderV1
    { public int Reads; public Action? AfterRead; public async Task<ApprovedAccountingSourceCatalogRecordV1?> ReadActiveAsync(CancellationToken token = default) { Reads++; var answer = await new Plan().ReadActiveAsync(token); AfterRead?.Invoke(); return answer; } }
    private sealed class CogsApprovalSource(Source actual) : IAccountingFinalizedInvoiceLineSourceV1
    { public Source Actual => actual; public Action? AfterRead; public Task<FinalizedInvoiceCogsSelectionReadV1> FindAsync(Guid company, string number, CancellationToken token = default) => actual.FindAsync(company, number, token); public async Task<FinalizedInvoiceLineSourceReadV1> GetAsync(Guid company, Guid invoice, Guid line, CancellationToken token = default) { var answer = await actual.GetAsync(company, invoice, line, token); AfterRead?.Invoke(); return answer; } }
    private sealed class CogsApprovalWriter(IAuditTransactionWriterV1 actual) : IAuditTransactionWriterV1
    { public int Writes, Discards; public bool AuditDeadlock, Injected; public bool HasPendingDrafts => actual.HasPendingDrafts; public Task WritePendingAsync(System.Data.IDbTransaction tx, CancellationToken token = default) { Writes++; if (AuditDeadlock && !Injected) { Injected = true; throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205); } return actual.WritePendingAsync(tx, token); } public void DiscardPending() { Discards++; actual.DiscardPending(); } }
    private sealed class CogsApprovalSave : SaveChangesInterceptor
    {
        public string? Fault; public bool Injected, Saved; public int Attempts;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default)
        {
            if (data.Context is AccountingDbContext db && db.ChangeTracker.Entries<InvoiceLineCogsRevisionRow>().Any(e => e.State == EntityState.Added))
            { Attempts++; if (Fault is not null && !Injected) { Injected = true; if (Fault == "concurrency") throw new DbUpdateConcurrencyException("SENTETIK COGS race"); if (Fault == "completed-provider") db.Database.CurrentTransaction!.GetDbTransaction().Rollback(); var error = SqlConcurrencyWrapperPolicyTests.SqlFailure(1205); if (Fault == "wrapped") throw new DbUpdateException("SENTETIK COGS wrapped 1205", error); throw error; } }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default)
        { if (Attempts > 0) Saved = true; return ValueTask.FromResult(result); }
    }
    private sealed class CogsApprovalCommits : DbTransactionInterceptor
    { public int Commits; public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData data, CancellationToken token = default) { Commits++; return Task.CompletedTask; } }
    private sealed class CogsApprovalCleanup(bool dispose) : DbTransactionInterceptor
    {
        public bool Failed;
        public void Configure(DbContextOptionsBuilder<AccountingDbContext> options) => options.LogTo((id, _) => id == RelationalEventId.TransactionDisposed, _ => { if (dispose && !Failed) { Failed = true; throw new InvalidOperationException("SENTETIK COGS dispose fault"); } });
        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction transaction, TransactionEventData data, InterceptionResult result, CancellationToken token = default)
        { if (!dispose && !Failed) { Failed = true; throw new InvalidOperationException("SENTETIK COGS rollback fault"); } return ValueTask.FromResult(result); }
    }
    private sealed class CogsApprovalReadProbe(Action<string> after) : DbCommandInterceptor
    { public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data, DbDataReader reader, CancellationToken token = default) { after(command.CommandText); return ValueTask.FromResult(reader); } }
    private static AccountingCommandRow CogsCaller() => new() { CompanyId = Company, CommandName = "SENTETIK caller", IdempotencyKeyHash = "caller", ActorId = "caller", PayloadHash = "hash", ResultJson = "{}", CorrelationId = "corr" };
    private static AuditEventDraft CogsCallerAudit() => new(Company, "SENTETIK caller", "Accounting.InvoiceLineCogsCorrection", "caller", "Approve", 1, 2, "User", Checker.ToString("D"), AccountingJournalReversalPermissionNamesV1.Approve, [], "{}", "{}", "SENTETIK", null, AuditAiInvolvement.None, null, "SQLSource", null, null, "SENTETIK-caller", null, "SENTETIK-key");
}
