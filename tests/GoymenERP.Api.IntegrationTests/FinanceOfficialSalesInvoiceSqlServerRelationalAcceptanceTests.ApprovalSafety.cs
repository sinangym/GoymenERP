using System.Data.Common;
using System.Text.Json;
using System.Transactions;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceOfficialSalesInvoiceSqlServerRelationalAcceptanceTests
{
    private const string SafetyChecker = "SENTETIK-checker", SafetyMaker = "SENTETIK-maker", SafetyReason = "SENTETIK ret";
    private static int safetyNumber = 100;

    [SqlServerFact]
    public async Task Official_decisions_late_authority_actor_company_membership_and_delegation_loss_roll_back_then_retry_once()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        foreach (var fault in new[] { "before-audit", "after-audit", "after-save", "precommit", "actor", "company", "membership", "delegation" })
        {
            var id = await OfficialSafetyPrepare(run); var probe = new OfficialSafetySaveProbe(id);
            await using var scope = new OfficialSafetyScope(run, probe: probe);
            var deny = true; var observed = false; var observedPendingAudit = false; var savedReads = 0;
            scope.Authority.BeforeRead = () =>
            {
                var staged = scope.Database.ChangeTracker.Entries<FinanceOfficialSalesInvoiceRow>().Any(row => row.Entity.Id == id && row.Entity.Version == 2);
                if (staged && scope.Writer.HasPendingDrafts) observedPendingAudit = true;
                if (probe.Saved) savedReads++;
                // Save's fence, wrapper post-execute fence, then the final precommit fence.
                var phase = fault == "before-audit" ? staged && scope.Writer.HasPendingDrafts
                    // A staged decision also exists before Audit enqueue; only a seen-and-drained draft proves the real writer ran.
                    : fault == "after-audit" ? staged && observedPendingAudit && !scope.Writer.HasPendingDrafts
                    : fault == "precommit" ? savedReads >= 3 : probe.Saved;
                if (!deny || !phase) return PermissionDecision.Allowed;
                observed = true;
                if (fault == "actor") { scope.User.Actor = "SENTETIK-changed"; return PermissionDecision.Allowed; }
                if (fault == "company") { scope.Company.Set(TestCompanyIds.CompanyB); return PermissionDecision.Allowed; }
                if (fault == "membership") { scope.User.Member = false; return PermissionDecision.Allowed; }
                // The authority-port denial also represents a revoked Work delegation callback; not actual Identity/WP3 acceptance.
                return PermissionDecision.Denied;
            };
            Assert.Equal(CommandOutcome.Denied, (await scope.Decide(id, approve, "official-late-" + fault)).Outcome);
            if (fault == "after-audit") { Assert.True(observedPendingAudit); Assert.False(probe.Saved); }
            Assert.True(observed); scope.AssertClean(); await OfficialSafetyDurable(run, id, 1, 1, 0, 0);
            deny = false; scope.User.Actor = SafetyChecker; scope.User.Member = true; scope.Company.Set(Company); probe.Saved = false;
            Assert.Equal(CommandOutcome.Succeeded, (await scope.Decide(id, approve, "official-late-" + fault)).Outcome);
            await OfficialSafetyDurable(run, id, 2, approve ? 2 : 3, 1, 1);
        }
    }

    [SqlServerFact]
    public async Task Official_decision_entry_preserves_borrowed_ambient_dirty_pending_and_await_created_caller_work()
    {
        await using var run = await Run.StartAsync();
        using (var actual = run.Factory.Services.CreateScope())
        {
            var service = actual.ServiceProvider.GetRequiredService<IFinanceOfficialSalesInvoiceServiceV1>();
            Assert.IsNotType<FinanceOfficialSalesInvoiceServiceV1>(service);
            Assert.Same(service, actual.ServiceProvider.GetRequiredService<IFinanceOfficialSalesInvoiceCandidatesV1>());
        }
        foreach (var approve in new[] { true, false })
        foreach (var boundary in new[] { "borrowed", "ambient", "dirty", "audit", "await-dirty", "await-audit", "missing-writer" })
        {
            var id = await OfficialSafetyPrepare(run);
            await using var scope = new OfficialSafetyScope(run, missingWriter: boundary == "missing-writer");
            var row = new FinanceOfficialSalesInvoiceRow { Id = Guid.NewGuid(), CompanyId = Company };
            if (boundary == "dirty") scope.Database.OfficialSalesInvoices.Add(row);
            if (boundary == "audit") scope.Collector.Enqueue(OfficialSafetyDraft(id));
            if (boundary.StartsWith("await-", StringComparison.Ordinal)) scope.Authority.BeforeRead = () =>
            { if (boundary == "await-dirty") scope.Database.OfficialSalesInvoices.Add(row); else scope.Collector.Enqueue(OfficialSafetyDraft(id)); return PermissionDecision.Allowed; };
            await using var borrowed = boundary == "borrowed" ? await scope.Database.Database.BeginTransactionAsync() : null;
            using var ambient = boundary == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
            var original = Transaction.Current;
            Assert.Equal(CommandOutcome.Unavailable, (await scope.Decide(id, approve, "official-entry-" + boundary)).Outcome);
            Assert.Same(original, Transaction.Current); Assert.Same(borrowed, scope.Database.Database.CurrentTransaction);
            Assert.Equal(boundary is "dirty" or "await-dirty", scope.Database.ChangeTracker.HasChanges());
            Assert.Equal(boundary is "audit" or "await-audit", scope.Writer.HasPendingDrafts);
            if (boundary is "dirty" or "await-dirty") Assert.Same(row, Assert.Single(scope.Database.ChangeTracker.Entries()).Entity);
            // A separate connection cannot enlist in the caller's ambient test transaction.
            if (ambient is null) await OfficialSafetyDurable(run, id, 1, 1, 0, 0);
            scope.Writer.DiscardPending();
        }
    }

    [SqlServerFact]
    public async Task Official_failed_save_cleans_own_audit_then_reads_one_original_winner_without_retrying_mutation()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        foreach (var publishWinner in new[] { true, false })
        {
            var id = await OfficialSafetyPrepare(run); var probe = new OfficialSafetySaveProbe(id) { FailSave = true };
            await using var scope = new OfficialSafetyScope(run, probe: probe);
            var key = "official-race-" + id; var boundaries = new HashSet<Guid>(); FinanceOfficialSalesInvoiceResultV1? winner = null;
            probe.BeforeFailure = () => scope.Collector.Enqueue(OfficialSafetyDraft(id));
            scope.Authority.AfterRead = async () =>
            {
                if (scope.Database.Database.CurrentTransaction is not { } tx) return;
                boundaries.Add(tx.TransactionId);
                if (boundaries.Count != 2 || winner is not null || !publishWinner) return;
                Assert.Empty(scope.Database.ChangeTracker.Entries()); Assert.False(scope.Writer.HasPendingDrafts); Assert.Equal(1, probe.Attempts);
                await using var actual = new OfficialSafetyScope(run); winner = await actual.Decide(id, approve, key);
                Assert.Equal(CommandOutcome.Succeeded, winner.Outcome); Assert.False(winner.IsReplay);
            };
            var result = await scope.Decide(id, approve, key);
            Assert.Equal(2, boundaries.Count); Assert.Equal(1, probe.Attempts); scope.AssertClean();
            if (publishWinner) { Assert.NotNull(winner); Assert.True(result.IsReplay); Assert.Equal(JsonSerializer.Serialize(winner.Resource, FinanceJson.CreateOptions()), JsonSerializer.Serialize(result.Resource, FinanceJson.CreateOptions())); Assert.Equal(winner.OriginalCorrelationId, result.OriginalCorrelationId); }
            else { Assert.Equal(CommandOutcome.VersionConflict, result.Outcome); Assert.Equal("concurrent_change", Assert.Single(result.Issues).Code); }
            await OfficialSafetyDurable(run, id, publishWinner ? 2 : 1, publishWinner ? approve ? 2 : 3 : 1, publishWinner ? 1 : 0, publishWinner ? 1 : 0);
            if (!publishWinner)
            {
                probe.FailSave = false; Assert.Equal(CommandOutcome.Succeeded, (await scope.Decide(id, approve, key)).Outcome);
                Assert.Equal(2, probe.Attempts); await OfficialSafetyDurable(run, id, 2, approve ? 2 : 3, 1, 1);
            }
        }
    }

    [SqlServerFact]
    public async Task Official_rollback_fault_still_disposes_and_discards_owned_work_without_attempting_winner_replay()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        {
            var id = await OfficialSafetyPrepare(run); var probe = new OfficialSafetySaveProbe(id) { FailSave = true }; var rollback = new OfficialSafetyRollbackFault();
            await using var scope = new OfficialSafetyScope(run, probe: probe, extra: rollback);
            probe.BeforeFailure = () => scope.Collector.Enqueue(OfficialSafetyDraft(id));
            var boundaries = new HashSet<Guid>(); scope.Authority.AfterRead = () => { if (scope.Database.Database.CurrentTransaction is { } tx) boundaries.Add(tx.TransactionId); return Task.CompletedTask; };
            var error = await Assert.ThrowsAsync<AggregateException>(() => scope.Decide(id, approve, "official-rollback-" + id));
            Assert.Contains(error.InnerExceptions, item => item is InvalidOperationException && item.Message == "SENTETIK official rollback fault");
            Assert.Equal(1, rollback.Attempts); Assert.Single(boundaries); Assert.Equal(1, probe.Attempts); scope.AssertClean();
            await OfficialSafetyDurable(run, id, 1, 1, 0, 0);
        }
    }

    [SqlServerFact]
    public async Task Official_save_created_audit_is_rolled_back_and_does_not_poison_the_original_key_retry()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        {
            var id = await OfficialSafetyPrepare(run); var probe = new OfficialSafetySaveProbe(id);
            await using var scope = new OfficialSafetyScope(run, probe: probe);
            probe.AfterSave = () => scope.Collector.Enqueue(OfficialSafetyDraft(id));
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Decide(id, approve, "official-save-draft-" + id));
            scope.AssertClean(); await OfficialSafetyDurable(run, id, 1, 1, 0, 0);
            probe.AfterSave = null; Assert.Equal(CommandOutcome.Succeeded, (await scope.Decide(id, approve, "official-save-draft-" + id)).Outcome);
            await OfficialSafetyDurable(run, id, 2, approve ? 2 : 3, 1, 1);
        }
    }

    [SqlServerFact]
    public async Task Official_commit_then_authority_loss_suppresses_release_but_keeps_the_original_receipt_and_single_audit()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        {
            var id = await OfficialSafetyPrepare(run); var committed = new OfficialSafetyCommitted();
            await using var scope = new OfficialSafetyScope(run, extra: committed);
            committed.AfterCommit = () => scope.User.Member = false;
            var key = "official-committed-" + id;
            Assert.Equal(CommandOutcome.Denied, (await scope.Decide(id, approve, key)).Outcome);
            Assert.Equal(1, committed.Commits); scope.AssertClean(); await OfficialSafetyDurable(run, id, 2, approve ? 2 : 3, 1, 1);
            var original = await run.ScalarAsync<string>("SELECT ResultJson FROM finance.OfficialSalesInvoiceCommands WHERE CompanyId=@company AND InvoiceId=@id AND CommandName=@command",
                ("@company", Company), ("@id", id), ("@command", approve ? FinanceOfficialSalesInvoiceServiceV1.ApproveCommand : FinanceOfficialSalesInvoiceServiceV1.RejectCommand));
            committed.AfterCommit = null; scope.User.Member = true; scope.Correlation = "SENTETIK-new-correlation";
            var replay = await scope.Decide(id, approve, key);
            Assert.True(replay.IsReplay); Assert.Equal(CommandOutcome.Succeeded, replay.Outcome);
            Assert.Equal("2", replay.Resource!.Version); Assert.Equal(approve ? FinanceOfficialSalesInvoiceStatusV1.Approved : FinanceOfficialSalesInvoiceStatusV1.Rejected, replay.Resource.Status);
            Assert.Equal(scope.OriginalCorrelation, replay.OriginalCorrelationId);
            Assert.Equal(original, await run.ScalarAsync<string>("SELECT ResultJson FROM finance.OfficialSalesInvoiceCommands WHERE CompanyId=@company AND InvoiceId=@id AND CommandName=@command",
                ("@company", Company), ("@id", id), ("@command", approve ? FinanceOfficialSalesInvoiceServiceV1.ApproveCommand : FinanceOfficialSalesInvoiceServiceV1.RejectCommand)));
            await OfficialSafetyDurable(run, id, 2, approve ? 2 : 3, 1, 1);
        }
    }

    [SqlServerFact]
    public async Task Official_native_maker_company_version_reason_and_terminal_replay_rules_survive_the_guarded_runtime()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        {
            var id = await OfficialSafetyPrepare(run);
            await using (var maker = new OfficialSafetyScope(run))
            {
                maker.User.Actor = SafetyMaker;
                Assert.Equal("approver_must_differ_from_recorder", Assert.Single((await maker.Decide(id, approve, "official-maker", actor: SafetyMaker)).Issues).Code);
                maker.AssertClean();
            }
            await using (var foreign = new OfficialSafetyScope(run, company: TestCompanyIds.CompanyB))
            {
                var hidden = await foreign.Decide(id, approve, "official-foreign");
                Assert.Equal("invoice_not_found", Assert.Single(hidden.Issues).Code); Assert.Null(hidden.Resource); foreign.AssertClean();
            }
            await using var scope = new OfficialSafetyScope(run);
            var stale = await scope.Decide(id, approve, "official-stale", version: "9"); Assert.Equal(CommandOutcome.VersionConflict, stale.Outcome);
            var staleReplay = await scope.Decide(id, approve, "official-stale", version: "9"); Assert.True(staleReplay.IsReplay); Assert.Equal(stale.OriginalCorrelationId, staleReplay.OriginalCorrelationId);
            if (!approve)
            {
                Assert.Equal(CommandOutcome.ValidationFailed, (await scope.Decide(id, false, "official-reason-empty", reason: " ")).Outcome);
                Assert.Equal(CommandOutcome.ValidationFailed, (await scope.Decide(id, false, "official-reason-long", reason: new string('x', 1001))).Outcome);
            }
            Assert.Equal(CommandOutcome.Succeeded, (await scope.Decide(id, approve, "official-valid", reason: new string('x', 1000))).Outcome);
            Assert.Equal(CommandOutcome.IdempotencyConflict, (await scope.Decide(id, approve, "official-valid", version: "2")).Outcome);
            scope.AssertClean(); await OfficialSafetyDurable(run, id, 2, approve ? 2 : 3, 2, 1);
        }
    }

    private static async Task<Guid> OfficialSafetyPrepare(Run run)
    {
        var id = await ReserveAsync(run, SafetyMaker); var number = Interlocked.Increment(ref safetyNumber);
        await using var maker = run.Service(SafetyMaker, pdfSha: Sha("SENTETIK safety " + id));
        var request = Request(id) with { OfficialNo = "SNT2026" + number.ToString("D9", System.Globalization.CultureInfo.InvariantCulture), Ettn = Guid.NewGuid().ToString("D"), PdfDocumentId = Guid.NewGuid() };
        Assert.Equal(CommandOutcome.Succeeded, (await maker.Service.RecordAsync(maker.Context, new("official-safety-record-" + id), request, default)).Outcome);
        return id;
    }
    private static async Task OfficialSafetyDurable(Run run, Guid id, long version, int status, int attempts, int audits)
    {
        Assert.Equal(version, await run.ScalarAsync<long>("SELECT Version FROM finance.OfficialSalesInvoices WHERE CompanyId=@company AND Id=@id", ("@company", Company), ("@id", id)));
        Assert.Equal(status, await run.ScalarAsync<int>("SELECT Status FROM finance.OfficialSalesInvoices WHERE CompanyId=@company AND Id=@id", ("@company", Company), ("@id", id)));
        Assert.Equal(attempts, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialSalesInvoiceCommands WHERE CompanyId=@company AND InvoiceId=@id AND CommandName IN (N'finance.official-sales-invoices.approve',N'finance.official-sales-invoices.reject')", ("@company", Company), ("@id", id)));
        Assert.Equal(audits, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE CompanyId=@company AND EntityId=@id AND EventType IN (N'finance.official-sales-invoice.approved.v1',N'finance.official-sales-invoice.rejected.v1')", ("@company", Company), ("@id", id.ToString("D"))));
    }
    private static AuditEventDraft OfficialSafetyDraft(Guid id) => new(Company, "finance.official-sales-invoice.approved.v1", "Finance.OfficialSalesInvoice", id.ToString("D"), "Approve", 1, 2,
        "User", SafetyChecker, FinanceOfficialSalesInvoicePermissionNamesV1.Approve, [], "{}", "{}", "SENTETIK own fault draft", null, AuditAiInvolvement.None, null, "Web", null, null, "SENTETIK", null, "SENTETIK-key");

    // Real migrated owner SQL + the actual scoped Audit writer and its collector. Document/Settings/customer and
    // authority/delegation timing are controlled ports; no actual Identity, HTTP or simultaneous-race claim.
    private sealed class OfficialSafetyScope : IAsyncDisposable
    {
        private readonly IServiceScope owner; private readonly Guid company;
        public readonly OfficialSafetyCompany Company = new(); public readonly OfficialSafetyUser User = new(); public readonly OfficialSafetyAuthority Authority = new();
        public FinanceDbContext Database { get; } public AuditEventCollector Collector { get; } public IAuditTransactionWriterV1 Writer { get; }
        private readonly IFinanceOfficialSalesInvoiceServiceV1 service;
        public string OriginalCorrelation { get; } = "official-safety-" + Guid.NewGuid().ToString("N"); public string Correlation { get; set; }
        public OfficialSafetyScope(Run run, OfficialSafetySaveProbe? probe = null, IInterceptor? extra = null, bool missingWriter = false, Guid? company = null)
        {
            this.company = company ?? FinanceOfficialSalesInvoiceSqlServerRelationalAcceptanceTests.Company; Company.Set(this.company); Correlation = OriginalCorrelation;
            owner = run.Factory.Services.CreateScope(); owner.ServiceProvider.GetRequiredService<ICompanyContext>().Set(this.company);
            Collector = owner.ServiceProvider.GetRequiredService<AuditEventCollector>(); Writer = owner.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
            var options = new DbContextOptionsBuilder<FinanceDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema));
            options.AddInterceptors(new AuditSaveChangesInterceptor(Collector, Company)); if (probe is not null) options.AddInterceptors(probe); if (extra is not null) options.AddInterceptors(extra);
            Database = new(options.Options, Company);
            var envelope = new FinanceAuditEnvelope(FinanceOfficialSalesInvoicePermissionNamesV1.Approve, [], "Web");
            service = new FinanceOfficialSalesInvoiceServiceV1(Database, Company, User, Authority, new OfficialSafetyDocuments(Database), new Candidates(), Collector,
                new(envelope, envelope, envelope, envelope), TimeProvider.System, new OfficialSafetyCustomer()).WithApprovalSafety(missingWriter ? null : Writer);
        }
        public Task<FinanceOfficialSalesInvoiceResultV1> Decide(Guid id, bool approve, string key, string version = "1", string reason = SafetyReason, string actor = SafetyChecker) => approve
            ? service.ApproveAsync(new(company, actor, Correlation, null), new(key), new(id, version), default)
            : service.RejectAsync(new(company, actor, Correlation, null), new(key), new(id, version, reason), default);
        public void AssertClean() { Assert.DoesNotContain(Database.ChangeTracker.Entries(), entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted); Assert.False(Writer.HasPendingDrafts); Assert.Null(Database.Database.CurrentTransaction); }
        public async ValueTask DisposeAsync() { try { await Database.DisposeAsync(); } finally { owner.Dispose(); } }
    }
    private sealed class OfficialSafetyCompany : ICompanyContext
    { public bool IsResolved => CompanyId != Guid.Empty; public Guid CompanyId { get; private set; } public void Set(Guid id) => CompanyId = id; }
    private sealed class OfficialSafetyUser : ICurrentUser
    { public string Actor = SafetyChecker; public bool Member = true; public bool IsAuthenticated => true; public string UserId => Actor;
      public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company, TestCompanyIds.CompanyB }; public bool CanAccessCompany(Guid id) => Member && AllowedCompanyIds.Contains(id); }
    private sealed class OfficialSafetyAuthority : IFinanceOfficialSalesInvoiceAuthorityV1
    {
        public Func<PermissionDecision>? BeforeRead; public Func<Task>? AfterRead;
        public async Task<PermissionDecision> CheckAsync(FinanceContext c, FinanceOfficialSalesInvoiceAuthorityActionV1 a, Guid id, CancellationToken token)
        { Assert.Contains(a, new[] { FinanceOfficialSalesInvoiceAuthorityActionV1.Approve, FinanceOfficialSalesInvoiceAuthorityActionV1.Reject }); var answer = BeforeRead?.Invoke() ?? PermissionDecision.Allowed; if (AfterRead is not null) await AfterRead(); return answer; }
    }
    private sealed class OfficialSafetyDocuments(FinanceDbContext database) : IFinanceOfficialSalesInvoiceDocumentPortV1
    { public async Task<FinanceOfficialSalesInvoicePdfCheckV1> CheckAsync(FinanceContext c, Guid document, Guid id, int? version, CancellationToken t)
      { var row = await database.OfficialSalesInvoices.AsNoTracking().SingleAsync(item => item.CompanyId == c.CompanyId && item.Id == id && item.PdfDocumentId == document, t); return new(FinanceOfficialSalesInvoicePdfOutcomeV1.Accepted, row.PdfDocumentVersionNo, row.PdfSha256); } }
    private sealed class OfficialSafetyCustomer : IFinanceOfficialSalesInvoiceCustomerPortV1
    { public Task<ReadOutcome> CheckCompanyPartyAsync(FinanceContext c, Guid id, CancellationToken t) => Task.FromResult(id == Customer ? ReadOutcome.Found : ReadOutcome.NotFound); }
    private sealed class OfficialSafetySaveProbe(Guid id) : SaveChangesInterceptor
    {
        public bool Saved, FailSave; public int Attempts; public Action? BeforeFailure, AfterSave;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default)
        { if (data.Context is FinanceDbContext db && db.ChangeTracker.Entries<FinanceOfficialSalesInvoiceCommandRow>().Any(row => row.Entity.InvoiceId == id && row.State == EntityState.Added)) { Attempts++; if (FailSave) { BeforeFailure?.Invoke(); throw new DbUpdateConcurrencyException("SENTETIK official save fault"); } } return ValueTask.FromResult(result); }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default)
        { if (Attempts > 0) { Saved = true; AfterSave?.Invoke(); } return ValueTask.FromResult(result); }
    }
    private sealed class OfficialSafetyRollbackFault : DbTransactionInterceptor
    { public int Attempts; public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction tx, TransactionEventData data, InterceptionResult result, CancellationToken token = default)
      { Attempts++; throw new InvalidOperationException("SENTETIK official rollback fault"); } }
    private sealed class OfficialSafetyCommitted : DbTransactionInterceptor
    { public int Commits; public Action? AfterCommit; public override Task TransactionCommittedAsync(DbTransaction tx, TransactionEndEventData data, CancellationToken token = default)
      { Commits++; AfterCommit?.Invoke(); return Task.CompletedTask; } }
}
