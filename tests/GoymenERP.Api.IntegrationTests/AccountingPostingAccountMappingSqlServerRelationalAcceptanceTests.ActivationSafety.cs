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

public sealed partial class AccountingPostingAccountMappingSqlServerRelationalAcceptanceTests
{
    // Actual migrated SQL and same scoped Audit writer/collector. Failures/authority are controlled;
    // injected 1205 is not evidence of a naturally occurring two-session deadlock.
    [SqlServerFact]
    public async Task Mapping_activation_owned_audit_save_precommit_and_release_fences_preserve_one_native_effect()
    {
        foreach (var fault in new[] { "after-audit", "after-save", "precommit", "company", "actor", "member", "delegation", "cancel", "postcommit" })
        {
            await using var run = await Run.StartAsync();
            await MappingSafetyPrepare(run); var before = await run.CountsAsync();
            var save = new MappingActivationSave(1); var commits = new MappingActivationCommitted();
            await using var scope = new MappingActivationScope(run, save, commits);
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
            if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.Activate(1, "owned-mapping", cancellation.Token));
            else { var refused = await scope.Activate(1, "owned-mapping"); Assert.Equal(AccountingApiStatus.Denied, refused.Status); Assert.Null(refused.Resource); }
            Assert.True(observed);
            if (fault == "precommit") Assert.Equal(5, savedFences); // Save1; mapping postSave2; lifecycle release3; owner4; precommit5.
            scope.AssertClean();
            Assert.Equal(fault == "postcommit" ? new Counts(1, 3, before.Commands + 1, before.Audits + 1) : before, await run.CountsAsync());
            Assert.Equal(fault == "postcommit" ? 2L : 1L, (await run.VersionAsync(1)).Version);
            deny = false; scope.Company.Set(CompanyA); scope.User.Actor = MappingSafetyActor; scope.User.Member = true;
            var success = await scope.Activate(1, "owned-mapping"); var replay = await scope.Activate(1, "owned-mapping");
            Assert.Equal(AccountingApiStatus.Ok, success.Status); Assert.True(replay.IsReplay);
            Assert.Equal(JsonSerializer.Serialize(success.Resource), JsonSerializer.Serialize(replay.Resource));
            Assert.Equal(scope.CorrelationId, replay.OriginalCorrelationId); scope.AssertClean();
            Assert.Equal(new Counts(1, 3, before.Commands + 1, before.Audits + 1), await run.CountsAsync());
        }
    }

    [SqlServerFact]
    public async Task Mapping_activation_entry_and_cleanup_faults_preserve_caller_work_and_block_unsafe_winner_reads()
    {
        foreach (var fault in new[] { "borrowed", "ambient", "dirty", "pending", "await-dirty", "await-pending", "missing-owner", "missing-writer", "other-context", "other-collector", "rollback", "dispose" })
        {
            await using var run = await Run.StartAsync();
            await MappingSafetyPrepare(run); var before = await run.CountsAsync();
            var save = new MappingActivationSave(1); var cleanup = fault is "rollback" or "dispose" ? new MappingActivationCleanup(fault == "dispose") : null;
            await using var scope = new MappingActivationScope(run, save, cleanup, fault);
            await using var borrowed = fault == "borrowed" ? await scope.Db.Database.BeginTransactionAsync() : null;
            using var ambient = fault == "ambient" ? new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled) : null;
            var caller = MappingSafetyCaller(); if (fault == "dirty") scope.Db.Commands.Add(caller);
            if (fault == "pending") scope.Collector.Enqueue(MappingSafetyAudit());
            var transactions = new HashSet<Guid>();
            scope.Authority.Read = () =>
            {
                if (scope.Db.Database.CurrentTransaction is { } tx) transactions.Add(tx.TransactionId);
                if (fault == "await-dirty" && !scope.Db.ChangeTracker.HasChanges()) scope.Db.Commands.Add(caller);
                if (fault == "await-pending" && !scope.Writer.HasPendingDrafts) scope.Collector.Enqueue(MappingSafetyAudit());
                if (cleanup is not null && save.Saved) { cleanup.Armed = true; return false; }
                return true;
            };
            if (cleanup is not null)
            {
                await Assert.ThrowsAsync<AggregateException>(() => scope.Activate(1, "entry-mapping"));
                Assert.True(cleanup.Failed); Assert.Single(transactions);
                Assert.False(scope.Db.ChangeTracker.HasChanges()); Assert.False(scope.Writer.HasPendingDrafts);
            }
            else
            {
                Assert.Equal(AccountingApiStatus.Unavailable, (await scope.Activate(1, "entry-mapping")).Status);
                Assert.Equal(0, save.Attempts);
                Assert.Equal(fault is "dirty" or "await-dirty", scope.Db.ChangeTracker.HasChanges());
                Assert.Equal(fault is "pending" or "await-pending", scope.Writer.HasPendingDrafts);
                if (scope.Db.ChangeTracker.HasChanges()) Assert.Same(caller, Assert.Single(scope.Db.ChangeTracker.Entries()).Entity);
                if (borrowed is not null) { Assert.Same(borrowed, scope.Db.Database.CurrentTransaction); await borrowed.RollbackAsync(); }
                else Assert.Null(scope.Db.Database.CurrentTransaction);
            }
            // Fixture teardown, never approval entry cleanup.
            ambient?.Dispose(); scope.Db.ChangeTracker.Clear(); scope.Writer.DiscardPending();
            Assert.Equal(before, await run.CountsAsync()); Assert.Equal(1L, (await run.VersionAsync(1)).Version);
        }
    }

    [SqlServerFact]
    public async Task Mapping_activation_failed_save_and_controlled_1205_only_read_original_key_after_clean_owned_abort()
    {
        foreach (var fault in new[] { "concurrency", "direct", "wrapped", "completed-provider", "audit" })
        foreach (var winner in new[] { false, true })
        {
            await using var run = await Run.StartAsync();
            await MappingSafetyPrepare(run); var before = await run.CountsAsync();
            var save = new MappingActivationSave(1) { Fault = fault == "audit" ? null : fault };
            await using var scope = new MappingActivationScope(run, save, auditDeadlock: fault == "audit");
            var winnerSaved = false; var transactions = new HashSet<Guid>();
            scope.Authority.Read = () => { if (scope.Db.Database.CurrentTransaction is { } tx) transactions.Add(tx.TransactionId); return true; };
            scope.Authority.BeforeRead = async () =>
            {
                var injected = save.Injected || scope.Writer is MappingActivationAuditDeadlock { Injected: true };
                if (!injected || scope.Db.Database.CurrentTransaction is not null || winnerSaved || !winner) return;
                scope.AssertClean(); winnerSaved = true;
                await using var other = new MappingActivationScope(run);
                Assert.Equal(AccountingApiStatus.Ok, (await other.Activate(1, "race-mapping")).Status);
                other.AssertClean();
            };
            var result = await scope.Activate(1, "race-mapping");
            Assert.Equal(winner ? AccountingApiStatus.Ok : AccountingApiStatus.Conflict, result.Status); Assert.Equal(winner, result.IsReplay);
            if (winner) Assert.Equal(2L, result.Resource?.Version);
            else { Assert.Equal(AccountingStoreCodes.ConcurrentChange, result.Code); Assert.Null(result.Resource); }
            Assert.Equal(fault == "audit" ? 0 : 1, save.Attempts); Assert.Equal(2, transactions.Count); scope.AssertClean();
            Assert.Equal(winner ? new Counts(1, 3, before.Commands + 1, before.Audits + 1) : before, await run.CountsAsync());
        }
    }

    [SqlServerFact]
    public async Task Mapping_activation_terminal_version_receipt_and_original_replay_survive_later_supersession()
    {
        await using var run = await Run.StartAsync(); await MappingSafetyPrepare(run);
        await using var scope = new MappingActivationScope(run);
        var stale = await scope.Service.ActivateAsync(1, new(9, 0), "stale-mapping");
        Assert.Equal(AccountingApiStatus.Conflict, stale.Status); Assert.Equal(AccountingApiCodes.VersionConflict, stale.Code);
        Assert.True((await scope.Service.ActivateAsync(1, new(9, 0), "stale-mapping")).IsReplay);
        var original = await scope.Activate(1, "original-mapping"); Assert.Equal(AccountingApiStatus.Ok, original.Status);
        await MappingSafetyPrepare(run, 1);
        Assert.Equal(AccountingApiStatus.Ok, (await scope.Service.ActivateAsync(2, new(1, 1), "next-mapping")).Status);
        Assert.Equal(3L, (await run.VersionAsync(1)).Version);
        var replay = await scope.Activate(1, "original-mapping"); Assert.True(replay.IsReplay);
        Assert.Equal("Active", replay.Resource?.Status); Assert.Equal(2L, replay.Resource?.Version);
        Assert.Equal(JsonSerializer.Serialize(original.Resource), JsonSerializer.Serialize(replay.Resource));
        Assert.Equal(scope.CorrelationId, replay.OriginalCorrelationId); scope.AssertClean();
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Commands WHERE CompanyId=@c AND CommandName=@n", ("@c", CompanyA), ("@n", AccountingPostingAccountMappingCommandNamesV1.Activate)));
    }

    [SqlServerFact]
    public async Task Mapping_activation_second_readonly_or_postcommit_1205_never_retries_native_mutation()
    {
        foreach (var postcommit in new[] { false, true })
        {
            await using var run = await Run.StartAsync();
            await MappingSafetyPrepare(run); var before = await run.CountsAsync();
            var save = new MappingActivationSave(1) { Fault = postcommit ? null : "concurrency" };
            var committed = new MappingActivationCommitted(); await using var scope = new MappingActivationScope(run, save, committed);
            var transactions = new HashSet<Guid>(); var injected = false;
            scope.Authority.BeforeRead = () =>
            {
                if (scope.Db.Database.CurrentTransaction is { } tx) transactions.Add(tx.TransactionId);
                if (!injected && (postcommit ? committed.Commits > 0 : transactions.Count == 2))
                { injected = true; throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205); }
                return Task.CompletedTask;
            };
            Assert.Equal(AccountingApiStatus.Unavailable, (await scope.Activate(1, "readonly-mapping")).Status);
            Assert.True(injected); Assert.Equal(postcommit ? 1 : 2, transactions.Count); Assert.Equal(1, save.Attempts); scope.AssertClean();
            Assert.Equal(postcommit ? new Counts(1, 3, before.Commands + 1, before.Audits + 1) : before, await run.CountsAsync());
        }
    }

    [SqlServerFact]
    public async Task Mapping_activation_only_exact_saved_native_maker_denial_commits_and_late_authority_rolls_it_back()
    {
        await using var run = await Run.StartAsync(); var maker = await MappingSafetyPrepare(run);
        var before = await run.CountsAsync();
        var save = new MappingActivationSave(1) { ObserveTerminalCommand = true };
        await using var scope = new MappingActivationScope(run, save); scope.User.Actor = maker;
        scope.Authority.Read = () => !save.Saved;
        var denied = await scope.Activate(1, "maker-mapping");
        Assert.Equal(AccountingApiCodes.AuthorityDenied, denied.Code); Assert.False(denied.IsReplay);
        Assert.True(save.Saved); scope.AssertClean(); Assert.Equal(before, await run.CountsAsync());
        scope.Authority.Read = null;
        var refusal = await scope.Activate(1, "maker-mapping"); var replay = await scope.Activate(1, "maker-mapping");
        Assert.Equal(AccountingApiStatus.Denied, refusal.Status); Assert.Equal(AccountingPostingAccountMappingCodesV1.SelfActivation, refusal.Code);
        Assert.Equal("activation", refusal.Field); Assert.Null(refusal.Resource); Assert.False(refusal.IsReplay);
        Assert.True(replay.IsReplay); Assert.Equal(refusal.Code, replay.Code); Assert.Equal(refusal.Field, replay.Field);
        Assert.Equal(scope.CorrelationId, replay.OriginalCorrelationId); Assert.Equal(1L, (await run.VersionAsync(1)).Version);
        var committed = new Counts(1, 3, before.Commands + 1, before.Audits);
        Assert.Equal(committed, await run.CountsAsync()); scope.AssertClean();

        // Behavioral negatives of the narrow durable-DENIED predicate: none may leave its staged command.
        foreach (var fault in new[] { "unsaved", "code", "field", "resource", "replay", "correlation", "expected", "current", "serialized", "marker", "type" })
        {
            await using var probe = new MappingActivationScope(run);
            var answer = new AccountingApiResult<AccountingPostingAccountMappingViewV1>(AccountingApiStatus.Denied, null, false,
                AccountingPostingAccountMappingCodesV1.SelfActivation, "activation");
            answer = fault switch
            {
                "code" => answer with { Code = AccountingApiCodes.AuthorityDenied },
                "field" => answer with { Field = "other" },
                "resource" => answer with { Resource = AccountingPostingAccountMappingServiceV1.View((await probe.Store.ReadAsync(1))!) },
                "replay" => answer with { IsReplay = true }, "correlation" => answer with { OriginalCorrelationId = "other" },
                "expected" => answer with { ExpectedVersion = 1 }, "current" => answer with { CurrentVersion = 1 }, _ => answer
            };
            var command = new AccountingCommandRecord(fault == "marker" ? AccountingPostingAccountMappingCommandNamesV1.Prepare : AccountingPostingAccountMappingCommandNamesV1.Activate,
                AccountingApiService.Sha256("predicate-" + fault), MappingSafetyActor.ToString("D"), "SENTETIK-payload",
                fault == "serialized" ? "{}" : JsonSerializer.Serialize(answer with { IsReplay = false, OriginalCorrelationId = null }, new JsonSerializerOptions(JsonSerializerDefaults.Web)), "SENTETIK-corr");
            if (fault == "marker")
                await Assert.ThrowsAsync<InvalidOperationException>(() => probe.Store.RunActivationAsync(async _ => { await probe.Core.RecordCommandAsync(command); return answer; }, _ => Task.CompletedTask));
            else if (fault == "type")
            {
                var normal = probe.Core.WithNormalDraftApprovalSafety();
                var wrongType = await normal.RunNormalApprovalAsync(_ => Task.FromResult(new AccountingApiResult<NormalJournalDraftViewV1>(AccountingApiStatus.Denied, null, false,
                    AccountingPostingAccountMappingCodesV1.SelfActivation, "activation")), _ => Task.CompletedTask);
                Assert.Equal(AccountingApiCodes.AuthorityDenied, wrongType.Code);
            }
            else
            {
                var result = await probe.Store.RunActivationAsync(async _ =>
                { if (fault != "unsaved") await probe.Core.RecordCommandAsync(command); return answer; }, _ => Task.CompletedTask);
                Assert.Equal(AccountingApiStatus.Denied, result.Status);
            }
            probe.AssertClean(); Assert.Equal(committed, await run.CountsAsync());
        }
    }

    private static readonly Guid MappingSafetyActor = Guid.Parse("61e738a6-730b-49bf-ae9c-d294da999340");
    private static async Task<Guid> MappingSafetyPrepare(Run run, long expectedLatest = 0)
    {
        var actor = await run.GrantAsync(Preparer, AccountingPostingAccountMappingPermissionNamesV1.Prepare);
        using var client = run.Client(Preparer);
        using var prepared = await PrepareAsync(client, expectedLatest, Key("safety-prepare"));
        Assert.Equal(System.Net.HttpStatusCode.Created, prepared.StatusCode);
        return actor;
    }
    private sealed class MappingActivationScope : IAsyncDisposable
    {
        private readonly IServiceScope auditScope; private readonly AccountingDbContext otherDb;
        public MappingActivationCompany Company { get; } = new(); public MappingActivationUser User { get; } = new();
        public MappingActivationAuthority Authority { get; } = new(); public string CorrelationId { get; } = Guid.NewGuid().ToString("N");
        public AccountingDbContext Db { get; } public AuditEventCollector Collector { get; } public IAuditTransactionWriterV1 Writer { get; }
        public AccountingPostingAccountMappingServiceV1 Service { get; }
        public AccountingPostingAccountMappingStoreV1 Store { get; } public AccountingStore Core { get; }
        public MappingActivationScope(Run run, MappingActivationSave? save = null, IInterceptor? extra = null, string? entryFault = null, bool auditDeadlock = false)
        {
            Company.Set(CompanyA); auditScope = run.Factory.Services.CreateScope(); auditScope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            Collector = auditScope.ServiceProvider.GetRequiredService<AuditEventCollector>();
            var actualWriter = auditScope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>(); Writer = auditDeadlock ? new MappingActivationAuditDeadlock(actualWriter) : actualWriter;
            var options = new DbContextOptionsBuilder<AccountingDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", AccountingDbContext.Schema)).AddInterceptors(new AuditSaveChangesInterceptor(Collector, Company));
            if (save is not null) options.AddInterceptors(save); if (extra is not null) options.AddInterceptors(extra); if (extra is MappingActivationCleanup cleanup) cleanup.Configure(options);
            Db = new(options.Options, Company); otherDb = new(options.Options, Company);
            var core = new AccountingStore(entryFault == "other-context" ? otherDb : Db, Company, entryFault == "other-collector" ? new AuditEventCollector() : Collector,
                TimeProvider.System, auditScope.ServiceProvider.GetRequiredService<AccountingAmountScaleVerifier>(), normalApprovalAuditWriter: entryFault == "missing-writer" ? null : Writer);
            Core = core;
            var catalog = new ApprovedAccountingSourceCatalogStoreV1(Db, Company, Collector, TimeProvider.System);
            Store = new AccountingPostingAccountMappingStoreV1(Db, catalog, Company, Collector, TimeProvider.System, entryFault == "missing-owner" ? null : core);
            var correlation = new CorrelationContext(); correlation.Set(CorrelationId, null, "Web", null, null);
            Service = new(Store, core, Authority, Company, User, correlation, TimeProvider.System);
        }
        public Task<AccountingApiResult<AccountingPostingAccountMappingViewV1>> Activate(long id, string key, CancellationToken token = default) => Service.ActivateAsync(id, new(1, 0), key, token);
        public void AssertClean() { Assert.False(Db.ChangeTracker.HasChanges()); Assert.False(Writer.HasPendingDrafts); Assert.Null(Db.Database.CurrentTransaction); }
        public async ValueTask DisposeAsync() { try { await Db.DisposeAsync(); await otherDb.DisposeAsync(); } finally { auditScope.Dispose(); } }
    }
    private sealed class MappingActivationCompany : ICompanyContext
    { public bool IsResolved => CompanyId != Guid.Empty; public Guid CompanyId { get; private set; } public void Set(Guid company) => CompanyId = company; }
    private sealed class MappingActivationUser : ICurrentUser
    {
        public Guid Actor = MappingSafetyActor; public bool Member = true; public bool IsAuthenticated => true; public string UserId => Actor.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { CompanyA }; public bool CanAccessCompany(Guid company) => Member && company == CompanyA;
    }
    private sealed class MappingActivationAuthority : IErpAuthorityRevalidationService
    {
        public Func<bool>? Read; public Func<Task>? BeforeRead;
        public async Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken token = default)
        { if (BeforeRead is not null) await BeforeRead(); return new(Read?.Invoke() ?? true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()); }
    }
    private sealed class MappingActivationAuditDeadlock(IAuditTransactionWriterV1 actual) : IAuditTransactionWriterV1
    {
        public bool Injected; public bool HasPendingDrafts => actual.HasPendingDrafts;
        public Task WritePendingAsync(System.Data.IDbTransaction transaction, CancellationToken token = default)
        { if (!Injected) { Injected = true; throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205); } return actual.WritePendingAsync(transaction, token); }
        public void DiscardPending() => actual.DiscardPending();
    }
    private sealed class MappingActivationSave(long id) : SaveChangesInterceptor
    {
        public string? Fault; public bool Saved, Injected, ObserveTerminalCommand; public int Attempts;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default)
        {
            if (data.Context is AccountingDbContext db && (db.ChangeTracker.Entries<AccountingPostingAccountMappingVersionRow>().Any(e => e.Entity.MappingVersion == id && e.State == EntityState.Modified && e.Entity.Version == 2)
                || ObserveTerminalCommand && db.ChangeTracker.Entries<AccountingCommandRow>().Any(e => e.State == EntityState.Added && e.Entity.CommandName == AccountingPostingAccountMappingCommandNamesV1.Activate)))
            {
                Attempts++;
                if (Fault is not null)
                {
                    Injected = true;
                    if (Fault == "completed-provider") db.Database.CurrentTransaction!.GetDbTransaction().Rollback();
                    if (Fault == "concurrency") throw new DbUpdateConcurrencyException("SENTETIK mapping save race");
                    var error = SqlConcurrencyWrapperPolicyTests.SqlFailure(1205);
                    if (Fault == "wrapped") throw new DbUpdateException("SENTETIK mapping 1205", error);
                    throw error;
                }
            }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default)
        { if (Attempts > 0) Saved = true; return ValueTask.FromResult(result); }
    }
    private sealed class MappingActivationCommitted : DbTransactionInterceptor
    { public int Commits; public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData data, CancellationToken token = default) { Commits++; return Task.CompletedTask; } }
    private sealed class MappingActivationCleanup(bool dispose) : DbTransactionInterceptor
    {
        public bool Armed, Failed;
        public void Configure(DbContextOptionsBuilder<AccountingDbContext> options) => options.LogTo((id, _) => id == RelationalEventId.TransactionDisposed,
            _ => { if (Armed && dispose && !Failed) { Failed = true; throw new InvalidOperationException("SENTETIK mapping dispose fault"); } });
        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction transaction, TransactionEventData data, InterceptionResult result, CancellationToken token = default)
        { if (Armed && !dispose && !Failed) { Failed = true; throw new InvalidOperationException("SENTETIK mapping rollback fault"); } return ValueTask.FromResult(result); }
    }
    private static AccountingCommandRow MappingSafetyCaller() => new() { CompanyId = CompanyA, CommandName = "SENTETIK caller", IdempotencyKeyHash = "caller-key", ActorId = "caller", PayloadHash = "hash", ResultJson = "{}", CorrelationId = "caller-corr" };
    private static AuditEventDraft MappingSafetyAudit() => new(CompanyA, "SENTETIK caller", "Accounting.PostingAccountMapping", "caller", "Activate", 1, 2, "User", MappingSafetyActor.ToString("D"),
        AccountingPostingAccountMappingPermissionNamesV1.Activate, [], "{}", "{}", "SENTETIK", null, AuditAiInvolvement.None, null, "SQLSource", null, null, "SENTETIK-caller", null, "SENTETIK-key");
}
