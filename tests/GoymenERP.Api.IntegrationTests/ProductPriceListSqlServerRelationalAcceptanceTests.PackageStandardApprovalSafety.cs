using System.Data.Common;
using System.Transactions;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.PriceLists;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ProductPriceListSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Package_decisions_roll_back_late_authority_actor_and_membership_loss_including_actual_audit_then_replay_original_204()
    {
        foreach (var approve in new[] { true, false })
        {
            await using var run = await Run.StartAsync(); await PackageSafetyPrepare(run);
            var checker = Guid.NewGuid(); var probe = new PackageDecisionSaveProbe();
            await using var decision = new PackageDecisionScope(run, checker, probe);
            foreach (var fault in new[] { "before-audit", "after-audit", "after-save", "precommit", "actor", "membership", "authentication" })
            {
                decision.Reset(); var afterSaveChecks = 0; var observed = false;
                decision.Authority.BeforeFresh = () =>
                {
                    var staged = decision.Database.ChangeTracker.Entries<ProductPackageStandardDecisionRow>().Any(row => row.State == EntityState.Added);
                    var reached = fault == "before-audit" ? staged && decision.Writer.HasPendingDrafts
                        : fault == "after-audit" ? staged && !decision.Writer.HasPendingDrafts
                        : fault == "precommit" ? probe.Saved && ++afterSaveChecks == 3 : probe.Saved;
                    if (!reached) return;
                    observed = true;
                    if (fault == "actor") decision.User.Actor = Guid.NewGuid().ToString("D");
                    else if (fault == "membership") decision.User.Access = false;
                    else if (fault == "authentication") decision.User.Authenticated = false;
                    else decision.Authority.Granted = false;
                };
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => decision.Decide(approve, "same-original"));
                Assert.True(observed); decision.AssertClean(); decision.Reset(); probe.Saved = false;
                await PackageSafetyDurable(run, decisions: 0, attempts: 0, audits: 0);
            }
            var original = await decision.Decide(approve, "same-original");
            Assert.Equal((204, false, "corr-same-original"), (original.Answer.StatusCode, original.IsReplay, original.OriginalCorrelationId));
            await PackageSafetyDurable(run, 1, 1, 1);
            // A new pending version does not turn the old key into a new decision or current-state receipt.
            await using (var preparation = run.Request())
                Assert.Equal(204, (await PackageService(preparation, run.CreatorActorId, new FixedClock(PackageClockNow))
                    .SetAsync(ProductId, new("1", 13, "SENTETIK later version"), PackageMetadata(run, "later-prepare"))).Answer.StatusCode);
            var replay = await decision.Decide(approve, "same-original");
            Assert.True(replay.IsReplay); Assert.Equal(original.Answer, replay.Answer); Assert.Equal(original.OriginalCorrelationId, replay.OriginalCorrelationId);
            var changedReason = await decision.Decide(approve, "same-original", reason: "SENTETIK changed payload");
            Assert.Equal(ProductPackageStandardProblemCodesV2.IdempotencyConflict, changedReason.Answer.Code);
            decision.User.Actor = Guid.NewGuid().ToString("D");
            Assert.Equal(ProductPackageStandardProblemCodesV2.IdempotencyConflict, (await decision.Decide(approve, "same-original")).Answer.Code);
            decision.Reset(); decision.Authority.Granted = false;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => decision.Decide(approve, "same-original"));
            await PackageSafetyDurable(run, 1, 1, 1);
        }
    }

    [SqlServerFact]
    public async Task Package_decision_suppresses_service_company_drift_after_save_and_rolls_back_the_original_company_rows()
    {
        await using var run = await Run.StartAsync(); await PackageSafetyPrepare(run); var probe = new PackageDecisionSaveProbe();
        await using var scope = new PackageDecisionScope(run, Guid.NewGuid(), probe, driftableServiceCompany: true);
        var observed = false;
        scope.Authority.BeforeFresh = () =>
        {
            if (!probe.Saved) return;
            observed = true; scope.ServiceCompany.Set(TestCompanyIds.CompanyB);
        };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => scope.Decide(true, "company-drift"));
        Assert.True(observed); scope.AssertClean(); await PackageSafetyDurable(run, 0, 0, 0);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.ProductPackageStandardAttempts WHERE CompanyId=@id", TestCompanyIds.CompanyB));
        scope.ServiceCompany.Set(TestCompanyIds.CompanyA); scope.Authority.BeforeFresh = null;
        Assert.Equal(204, (await scope.Decide(true, "company-drift")).Answer.StatusCode);
        await PackageSafetyDurable(run, 1, 1, 1);
    }

    [SqlServerFact]
    public async Task Package_decision_entry_refusals_preserve_borrowed_ambient_dirty_and_actual_pending_audit_even_for_replay()
    {
        await using var run = await Run.StartAsync(); await PackageSafetyPrepare(run); var checker = Guid.NewGuid();
        await using (var initial = new PackageDecisionScope(run, checker)) Assert.Equal(204, (await initial.Decide(true, "original-entry")).Answer.StatusCode);
        foreach (var boundary in new[] { "missing-writer", "borrowed", "ambient", "dirty", "audit" })
        foreach (var replay in new[] { true, false })
        {
            await using var scope = new PackageDecisionScope(run, checker, missingWriter: boundary == "missing-writer");
            await using var borrowed = boundary == "borrowed" ? await scope.Database.Database.BeginTransactionAsync() : null;
            var caller = new ProductPackageStandardDecisionRow { CompanyId = TestCompanyIds.CompanyA, Version = 99, DecisionId = Guid.NewGuid() };
            if (boundary == "dirty") scope.Database.PackageStandardDecisions.Add(caller);
            if (boundary == "audit") scope.Collector.Enqueue(PackageSafetyCallerDraft(checker));
            using var ambient = boundary == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
            var originalAmbient = Transaction.Current;
            var result = await scope.Decide(true, replay ? "original-entry" : "different-key");
            Assert.Equal(503, result.Answer.StatusCode); Assert.Equal(0, scope.Authority.Calls);
            Assert.Same(originalAmbient, Transaction.Current); Assert.Same(borrowed, scope.Database.Database.CurrentTransaction);
            Assert.Equal(boundary == "dirty", scope.Database.ChangeTracker.HasChanges());
            Assert.Equal(boundary == "audit", scope.Writer.HasPendingDrafts);
            if (boundary == "dirty") Assert.Same(caller, Assert.Single(scope.Database.ChangeTracker.Entries()).Entity);
            if (boundary == "audit") scope.Writer.DiscardPending();
        }
        await PackageSafetyDurable(run, 1, 1, 1);
    }

    [SqlServerFact]
    public async Task Package_decision_failed_save_aborts_actual_audit_before_one_ordered_native_winner_read_without_mutation_retry()
    {
        foreach (var approve in new[] { true, false })
        foreach (var publish in new[] { true, false })
        {
            await using var run = await Run.StartAsync(); await PackageSafetyPrepare(run); var checker = Guid.NewGuid();
            var probe = new PackageDecisionSaveProbe { FailSave = true }; await using var scope = new PackageDecisionScope(run, checker, probe);
            var transactions = new HashSet<Guid>(); ProductPackageStandardResultV2? winner = null;
            scope.Authority.AfterFresh = async () =>
            {
                if (scope.Database.Database.CurrentTransaction is not { } transaction) return;
                transactions.Add(transaction.TransactionId);
                if (transactions.Count != 2 || winner is not null || !publish) return;
                scope.AssertNoPending(); Assert.Equal(1, probe.Attempts);
                await using var actual = new PackageDecisionScope(run, checker);
                winner = await actual.Decide(approve, "save-race-original"); Assert.Equal(204, winner.Answer.StatusCode);
            };
            var answer = await scope.Decide(approve, "save-race-original");
            Assert.Equal(2, transactions.Count); Assert.Equal(1, probe.Attempts); scope.AssertClean();
            if (publish)
            { Assert.NotNull(winner); Assert.True(answer.IsReplay); Assert.Equal(winner.Answer, answer.Answer); Assert.Equal(winner.OriginalCorrelationId, answer.OriginalCorrelationId); }
            else
            { Assert.False(answer.IsReplay); Assert.Equal(ProductPackageStandardProblemCodesV2.VersionConflict, answer.Answer.Code); }
            await PackageSafetyDurable(run, publish ? 1 : 0, publish ? 1 : 0, publish ? 1 : 0);
        }
    }

    [SqlServerFact]
    public async Task Package_native_maker_and_stale_terminal_receipts_keep_their_original_policy_and_current_authority()
    {
        await using var run = await Run.StartAsync(); await PackageSafetyPrepare(run);
        await using (var maker = new PackageDecisionScope(run, run.CreatorActorId))
        {
            var refused = await maker.Decide(false, "maker-key");
            Assert.Equal(ProductPackageStandardProblemCodesV3.PreparerCannotDecide, refused.Answer.Code);
            Assert.True((await maker.Decide(false, "maker-key")).IsReplay);
        }
        await using var checker = new PackageDecisionScope(run, Guid.NewGuid());
        var stale = await checker.Decide(true, "stale-key", version: 99);
        Assert.Equal(ProductPackageStandardProblemCodesV3.VersionNotPending, stale.Answer.Code);
        Assert.Equal(204, (await checker.Decide(true, "actual-key")).Answer.StatusCode);
        var replay = await checker.Decide(true, "stale-key", version: 99);
        Assert.True(replay.IsReplay); Assert.Equal(stale.Answer, replay.Answer); Assert.Equal(stale.OriginalCorrelationId, replay.OriginalCorrelationId);
        await PackageSafetyDurable(run, 1, 3, 1);
    }

    [SqlServerFact]
    public async Task Package_owned_rollback_fault_still_discards_only_owned_audit_and_never_reads_a_winner()
    {
        await using var run = await Run.StartAsync(); await PackageSafetyPrepare(run); var fault = new PackageDecisionRollbackFault();
        await using var scope = new PackageDecisionScope(run, Guid.NewGuid(), extra: fault);
        scope.Authority.BeforeFresh = () => { if (scope.Writer.HasPendingDrafts) scope.Authority.Granted = false; };
        await Assert.ThrowsAsync<AggregateException>(() => scope.Decide(true, "rollback-fault"));
        Assert.Equal(1, fault.Attempts); scope.AssertClean();
        await PackageSafetyDurable(run, 0, 0, 0);
    }

    private static async Task PackageSafetyPrepare(Run run)
    {
        await using var scope = run.Request();
        var answer = await PackageService(scope, run.CreatorActorId, new FixedClock(PackageClockNow))
            .SetAsync(ProductId, new("0", 12, "SENTETIK owned decision"), PackageMetadata(run, "owned-prepare"));
        Assert.Equal(204, answer.Answer.StatusCode);
    }
    private static async Task PackageSafetyDurable(Run run, int decisions, int attempts, int audits)
    {
        Assert.Equal(decisions, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.ProductPackageStandardDecisions"));
        Assert.Equal(attempts, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.ProductPackageStandardAttempts WHERE CommandName IN (N'product.package-standards.approve.v3',N'product.package-standards.reject.v3')"));
        Assert.Equal(audits, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType IN (N'product.package-standards.version-approved.v3',N'product.package-standards.version-rejected.v3')"));
    }
    private static AuditEventDraft PackageSafetyCallerDraft(Guid actor) => new(TestCompanyIds.CompanyA,
        "synthetic.package.caller.v1", "Product.PackageStandardTable", Guid.NewGuid().ToString("D"), "Caller", null, 1,
        AuditActorTypes.User, actor.ToString("D"), ProductPackageStandardPermissionsV3.Approve, [], "{}", "{}", "SENTETIK caller", null,
        AuditAiInvolvement.None, null, "Web", null, null, "SENTETIK-caller", null, "caller-key");

    // Reuses the native disposable Product fixture and its actual scoped writer/collector/company.
    // Only current user/permission observations, owner Save concurrency and rollback failure are controlled.
    private sealed class PackageDecisionScope : IAsyncDisposable
    {
        private readonly RequestScope owner; private readonly Guid originalActor; private readonly PackageDecisionSaveProbe probe;
        public readonly PackageDecisionUser User; public readonly PackageDecisionAuthority Authority = new();
        public ProductPriceListDbContext Database { get; }
        public ICompanyContext ServiceCompany { get; }
        public AuditEventCollector Collector { get; }
        public IAuditTransactionWriterV1 Writer { get; }
        private readonly IProductPackageStandardServiceV3 service;
        public PackageDecisionScope(Run run, Guid actor, PackageDecisionSaveProbe? save = null, bool missingWriter = false, IInterceptor? extra = null,
            bool driftableServiceCompany = false)
        {
            owner = run.Request(); originalActor = actor; User = new(actor); probe = save ?? new();
            // Only the explicit drift case substitutes the service context; the real database/writer retain A and must roll back A.
            ServiceCompany = driftableServiceCompany ? new DriftableCompanyContext(TestCompanyIds.CompanyA) : owner.CompanyContext;
            Collector = owner.Services.GetRequiredService<AuditEventCollector>(); Writer = owner.Services.GetRequiredService<IAuditTransactionWriterV1>();
            Assert.Same(Collector, owner.Services.GetRequiredService<IAuditEventCollector>());
            var options = new DbContextOptionsBuilder<ProductPriceListDbContext>().UseSqlServer(owner.Context.Database.GetConnectionString(),
                sql => sql.MigrationsHistoryTable(ProductPriceListDbContext.MigrationsHistoryTable, ProductPriceListDbContext.Schema));
            options.AddInterceptors(new AuditSaveChangesInterceptor(Collector, owner.CompanyContext), probe);
            if (extra is not null) options.AddInterceptors(extra);
            Database = new(options.Options, owner.CompanyContext);
            service = new GuardedProductPackageStandardService(new(Database, ServiceCompany, User, Authority, Collector,
                new FixedClock(PackageClockNow)), missingWriter ? null : Writer);
        }
        public Task<ProductPackageStandardResultV2> Decide(bool approve, string key, long version = 1, string reason = "SENTETIK native reason") =>
            approve ? service.ApproveAsync(version, new("2026-11-16", reason), Metadata(key)) : service.RejectAsync(version, new(reason), Metadata(key));
        private ProductPackageStandardCommandMetadataV2 Metadata(string key) => new(TestCompanyIds.CompanyA, User.Actor, "corr-" + key, null, "Web", null, null, key);
        public void Reset() { User.Actor = originalActor.ToString("D"); User.Access = true; User.Authenticated = true; Authority.Granted = true; Authority.BeforeFresh = null; Authority.AfterFresh = null; }
        public void AssertNoPending() { Assert.Empty(Database.ChangeTracker.Entries()); Assert.False(Writer.HasPendingDrafts); }
        public void AssertClean()
        {
            Assert.False(Database.ChangeTracker.HasChanges()); Assert.False(Writer.HasPendingDrafts);
            Assert.Null(Database.Database.CurrentTransaction);
        }
        public async ValueTask DisposeAsync() { try { await Database.DisposeAsync(); } finally { await owner.DisposeAsync(); } }
    }
    private sealed class PackageDecisionUser(Guid actor) : ICurrentUser
    {
        public string Actor = actor.ToString("D"); public bool Access = true, Authenticated = true;
        public bool IsAuthenticated => Authenticated; public string UserId => Actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { TestCompanyIds.CompanyA, TestCompanyIds.CompanyB };
        public bool CanAccessCompany(Guid company) => Access && AllowedCompanyIds.Contains(company);
    }
    private sealed class PackageDecisionAuthority : IErpAuthorityRevalidationService
    {
        public int Calls; public bool Granted = true; public Action? BeforeFresh; public Func<Task>? AfterFresh;
        public async Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken token = default)
        {
            Calls++; Assert.Equal(ProductPackageStandardPermissionsV3.Approve, permission); BeforeFresh?.Invoke();
            if (AfterFresh is not null) await AfterFresh();
            return new(Granted, true, new HashSet<string> { "SENTETIK-package-checker" }, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
        }
    }
    private sealed class PackageDecisionSaveProbe : SaveChangesInterceptor
    {
        public bool Saved, FailSave; public int Attempts;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default)
        {
            if (data.Context is ProductPriceListDbContext database && database.ChangeTracker.Entries<ProductPackageStandardAttemptRow>()
                .Any(row => row.State == EntityState.Added && row.Entity.CommandName is ProductPackageStandardService.ApproveCommandName or ProductPackageStandardService.RejectCommandName))
            { Attempts++; if (FailSave) throw new DbUpdateConcurrencyException("SENTETIK package decision owner Save race"); }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default)
        { if (data.Context is ProductPriceListDbContext && Attempts > 0) Saved = true; return ValueTask.FromResult(result); }
    }
    private sealed class PackageDecisionRollbackFault : DbTransactionInterceptor
    {
        public int Attempts;
        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction transaction, TransactionEventData data,
            InterceptionResult result, CancellationToken token = default)
        { Attempts++; throw new InvalidOperationException("SENTETIK package decision rollback fault"); }
    }
}
