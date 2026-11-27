using System.Data.Common;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingApprovedSourceCatalogSqlServerRelationalAcceptanceTests
{
    // Actual migrated SQL and same scoped Audit writer/collector. Failures/authority are controlled;
    // injected 1205 is not evidence of a naturally occurring two-session deadlock.
    [SqlServerFact]
    public async Task Catalog_activation_owned_audit_save_precommit_and_release_fences_preserve_one_native_effect()
    {
        foreach (var fault in new[] { "after-audit", "after-save", "precommit", "company", "actor", "member", "delegation", "cancel", "postcommit" })
        {
            await using var run = await Run.StartAsync(); var package = Package(Guid.NewGuid(), "SENTETIK-owned-catalog");
            await CatalogSafetyPrepare(run, package); var before = await run.CountsAsync(package.Id);
            var save = new CatalogActivationSave(package.Id); var commits = new CatalogActivationCommitted();
            await using var scope = new CatalogActivationScope(run, save, commits);
            var deny = true; var observed = false; var pendingSeen = false; var savedFences = 0;
            using var cancellation = new CancellationTokenSource();
            scope.Authority.Read = () =>
            {
                if (scope.Writer.HasPendingDrafts) pendingSeen = true;
                if (save.Saved) savedFences++;
                var reached = fault == "after-audit" ? pendingSeen && !scope.Writer.HasPendingDrafts && !save.Saved
                    : fault == "precommit" ? savedFences == 5 : fault == "postcommit" ? commits.Commits > 0 : save.Saved;
                if (!deny || !reached) return true;
                observed = true;
                if (fault == "company") { scope.Company.Set(CompanyB); return true; }
                if (fault == "actor") { scope.User.Actor = Guid.NewGuid(); return true; }
                if (fault == "member") { scope.User.Member = false; return true; }
                if (fault == "cancel") { cancellation.Cancel(); return true; }
                return false;
            };
            if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.Activate(package.Id, "owned-catalog", cancellation.Token));
            else { var refused = await scope.Activate(package.Id, "owned-catalog"); Assert.Equal(AccountingApiStatus.Denied, refused.Status); Assert.Null(refused.Resource); }
            Assert.True(observed);
            if (fault == "precommit") Assert.Equal(5, savedFences); // Save1; catalog postSave2; lifecycle release3; owner4; precommit5.
            scope.AssertClean();
            Assert.Equal(fault == "postcommit" ? new Counts(1, before.Commands + 1, before.Audits + 1) : before, await run.CountsAsync(package.Id));
            Assert.Equal(fault == "postcommit" ? 2L : 1L, (await run.CatalogAsync(package.Id)).Version);
            deny = false; scope.Company.Set(CompanyA); scope.User.Actor = CatalogSafetyActor; scope.User.Member = true;
            var success = await scope.Activate(package.Id, "owned-catalog"); var replay = await scope.Activate(package.Id, "owned-catalog");
            Assert.Equal(AccountingApiStatus.Ok, success.Status); Assert.True(replay.IsReplay);
            Assert.Equal(JsonSerializer.Serialize(success.Resource), JsonSerializer.Serialize(replay.Resource));
            Assert.Equal(scope.CorrelationId, replay.OriginalCorrelationId); scope.AssertClean();
            Assert.Equal(new Counts(1, before.Commands + 1, before.Audits + 1), await run.CountsAsync(package.Id));
        }
    }

    [SqlServerFact]
    public async Task Catalog_activation_entry_and_cleanup_faults_preserve_caller_work_and_block_unsafe_winner_reads()
    {
        foreach (var fault in new[] { "borrowed", "ambient", "dirty", "pending", "await-dirty", "await-pending", "missing-owner", "missing-writer", "other-context", "other-collector", "rollback", "dispose" })
        {
            await using var run = await Run.StartAsync(); var package = Package(Guid.NewGuid(), "SENTETIK-entry-catalog");
            await CatalogSafetyPrepare(run, package); var before = await run.CountsAsync(package.Id);
            var save = new CatalogActivationSave(package.Id); var cleanup = fault is "rollback" or "dispose" ? new CatalogActivationCleanup(fault == "dispose") : null;
            await using var scope = new CatalogActivationScope(run, save, cleanup, fault);
            await using var borrowed = fault == "borrowed" ? await scope.Db.Database.BeginTransactionAsync() : null;
            using var ambient = fault == "ambient" ? new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled) : null;
            var caller = CatalogSafetyCaller(); if (fault == "dirty") scope.Db.Commands.Add(caller);
            if (fault == "pending") scope.Collector.Enqueue(CatalogSafetyAudit());
            var transactions = new HashSet<Guid>();
            scope.Authority.Read = () =>
            {
                if (scope.Db.Database.CurrentTransaction is { } tx) transactions.Add(tx.TransactionId);
                if (fault == "await-dirty" && !scope.Db.ChangeTracker.HasChanges()) scope.Db.Commands.Add(caller);
                if (fault == "await-pending" && !scope.Writer.HasPendingDrafts) scope.Collector.Enqueue(CatalogSafetyAudit());
                if (cleanup is not null && save.Saved) { cleanup.Armed = true; return false; }
                return true;
            };
            if (cleanup is not null)
            {
                await Assert.ThrowsAsync<AggregateException>(() => scope.Activate(package.Id, "entry-catalog"));
                Assert.True(cleanup.Failed); Assert.Single(transactions);
                Assert.False(scope.Db.ChangeTracker.HasChanges()); Assert.False(scope.Writer.HasPendingDrafts);
            }
            else
            {
                Assert.Equal(AccountingApiStatus.Unavailable, (await scope.Activate(package.Id, "entry-catalog")).Status);
                Assert.Equal(0, save.Attempts);
                Assert.Equal(fault is "dirty" or "await-dirty", scope.Db.ChangeTracker.HasChanges());
                Assert.Equal(fault is "pending" or "await-pending", scope.Writer.HasPendingDrafts);
                if (scope.Db.ChangeTracker.HasChanges()) Assert.Same(caller, Assert.Single(scope.Db.ChangeTracker.Entries()).Entity);
                if (borrowed is not null) { Assert.Same(borrowed, scope.Db.Database.CurrentTransaction); await borrowed.RollbackAsync(); }
                else Assert.Null(scope.Db.Database.CurrentTransaction);
            }
            // Fixture teardown, never approval entry cleanup.
            ambient?.Dispose(); scope.Db.ChangeTracker.Clear(); scope.Writer.DiscardPending();
            Assert.Equal(before, await run.CountsAsync(package.Id)); Assert.Equal(1L, (await run.CatalogAsync(package.Id)).Version);
        }
    }

    [SqlServerFact]
    public async Task Catalog_activation_failed_save_and_controlled_1205_only_read_original_key_after_clean_owned_abort()
    {
        foreach (var fault in new[] { "concurrency", "direct", "wrapped", "completed-provider", "audit" })
        foreach (var winner in new[] { false, true })
        {
            await using var run = await Run.StartAsync(); var package = Package(Guid.NewGuid(), "SENTETIK-race-catalog");
            await CatalogSafetyPrepare(run, package); var before = await run.CountsAsync(package.Id);
            var save = new CatalogActivationSave(package.Id) { Fault = fault == "audit" ? null : fault };
            await using var scope = new CatalogActivationScope(run, save, auditDeadlock: fault == "audit");
            var winnerSaved = false; var transactions = new HashSet<Guid>();
            scope.Authority.Read = () => { if (scope.Db.Database.CurrentTransaction is { } tx) transactions.Add(tx.TransactionId); return true; };
            scope.Authority.BeforeRead = async () =>
            {
                var injected = save.Injected || scope.Writer is CatalogActivationAuditDeadlock { Injected: true };
                if (!injected || scope.Db.Database.CurrentTransaction is not null || winnerSaved || !winner) return;
                scope.AssertClean(); winnerSaved = true;
                await using var other = new CatalogActivationScope(run);
                Assert.Equal(AccountingApiStatus.Ok, (await other.Activate(package.Id, "race-catalog")).Status);
                other.AssertClean();
            };
            var result = await scope.Activate(package.Id, "race-catalog");
            Assert.Equal(winner ? AccountingApiStatus.Ok : AccountingApiStatus.Conflict, result.Status); Assert.Equal(winner, result.IsReplay);
            if (winner) Assert.Equal(2L, result.Resource?.Version);
            else { Assert.Equal(AccountingStoreCodes.ConcurrentChange, result.Code); Assert.Null(result.Resource); }
            Assert.Equal(fault == "audit" ? 0 : 1, save.Attempts); Assert.Equal(2, transactions.Count); scope.AssertClean();
            Assert.Equal(winner ? new Counts(1, before.Commands + 1, before.Audits + 1) : before, await run.CountsAsync(package.Id));
        }
    }

    [SqlServerFact]
    public async Task Catalog_activation_terminal_version_receipt_and_original_replay_survive_later_supersession()
    {
        await using var run = await Run.StartAsync(); var first = Package(Guid.NewGuid(), "SENTETIK-catalog-first");
        await CatalogSafetyPrepare(run, first);
        await using var scope = new CatalogActivationScope(run);
        var stale = await scope.Service.ActivateAsync(first.Id, new(9, 0), "stale-catalog");
        Assert.Equal(AccountingApiStatus.Conflict, stale.Status); Assert.Equal(AccountingApiCodes.VersionConflict, stale.Code);
        var staleReplay = await scope.Service.ActivateAsync(first.Id, new(9, 0), "stale-catalog");
        Assert.True(staleReplay.IsReplay); Assert.Equal(stale.Code, staleReplay.Code);
        var original = await scope.Activate(first.Id, "original-catalog"); Assert.Equal(AccountingApiStatus.Ok, original.Status);
        Assert.Equal(CatalogSafetyActor, original.Resource?.PreparedByUserId); Assert.Equal(CatalogSafetyActor, original.Resource?.ActivatedByUserId); // Native self activation stays allowed.
        var second = Package(Guid.NewGuid(), "SENTETIK-catalog-second"); await CatalogSafetyPrepare(run, second);
        var next = await scope.Service.ActivateAsync(second.Id, new(1, 1), "next-catalog"); Assert.Equal(AccountingApiStatus.Ok, next.Status);
        Assert.Equal(3L, (await run.CatalogAsync(first.Id)).Version);
        var replay = await scope.Activate(first.Id, "original-catalog"); Assert.True(replay.IsReplay);
        Assert.Equal("Active", replay.Resource?.Status); Assert.Equal(2L, replay.Resource?.Version);
        Assert.Equal(JsonSerializer.Serialize(original.Resource), JsonSerializer.Serialize(replay.Resource));
        scope.AssertClean();
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Commands WHERE CompanyId=@c AND CommandName=@n", ("@c", CompanyA), ("@n", ApprovedAccountingSourceCatalogCommandNamesV1.Activate)));
    }

    [SqlServerFact]
    public async Task Catalog_activation_second_readonly_or_postcommit_1205_never_retries_native_mutation()
    {
        foreach (var postcommit in new[] { false, true })
        {
            await using var run = await Run.StartAsync(); var package = Package(Guid.NewGuid(), "SENTETIK-readonly-catalog");
            await CatalogSafetyPrepare(run, package); var before = await run.CountsAsync(package.Id);
            var save = new CatalogActivationSave(package.Id) { Fault = postcommit ? null : "concurrency" };
            var committed = new CatalogActivationCommitted(); await using var scope = new CatalogActivationScope(run, save, committed);
            var transactions = new HashSet<Guid>(); var injected = false;
            scope.Authority.BeforeRead = () =>
            {
                if (scope.Db.Database.CurrentTransaction is { } tx) transactions.Add(tx.TransactionId);
                if (!injected && (postcommit ? committed.Commits > 0 : transactions.Count == 2))
                { injected = true; throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205); }
                return Task.CompletedTask;
            };
            Assert.Equal(AccountingApiStatus.Unavailable, (await scope.Activate(package.Id, "readonly-catalog")).Status);
            Assert.True(injected); Assert.Equal(postcommit ? 1 : 2, transactions.Count); Assert.Equal(1, save.Attempts); scope.AssertClean();
            Assert.Equal(postcommit ? new Counts(1, before.Commands + 1, before.Audits + 1) : before, await run.CountsAsync(package.Id));
        }
    }

    [SqlServerFact]
    public async Task Catalog_activation_terminal_refusal_receipt_is_rolled_back_when_saved_authority_is_lost()
    {
        await using var run = await Run.StartAsync(); var package = Package(Guid.NewGuid(), "SENTETIK-terminal-catalog");
        await CatalogSafetyPrepare(run, package); var before = await run.CountsAsync(package.Id);
        var save = new CatalogActivationSave(package.Id) { ObserveTerminalCommand = true };
        await using var scope = new CatalogActivationScope(run, save);
        scope.Authority.Read = () => !save.Saved;
        var denied = await scope.Service.ActivateAsync(package.Id, new(9, 0), "terminal-catalog");
        Assert.Equal(AccountingApiStatus.Denied, denied.Status); Assert.True(save.Saved); scope.AssertClean();
        Assert.Equal(before, await run.CountsAsync(package.Id)); Assert.Equal(1L, (await run.CatalogAsync(package.Id)).Version);
        scope.Authority.Read = null;
        var conflict = await scope.Service.ActivateAsync(package.Id, new(9, 0), "terminal-catalog");
        var replay = await scope.Service.ActivateAsync(package.Id, new(9, 0), "terminal-catalog");
        Assert.Equal(AccountingApiCodes.VersionConflict, conflict.Code); Assert.True(replay.IsReplay); Assert.Equal(conflict.Code, replay.Code);
        Assert.Equal(new Counts(1, before.Commands + 1, before.Audits), await run.CountsAsync(package.Id)); scope.AssertClean();
    }

    private static readonly Guid CatalogSafetyActor = Guid.Parse("61e738a6-730b-49bf-ae9c-d294da999340");
    private static async Task CatalogSafetyPrepare(Run run, PackageData package)
    {
        await using var scope = new CatalogActivationScope(run);
        var result = await scope.Service.PrepareAsync(new(package.Id, package.AccountPlanSourceVersion, package.Accounts, package.Mappings, package.AccountsSha256, package.MappingsSha256), Key("safety-prepare"));
        Assert.Equal(AccountingApiStatus.Created, result.Status); scope.AssertClean();
    }
    private sealed class CatalogActivationScope : IAsyncDisposable
    {
        private readonly IServiceScope auditScope; private readonly AccountingDbContext otherDb;
        public CatalogActivationCompany Company { get; } = new(); public CatalogActivationUser User { get; } = new();
        public CatalogActivationAuthority Authority { get; } = new(); public string CorrelationId { get; } = Guid.NewGuid().ToString("N");
        public AccountingDbContext Db { get; } public AuditEventCollector Collector { get; } public IAuditTransactionWriterV1 Writer { get; }
        public ApprovedAccountingSourceCatalogServiceV1 Service { get; }
        public CatalogActivationScope(Run run, CatalogActivationSave? save = null, IInterceptor? extra = null, string? entryFault = null, bool auditDeadlock = false)
        {
            Company.Set(CompanyA); auditScope = run.Factory.Services.CreateScope(); auditScope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            Collector = auditScope.ServiceProvider.GetRequiredService<AuditEventCollector>();
            var actualWriter = auditScope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>(); Writer = auditDeadlock ? new CatalogActivationAuditDeadlock(actualWriter) : actualWriter;
            var options = new DbContextOptionsBuilder<AccountingDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", AccountingDbContext.Schema)).AddInterceptors(new AuditSaveChangesInterceptor(Collector, Company));
            if (save is not null) options.AddInterceptors(save); if (extra is not null) options.AddInterceptors(extra); if (extra is CatalogActivationCleanup cleanup) cleanup.Configure(options);
            Db = new(options.Options, Company); otherDb = new(options.Options, Company);
            var core = new AccountingStore(entryFault == "other-context" ? otherDb : Db, Company, entryFault == "other-collector" ? new AuditEventCollector() : Collector,
                TimeProvider.System, auditScope.ServiceProvider.GetRequiredService<AccountingAmountScaleVerifier>(), normalApprovalAuditWriter: entryFault == "missing-writer" ? null : Writer);
            var store = new ApprovedAccountingSourceCatalogStoreV1(Db, Company, Collector, TimeProvider.System, entryFault == "missing-owner" ? null : core);
            var correlation = new CorrelationContext(); correlation.Set(CorrelationId, null, "Web", null, null);
            Service = new(new ApprovedAccountingSourcePackageParserV1(), store, core, Authority, Company, User, correlation, TimeProvider.System);
        }
        public Task<AccountingApiResult<ApprovedAccountingSourceCatalogViewV1>> Activate(Guid id, string key, CancellationToken token = default) => Service.ActivateAsync(id, new(1, 0), key, token);
        public void AssertClean() { Assert.False(Db.ChangeTracker.HasChanges()); Assert.False(Writer.HasPendingDrafts); Assert.Null(Db.Database.CurrentTransaction); }
        public async ValueTask DisposeAsync() { try { await Db.DisposeAsync(); await otherDb.DisposeAsync(); } finally { auditScope.Dispose(); } }
    }
    private sealed class CatalogActivationCompany : ICompanyContext
    { public bool IsResolved => CompanyId != Guid.Empty; public Guid CompanyId { get; private set; } public void Set(Guid company) => CompanyId = company; }
    private sealed class CatalogActivationUser : ICurrentUser
    {
        public Guid Actor = CatalogSafetyActor; public bool Member = true; public bool IsAuthenticated => true; public string UserId => Actor.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { CompanyA }; public bool CanAccessCompany(Guid company) => Member && company == CompanyA;
    }
    private sealed class CatalogActivationAuthority : IErpAuthorityRevalidationService
    {
        public Func<bool>? Read; public Func<Task>? BeforeRead;
        public async Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken token = default)
        { if (BeforeRead is not null) await BeforeRead(); return new(Read?.Invoke() ?? true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()); }
    }
    private sealed class CatalogActivationAuditDeadlock(IAuditTransactionWriterV1 actual) : IAuditTransactionWriterV1
    {
        public bool Injected; public bool HasPendingDrafts => actual.HasPendingDrafts;
        public Task WritePendingAsync(System.Data.IDbTransaction transaction, CancellationToken token = default)
        { if (!Injected) { Injected = true; throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205); } return actual.WritePendingAsync(transaction, token); }
        public void DiscardPending() => actual.DiscardPending();
    }
    private sealed class CatalogActivationSave(Guid id) : SaveChangesInterceptor
    {
        public string? Fault; public bool Saved, Injected, ObserveTerminalCommand; public int Attempts;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default)
        {
            if (data.Context is AccountingDbContext db && (db.ChangeTracker.Entries<ApprovedAccountingSourceCatalogRow>().Any(e => e.Entity.Id == id && e.State == EntityState.Modified && e.Entity.Version == 2)
                || ObserveTerminalCommand && db.ChangeTracker.Entries<AccountingCommandRow>().Any(e => e.State == EntityState.Added && e.Entity.CommandName == ApprovedAccountingSourceCatalogCommandNamesV1.Activate)))
            {
                Attempts++;
                if (Fault is not null)
                {
                    Injected = true;
                    if (Fault == "completed-provider") db.Database.CurrentTransaction!.GetDbTransaction().Rollback();
                    if (Fault == "concurrency") throw new DbUpdateConcurrencyException("SENTETIK catalog save race");
                    var error = SqlConcurrencyWrapperPolicyTests.SqlFailure(1205);
                    if (Fault == "wrapped") throw new DbUpdateException("SENTETIK catalog 1205", error);
                    throw error;
                }
            }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default)
        { if (Attempts > 0) Saved = true; return ValueTask.FromResult(result); }
    }
    private sealed class CatalogActivationCommitted : DbTransactionInterceptor
    { public int Commits; public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData data, CancellationToken token = default) { Commits++; return Task.CompletedTask; } }
    private sealed class CatalogActivationCleanup(bool dispose) : DbTransactionInterceptor
    {
        public bool Armed, Failed;
        public void Configure(DbContextOptionsBuilder<AccountingDbContext> options) => options.LogTo((id, _) => id == RelationalEventId.TransactionDisposed,
            _ => { if (Armed && dispose && !Failed) { Failed = true; throw new InvalidOperationException("SENTETIK catalog dispose fault"); } });
        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction transaction, TransactionEventData data, InterceptionResult result, CancellationToken token = default)
        { if (Armed && !dispose && !Failed) { Failed = true; throw new InvalidOperationException("SENTETIK catalog rollback fault"); } return ValueTask.FromResult(result); }
    }
    private static AccountingCommandRow CatalogSafetyCaller() => new() { CompanyId = CompanyA, CommandName = "SENTETIK caller", IdempotencyKeyHash = "caller-key", ActorId = "caller", PayloadHash = "hash", ResultJson = "{}", CorrelationId = "caller-corr" };
    private static AuditEventDraft CatalogSafetyAudit() => new(CompanyA, "SENTETIK caller", "Accounting.ApprovedSourceCatalog", "caller", "Activate", 1, 2, "User", CatalogSafetyActor.ToString("D"),
        ActivatePermission, [], "{}", "{}", "SENTETIK", null, AuditAiInvolvement.None, null, "SQLSource", null, null, "SENTETIK-caller", null, "SENTETIK-key");
}
