using System.Data.Common;
using System.Transactions;
using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceCommandApiSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Manual_bank_approval_revoked_before_save_after_save_or_before_commit_rolls_back_native_receipt_and_actual_audit()
    {
        await using var run = await ManualApprovalRun();
        foreach (var boundary in new[] { "pre-save", "post-save", "pre-commit", "post-save-actor", "post-save-company", "post-save-delegation" })
        {
            var id = await ManualApprovalDraft(run);
            var probe = new ManualApprovalSaveProbe(id);
            await using var scope = new ManualApprovalScope(run, probe: probe);
            var deny = true; var observed = false; var afterSaveChecks = 0;
            scope.Permissions.BeforeRead = () =>
            {
                if (!deny) return PermissionDecision.Allowed;
                if (probe.Saved && boundary is "post-save-actor" or "post-save-company")
                {
                    observed = true;
                    if (boundary == "post-save-actor") scope.User.Actor = Proposer;
                    else scope.Company.Set(CompanyB);
                    return PermissionDecision.Allowed;
                }
                if (boundary == "pre-save" && scope.Writer.HasPendingDrafts
                    || probe.Saved && (boundary is "post-save" or "post-save-delegation" || boundary == "pre-commit" && ++afterSaveChecks == 2))
                { observed = true; return PermissionDecision.Denied; }
                return PermissionDecision.Allowed;
            };
            var refused = await scope.Approve(id, "late-" + boundary);
            Assert.True(observed); Assert.Equal(CommandOutcome.Denied, refused.Outcome);
            Assert.Equal(boundary != "pre-save", probe.Saved);
            Assert.Empty(scope.Database.ChangeTracker.Entries()); Assert.False(scope.Writer.HasPendingDrafts);
            Assert.Null(scope.Database.Database.CurrentTransaction);
            Assert.Equal(1L, await run.ScalarAsync<long>("SELECT Version FROM finance.ManualBankPayments WHERE Id=@id", ("@id", id)));
            Assert.Equal(0, await ManualApprovalCommands(run, id)); Assert.Equal(0, await ManualApprovalAudits(run, id));
            deny = false; scope.Company.Set(CompanyA); scope.User.Actor = Approver;
            var saved = await scope.Approve(id, "late-" + boundary);
            Assert.Equal(CommandOutcome.Succeeded, saved.Outcome); Assert.False(saved.IsReplay);
            var replay = await scope.Approve(id, "late-" + boundary);
            Assert.True(replay.IsReplay); Assert.Equal(saved.Resource, replay.Resource);
            Assert.Equal(saved.OriginalCorrelationId, replay.OriginalCorrelationId); Assert.False(scope.Writer.HasPendingDrafts);
            Assert.Equal(1, await ManualApprovalCommands(run, id)); Assert.Equal(1, await ManualApprovalAudits(run, id));
            Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutgoingMoneyEvents WHERE PaymentId=@id", ("@id", id)));
        }
    }

    [SqlServerFact]
    public async Task Manual_bank_approval_entry_refusals_preserve_borrowed_dirty_ambient_and_actual_pending_audit_even_for_original_key_replay()
    {
        await using var run = await ManualApprovalRun(); var id = await ManualApprovalDraft(run);
        await using (var initial = new ManualApprovalScope(run))
            Assert.Equal(CommandOutcome.Succeeded, (await initial.Approve(id, "entry-original")).Outcome);
        foreach (var boundary in new[] { "missing-writer", "borrowed", "dirty", "audit", "ambient" })
        foreach (var replay in new[] { false, true })
        {
            await using var scope = new ManualApprovalScope(run, missingWriter: boundary == "missing-writer");
            await using var borrowed = boundary == "borrowed"
                ? await scope.Database.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable) : null;
            var dirty = new ManualBankPaymentRow { CompanyId = CompanyA, Id = Guid.NewGuid() };
            if (boundary == "dirty") scope.Database.ManualBankPayments.Add(dirty);
            if (boundary == "audit") scope.Collector.Enqueue(ManualApprovalCallerAudit(scope, id));
            using var ambient = boundary == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
            var result = await scope.Approve(id, replay ? "entry-original" : "entry-new");
            Assert.Equal(CommandOutcome.Unavailable, result.Outcome); Assert.Null(result.Resource);
            Assert.Equal(0, scope.Permissions.Calls);
            Assert.Equal(boundary == "dirty", scope.Database.ChangeTracker.HasChanges());
            if (boundary == "dirty") Assert.Same(dirty, Assert.Single(scope.Database.ChangeTracker.Entries()).Entity);
            Assert.Equal(boundary == "audit", scope.Writer.HasPendingDrafts);
            if (borrowed is not null) Assert.Same(borrowed, scope.Database.Database.CurrentTransaction);
        }
        Assert.Equal(1, await ManualApprovalCommands(run, id)); Assert.Equal(1, await ManualApprovalAudits(run, id));
    }

    [SqlServerFact]
    public async Task Manual_bank_approval_failed_save_reads_only_a_fresh_native_winner_after_owned_abort_without_mutation_retry()
    {
        await using var run = await ManualApprovalRun();
        foreach (var publish in new[] { false, true })
        {
            var id = await ManualApprovalDraft(run); var probe = new ManualApprovalSaveProbe(id) { FailSave = true };
            await using var scope = new ManualApprovalScope(run, probe: probe);
            var transactions = new HashSet<Guid>(); ManualBankPaymentCommandResult? winner = null;
            // Ordered publication after the first owned transaction is disposed, not a claim of simultaneous SQL writers.
            scope.Permissions.AfterRead = async () =>
            {
                if (scope.Database.Database.CurrentTransaction is not { } tx) return;
                transactions.Add(tx.TransactionId);
                if (transactions.Count != 2 || winner is not null || !publish) return;
                Assert.Empty(scope.Database.ChangeTracker.Entries()); Assert.False(scope.Writer.HasPendingDrafts);
                Assert.Equal(1, probe.Attempts);
                await using var actual = new ManualApprovalScope(run);
                winner = await actual.Approve(id, "race-original");
                Assert.Equal(CommandOutcome.Succeeded, winner.Outcome); Assert.False(winner.IsReplay);
            };
            var result = await scope.Approve(id, "race-original");
            Assert.Equal(2, transactions.Count); Assert.Equal(1, probe.Attempts);
            Assert.Empty(scope.Database.ChangeTracker.Entries()); Assert.False(scope.Writer.HasPendingDrafts);
            Assert.Null(scope.Database.Database.CurrentTransaction);
            if (publish)
            {
                Assert.NotNull(winner); Assert.True(result.IsReplay); Assert.Equal(winner.Resource, result.Resource);
                Assert.Equal(winner.OriginalCorrelationId, result.OriginalCorrelationId);
            }
            else
            {
                Assert.Equal(CommandOutcome.VersionConflict, result.Outcome);
                Assert.Equal("concurrent_change", Assert.Single(result.Issues).Code);
            }
            Assert.Equal(publish ? 1 : 0, await ManualApprovalCommands(run, id));
            Assert.Equal(publish ? 1 : 0, await ManualApprovalAudits(run, id));
            Assert.Equal(publish ? 2L : 1L, await run.ScalarAsync<long>("SELECT Version FROM finance.ManualBankPayments WHERE Id=@id", ("@id", id)));
        }
    }

    [SqlServerFact]
    public async Task Manual_bank_approval_terminal_version_receipt_is_atomic_immutable_and_replay_still_requires_fresh_authority()
    {
        await using var run = await ManualApprovalRun(); var id = await ManualApprovalDraft(run);
        var probe = new ManualApprovalSaveProbe(id);
        await using var scope = new ManualApprovalScope(run, probe: probe);
        scope.Permissions.BeforeRead = () => probe.Saved ? PermissionDecision.Denied : PermissionDecision.Allowed;
        Assert.Equal(CommandOutcome.Denied, (await scope.Approve(id, "stale-original", "9")).Outcome);
        Assert.Equal(0, await ManualApprovalCommands(run, id)); Assert.Equal(0, await ManualApprovalAudits(run, id));
        scope.Permissions.BeforeRead = null;
        var stored = await scope.Approve(id, "stale-original", "9"); Assert.Equal(CommandOutcome.VersionConflict, stored.Outcome);
        Assert.False(stored.IsReplay); Assert.Equal(1, await ManualApprovalCommands(run, id));
        Assert.Equal(CommandOutcome.Succeeded, (await scope.Approve(id, "valid-other")).Outcome);
        var replay = await scope.Approve(id, "stale-original", "9");
        Assert.Equal(CommandOutcome.VersionConflict, replay.Outcome); Assert.True(replay.IsReplay);
        Assert.Equal(stored.OriginalCorrelationId, replay.OriginalCorrelationId);
        scope.Permissions.BeforeRead = () => PermissionDecision.Denied;
        Assert.Equal(CommandOutcome.Denied, (await scope.Approve(id, "stale-original", "9")).Outcome);
        Assert.Equal(2, await ManualApprovalCommands(run, id)); Assert.Equal(1, await ManualApprovalAudits(run, id));
    }

    [SqlServerFact]
    public async Task Manual_bank_approval_rollback_failure_still_cleans_only_owned_tracker_and_actual_audit_without_replay()
    {
        await using var run = await ManualApprovalRun(); var id = await ManualApprovalDraft(run);
        var fault = new ManualApprovalRollbackFailure();
        await using var scope = new ManualApprovalScope(run, extra: fault);
        scope.Permissions.BeforeRead = () => scope.Writer.HasPendingDrafts ? PermissionDecision.Denied : PermissionDecision.Allowed;
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Approve(id, "rollback-failure"));
        Assert.Equal(1, fault.Attempts); Assert.Empty(scope.Database.ChangeTracker.Entries());
        Assert.False(scope.Writer.HasPendingDrafts); Assert.Null(scope.Database.Database.CurrentTransaction);
        Assert.Equal(0, await ManualApprovalCommands(run, id)); Assert.Equal(0, await ManualApprovalAudits(run, id));
        Assert.Equal(1L, await run.ScalarAsync<long>("SELECT Version FROM finance.ManualBankPayments WHERE Id=@id", ("@id", id)));
    }

    private static Task<Run> ManualApprovalRun() => Run.StartAsync(services =>
    {
        services.RemoveAll<IFinancePartyAccountPort>(); services.AddSingleton<SyntheticGate>();
        services.AddScoped<IFinancePartyAccountPort, SyntheticParties>();
        // Controlled context-switch boundary; the actual Audit writer and Finance context share this very same scoped instance.
        services.RemoveAll<ICompanyContext>(); services.AddScoped<ICompanyContext>(_ => new ManualApprovalCompany());
    });

    private static async Task<Guid> ManualApprovalDraft(Run run)
    {
        var bank = await run.ApprovedBankAccountAsync(CompanyA);
        await run.GrantAsync(Proposer, CompanyA, ManualPaymentPreparerGrants);
        using var maker = run.Client(Proposer, CompanyA);
        using var response = await maker.SendAsync(Post(FinanceEndpoints.ManualBankPaymentsPath,
            "SENTETIK-native-draft-" + Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), PaymentSave(bank, null)));
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return (await Json(response)).GetProperty("resource").GetProperty("id").GetGuid();
    }

    private static Task<int> ManualApprovalCommands(Run run, Guid id) => run.ScalarAsync<int>(
        "SELECT COUNT(*) FROM finance.Commands WHERE CommandName=@command AND ManualBankPaymentId=@id", ("@command", ManualBankPaymentService.ApproveCommand), ("@id", id));
    private static Task<int> ManualApprovalAudits(Run run, Guid id) => run.ScalarAsync<int>(
        "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'finance.manual-bank-payment.approved.v1' AND EntityId=@id", ("@id", id.ToString("D")));
    private static AuditEventDraft ManualApprovalCallerAudit(ManualApprovalScope scope, Guid id) => new(CompanyA,
        "finance.manual-bank-payment.approved.v1", "Finance.ManualBankPayment", id.ToString("D"), "Approve", 1, 2,
        "User", Approver, FinancePermissionNames.ManualBankPaymentApprove, [], "{}", "{}", "SENTETIK caller draft", null,
        AuditAiInvolvement.None, null, "Web", null, null, scope.Correlation, null, "SENTETIK caller");

    // Real Finance SQL and the real Audit writer sharing the very same collector, company and owned transaction.
    // Permission/delegation-fence decisions and the Save fault are controlled; not a full Identity/Work delegation E2E claim.
    private sealed class ManualApprovalScope : IAsyncDisposable
    {
        private readonly IServiceScope auditScope;
        public ICompanyContext Company { get; }
        public FinanceDbContext Database { get; }
        public AuditEventCollector Collector { get; }
        public IAuditTransactionWriterV1 Writer { get; }
        public ManualApprovalPermissions Permissions { get; } = new();
        public IManualBankPaymentService Service { get; }
        public ManualApprovalUser User { get; } = new();
        public string Correlation { get; } = "manual-approval-safety-" + Guid.NewGuid().ToString("N");
        public ManualApprovalScope(Run run, bool missingWriter = false, ManualApprovalSaveProbe? probe = null, IInterceptor? extra = null)
        {
            auditScope = run.Factory.Services.CreateScope();
            Company = auditScope.ServiceProvider.GetRequiredService<ICompanyContext>(); Company.Set(CompanyA);
            Collector = auditScope.ServiceProvider.GetRequiredService<AuditEventCollector>();
            Writer = auditScope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
            var options = new DbContextOptionsBuilder<FinanceDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema));
            options.AddInterceptors(new AuditSaveChangesInterceptor(Collector, Company));
            if (probe is not null) options.AddInterceptors(probe);
            if (extra is not null) options.AddInterceptors(extra);
            Database = new(options.Options, Company);
            var core = new ManualBankPaymentService(Database, Company, User, Permissions,
                new SyntheticParties(new SyntheticGate()), new UnavailableManualBankPaymentDocumentValidationPort(),
                auditScope.ServiceProvider.GetRequiredService<ISettingsPrerequisiteProvider>(), new SyntheticOpenPeriodRule(), Collector,
                new(FinanceMoneyOperations.ManualBankPaymentV1), new(new(FinancePermissionNames.ManualBankPaymentDraftSave, [], "Web"),
                    new(FinancePermissionNames.ManualBankPaymentApprove, [], "Web"), new(FinancePermissionNames.ManualBankPaymentRecord, [], "Web")),
                TimeProvider.System, new SyntheticSufficientBankBalance(), missingWriter ? null : Writer);
            Service = new GuardedManualBankPaymentService(core, Database, missingWriter ? null : Writer);
        }
        public Task<ManualBankPaymentCommandResult> Approve(Guid id, string key, string version = "1") =>
            Service.ApproveAsync(new(CompanyA, Approver, Correlation, null), new(key), new(id, version), default);
        public async ValueTask DisposeAsync() { try { await Database.DisposeAsync(); } finally { auditScope.Dispose(); } }
    }
    private sealed class ManualApprovalUser : ICurrentUser
    {
        public string Actor = Approver;
        public bool IsAuthenticated => true; public string UserId => Actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([CompanyA]);
        public bool CanAccessCompany(Guid id) => id == CompanyA;
    }
    private sealed class ManualApprovalCompany : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; private set; }
        public void Set(Guid value) => CompanyId = value;
    }
    private sealed class ManualApprovalPermissions : IFinancePermissionPort
    {
        public int Calls; public Func<PermissionDecision>? BeforeRead; public Func<Task>? AfterRead;
        public async Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? id, CancellationToken token)
        { Calls++; Assert.Equal(FinanceAction.ApproveManualBankPayment, action); var result = BeforeRead?.Invoke() ?? PermissionDecision.Allowed; if (AfterRead is not null) await AfterRead(); return result; }
    }
    private sealed class ManualApprovalSaveProbe(Guid id) : SaveChangesInterceptor
    {
        public bool Saved, FailSave; public int Attempts;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default)
        {
            if (data.Context is FinanceDbContext db && (db.ChangeTracker.Entries<ManualBankPaymentRow>().Any(row => row.Entity.Id == id && row.State == EntityState.Modified)
                || db.ChangeTracker.Entries<FinanceCommandRow>().Any(row => row.Entity.ManualBankPaymentId == id && row.State == EntityState.Added)))
            { Attempts++; if (FailSave) throw new DbUpdateConcurrencyException("SENTETIK native manual approval Save fault"); }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default)
        { if (data.Context is FinanceDbContext && Attempts > 0) Saved = true; return ValueTask.FromResult(result); }
    }
    private sealed class ManualApprovalRollbackFailure : DbTransactionInterceptor
    {
        public int Attempts;
        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction transaction, TransactionEventData data,
            InterceptionResult result, CancellationToken token = default)
        { Attempts++; throw new InvalidOperationException("SENTETIK manual approval rollback fault"); }
    }
}
