using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Native_order_approval_refuses_late_authority_after_source_share_reads_without_consuming_the_request()
    {
        await using var run = await Run.StartAsync();
        var (id, requestId) = await DraftOrderForDecisionFenceAsync(run);
        var before = await run.OrderCountsAsync(id);
        var sourcingBefore = await OrderFenceSourcingVersionAsync(run, requestId);
        await using var writer = run.Store(CompanyA);
        var checks = 0;
        var denied = await Assert.ThrowsAsync<PurchasingServiceException>(() => writer.Store.MutateOrderGuardedAsync(
            run.Envelope("order-fence-late", actor: OrderApprover), Hash("order-fence-late"), PurchasingCommands.OrderApproveV1,
            id, (order, now) => order.Approve(2, OrderApprover, now), Approved,
            _ => ++checks == 3 ? throw new PurchasingServiceException(403, "forbidden") : Task.CompletedTask));
        Assert.Equal(403, denied.Status);
        Assert.Equal(3, checks); // Entry, restored order, then completed source-request share validation.
        Assert.Empty(writer.Context.ChangeTracker.Entries());
        Assert.Equal(before, await run.OrderCountsAsync(id));
        Assert.Equal(sourcingBefore, await OrderFenceSourcingVersionAsync(run, requestId));
    }

    [SqlServerFact]
    public async Task Work_delegation_revoked_during_native_order_save_rolls_back_and_same_key_can_retry_once()
    {
        await using var run = await Run.StartAsync();
        var (id, requestId) = await DraftOrderForDecisionFenceAsync(run);
        var before = await run.OrderCountsAsync(id);
        var sourcingBefore = await OrderFenceSourcingVersionAsync(run, requestId);
        var allowed = true;
        var execution = new WorkDecisionExecutionFenceV1();
        // Synthetic eligibility callback; the native Purchasing write, Save and rollback below use real SQL.
        using var lease = execution.Enter(_ => Task.FromResult(allowed));
        var authority = new WorkDecisionAuthorityV1(new RequestDecisionAuthority(), execution);
        var saved = new RequestDecisionSavedInterceptor(() => allowed = false);
        async Task Fence(CancellationToken token)
        {
            var scope = await authority.RevalidatePermissionScopeAsync(PurchasingPermissionManifestV1.OrdersApprove, CompanyA, token);
            if (!scope.IsGranted) throw new PurchasingServiceException(403, "forbidden");
        }
        await using (var writer = run.Store(CompanyA, saved))
        {
            var denied = await Assert.ThrowsAsync<PurchasingServiceException>(() => writer.Store.MutateOrderGuardedAsync(
                run.Envelope("order-fence-save", actor: OrderApprover), Hash("order-fence-save"), PurchasingCommands.OrderApproveV1,
                id, (order, now) => order.Approve(2, OrderApprover, now), Approved, Fence));
            Assert.Equal(403, denied.Status);
            Assert.Equal(1, saved.Calls);
            Assert.Empty(writer.Context.ChangeTracker.Entries());
        }
        Assert.Equal(before, await run.OrderCountsAsync(id));
        Assert.Equal(sourcingBefore, await OrderFenceSourcingVersionAsync(run, requestId));
        allowed = true;
        await using (var retry = run.Store(CompanyA))
        {
            var accepted = await retry.Store.MutateOrderGuardedAsync(run.Envelope("order-fence-save", actor: OrderApprover),
                Hash("order-fence-save"), PurchasingCommands.OrderApproveV1, id,
                (order, now) => order.Approve(2, OrderApprover, now), Approved, Fence);
            Assert.Equal(3L, accepted!.Order.Order.Version);
        }
        var committed = await run.OrderCountsAsync(id);
        Assert.Equal((before.Orders, before.Lines, before.Attempts + 1, before.AuditEvents + 1, 3L), committed);
        Assert.Equal(sourcingBefore + 1, await OrderFenceSourcingVersionAsync(run, requestId));
        await using var replay = run.Store(CompanyA);
        var checks = 0;
        var answer = await replay.Store.MutateOrderGuardedAsync(run.Envelope("order-fence-save", correlation: "SENTETIK-later", actor: OrderApprover),
            Hash("order-fence-save"), PurchasingCommands.OrderApproveV1, id,
            (_, _) => throw new InvalidOperationException("Replay must not mutate"), Approved,
            token => { checks++; return Fence(token); });
        Assert.Equal((PurchasingWriteOutcome.Replayed, run.Correlation), (answer!.Outcome, answer.OriginalCorrelationId));
        Assert.Equal(3, checks);
        allowed = false;
        await Assert.ThrowsAsync<PurchasingServiceException>(() => replay.Store.MutateOrderGuardedAsync(
            run.Envelope("order-fence-save", actor: OrderApprover), Hash("order-fence-save"), PurchasingCommands.OrderApproveV1,
            id, (_, _) => throw new InvalidOperationException("Replay must not mutate"), Approved, Fence));
        Assert.Equal(committed, await run.OrderCountsAsync(id));
        Assert.Equal(sourcingBefore + 1, await OrderFenceSourcingVersionAsync(run, requestId));
    }

    [SqlServerFact]
    public async Task Native_order_approval_refuses_ambient_transaction_before_callbacks_for_new_key_and_replay()
    {
        await using var run = await Run.StartAsync();
        var id = await RecordedOrderDecisionFenceAsync(run);
        await using var writer = run.Store(CompanyA);
        var tracked = await writer.Context.Orders.SingleAsync(x => x.Id == id);
        var calls = 0;
        using (var ambient = new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
        {
            foreach (var key in OrderFenceKeys)
                await Assert.ThrowsAsync<InvalidOperationException>(() => writer.Store.MutateOrderGuardedAsync(
                    run.Envelope(key, actor: OrderApprover), Hash(key), PurchasingCommands.OrderApproveV1, id,
                    (_, _) => calls++, Approved, _ => { calls++; return Task.CompletedTask; }));
            Assert.Equal(System.Transactions.TransactionStatus.Active, System.Transactions.Transaction.Current!.TransactionInformation.Status);
            Assert.Null(writer.Context.Database.CurrentTransaction);
        }
        Assert.Equal(0, calls);
        Assert.Same(tracked, writer.Context.ChangeTracker.Entries().Single().Entity);
        Assert.Equal(EntityState.Unchanged, writer.Context.Entry(tracked).State);
        await AssertSingleOrderFenceReceiptAsync(run, id);
    }

    [SqlServerFact]
    public async Task Native_order_approval_preserves_borrowed_transaction_for_new_key_and_replay()
    {
        await using var run = await Run.StartAsync();
        var id = await RecordedOrderDecisionFenceAsync(run);
        await using var writer = run.Store(CompanyA);
        var tracked = await writer.Context.Orders.SingleAsync(x => x.Id == id);
        await using var borrowed = await writer.Context.Database.BeginTransactionAsync();
        var calls = 0;
        foreach (var key in OrderFenceKeys)
            await Assert.ThrowsAsync<InvalidOperationException>(() => writer.Store.MutateOrderGuardedAsync(
                run.Envelope(key, actor: OrderApprover), Hash(key), PurchasingCommands.OrderApproveV1, id,
                (_, _) => calls++, Approved, _ => { calls++; return Task.CompletedTask; }));
        Assert.Equal(0, calls);
        Assert.Same(borrowed, writer.Context.Database.CurrentTransaction);
        Assert.Same(tracked, writer.Context.ChangeTracker.Entries().Single().Entity);
        Assert.Equal(EntityState.Unchanged, writer.Context.Entry(tracked).State);
        Assert.Equal(3L, await writer.Context.Orders.AsNoTracking().Where(x => x.Id == id).Select(x => x.Version).SingleAsync());
        await borrowed.RollbackAsync();
        await AssertSingleOrderFenceReceiptAsync(run, id);
    }

    [SqlServerFact]
    public async Task Native_order_approval_preserves_dirty_caller_values_for_new_key_and_replay()
    {
        await using var run = await Run.StartAsync();
        var id = await RecordedOrderDecisionFenceAsync(run);
        await using var writer = run.Store(CompanyA);
        var tracked = await writer.Context.Orders.SingleAsync(x => x.Id == id);
        var original = tracked.UpdatedAtUtc;
        tracked.UpdatedAtUtc = original.AddMinutes(1);
        Assert.True(writer.Context.ChangeTracker.HasChanges());
        var calls = 0;
        foreach (var key in OrderFenceKeys)
            await Assert.ThrowsAsync<InvalidOperationException>(() => writer.Store.MutateOrderGuardedAsync(
                run.Envelope(key, actor: OrderApprover), Hash(key), PurchasingCommands.OrderApproveV1, id,
                (_, _) => calls++, Approved, _ => { calls++; return Task.CompletedTask; }));
        Assert.Equal(0, calls);
        Assert.Same(tracked, writer.Context.ChangeTracker.Entries().Single().Entity);
        Assert.Equal(EntityState.Modified, writer.Context.Entry(tracked).State);
        Assert.Equal(original.AddMinutes(1), tracked.UpdatedAtUtc);
        Assert.Equal(original, writer.Context.Entry(tracked).Property(x => x.UpdatedAtUtc).OriginalValue);
        Assert.Equal(original, await run.ScalarAsync<DateTimeOffset>("SELECT UpdatedAtUtc FROM purchasing.PurchaseOrders WHERE Id=@id", ("@id", id)));
        await AssertSingleOrderFenceReceiptAsync(run, id);
    }

    [SqlServerFact]
    public async Task Native_order_approval_preserves_pending_caller_audit_for_new_key_and_replay()
    {
        await using var run = await Run.StartAsync();
        var id = await RecordedOrderDecisionFenceAsync(run);
        await using var contextScope = run.Store(CompanyA);
        var tracked = await contextScope.Context.Orders.SingleAsync(x => x.Id == id);
        await using var ownerScope = run.Factory.Services.CreateAsyncScope();
        var company = ownerScope.ServiceProvider.GetRequiredService<ICompanyContext>(); company.Set(CompanyA);
        var collector = ownerScope.ServiceProvider.GetRequiredService<IAuditEventCollector>();
        var auditWriter = ownerScope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
        collector.Enqueue(OrderFencePendingAudit(run, id));
        Assert.True(auditWriter.HasPendingDrafts);
        // Guard-only store: refusal must happen before every read, callback and Save.
        var store = new PurchasingStore(contextScope.Context, company, collector, TimeProvider.System, decisionAuditWriter: auditWriter);
        var calls = 0;
        foreach (var key in OrderFenceKeys)
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.MutateOrderGuardedAsync(
                run.Envelope(key, actor: OrderApprover), Hash(key), PurchasingCommands.OrderApproveV1, id,
                (_, _) => calls++, Approved, _ => { calls++; return Task.CompletedTask; }));
        Assert.Equal(0, calls);
        Assert.True(auditWriter.HasPendingDrafts);
        Assert.Same(tracked, contextScope.Context.ChangeTracker.Entries().Single().Entity);
        Assert.Equal(EntityState.Unchanged, contextScope.Context.Entry(tracked).State);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId=@id AND EventType=N'purchasing.synthetic-caller.v1'", ("@id", id.ToString("D"))));
        auditWriter.DiscardPending(); // Caller-owned state remains the caller's responsibility.
        await AssertSingleOrderFenceReceiptAsync(run, id);
    }

    private static readonly string[] OrderFenceKeys = ["order-fence-new", "order-fence-recorded"];

    [SqlServerFact]
    public async Task Native_order_approval_discards_only_its_own_pending_audit_after_post_save_refusal()
    {
        await using var run = await Run.StartAsync();
        var (id, requestId) = await DraftOrderForDecisionFenceAsync(run);
        var before = await run.OrderCountsAsync(id);
        var sourcingBefore = await OrderFenceSourcingVersionAsync(run, requestId);
        await using var ownerScope = run.Factory.Services.CreateAsyncScope();
        var company = ownerScope.ServiceProvider.GetRequiredService<ICompanyContext>(); company.Set(CompanyA);
        var collector = ownerScope.ServiceProvider.GetRequiredService<AuditEventCollector>();
        var auditWriter = ownerScope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
        Assert.False(auditWriter.HasPendingDrafts);
        var saved = new RequestDecisionSavedInterceptor(() => collector.Enqueue(OrderFencePendingAudit(run, id)));
        var options = new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", PurchasingDbContext.Schema))
            .AddInterceptors(new AuditSaveChangesInterceptor(collector, company), saved).Options;
        await using var context = new PurchasingDbContext(options, company);
        var store = new PurchasingStore(context, company, collector, TimeProvider.System, decisionAuditWriter: auditWriter);
        var denied = await Assert.ThrowsAsync<PurchasingServiceException>(() => store.MutateOrderGuardedAsync(
            run.Envelope("order-fence-own-audit", actor: OrderApprover), Hash("order-fence-own-audit"), PurchasingCommands.OrderApproveV1,
            id, (order, now) => order.Approve(2, OrderApprover, now), Approved, _ =>
            {
                if (saved.Calls > 0)
                {
                    Assert.True(auditWriter.HasPendingDrafts);
                    throw new PurchasingServiceException(403, "forbidden");
                }
                return Task.CompletedTask;
            }));
        Assert.Equal(403, denied.Status);
        Assert.Equal(1, saved.Calls);
        Assert.False(auditWriter.HasPendingDrafts);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(before, await run.OrderCountsAsync(id));
        Assert.Equal(sourcingBefore, await OrderFenceSourcingVersionAsync(run, requestId));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId=@id AND EventType=N'purchasing.synthetic-caller.v1'", ("@id", id.ToString("D"))));
    }

    private static async Task<(Guid OrderId, Guid RequestId)> DraftOrderForDecisionFenceAsync(Run run)
    {
        var owners = Owners.For(CompanyA);
        var requestId = await SubmittedRequestAsync(run, owners, 100m, "order-fence");
        await using var writer = run.Store(CompanyA);
        var approved = await ApproveRequestAsync(run, writer, requestId, 3, "order-fence-source-approve");
        var order = Order(owners, requestId, approved.Request.Lines[0].Id, 60m);
        var stored = await writer.Store.StoreOrderAsync(run.Envelope("order-fence-create"), Hash("order-fence-create"), owners.Account,
            Day, () => order, VersionsOf(order));
        return (stored.Order.Order.Id, requestId);
    }

    private static async Task<Guid> RecordedOrderDecisionFenceAsync(Run run)
    {
        var (id, _) = await DraftOrderForDecisionFenceAsync(run);
        await using var writer = run.Store(CompanyA);
        await writer.Store.MutateOrderGuardedAsync(run.Envelope("order-fence-recorded", actor: OrderApprover), Hash("order-fence-recorded"),
            PurchasingCommands.OrderApproveV1, id, (order, now) => order.Approve(2, OrderApprover, now), Approved, _ => Task.CompletedTask);
        return id;
    }

    private static Task<long> OrderFenceSourcingVersionAsync(Run run, Guid requestId) =>
        run.ScalarAsync<long>("SELECT SourcingVersion FROM purchasing.PurchaseRequests WHERE Id=@id", ("@id", requestId));

    private static async Task AssertSingleOrderFenceReceiptAsync(Run run, Guid id) =>
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts WHERE OrderId=@id AND CommandName=@command",
            ("@id", id), ("@command", PurchasingCommands.OrderApproveV1)));

    private static AuditEventDraft OrderFencePendingAudit(Run run, Guid id) =>
        new(CompanyA, "purchasing.synthetic-caller.v1", "Purchasing.SyntheticCaller", id.ToString("D"),
            "prepared", null, 1, AuditActorTypes.User, run.Actor, PurchasingPermissionManifestV1.OrdersApprove,
            [], "{}", "{}", "SENTETIK caller-owned audit", null, AuditAiInvolvement.None, null,
            "Web", null, null, run.Correlation, null, "order-fence-caller-audit");
}
