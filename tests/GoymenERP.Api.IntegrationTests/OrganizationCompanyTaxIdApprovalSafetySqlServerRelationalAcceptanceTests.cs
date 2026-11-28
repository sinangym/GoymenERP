using System.Data.Common;
using System.Text.Json;
using System.Transactions;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Domain.Companies;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace GoymenERP.Api.IntegrationTests;

// Real migrated SQL and actual scoped Audit collector/writer. Authority revocation and provider failures below are
// controlled injection, not real Identity/WP3/simultaneous deadlock/HTTP acceptance. Existing Identity SQL tests remain.
public sealed partial class OrganizationCompanyTaxIdApprovalSafetySqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyId = TestCompanyIds.CompanyA;
    private const string Maker = "SENTETIK-tax-safety-maker";
    private const string Checker = "SENTETIK-tax-safety-checker";

    [SqlServerFact]
    public async Task Tax_decisions_rollback_actual_audit_mutation_and_receipt_at_each_late_authority_boundary()
    {
        await using var run = new Run();
        foreach (var approve in new[] { true, false })
        foreach (var boundary in new[] { "before-audit", "after-audit", "post-save", "precommit", "actor", "company", "membership" })
        {
            var prepared = await run.Prepare(); var save = new SaveProbe(); await using var scope = new Scope(run, save);
            var pendingSeen = false; var savedFences = 0; var triggered = false; var deny = true;
            scope.Authority.AfterRead = () =>
            {
                var staged = scope.Db.ChangeTracker.Entries<CompanyTaxIdChangeRequest>().Any(x => x.Entity.Id == prepared.Id && !x.Entity.IsPending);
                if (staged && scope.Writer.HasPendingDrafts) pendingSeen = true;
                if (save.Saved) savedFences++;
                var hit = boundary == "before-audit" ? staged && scope.Writer.HasPendingDrafts
                    : boundary == "after-audit" ? staged && pendingSeen && !scope.Writer.HasPendingDrafts
                    // Save release (1), post-execute (2), precommit (3), with no receipt read in this native204 path.
                    : boundary == "precommit" ? savedFences >= 3 : save.Saved;
                if (!deny || !hit) return Task.CompletedTask;
                triggered = true;
                if (boundary == "actor") scope.User.Actor = "SENTETIK-other";
                else if (boundary == "company") scope.Company.Set(TestCompanyIds.CompanyB);
                else if (boundary == "membership") scope.User.Member = false;
                else scope.Authority.Granted = false;
                return Task.CompletedTask;
            };
            Assert.Equal(CommandResult.NotFound, (await scope.Decide(prepared, approve)).Result);
            Assert.True(triggered); if (boundary == "after-audit") { Assert.True(pendingSeen); Assert.False(save.Saved); }
            if (boundary == "precommit") Assert.Equal(3, savedFences);
            scope.AssertClean(); await run.AssertDurable(prepared, approve, scope.OriginalCorrelation, false);
            deny = false; scope.Authority.Granted = true; scope.User.Actor = Checker; scope.User.Member = true; scope.Company.Set(CompanyId); save.Saved = false;
            var first = await scope.Decide(prepared, approve); Assert.Equal(CommandResult.Succeeded, first.Result); Assert.False(first.IsReplay);
            var replay = await scope.Decide(prepared, approve); Assert.True(replay.IsReplay); Assert.Equal(first.OriginalCorrelationId, replay.OriginalCorrelationId);
            scope.AssertClean(); await run.AssertDurable(prepared, approve, scope.OriginalCorrelation, true);
        }
    }

    [SqlServerFact]
    public async Task Tax_entry_preserves_borrowed_ambient_dirty_pending_and_await_created_caller_work()
    {
        await using var run = new Run();
        foreach (var boundary in new[] { "borrowed", "ambient", "dirty", "audit", "await-dirty", "await-audit", "missing-writer" })
        {
            var prepared = await run.Prepare(); await using var scope = new Scope(run, missingWriter: boundary == "missing-writer");
            var row = new CompanyTaxIdChangeRequest(Guid.NewGuid(), CompanyId, prepared.Vkn, prepared.Proposed, prepared.Version, "SENTETIK caller", Maker, DateTimeOffset.UtcNow);
            void AddWork() { if (boundary.EndsWith("dirty", StringComparison.Ordinal)) scope.Db.CompanyTaxIdChangeRequests.Add(row); else scope.Collector.Enqueue(Draft()); }
            if (boundary is "dirty" or "audit") AddWork();
            if (boundary.StartsWith("await-", StringComparison.Ordinal)) scope.Authority.AfterRead = () => { AddWork(); return Task.CompletedTask; };
            await using var borrowed = boundary == "borrowed" ? await scope.Db.Database.BeginTransactionAsync() : null;
            using var ambient = boundary == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
            var originalAmbient = Transaction.Current;
            Assert.Equal(CommandResult.DependencyUnavailable, (await scope.Decide(prepared, true)).Result);
            Assert.Same(originalAmbient, Transaction.Current); Assert.Same(borrowed, scope.Db.Database.CurrentTransaction);
            Assert.Equal(boundary.EndsWith("dirty", StringComparison.Ordinal), scope.Db.ChangeTracker.HasChanges());
            if (boundary.EndsWith("dirty", StringComparison.Ordinal)) Assert.Same(row, Assert.Single(scope.Db.ChangeTracker.Entries()).Entity);
            Assert.Equal(boundary.EndsWith("audit", StringComparison.Ordinal), scope.Writer.HasPendingDrafts);
            if (ambient is null) await run.AssertDurable(prepared, true, scope.OriginalCorrelation, false);
            scope.Writer.DiscardPending();
            // This fixture's pending request belongs to setup; reject it in another clean request before the next case.
            if (ambient is null && borrowed is null) { await using var close = new Scope(run); Assert.Equal(CommandResult.Succeeded, (await close.Decide(prepared, false)).Result); }
        }
    }

    [SqlServerFact]
    public async Task Tax_failed_final_save_cleans_before_one_original_key_pass_and_never_retries_a_mutation()
    {
        await using var run = new Run();
        foreach (var error in new[] { "concurrency", "2601", "2627", "1205", "wrapped1205", "completed1205" })
        foreach (var publishWinner in new[] { false, true })
        {
            var prepared = await run.Prepare(); var save = new SaveProbe { Failure = error }; await using var scope = new Scope(run, save);
            var transactions = new HashSet<Guid>(); CommandExecutionResult? winner = null;
            save.BeforeFailure = () => scope.Collector.Enqueue(Draft());
            scope.Authority.AfterRead = async () =>
            {
                if (scope.Db.Database.CurrentTransaction is not { } tx) return;
                transactions.Add(tx.TransactionId);
                if (transactions.Count != 2 || !publishWinner || winner is not null) return;
                Assert.Empty(scope.Db.ChangeTracker.Entries()); Assert.False(scope.Writer.HasPendingDrafts); Assert.Equal(1, save.Attempts);
                await using var other = new Scope(run); winner = await other.Decide(prepared, true, scope.Key);
                Assert.Equal(CommandResult.Succeeded, winner.Result);
            };
            var result = await scope.Decide(prepared, true);
            Assert.Equal(2, transactions.Count); Assert.Equal(1, save.Attempts); scope.AssertClean();
            if (error == "completed1205") Assert.True(save.CompletedConnectionWasNull);
            if (publishWinner)
            { Assert.NotNull(winner); Assert.True(result.IsReplay); Assert.Equal(winner.Response, result.Response); Assert.Equal(winner.OriginalCorrelationId, result.OriginalCorrelationId); await run.AssertDurable(prepared, true, winner.OriginalCorrelationId, true); }
            else
            {
                Assert.Equal(error == "concurrency" ? CommandResult.VersionConflict : CommandResult.DependencyUnavailable, result.Result);
                await run.AssertDurable(prepared, true, scope.OriginalCorrelation, false);
                save.Failure = null; Assert.Equal(CommandResult.Succeeded, (await scope.Decide(prepared, true)).Result);
                Assert.Equal(2, save.Attempts); await run.AssertDurable(prepared, true, scope.OriginalCorrelation, true);
            }
        }
    }

    [SqlServerFact]
    public async Task Tax_cleanup_fault_is_visible_disposes_and_discards_without_readonly_recovery()
    {
        await using var run = new Run(); var prepared = await run.Prepare(); var save = new SaveProbe { Failure = "concurrency" }; var rollback = new RollbackFault();
        await using var scope = new Scope(run, save, rollback); save.BeforeFailure = () => scope.Collector.Enqueue(Draft());
        var transactions = new HashSet<Guid>(); scope.Authority.AfterRead = () => { if (scope.Db.Database.CurrentTransaction is { } tx) transactions.Add(tx.TransactionId); return Task.CompletedTask; };
        var failure = await Assert.ThrowsAsync<AggregateException>(() => scope.Decide(prepared, true));
        Assert.Contains(failure.InnerExceptions, e => e is DbUpdateConcurrencyException);
        Assert.Contains(failure.InnerExceptions, e => e is InvalidOperationException && e.Message == "SENTETIK tax rollback fault");
        Assert.Single(transactions); Assert.Equal(1, rollback.Attempts); Assert.Equal(1, save.Attempts); scope.AssertClean();
        await run.AssertDurable(prepared, true, scope.OriginalCorrelation, false);
    }

    [SqlServerFact]
    public async Task Tax_save_created_audit_or_domain_failure_rolls_back_and_does_not_poison_the_original_key()
    {
        await using var run = new Run();
        foreach (var domainFailure in new[] { false, true })
        {
            var prepared = await run.Prepare(); var save = new SaveProbe(); await using var scope = new Scope(run, save);
            save.AfterSave = () => { scope.Collector.Enqueue(Draft()); if (domainFailure) throw new InvalidOperationException("SENTETIK after accepted save"); };
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Decide(prepared, true)); scope.AssertClean();
            await run.AssertDurable(prepared, true, scope.OriginalCorrelation, false);
            save.AfterSave = null; Assert.Equal(CommandResult.Succeeded, (await scope.Decide(prepared, true)).Result);
            await run.AssertDurable(prepared, true, scope.OriginalCorrelation, true);
        }
    }

    [SqlServerFact]
    public async Task Tax_postcommit_permission_loss_suppresses_response_but_keeps_single_native204_receipt_and_audit()
    {
        await using var run = new Run();
        foreach (var approve in new[] { true, false })
        {
            var prepared = await run.Prepare(); var commit = new Committed(); await using var scope = new Scope(run, extra: commit);
            commit.AfterCommit = () => scope.Authority.Granted = false;
            Assert.Equal(CommandResult.NotFound, (await scope.Decide(prepared, approve)).Result); Assert.Equal(1, commit.Commits); scope.AssertClean();
            await run.AssertDurable(prepared, approve, scope.OriginalCorrelation, true);
            scope.Authority.Granted = true; commit.AfterCommit = null; scope.Correlation = "SENTETIK-reconcile-" + Guid.NewGuid().ToString("N");
            var replay = await scope.Decide(prepared, approve); Assert.True(replay.IsReplay); Assert.Equal(204, replay.Response.StatusCode); Assert.Equal(scope.OriginalCorrelation, replay.OriginalCorrelationId);
            await run.AssertDurable(prepared, approve, scope.OriginalCorrelation, true);
        }
    }

    [SqlServerFact]
    public async Task Tax_cancellation_after_actual_save_rolls_back_even_when_authority_ignores_cancellation()
    {
        await using var run = new Run(); var prepared = await run.Prepare(); var save = new SaveProbe(); await using var scope = new Scope(run, save); using var cancel = new CancellationTokenSource();
        save.AfterSave = cancel.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.Decide(prepared, true, token: cancel.Token));
        scope.AssertClean(); await run.AssertDurable(prepared, true, scope.OriginalCorrelation, false);
        save.AfterSave = null; Assert.Equal(CommandResult.Succeeded, (await scope.Decide(prepared, true)).Result);
    }

    [SqlServerFact]
    public async Task Tax_native_maker_stale_reject_only_versions_and_immutable204_replay_stay_unchanged()
    {
        await using var run = new Run(); var prepared = await run.Prepare();
        await using (var maker = new Scope(run))
        {
            maker.User.Actor = Maker;
            foreach (var approve in new[] { true, false })
            {
                var refused = await maker.Decide(prepared, approve, "SENTETIK-maker-" + approve);
                Assert.Equal(CommandResult.TaxIdSelfDecision, refused.Result); Assert.Equal(409, refused.Response.StatusCode);
            }
            maker.AssertClean();
        }
        await using (var stale = new Scope(run))
        {
            var conflict = await stale.Decide(prepared with { Version = prepared.Version + 1 }, true, "SENTETIK-version");
            Assert.Equal(CommandResult.VersionConflict, conflict.Result); Assert.Equal((prepared.Version + 1, prepared.Version), (conflict.Response.Conflict!.ExpectedVersion, conflict.Response.Conflict.CurrentVersion));
        }
        // Explicit fixture setup through the native domain correction method, not a hidden SQL patch.
        await using (var setup = run.Factory.Services.CreateAsyncScope())
        {
            setup.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyId); var db = setup.ServiceProvider.GetRequiredService<OrganizationDbContext>();
            var company = await db.Companies.SingleAsync(x => x.Id == CompanyId); company.ApplyTaxIdCorrection(company.Vkn!, "9000000041", DateTimeOffset.UtcNow); await db.SaveChangesAsync();
        }
        var current = prepared with { Version = prepared.Version + 1, Vkn = "9000000041" };
        await using var checker = new Scope(run);
        Assert.Equal(CommandResult.TaxIdChangeStale, (await checker.Decide(current, true, "SENTETIK-stale-approve")).Result);
        var first = await checker.Decide(current, false); Assert.Equal(CommandResult.Succeeded, first.Result); Assert.Equal(204, first.Response.StatusCode); Assert.Null(first.Response.Created);
        var raw = await checker.Db.CommandExecutions.AsNoTracking().SingleAsync(x => x.OriginalCorrelationId == checker.OriginalCorrelation && x.Outcome == CommandResult.Succeeded.ToString());
        Assert.Equal(first.Response, JsonSerializer.Deserialize<CommandResponseSnapshot>(raw.ResponseJson, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        checker.Correlation = "SENTETIK-later-correlation";
        var replay = await checker.Decide(current, false); Assert.True(replay.IsReplay); Assert.Equal(first.OriginalCorrelationId, replay.OriginalCorrelationId); Assert.Equal(first.Response, replay.Response);
        Assert.Equal(CommandResult.IdempotencyConflict, (await checker.Decide(current, true)).Result);
        checker.User.Actor = "SENTETIK-different-checker";
        Assert.Equal(CommandResult.IdempotencyConflict, (await checker.Decide(current, false)).Result);
        checker.AssertClean();
    }

    [SqlServerFact]
    public async Task Tax_actual_runtime_interface_uses_the_guarded_scoped_master_and_actual_audit_writer()
    {
        await using var run = new Run(); var prepared = await run.Prepare();
        await using var scope = run.Factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyId);
        var core = Assert.IsType<OrganizationMasterDataService>(scope.ServiceProvider.GetRequiredService<IOrganizationMasterDataService>());
        Assert.Same(core, scope.ServiceProvider.GetRequiredService<IOrganizationMasterDataService>());
        var service = Assert.IsType<GuardedCompanyTaxIdService>(scope.ServiceProvider.GetRequiredService<IOrganizationCompanyTaxIdService>());
        Assert.Same(scope.ServiceProvider.GetRequiredService<AuditEventCollector>(), scope.ServiceProvider.GetRequiredService<IAuditEventCollector>());
        var db = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>(); var writer = scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
        Assert.False(writer.HasPendingDrafts); Assert.Empty(db.ChangeTracker.Entries());
        // Actual authority is intentionally not replaced here: the ungranted host actor must not enter native mutation.
        var user = scope.ServiceProvider.GetRequiredService<ICurrentUser>();
        var metadata = new CommandMetadata(CompanyId, user.UserId!, "SENTETIK-actual-composition", null, SourceChannels.Web, null, null, "SENTETIK-actual-composition");
        Assert.Equal(CommandResult.NotFound, (await service.DecideCompanyTaxIdChangeAsync(prepared.Id, new(prepared.Version, "approve", null), metadata, default)).Result);
        Assert.False(writer.HasPendingDrafts); Assert.Empty(db.ChangeTracker.Entries());
    }

    private sealed record Prepared(Guid Id, long Version, string? Vkn, string Proposed);
    private sealed class Run : IAsyncDisposable
    {
        internal SqlServerRelationalApiFactory Factory { get; } = new(Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!, services =>
        {
            services.RemoveAll<ICurrentUser>(); services.AddSingleton<ICurrentUser>(new User());
        });
        internal async Task<Prepared> Prepare()
        {
            await using var scope = Factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyId); var db = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
            // Previous setup-only pending requests are closed through the native guarded command, never deleted.
            var pending = await db.CompanyTaxIdChangeRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Status == CompanyTaxIdChangeStatus.Pending);
            if (pending is not null)
            {
                await using var close = new Scope(this); var companyBefore = await db.Companies.AsNoTracking().SingleAsync(x => x.Id == CompanyId);
                Assert.Equal(CommandResult.Succeeded, (await close.Decide(new(pending.Id, companyBefore.Version, companyBefore.Vkn, pending.ProposedVkn), false)).Result);
            }
            var company = await db.Companies.SingleAsync(x => x.Id == CompanyId);
            var proposed = company.Vkn == "9000000033" ? "9000000017" : "9000000033";
            Assert.False(await db.Companies.AnyAsync(x => x.Id != CompanyId && x.Vkn == proposed));
            var id = Guid.NewGuid(); db.CompanyTaxIdChangeRequests.Add(new(id, CompanyId, company.Vkn, proposed, company.Version, "SENTETIK tax safety", Maker, DateTimeOffset.UtcNow)); await db.SaveChangesAsync();
            return new(id, company.Version, company.Vkn, proposed);
        }
        internal async Task AssertDurable(Prepared prepared, bool approve, string correlation, bool succeeded)
        {
            await using var scope = Factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyId); var db = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
            var company = await db.Companies.AsNoTracking().SingleAsync(x => x.Id == CompanyId); var request = await db.CompanyTaxIdChangeRequests.AsNoTracking().SingleAsync(x => x.Id == prepared.Id);
            Assert.Equal(prepared.Version + (succeeded && approve ? 1 : 0), company.Version); Assert.Equal(succeeded && approve ? prepared.Proposed : prepared.Vkn, company.Vkn);
            Assert.Equal(succeeded ? approve ? CompanyTaxIdChangeStatus.Approved : CompanyTaxIdChangeStatus.Rejected : CompanyTaxIdChangeStatus.Pending, request.Status);
            Assert.Equal(succeeded ? 2 : 1, request.Version);
            Assert.Equal(succeeded ? 1 : 0, await db.CommandExecutions.CountAsync(x => x.OriginalCorrelationId == correlation));
            Assert.Equal(succeeded ? 1 : 0, await db.Set<AuditEvent>().CountAsync(x => x.CorrelationId == correlation));
        }
        public async ValueTask DisposeAsync() { await using var scope = Factory.Services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<OrganizationDbContext>().Database.EnsureDeletedAsync(); await Factory.DisposeAsync(); }
    }
    private sealed class Scope : IAsyncDisposable
    {
        private readonly IServiceScope owner; internal readonly MutableCompany Company = new(); internal readonly User User = new(); internal readonly Authority Authority = new();
        internal OrganizationDbContext Db { get; } internal AuditEventCollector Collector { get; } internal IAuditTransactionWriterV1 Writer { get; } internal IOrganizationCompanyTaxIdService Service { get; }
        internal string OriginalCorrelation { get; } = "SENTETIK-tax-safety-" + Guid.NewGuid().ToString("N"); internal string Correlation; internal string Key { get; } = "SENTETIK-tax-key-" + Guid.NewGuid().ToString("N");
        internal Scope(Run run, SaveProbe? save = null, IInterceptor? extra = null, bool missingWriter = false)
        {
            Correlation = OriginalCorrelation; Company.Set(CompanyId); owner = run.Factory.Services.CreateScope(); owner.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyId);
            Collector = owner.ServiceProvider.GetRequiredService<AuditEventCollector>(); Writer = owner.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
            var options = new DbContextOptionsBuilder<OrganizationDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "organization"));
            options.AddInterceptors(new AuditSaveChangesInterceptor(Collector, Company)); if (save is not null) options.AddInterceptors(save); if (extra is not null) options.AddInterceptors(extra);
            Db = new(options.Options, Company, new());
            Service = new OrganizationMasterDataService(Db, User, Company, Authority, Authority, new Eligibility(), Collector, NullLogger<OrganizationMasterDataService>.Instance).WithCompanyTaxIdApprovalSafety(missingWriter ? null : Writer);
        }
        internal Task<CommandExecutionResult> Decide(Prepared prepared, bool approve, string? key = null, CancellationToken token = default) => Service.DecideCompanyTaxIdChangeAsync(prepared.Id, new(prepared.Version, approve ? "approve" : "reject", null), new(CompanyId, User.Actor, Correlation, null, SourceChannels.Web, null, null, key ?? Key), token);
        internal void AssertClean() { Assert.DoesNotContain(Db.ChangeTracker.Entries(), e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted); Assert.Null(Db.Database.CurrentTransaction); Assert.False(Writer.HasPendingDrafts); }
        public async ValueTask DisposeAsync() { try { await Db.DisposeAsync(); } finally { owner.Dispose(); } }
    }
    private sealed class MutableCompany : ICompanyContext { public bool IsResolved => CompanyId != Guid.Empty; public Guid CompanyId { get; private set; } public void Set(Guid id) => CompanyId = id; }
    private sealed class User : ICurrentUser { internal string Actor = Checker; internal bool Member = true; public bool IsAuthenticated => true; public string UserId => Actor; public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([CompanyId, TestCompanyIds.CompanyB]); public bool CanAccessCompany(Guid id) => Member && AllowedCompanyIds.Contains(id); }
    private sealed class Authority : IErpAuthorizationService, IErpAuthorityRevalidationService
    {
        internal bool Granted = true; internal Func<Task>? AfterRead;
        public async Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) { if (AfterRead is { } after) await after(); return new(Granted, Granted, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()); }
        public Task<PermissionScope> GetPermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) => RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
        public Task<bool> HasPermissionAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) => Task.FromResult(Granted);
        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(Guid companyId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class Eligibility : IOrganizationLocationOperatorEligibilityPortV1 { public Task<OrganizationLocationOperatorEligibilityV1> CheckAsync(Guid companyId, string actorId, Guid partyId, CancellationToken cancellationToken = default) => throw new NotSupportedException(); }
    private sealed class SaveProbe : SaveChangesInterceptor
    {
        internal int Attempts; internal bool Saved; internal string? Failure; internal Action? BeforeFailure; internal Action? AfterSave; internal bool CompletedConnectionWasNull;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default)
        {
            if (data.Context is not OrganizationDbContext db || !db.ChangeTracker.Entries<CommandExecution>().Any(x => x.State == EntityState.Added)) return result;
            Attempts++; if (Failure is null) return result; BeforeFailure?.Invoke();
            if (Failure == "concurrency") throw new DbUpdateConcurrencyException("SENTETIK tax final save");
            if (Failure == "completed1205") { var actual = (SqlTransaction)db.Database.CurrentTransaction!.GetDbTransaction(); await actual.RollbackAsync(token); CompletedConnectionWasNull = actual.Connection is null; }
            var sql = SqlConcurrencyWrapperPolicyTests.SqlFailure(Failure is "1205" or "wrapped1205" or "completed1205" ? 1205 : int.Parse(Failure, System.Globalization.CultureInfo.InvariantCulture));
            if (Failure == "1205" || Failure == "completed1205") throw sql;
            throw new DbUpdateException("SENTETIK injected provider failure", sql);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default) { if (Attempts > 0) { Saved = true; AfterSave?.Invoke(); } return ValueTask.FromResult(result); }
    }
    private sealed class RollbackFault : DbTransactionInterceptor { internal int Attempts; public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction transaction, TransactionEventData data, InterceptionResult result, CancellationToken token = default) { Attempts++; throw new InvalidOperationException("SENTETIK tax rollback fault"); } }
    private sealed class Committed : DbTransactionInterceptor { internal int Commits; internal Action? AfterCommit; public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData data, CancellationToken token = default) { Commits++; AfterCommit?.Invoke(); return Task.CompletedTask; } }
    private static AuditEventDraft Draft() => new(CompanyId, "organization.company.tax-id.change-approved.v1", "Organization.Company", CompanyId.ToString("D"), "Update", 1, 2, "User", Checker,
        OrganizationCompanyTaxIdPermissionsV1.Approve, [], "{}", "{}", "SENTETIK tax own fault", null, AuditAiInvolvement.None, null, "Web", null, null, "SENTETIK", null, "SENTETIK-key");
}
