using System.Transactions;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Returns;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
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
        public ReturnRejectScope(Run run, bool interceptAudit = true)
        {
            auditScope = run.Factory.Services.CreateScope();
            Company = auditScope.ServiceProvider.GetRequiredService<ICompanyContext>(); Company.Set(CompanyA);
            Collector = auditScope.ServiceProvider.GetRequiredService<AuditEventCollector>();
            Writer = auditScope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
            var options = new DbContextOptionsBuilder<PurchasingDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", PurchasingDbContext.Schema));
            if (interceptAudit) options.AddInterceptors(new AuditSaveChangesInterceptor(Collector, Company));
            Database = new PurchasingDbContext(options.Options, Company);
            Store = new(Database, Company, Collector, TimeProvider.System, Writer);
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

    private static AuditEventDraft ReturnRejectCallerDraft(Run run, Guid id) => new(CompanyA,
        "purchasing.supplier-return.cost-exception-rejected.v1", "Purchasing.SupplierReturn", id.ToString("D"), "Reject", 1, 2,
        AuditActorTypes.User, run.Actor, "SENTETIK.permission", [], "{}", "{}", "Caller-owned draft", null,
        AuditAiInvolvement.None, null, "Web", null, null, run.Correlation, null, "caller-owned");
}
