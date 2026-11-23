using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Supplier_revision_migration_Down_passes_with_ordinary_orders_and_refuses_while_revision_or_invoice_binding_exists()
    {
        var guard = Assert.IsType<SqlOperation>(new AddPurchaseSupplierRevisions().DownOperations[0]).Sql;
        foreach (var invoiceBinding in new[] { false, true })
        {
            await using Run run = await Run.StartAsync();
            await run.ExecuteAsync(guard);
            var (id, line, version) = await InvoiceCostRateOrderAsync(run, SupplierRevisionMoney(), Day);
            await run.ExecuteAsync(guard);
            if (invoiceBinding)
            {
                await run.ExecuteAsync("INSERT INTO purchasing.PurchaseInvoiceOrderBindings (CompanyId,InvoiceId,InvoiceLineId,OrderId,OrderLineId,OrderVersion,SupplierPartyId,RecordedAtUtc) "
                    + "VALUES (@company,@invoice,@invoiceLine,@order,@line,@version,@supplier,SYSDATETIMEOFFSET())",
                    ("@company", CompanyA), ("@invoice", Guid.NewGuid()), ("@invoiceLine", Guid.NewGuid()), ("@order", id),
                    ("@line", line), ("@version", version), ("@supplier", AcceptanceSupplier));
            }
            else
            {
                await using StoreScope scope = run.Store(CompanyA);
                await ((IPurchasingSupplierRevisionStoreV1)scope.Store).ReviseSupplierAsync(id, version, Guid.NewGuid(), Guid.NewGuid(),
                    "SENTETIK rollback protection", run.Envelope("down-revision"), Hash("down-revision"), _ => Task.CompletedTask);
            }
            var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
            Assert.Equal(51977, refused.Number);
            Assert.Equal(1, invoiceBinding
                ? await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseInvoiceOrderBindings WHERE OrderId=@id", ("@id", id))
                : await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseOrders WHERE Id=@id AND SupplierRevisionHistoryJson IS NOT NULL", ("@id", id)));
        }
    }

    [SqlServerFact]
    public async Task Supplier_revision_reloads_draft_and_prices_reapproves_with_another_actor_and_replays_without_a_second_history()
    {
        await using Run run = await Run.StartAsync();
        var (id, line, version) = await InvoiceCostRateOrderAsync(run,
            PurchaseOrderMoney.Of(InvoiceCostRateCurrency, "TRY", InvoiceCostRateProfile, 1, 2), Day);
        var supplier = Guid.NewGuid(); var account = Guid.NewGuid();
        var envelope = run.Envelope("supplier-revise") with { ActorId = OrderApprover, PermissionCode = PurchasingPermissionManifestV1.OrdersRevise };
        // ALS-D126: the allocation guard runs inside the revision's own SQL transaction after the domain decided; when it refuses
        // (an active allocation without a confirmed set) nothing of the revision is written, no attempt is recorded.
        await using (StoreScope scope = run.Store(CompanyA))
        {
            var refusedEnvelope = run.Envelope("supplier-revise-allocated") with { ActorId = OrderApprover,
                PermissionCode = PurchasingPermissionManifestV1.OrdersRevise };
            var refused = await Assert.ThrowsAsync<PurchaseFutureAllocationLinkException>(() =>
                ((IPurchasingSupplierRevisionStoreV1)scope.Store).ReviseSupplierAsync(id, version, supplier, account,
                    "SENTETIK supplier revision", refusedEnvelope, Hash("supplier-revise-allocated"), _ => Task.CompletedTask, default,
                    (order, _, _) =>
                    {
                        Assert.Equal((PurchaseOrderStatus.Draft, supplier), (order.Status, order.SupplierPartyId));
                        throw new PurchaseFutureAllocationLinkException(409, PurchasingProblemCodesV1.LineHasAllocations, terminal: false);
                    }));
            Assert.Equal(PurchasingProblemCodesV1.LineHasAllocations, refused.Code);
        }
        Assert.Equal((2, version), (await run.ScalarAsync<int>("SELECT Status FROM purchasing.PurchaseOrders WHERE Id=@id", ("@id", id)),
            await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id=@id", ("@id", id))));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseOrders WHERE Id=@id AND SupplierRevisionHistoryJson IS NOT NULL",
            ("@id", id)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts WHERE OrderId=@id AND CommandName=@command",
            ("@id", id), ("@command", PurchasingCommands.OrderSupplierReviseV1)));
        var guardCalls = 0;
        await using (StoreScope scope = run.Store(CompanyA))
        {
            var result = await ((IPurchasingSupplierRevisionStoreV1)scope.Store).ReviseSupplierAsync(id, version, supplier, account,
                "SENTETIK supplier revision", envelope, Hash("supplier-revise"), _ => Task.CompletedTask, default,
                (_, _, _) => { guardCalls++; return Task.CompletedTask; });
            Assert.Equal(1, guardCalls);
            Assert.Equal((PurchaseOrderStatus.Draft, version + 1, supplier, account, OrderApprover),
                (result!.Order.Order.Status, result.Order.Order.Version, result.Order.Order.SupplierPartyId, result.Order.CompanyAccountId, result.Order.Order.PreparedBy));
            Assert.Null(result.Order.Order.Approval);
        }
        await using (StoreScope scope = run.Store(CompanyA))
        {
            var replay = await ((IPurchasingSupplierRevisionStoreV1)scope.Store).ReviseSupplierAsync(id, version, supplier, account,
                "SENTETIK supplier revision", envelope, Hash("supplier-revise"), _ => Task.CompletedTask);
            Assert.Equal(PurchasingWriteOutcome.Replayed, replay!.Outcome);
            Assert.Single(replay.Order.Order.SupplierRevisions);
            var price = await ((IPurchasingSupplierRevisionStoreV1)scope.Store).SetRevisionPriceAsync(id, line, version + 1,
                new(17.25m, AcceptanceUnit), run.Envelope("revision-price"), Hash("revision-price"), _ => Task.CompletedTask);
            Assert.Equal(17.25m, price!.Order.Order.Lines[0].Price!.UnitPrice);
        }
        await using (StoreScope scope = run.Store(CompanyA))
        {
            var refused = await Assert.ThrowsAsync<PurchasingRuleException>(() => scope.Store.MutateOrderAsync(run.Envelope("revision-self-approve"),
                Hash("revision-self-approve"), PurchasingCommands.OrderApproveV1, id,
                (order, now) => order.Approve(order.Version, OrderApprover, now), Approved));
            Assert.Equal(PurchasingRuleCodes.OrderSameActor, refused.Code);
        }
        await using (StoreScope scope = run.Store(CompanyA))
        {
            var approved = await scope.Store.MutateOrderAsync(run.Envelope("revision-approve"), Hash("revision-approve"),
                PurchasingCommands.OrderApproveV1, id, (order, now) => order.Approve(order.Version, run.Actor, now), Approved);
            Assert.Equal(run.Actor, approved!.Order.Order.Approval!.ApprovedBy);
        }
        await using (StoreScope scope = run.Store(CompanyA))
        {
            var restored = (await scope.Store.FindOrderAsync(id))!;
            Assert.Equal((version + 3, supplier, OrderApprover, run.Actor, 17.25m),
                (restored.Order.Version, restored.Order.SupplierPartyId, restored.Order.PreparedBy, restored.Order.Approval!.ApprovedBy, restored.Order.Lines[0].Price!.UnitPrice));
            Assert.Single(restored.Order.SupplierRevisions);
            Assert.Single(restored.Order.SupplierRevisionPrices);
        }
        Assert.Equal(run.Actor, await run.ScalarAsync<string>("SELECT CreatedBy FROM purchasing.PurchaseOrders WHERE Id=@id", ("@id", id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId=@id AND EventType=N'purchasing.purchase-order.supplier-revised.v1'",
            ("@id", id.ToString("D"))));
        var forbidden = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE purchasing.PurchaseOrders SET ApprovedBy=CurrentPreparedBy WHERE Id=@id", ("@id", id)));
        Assert.Equal(547, forbidden.Number);
    }

    [SqlServerFact]
    public async Task Recorded_invoice_binding_blocks_supplier_revision_and_foreign_company_cannot_disclose_the_order()
    {
        await using Run run = await Run.StartAsync();
        var (id, line, version) = await InvoiceCostRateOrderAsync(run, SupplierRevisionMoney(), Day);
        await run.ExecuteAsync("INSERT INTO purchasing.PurchaseInvoiceOrderBindings (CompanyId,InvoiceId,InvoiceLineId,OrderId,OrderLineId,OrderVersion,SupplierPartyId,RecordedAtUtc) "
            + "VALUES (@company,@invoice,@invoiceLine,@order,@line,@version,@supplier,SYSDATETIMEOFFSET())",
            ("@company", CompanyA), ("@invoice", Guid.NewGuid()), ("@invoiceLine", Guid.NewGuid()), ("@order", id),
            ("@line", line), ("@version", version), ("@supplier", AcceptanceSupplier));
        var before = await InvoiceCostRateWriteCountsAsync(run);
        await using (StoreScope scope = run.Store(CompanyA))
        {
            var refusal = await Assert.ThrowsAsync<PurchasingRuleException>(() => ((IPurchasingSupplierRevisionStoreV1)scope.Store)
                .ReviseSupplierAsync(id, version, Guid.NewGuid(), Guid.NewGuid(), "SENTETIK", run.Envelope("invoice-block"), Hash("invoice-block"), _ => Task.CompletedTask));
            Assert.Equal(PurchaseOrder.SupplierRevisionBlocked, refusal.Code);
        }
        await using (StoreScope scope = run.Store(CompanyB))
            Assert.Null(await ((IPurchasingSupplierRevisionStoreV1)scope.Store).ReviseSupplierAsync(id, version, Guid.NewGuid(), Guid.NewGuid(),
                "SENTETIK", run.Envelope("foreign-revise"), Hash("foreign-revise"), _ => Task.CompletedTask));
        Assert.Equal(before, await InvoiceCostRateWriteCountsAsync(run));
    }

    [SqlServerFact]
    public async Task Post_save_authority_failure_rolls_back_supplier_revision_attempt_and_audit_together()
    {
        await using Run run = await Run.StartAsync();
        var (id, _, version) = await InvoiceCostRateOrderAsync(run, SupplierRevisionMoney(), Day);
        var before = await InvoiceCostRateWriteCountsAsync(run);
        var calls = 0;
        await using (StoreScope scope = run.Store(CompanyA))
            await Assert.ThrowsAsync<InvalidOperationException>(() => ((IPurchasingSupplierRevisionStoreV1)scope.Store)
                .ReviseSupplierAsync(id, version, Guid.NewGuid(), Guid.NewGuid(), "SENTETIK", run.Envelope("revoked-revise"), Hash("revoked-revise"),
                    _ => ++calls == 4 ? throw new InvalidOperationException("SENTETIK authority revoked after save") : Task.CompletedTask));
        Assert.Equal(4, calls);
        Assert.Equal(before, await InvoiceCostRateWriteCountsAsync(run));
        await using StoreScope reloaded = run.Store(CompanyA);
        var order = (await reloaded.Store.FindOrderAsync(id))!.Order;
        Assert.Equal((version, PurchaseOrderStatus.Approved, AcceptanceSupplier), (order.Version, order.Status, order.SupplierPartyId));
        Assert.Empty(order.SupplierRevisions);
    }
    private static PurchaseOrderMoney SupplierRevisionMoney() => PurchaseOrderMoney.Of(InvoiceCostRateCurrency, "TRY", InvoiceCostRateProfile, 1, 2);
}
