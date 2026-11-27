using System.Transactions;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Returns;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Native_supplier_return_rejection_failed_save_without_winner_uses_two_owned_boundaries_and_no_conflict_receipt()
    {
        await using var run = await Run.StartAsync(); var (order, id) = await SeedRejectionReturnAsync(run);
        var checker = Guid.NewGuid(); await GrantReturnDisclosureActorAsync(run, checker, price: false);
        var probe = new ReturnRejectSaveRace(id);
        await using var scope = new ReturnRejectScope(run, true, probe);
        var boundaries = new HashSet<Guid>();
        Task Observe(CancellationToken _)
        {
            if (scope.Database.Database.CurrentTransaction is { } transaction && boundaries.Add(transaction.TransactionId) && boundaries.Count == 2)
            { Assert.Empty(scope.Database.ChangeTracker.Entries()); Assert.False(scope.Writer.HasPendingDrafts); }
            return Task.CompletedTask;
        }
        var service = scope.ServiceFor(checker, Observe);
        var error = await Assert.ThrowsAsync<PurchasingServiceException>(() => service.RejectSupplierReturnCostExceptionAsync(order, id,
            new("1", "SENTETIK ret"), new("SENTETIK-return-race-absent", "SENTETIK-loser", null, AcceptanceTime)));
        Assert.Equal((409, PurchasingProblemCodesV1.ConcurrentChange), (error.Status, error.Code));
        Assert.Equal(2, boundaries.Count); Assert.Equal(1, probe.SaveAttempts);
        Assert.Null(scope.Database.Database.CurrentTransaction); Assert.Empty(scope.Database.ChangeTracker.Entries()); Assert.False(scope.Writer.HasPendingDrafts);
        Assert.Equal(1L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseSupplierReturns WHERE ReturnId=@id", ("@id", id)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseSupplierReturnAttempts WHERE CommandName=@command",
            ("@command", PurchaseSupplierReturnCommandsV1.RejectCostExceptionV1)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'purchasing.supplier-return.cost-exception-rejected.v1' AND EntityId=@id", ("@id", id.ToString("D"))));
    }

    [SqlServerFact]
    public async Task Native_supplier_return_rejection_reads_a_separate_committed_winner_only_after_owned_abort_without_second_mutation()
    {
        await using var run = await Run.StartAsync(); var (order, id) = await SeedRejectionReturnAsync(run);
        var checker = Guid.NewGuid(); await GrantReturnDisclosureActorAsync(run, checker, price: false);
        var probe = new ReturnRejectSaveRace(id);
        await using var scope = new ReturnRejectScope(run, true, probe);
        var body = new DecidePurchaseSupplierReturnV1("1", "SENTETIK ret");
        const string key = "SENTETIK-return-race-shared";
        var boundaries = new HashSet<Guid>(); var won = false;
        async Task Observe(CancellationToken token)
        {
            if (scope.Database.Database.CurrentTransaction is not { } transaction) return;
            boundaries.Add(transaction.TransactionId);
            if (boundaries.Count != 2 || won) return;
            Assert.Empty(scope.Database.ChangeTracker.Entries()); Assert.False(scope.Writer.HasPendingDrafts);
            Assert.Equal(1, probe.SaveAttempts); won = true;
            // No loser owner read has run in this second transaction. A separate real native command wins now.
            await using var winnerScope = run.Factory.Services.CreateAsyncScope();
            var winner = await ReturnDisclosureService(winnerScope.ServiceProvider, checker).RejectSupplierReturnCostExceptionAsync(order, id, body,
                new(key, "SENTETIK-winning-correlation", null, AcceptanceTime), token);
            Assert.False(winner.IsReplay); Assert.Equal(("Rejected", "2", checker.ToString("D")),
                (winner.Return.Status, winner.Return.Version, winner.Return.DecidedBy));
        }
        var service = scope.ServiceFor(checker, Observe);
        var result = await service.RejectSupplierReturnCostExceptionAsync(order, id, body,
            new(key, "SENTETIK-losing-correlation", null, AcceptanceTime));
        Assert.True(won); Assert.True(result.IsReplay); Assert.Equal("SENTETIK-winning-correlation", result.OriginalCorrelationId);
        Assert.Equal(("Rejected", "2", checker.ToString("D")), (result.Return.Status, result.Return.Version, result.Return.DecidedBy));
        Assert.Equal(2, boundaries.Count); Assert.Equal(1, probe.SaveAttempts);
        Assert.False(scope.Writer.HasPendingDrafts); Assert.Null(scope.Database.Database.CurrentTransaction);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseSupplierReturnAttempts WHERE CommandName=@command",
            ("@command", PurchaseSupplierReturnCommandsV1.RejectCostExceptionV1)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'purchasing.supplier-return.cost-exception-rejected.v1' AND EntityId=@id", ("@id", id.ToString("D"))));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements WHERE Id=(SELECT MovementId FROM purchasing.PurchaseSupplierReturns WHERE ReturnId=@id)", ("@id", id)));
    }

    [SqlServerFact]
    public async Task Supplier_return_rejection_before_and_after_save_rolls_back_real_audit_then_same_key_succeeds()
    {
        foreach (var afterSave in new[] { false, true })
        foreach (var interceptAudit in new[] { false, true })
        {
            await using var run = await Run.StartAsync(); var (order, id) = await SeedRejectionReturnAsync(run);
            await using var scope = new ReturnRejectScope(run, interceptAudit);
            var envelope = run.Envelope("return-reject-rollback", actor: Guid.NewGuid().ToString("D"));
            var observed = false;
            Task Fence(CancellationToken _)
            {
                var saved = scope.Database.ChangeTracker.Entries<PurchaseSupplierReturnRow>()
                    .Any(row => row.State == EntityState.Unchanged && row.Entity.DecidedBy is not null);
                if (afterSave ? saved : scope.Writer.HasPendingDrafts)
                { observed = true; throw new PurchasingServiceException(403, "forbidden"); }
                return Task.CompletedTask;
            }
            await Assert.ThrowsAsync<PurchasingServiceException>(() => scope.RejectAsync(order, id, envelope, Fence));
            Assert.True(observed); Assert.Empty(scope.Database.ChangeTracker.Entries()); Assert.False(scope.Writer.HasPendingDrafts);
            Assert.Equal(1L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseSupplierReturns WHERE ReturnId=@id", ("@id", id)));
            Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseSupplierReturnAttempts WHERE ReturnId=@id", ("@id", id)));
            Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'purchasing.supplier-return.cost-exception-rejected.v1' AND EntityId=@id", ("@id", id.ToString("D"))));
            var saved = await scope.RejectAsync(order, id, envelope, _ => Task.CompletedTask);
            Assert.Equal((PurchaseSupplierReturnStatus.Rejected, 2L), (saved.Return.Status, saved.Version));
            var replay = await scope.Store.ExecuteRejectionGuardedAsync(token => scope.Store.FindReplayAsync(envelope, Hash("return-rejection"),
                PurchaseSupplierReturnCommandsV1.RejectCostExceptionV1, token), _ => Task.CompletedTask);
            Assert.NotNull(replay); Assert.Equal(envelope.CorrelationId, replay.OriginalCorrelationId);
            Assert.Equal(saved, replay.Original);
            Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseSupplierReturnAttempts WHERE ReturnId=@id", ("@id", id)));
            Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'purchasing.supplier-return.cost-exception-rejected.v1' AND EntityId=@id", ("@id", id.ToString("D"))));
            Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements WHERE Id=(SELECT MovementId FROM purchasing.PurchaseSupplierReturns WHERE ReturnId=@id)", ("@id", id)));
        }
    }

    [SqlServerFact]
    public async Task Supplier_return_rejection_entry_refusals_preserve_borrowed_dirty_audit_and_ambient_caller_state()
    {
        await using var run = await Run.StartAsync(); var (order, id) = await SeedRejectionReturnAsync(run);
        foreach (var boundary in new[] { "missing-writer", "borrowed", "dirty", "audit", "ambient" })
        {
            await using var scope = new ReturnRejectScope(run);
            var store = boundary == "missing-writer" ? new PurchaseSupplierReturnStore(scope.Database, scope.Company, scope.Collector, TimeProvider.System) : scope.Store;
            await using var borrowed = boundary == "borrowed" ? await scope.Database.Database.BeginTransactionAsync() : null;
            var row = boundary == "dirty" ? await scope.Database.SupplierReturns.SingleAsync(row => row.ReturnId == id) : null;
            if (row is not null) row.Version++;
            if (boundary is "audit" or "missing-writer") scope.Collector.Enqueue(ReturnRejectCallerDraft(run, id));
            using var ambient = boundary == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
            var called = false;
            var error = await Assert.ThrowsAsync<PurchasingServiceException>(() => store.ExecuteRejectionGuardedAsync<int>(
                _ => throw new InvalidOperationException("must not execute"), _ => { called = true; return Task.CompletedTask; }));
            Assert.Equal((503, PurchaseSupplierReturnCodesV1.Unavailable), (error.Status, error.Code)); Assert.False(called);
            Assert.Same(borrowed, scope.Database.Database.CurrentTransaction); Assert.Equal(boundary == "dirty", scope.Database.ChangeTracker.HasChanges());
            Assert.Equal(boundary is "audit" or "missing-writer", scope.Writer.HasPendingDrafts);
            if (row is not null) Assert.Equal(2, row.Version);
            if (boundary is "audit" or "missing-writer") scope.Writer.DiscardPending();
        }
    }

    [SqlServerFact]
    public async Task Supplier_return_rejection_terminal_version_refusal_commits_and_replays_through_the_native_service()
    {
        await using var run = await Run.StartAsync(); var (order, id) = await SeedRejectionReturnAsync(run);
        var checker = Guid.NewGuid(); await GrantReturnDisclosureActorAsync(run, checker, price: false);
        await using var scope = run.Factory.Services.CreateAsyncScope();
        var service = ReturnDisclosureService(scope.ServiceProvider, checker);
        var body = new DecidePurchaseSupplierReturnV1("99", "SENTETIK ret");
        var first = await Assert.ThrowsAsync<PurchasingServiceException>(() => service.RejectSupplierReturnCostExceptionAsync(order, id, body,
            new("SENTETIK-return-terminal", "SENTETIK-original", null, AcceptanceTime)));
        var replay = await Assert.ThrowsAsync<PurchasingServiceException>(() => service.RejectSupplierReturnCostExceptionAsync(order, id, body,
            new("SENTETIK-return-terminal", "SENTETIK-later", null, AcceptanceTime)));
        Assert.Equal(409, first.Status); Assert.Equal(first.Code, replay.Code); Assert.False(first.IsReplay); Assert.True(replay.IsReplay);
        Assert.Equal("SENTETIK-original", replay.OriginalCorrelationId);
        Assert.Equal(1L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseSupplierReturns WHERE ReturnId=@id", ("@id", id)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'purchasing.supplier-return.cost-exception-rejected.v1' AND EntityId=@id", ("@id", id.ToString("D"))));
    }

    [SqlServerFact]
    public async Task Supplier_return_rejection_captured_company_change_after_save_rolls_back_receipt_and_audit()
    {
        await using var run = await Run.StartAsync(); var (order, id) = await SeedRejectionReturnAsync(run);
        await using var scope = new ReturnRejectScope(run);
        var changed = false;
        await Assert.ThrowsAsync<PurchasingStoreException>(() => scope.RejectAsync(order, id,
            run.Envelope("return-company", actor: Guid.NewGuid().ToString("D")), _ =>
            {
                if (scope.Database.ChangeTracker.Entries<PurchaseSupplierReturnRow>().Any(row => row.State == EntityState.Unchanged && row.Entity.DecidedBy is not null))
                { changed = true; scope.Company.Set(CompanyB); }
                return Task.CompletedTask;
            }));
        Assert.True(changed); Assert.Empty(scope.Database.ChangeTracker.Entries()); Assert.False(scope.Writer.HasPendingDrafts);
        Assert.Equal(1L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseSupplierReturns WHERE ReturnId=@id", ("@id", id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseSupplierReturnAttempts WHERE ReturnId=@id", ("@id", id)));
    }

    private static async Task<(Guid Order, Guid Return)> SeedRejectionReturnAsync(Run run)
    {
        var receipt = await SeedReturnInvoiceCostReceiptAsync(run, 5m);
        await GrantReturnDisclosureActorAsync(run, Guid.Parse(run.Actor), price: false);
        await using var scope = run.Factory.Services.CreateAsyncScope();
        var request = DisclosureRequest(receipt.Match.AcceptanceId);
        var response = await ReturnDisclosureService(scope.ServiceProvider, Guid.Parse(run.Actor)).CreateSupplierReturnAsync(receipt.Match.OrderId, request,
            new("SENTETIK-reject-safety-create", run.Correlation, null, AcceptanceTime));
        Assert.Equal("PendingCostException", response.Return.Status);
        return (receipt.Match.OrderId, request.ReturnId);
    }

    // Controlled fresh authority callback; owner SQL, immutable receipts, interceptor and Audit writer are real.
    private sealed class ReturnRejectScope : IAsyncDisposable
    {
        private readonly IServiceScope auditScope;
        public ICompanyContext Company { get; }
        public AuditEventCollector Collector { get; }
        public IAuditTransactionWriterV1 Writer { get; }
        public PurchasingDbContext Database { get; }
        public PurchaseSupplierReturnStore Store { get; }
        public ReturnRejectScope(Run run, bool interceptAudit = true, params IInterceptor[] extra)
        {
            auditScope = run.Factory.Services.CreateScope();
            Company = auditScope.ServiceProvider.GetRequiredService<ICompanyContext>(); Company.Set(CompanyA);
            Collector = auditScope.ServiceProvider.GetRequiredService<AuditEventCollector>();
            Writer = auditScope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
            var options = new DbContextOptionsBuilder<PurchasingDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", PurchasingDbContext.Schema));
            if (interceptAudit) options.AddInterceptors(new AuditSaveChangesInterceptor(Collector, Company));
            options.AddInterceptors(extra);
            Database = new PurchasingDbContext(options.Options, Company);
            Store = new(Database, Company, Collector, TimeProvider.System, Writer);
        }
        public PurchasingService ServiceFor(Guid actor, Func<CancellationToken, Task> afterFreshAuthority)
        {
            var services = auditScope.ServiceProvider;
            var user = new ReturnDisclosureUser(actor);
            var authority = new ReturnRejectObservedAuthority(new ErpAuthorizationService(services.GetRequiredService<IdentityDbContext>(), user), afterFreshAuthority);
            var correlation = services.GetRequiredService<ICorrelationContext>();
            correlation.Set("SENTETIK-return-reject-race", null, SourceChannels.Web, "127.0.0.1", "SENTETIK-agent");
            return new PurchasingService(Company, user, authority, correlation, services.GetRequiredService<IPurchasingPartyAccountPortV1>(),
                services.GetService<IProductPurchaseEligibilityPortV1>(), services.GetRequiredService<PurchaseUnitVerifier>(),
                services.GetRequiredService<IPurchasingStore>(), new PurchasingServiceBindings(), TimeProvider.System, returnStore: Store);
        }
        public Task<StoredPurchaseSupplierReturn> RejectAsync(Guid order, Guid id, PurchasingWriteEnvelope envelope, Func<CancellationToken, Task> fence) =>
            Store.ExecuteRejectionGuardedAsync(async token =>
            {
                var current = (await Store.FindAsync(order, id, token))!;
                await fence(token);
                var decided = new PurchaseSupplierReturn(id, current.Return.Status, current.Version, current.RequestedBy);
                decided.Reject(1, envelope.ActorId, "SENTETIK ret");
                return await Store.SaveDecisionAsync(order, decided, 1, envelope, Hash("return-rejection"), PurchaseSupplierReturnCommandsV1.RejectCostExceptionV1, token);
            }, fence);
        public async ValueTask DisposeAsync() { await Database.DisposeAsync(); auditScope.Dispose(); }
    }

    private sealed class ReturnRejectObservedAuthority(IErpAuthorityRevalidationService actual, Func<CancellationToken, Task> afterFresh)
        : IErpAuthorityRevalidationService
    {
        public async Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken token = default)
        {
            var result = await actual.RevalidatePermissionScopeAsync(permission, companyId, token);
            if (permission == PurchasingPermissionManifestV1.OrdersApprove && result.IsGranted) await afterFresh(token);
            return result;
        }
    }

    private sealed class ReturnRejectSaveRace(Guid id) : SaveChangesInterceptor
    {
        public int SaveAttempts;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default)
        {
            if (data.Context is PurchasingDbContext database && database.ChangeTracker.Entries<PurchaseSupplierReturnRow>()
                .Any(row => row.Entity.ReturnId == id && row.State == EntityState.Modified && row.Entity.DecidedBy is not null))
            { SaveAttempts++; throw new DbUpdateConcurrencyException("SENTETIK failed supplier return rejection save"); }
            return ValueTask.FromResult(result);
        }
    }

    private static AuditEventDraft ReturnRejectCallerDraft(Run run, Guid id) => new(CompanyA,
        "purchasing.supplier-return.cost-exception-rejected.v1", "Purchasing.SupplierReturn", id.ToString("D"), "Reject", 1, 2,
        AuditActorTypes.User, run.Actor, "SENTETIK.permission", [], "{}", "{}", "Caller-owned draft", null,
        AuditAiInvolvement.None, null, "Web", null, null, run.Correlation, null, "caller-owned");
}
