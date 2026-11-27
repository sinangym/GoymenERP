using System.Transactions;
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
    public async Task Price_list_publish_rolls_back_actual_audit_attempt_and_state_on_late_authority_loss_then_same_key_has_one_effect()
    {
        await using var run = await Run.StartAsync(); var id = await PublishSafetyPrepare(run);
        await using var scope = new PublishSafetyScope(run, Guid.NewGuid());
        foreach (var fault in new[] { "before-audit", "after-audit", "after-save", "precommit", "actor", "company-membership" })
        {
            var seen = false; var postSaveChecks = 0;
            scope.Authority.Before = () =>
            {
                var staged = scope.Database.ChangeTracker.Entries<PriceListAttemptRow>().Any(row => row.State == EntityState.Added);
                var reached = fault == "before-audit" ? staged && scope.Writer.HasPendingDrafts
                    : fault == "after-audit" ? staged && !scope.Writer.HasPendingDrafts
                    : fault == "precommit" ? scope.Probe.Saved && ++postSaveChecks == 3 : scope.Probe.Saved;
                if (!reached) return; seen = true;
                if (fault == "actor") scope.User.Actor = Guid.NewGuid().ToString("D");
                else if (fault == "company-membership") scope.User.Access = false;
                else scope.Authority.Granted = false;
            };
            Assert.Equal(403, (await Assert.ThrowsAsync<ProductPriceListException>(() => scope.Publish(id, "original-key"))).Status);
            Assert.True(seen); scope.AssertClean(); scope.Reset();
            await PublishSafetyDurable(run, id, published: false, attempts: 0, audits: 0);
        }
        var original = await scope.Publish(id, "original-key");
        Assert.False(original.IsReplay); Assert.Equal("2", original.PriceList.Version);
        Assert.Equal("Published", Assert.Single(original.PriceList.Versions).State);
        var replay = await scope.Publish(id, "original-key");
        Assert.True(replay.IsReplay); Assert.Equal(original.OriginalCorrelationId, replay.OriginalCorrelationId);
        await PublishSafetyDurable(run, id, true, 1, 1);
    }

    [SqlServerFact]
    public async Task Price_list_publish_company_drift_after_real_save_rolls_back_the_captured_company()
    {
        await using var run = await Run.StartAsync(); var id = await PublishSafetyPrepare(run);
        await using var scope = new PublishSafetyScope(run, Guid.NewGuid(), driftableCompany: true);
        var observed = false;
        scope.Authority.Before = () =>
        { if (scope.Probe.Saved) { observed = true; scope.ServiceCompany.Set(TestCompanyIds.CompanyB); } };
        Assert.Equal(403, (await Assert.ThrowsAsync<ProductPriceListException>(() => scope.Publish(id, "company-drift"))).Status);
        Assert.True(observed); scope.AssertClean(); await PublishSafetyDurable(run, id, false, 0, 0);
    }

    [SqlServerFact]
    public async Task Price_list_publish_entry_preserves_borrowed_dirty_pending_audit_and_ambient_state_even_for_original_replay()
    {
        await using var run = await Run.StartAsync(); var id = await PublishSafetyPrepare(run);
        var actor = Guid.NewGuid();
        await using (var success = new PublishSafetyScope(run, actor)) await success.Publish(id, "original");
        foreach (var replay in new[] { false, true })
        foreach (var boundary in new[] { "borrowed", "dirty", "audit", "ambient", "missing-writer" })
        {
            await using var scope = new PublishSafetyScope(run, actor, missingWriter: boundary == "missing-writer");
            await using var borrowed = boundary == "borrowed" ? await scope.Database.Database.BeginTransactionAsync() : null;
            var caller = new ProductPackageStandardDecisionRow { CompanyId = TestCompanyIds.CompanyA, Version = 99, DecisionId = Guid.NewGuid() };
            if (boundary == "dirty") scope.Database.PackageStandardDecisions.Add(caller);
            if (boundary == "audit") scope.Collector.Enqueue(PackageSafetyCallerDraft(actor));
            using var ambient = boundary == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
            var before = Transaction.Current;
            var error = await Assert.ThrowsAsync<ProductPriceListException>(() => scope.Publish(id, replay ? "original" : "other-key"));
            Assert.Equal((503, ProductPriceListCodesV1.Unavailable), (error.Status, error.Code));
            Assert.Equal(0, scope.Authority.Calls); Assert.Same(before, Transaction.Current);
            Assert.Same(borrowed, scope.Database.Database.CurrentTransaction);
            Assert.Equal(boundary == "audit", scope.Writer.HasPendingDrafts);
            Assert.Equal(boundary == "dirty", scope.Database.ChangeTracker.HasChanges());
            if (boundary == "dirty") Assert.Same(caller, Assert.Single(scope.Database.ChangeTracker.Entries()).Entity);
            if (boundary == "audit") scope.Writer.DiscardPending(); // Explicit fixture-owner cleanup, never production entry cleanup.
        }
        await PublishSafetyDurable(run, id, true, 1, 1);
    }

    [SqlServerFact]
    public async Task Price_list_publish_failed_owner_save_recovers_only_an_original_native_winner_without_mutation_or_conflict_receipt_retry()
    {
        foreach (var winner in new[] { false, true })
        {
            await using var run = await Run.StartAsync(); var id = await PublishSafetyPrepare(run); var actor = Guid.NewGuid();
            await using var scope = new PublishSafetyScope(run, actor); scope.Probe.FailOnce = true;
            var inserted = false; var transactions = new HashSet<Guid>();
            scope.Authority.After = async () =>
            {
                if (scope.Database.Database.CurrentTransaction is { } transaction) transactions.Add(transaction.TransactionId);
                // The failed owner transaction has been rolled back AND disposed before this bounded fresh read.
                if (!winner || !scope.Probe.Failed || inserted || scope.Database.Database.CurrentTransaction is not null) return;
                inserted = true; scope.AssertClean();
                await using var realWinner = new PublishSafetyScope(run, actor);
                var answer = await realWinner.Publish(id, "race-key"); Assert.False(answer.IsReplay);
            };
            if (winner) Assert.True((await scope.Publish(id, "race-key")).IsReplay);
            else Assert.Equal(ProductPriceListCodesV1.ConcurrentChange,
                (await Assert.ThrowsAsync<ProductPriceListException>(() => scope.Publish(id, "race-key"))).Code);
            Assert.Equal(1, scope.Probe.Attempts); Assert.Equal(2, transactions.Count); scope.AssertClean();
            await PublishSafetyDurable(run, id, winner, winner ? 1 : 0, winner ? 1 : 0);
        }
    }

    [SqlServerFact]
    public async Task Price_list_publish_release_denial_keeps_committed_receipt_and_original_snapshot_after_later_draft_and_terminal_refusal_replay()
    {
        await using var run = await Run.StartAsync(); var id = await PublishSafetyPrepare(run);
        await using var scope = new PublishSafetyScope(run, Guid.NewGuid());
        scope.Authority.Before = () =>
        { if (scope.Probe.Saved && scope.Database.Database.CurrentTransaction is null) scope.Authority.Granted = false; };
        Assert.Equal(403, (await Assert.ThrowsAsync<ProductPriceListException>(() => scope.Publish(id, "committed"))).Status);
        await PublishSafetyDurable(run, id, true, 1, 1); scope.Reset();
        await using (var next = run.Request())
            await next.Service.StartVersionAsync(id, new("2", Guid.NewGuid(), ProductPriceListWire.FormatDate(ProductPriceBusinessDayV2.DateOf(DateTimeOffset.UtcNow).AddDays(1)),
                null), Metadata("later-draft"));
        var replay = await scope.Publish(id, "committed");
        Assert.True(replay.IsReplay); Assert.Equal("2", replay.PriceList.Version); Assert.Null(replay.PriceList.DraftVersionNo);
        var terminal = await Assert.ThrowsAsync<ProductPriceListException>(() => scope.Publish(id, "terminal-key", expected: "1"));
        Assert.Equal(ProductPriceListCodesV1.VersionConflict, terminal.Code);
        var again = await Assert.ThrowsAsync<ProductPriceListException>(() => scope.Publish(id, "terminal-key", expected: "1"));
        Assert.Equal(terminal.Code, again.Code);
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PriceListAttempts WHERE CommandName=N'product.price-list.publish.v1'"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'product.price-list.version-published.v1'"));
    }

    [SqlServerFact]
    public async Task Price_list_publish_guarded_runtime_keeps_creator_and_all_editors_excluded_with_original_terminal_receipts()
    {
        await using var run = await Run.StartAsync(); var id = await PublishSafetyPrepare(run); var editor = Guid.NewGuid();
        await using (var revision = run.Request(actor: editor))
            await revision.Service.ReviseDraftAsync(id, new("1", "SENTETIK revised", ProductPriceListWire.FormatDate(
                ProductPriceBusinessDayV2.DateOf(DateTimeOffset.UtcNow)), null, [new(Guid.NewGuid(), ProductId, Kg, "41000", null)]), Metadata("revise-before-publish"));
        foreach (var maker in new[] { run.CreatorActorId, editor })
        {
            await using var scope = new PublishSafetyScope(run, maker);
            var error = await Assert.ThrowsAsync<ProductPriceListException>(() => scope.Publish(id, "maker-" + maker, "2"));
            Assert.Equal(ProductPriceListCodesV1.StateConflict, error.Code);
            Assert.Equal(GoymenERP.Modules.Product.Domain.PriceLists.PriceListRuleCodes.PublisherPreparedVersion, Assert.Single(error.Errors).Code);
            var replay = await Assert.ThrowsAsync<ProductPriceListException>(() => scope.Publish(id, "maker-" + maker, "2"));
            Assert.Equal(error.Code, replay.Code); scope.AssertClean();
        }
        await PublishSafetyDurable(run, id, false, 2, 0);
    }

    [SqlServerFact]
    public async Task Price_list_publish_rollback_failure_still_cleans_owned_tracker_and_actual_audit_without_starting_a_replay()
    {
        await using var run = await Run.StartAsync(); var id = await PublishSafetyPrepare(run);
        var fault = new PackageDecisionRollbackFault();
        await using var scope = new PublishSafetyScope(run, Guid.NewGuid(), extra: fault); scope.Probe.FailOnce = true;
        await Assert.ThrowsAsync<AggregateException>(() => scope.Publish(id, "rollback-fault"));
        Assert.Equal(1, fault.Attempts); Assert.Equal(1, scope.Probe.Attempts); scope.AssertClean();
        await PublishSafetyDurable(run, id, false, 0, 0);
    }

    private static async Task<Guid> PublishSafetyPrepare(Run run)
    {
        var id = Guid.NewGuid(); await using var setup = run.Request();
        await setup.Service.CreateAsync(new(id, Guid.NewGuid(), "SENTETIK publish safety", "TRY", "Sales", "ExcludingVat",
            ProductPriceListWire.FormatDate(ProductPriceBusinessDayV2.DateOf(DateTimeOffset.UtcNow)), null,
            [new(Guid.NewGuid(), ProductId, Kg, "42000", "40000")]), Metadata("prepare-publish"));
        return id;
    }
    private static async Task PublishSafetyDurable(Run run, Guid id, bool published, int attempts, int audits)
    {
        Assert.Equal(published ? 2 : 1, await run.ScalarAsync<int>("SELECT State FROM product.PriceListVersions WHERE PriceListId=@id AND VersionNo=1", id));
        Assert.Equal(attempts, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PriceListAttempts WHERE CommandName=N'product.price-list.publish.v1'"));
        Assert.Equal(audits, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'product.price-list.version-published.v1'"));
    }
    private sealed class PublishSafetyScope : IAsyncDisposable
    {
        private readonly RequestScope owner; private readonly string actor;
        public readonly PackageDecisionUser User; public readonly PublishSafetyAuthority Authority = new(); public readonly PublishSafetyProbe Probe = new();
        public readonly ProductPriceListDbContext Database; public readonly AuditEventCollector Collector; public readonly IAuditTransactionWriterV1 Writer;
        public readonly GoymenERP.BuildingBlocks.Multitenancy.ICompanyContext ServiceCompany;
        private readonly IProductPriceListServiceV1 service;
        public PublishSafetyScope(Run run, Guid actorId, bool missingWriter = false, IInterceptor? extra = null, bool driftableCompany = false)
        {
            owner = run.Request(); actor = actorId.ToString("D"); User = new(actorId);
            ServiceCompany = driftableCompany ? new DriftableCompanyContext(TestCompanyIds.CompanyA) : owner.CompanyContext;
            Collector = owner.Services.GetRequiredService<AuditEventCollector>(); Writer = owner.Services.GetRequiredService<IAuditTransactionWriterV1>();
            Assert.Same(Collector, owner.Services.GetRequiredService<IAuditEventCollector>());
            var options = new DbContextOptionsBuilder<ProductPriceListDbContext>().UseSqlServer(owner.Context.Database.GetConnectionString(),
                sql => sql.MigrationsHistoryTable(ProductPriceListDbContext.MigrationsHistoryTable, ProductPriceListDbContext.Schema));
            options.AddInterceptors(new AuditSaveChangesInterceptor(Collector, owner.CompanyContext), Probe);
            if (extra is not null) options.AddInterceptors(extra);
            Database = new(options.Options, owner.CompanyContext);
            service = new GuardedProductPriceListService(new(ServiceCompany, User, Authority,
                new(Database, owner.CompanyContext, Collector, TimeProvider.System), TimeProvider.System, null, _ => true), missingWriter ? null : Writer);
        }
        public Task<PriceListResultV1> Publish(Guid id, string key, string expected = "1") => service.PublishAsync(id, new(expected, 1),
            new(User.Actor, ProductPriceListPermissionsV1.Publish, [], "corr-" + key, null, key, "Web"));
        public void Reset() { User.Actor = actor; User.Access = true; Authority.Granted = true; Authority.Before = null; Authority.After = null; Probe.Saved = false; }
        public void AssertClean() { Assert.False(Database.ChangeTracker.HasChanges()); Assert.Null(Database.Database.CurrentTransaction); Assert.False(Writer.HasPendingDrafts); }
        public async ValueTask DisposeAsync() { try { await Database.DisposeAsync(); } finally { await owner.DisposeAsync(); } }
    }
    private sealed class PublishSafetyAuthority : IErpAuthorityRevalidationService
    {
        public bool Granted = true; public int Calls; public Action? Before; public Func<Task>? After;
        public async Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken token = default)
        { Calls++; Assert.Equal(ProductPriceListPermissionsV1.Publish, permission); Before?.Invoke(); if (After is not null) await After(); return new(Granted, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()); }
    }
    private sealed class PublishSafetyProbe : SaveChangesInterceptor
    {
        public bool Saved, FailOnce, Failed; public int Attempts;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default)
        {
            if (data.Context is ProductPriceListDbContext db && db.ChangeTracker.Entries<PriceListAttemptRow>()
                .Any(row => row.State == EntityState.Added && row.Entity.CommandName == ProductPriceListCommandsV1.PublishV1))
            { Attempts++; if (FailOnce && !Failed) { Failed = true; throw new DbUpdateConcurrencyException("SENTETIK native publish Save collision"); } }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default)
        { if (Attempts > 0) Saved = true; return ValueTask.FromResult(result); }
    }
}
