using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    // Actual SQL owner/transaction/read fences. Preparation is an explicitly controlled readonly callback here;
    // this does not claim reference-provider, atomic approval, command receipt or browser acceptance.
    [SqlServerFact]
    public async Task Mixed_preview_reads_complete_current_child_in_its_parent_transaction_without_command_or_audit_effects()
    {
        await using var run = await Run.StartAsync(); var f = await MembershipSourcesAsync(run, CompanyA);
        await StageMembershipFixtureAsync(run, f);
        var attempts = await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts");
        var serviceAttempts = await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.ServiceOrderAttempts");
        var audits = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents");
        await using var scope = run.Factory.Services.CreateAsyncScope();
        var company = scope.ServiceProvider.GetRequiredService<ICompanyContext>(); company.Set(CompanyA);
        var db = scope.ServiceProvider.GetRequiredService<PurchasingDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
        var store = new PurchasingStore(db, company, scope.ServiceProvider.GetRequiredService<IAuditEventCollector>(), TimeProvider.System,
            decisionAuditWriter: writer);
        var calls = 0;
        var answer = await store.ReadMixedOrderApprovalPreviewAsync(f.Parent.Order.Id, 3, (parent, current, _) =>
        {
            calls++; Assert.NotNull(db.Database.CurrentTransaction);
            var child = Assert.Single(current); Assert.Equal(f.Service.OrderId, child.OrderId); Assert.Equal(1L, child.Version);
            Assert.Equal(f.Service.Lines.Select(x => x.LineId), child.Lines.Select(x => x.LineId));
            return Task.FromResult(new PurchaseMixedOrderPreparedPreviewV1(parent, current));
        }, _ => Task.CompletedTask);
        Assert.NotNull(answer); Assert.Equal(1, calls); Assert.Equal(3L, answer.Parent.Order.Version);
        Assert.Empty(db.ChangeTracker.Entries()); Assert.False(writer.HasPendingDrafts); Assert.Null(db.Database.CurrentTransaction);
        Assert.Equal(attempts, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts"));
        Assert.Equal(serviceAttempts, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.ServiceOrderAttempts"));
        Assert.Equal(audits, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));
    }

    [SqlServerFact]
    public async Task Mixed_preview_completed_read_fence_cancellation_stops_before_preparation_and_releases_no_effects()
    {
        await using var run = await Run.StartAsync(); var f = await MembershipSourcesAsync(run, CompanyA);
        await StageMembershipFixtureAsync(run, f);
        await using var scope = run.Factory.Services.CreateAsyncScope();
        var company = scope.ServiceProvider.GetRequiredService<ICompanyContext>(); company.Set(CompanyA);
        var db = scope.ServiceProvider.GetRequiredService<PurchasingDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
        var store = new PurchasingStore(db, company, scope.ServiceProvider.GetRequiredService<IAuditEventCollector>(), TimeProvider.System,
            decisionAuditWriter: writer);
        using var cancellation = new CancellationTokenSource(); var fences = 0; var preparations = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ReadMixedOrderApprovalPreviewAsync(f.Parent.Order.Id, 3,
            (parent, current, _) => { preparations++; return Task.FromResult(new PurchaseMixedOrderPreparedPreviewV1(parent, current)); },
            _ => { if (++fences == 5) cancellation.Cancel(); return Task.CompletedTask; }, cancellation.Token));
        Assert.Equal(5, fences); Assert.Equal(0, preparations); Assert.Empty(db.ChangeTracker.Entries());
        Assert.False(writer.HasPendingDrafts); Assert.Null(db.Database.CurrentTransaction);
        Assert.Equal(3L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id=@id", ("@id", f.Parent.Order.Id)));
        Assert.Equal(1L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.ServiceOrders WHERE CompanyId=@company AND OrderId=@id",
            ("@company", CompanyA), ("@id", f.Service.OrderId)));
    }

    [SqlServerFact]
    public async Task Mixed_preview_refuses_caller_transaction_or_dirty_tracker_without_adopting_or_clearing_either()
    {
        await using var run = await Run.StartAsync();
        foreach (var dirty in new[] { false, true })
        {
            await using var scope = run.Factory.Services.CreateAsyncScope();
            var company = scope.ServiceProvider.GetRequiredService<ICompanyContext>(); company.Set(CompanyA);
            var db = scope.ServiceProvider.GetRequiredService<PurchasingDbContext>();
            var writer = scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
            var store = new PurchasingStore(db, company, scope.ServiceProvider.GetRequiredService<IAuditEventCollector>(), TimeProvider.System,
                decisionAuditWriter: writer);
            await using var transaction = dirty ? null : await db.Database.BeginTransactionAsync();
            var caller = new PurchaseOrderRow { CompanyId = CompanyA, Id = Guid.NewGuid(), Version = 1 };
            if (dirty) db.Orders.Add(caller);
            var fences = 0;
            var failure = await Assert.ThrowsAsync<PurchasingServiceException>(() => store.ReadMixedOrderApprovalPreviewAsync(Guid.NewGuid(), 1,
                (parent, current, _) => Task.FromResult(new PurchaseMixedOrderPreparedPreviewV1(parent, current)),
                _ => { fences++; return Task.CompletedTask; }));
            Assert.Equal(503, failure.Status); Assert.Equal(0, fences);
            if (dirty) { Assert.Same(caller, Assert.Single(db.ChangeTracker.Entries()).Entity); Assert.Equal(EntityState.Added, db.Entry(caller).State); }
            else Assert.Same(transaction, db.Database.CurrentTransaction);
            Assert.False(writer.HasPendingDrafts);
        }
    }
}
