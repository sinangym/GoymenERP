using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Invoices;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Services;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    // Actual relational participant/native owner persistence; reference prep and invoice policy bridges are controlled.
    // No HTTP, physical Inventory receipt, external supplier or complete first-use acceptance claim.
    [SqlServerFact]
    public async Task Mixed_partial_goods_and_service_progression_preserves_original_approval_and_invoice_replay_without_double_effects()
    {
        await using var run = await Run.StartAsync(); var f = await SupplierRevisionSourcesAsync(run);
        var goods = MixedGoodsRequest(f, Guid.NewGuid(), "4"); var key = Envelope("mixed-partial-goods", "mixed-goods-original");
        var first = await StageAndCommitAsync(run, CompanyA, goods, key);
        Assert.Equal((false, "5", "3"), (first.IsReplay, first.PurchaseOrderVersion, first.RemainingQuantity));
        var serviceAcceptance = Guid.NewGuid(); PurchaseServiceOrderState accepted;
        await using (var child = ServiceScope(run, CompanyA))
        {
            accepted = (await child.Store.WriteAsync(f.Service.OrderId, PurchaseServiceOrderCommandsV1.Accept,
                run.Envelope("mixed-partial-service"), Hash("mixed-partial-service"), "SENTETIK performed service",
                (old, _) => Task.FromResult(PurchaseServiceOrder.Accept(old!, 2, serviceAcceptance, f.Service.Lines[0].LineId,
                    4m, Day, run.Actor, "SENTETIK actual partial service", DateTimeOffset.UtcNow)), _ => Task.CompletedTask, default))!.Order!;
            var source = await ((IPurchaseServiceAcceptanceReadPortV1)child.Store).GetAsync(CompanyA, f.Service.OrderId, serviceAcceptance);
            Assert.Equal(PurchaseServiceAcceptanceOutcomeV1.Available, source.Outcome); Assert.Equal("4", source.Source!.Quantity);
        }
        Assert.Equal(3L, accepted.Version); Assert.Equal(4m, accepted.Lines[0].AcceptedQuantity);
        var invoice = ServiceInvoice(run, accepted);
        await using (var scope = run.Store(CompanyA))
        {
            var store = (IPurchasingIncomingInvoiceStoreV1)scope.Store;
            await store.RecordIncomingInvoiceAsync(invoice, run.Envelope("mixed-service-invoice"), Hash("mixed-service-invoice"), _ => Task.CompletedTask);
            var workflow = InvoiceWorkflow(run, scope, new(_ => Task.CompletedTask), InvoiceMatchSource(scope, new()));
            var body = new PurchaseIncomingInvoiceMatchRequestV1("1", [new(invoice.Lines[0].Id, accepted.OrderId, serviceAcceptance, "4")]);
            var metadata = new PurchasingCommandMetadata("SENTETIK-mixed-service-match", run.Correlation, null, AcceptanceTime);
            var matched = await workflow.MatchAsync(invoice.Id, body, metadata);
            Assert.Equal(PurchaseIncomingInvoiceStatus.Matched, matched.Invoice.Status);
            Assert.True((await workflow.MatchAsync(invoice.Id, body, metadata)).IsReplay);
        }
        var audit = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents");
        var replay = await StageAndCommitAsync(run, CompanyA, goods, key with { CorrelationId = "mixed-goods-retry" });
        Assert.Equal((true, "5", "mixed-goods-original"), (replay.IsReplay, replay.PurchaseOrderVersion, replay.OriginalCorrelationId));
        Assert.Equal(audit, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.ServiceInvoiceBindings WHERE InvoiceId=@id", ("@id", invoice.Id)));
        await using (var parent = MixedApprovalScope(run))
        {
            var original = await MixedApprove(parent, f, MixedRequest(f),
                run.Envelope("mixed-revision-approve-" + f.Parent.Order.Id, actor: OrderApprover));
            Assert.True(original.IsReplay); Assert.Equal("4", original.Receipt!.Original.Visible.Order.Version);
            Assert.Equal(2L, original.Receipt.Original.Children[0].After.Version);
            var current = (await parent.Store.FindOrderAsync(f.Parent.Order.Id))!.Order;
            Assert.Equal(5L, current.Version); Assert.Single(current.Acceptances); Assert.Single(current.ServiceMemberships);
        }
        await using var foreign = ServiceScope(run, CompanyB);
        Assert.Equal(PurchaseServiceAcceptanceOutcomeV1.Unavailable,
            (await ((IPurchaseServiceAcceptanceReadPortV1)foreign.Store).GetAsync(CompanyA, f.Service.OrderId, serviceAcceptance)).Outcome);
    }

    [SqlServerFact]
    public async Task Mixed_supplier_history_single_and_batch_goods_adjustment_roll_back_and_replay_the_original_result_after_later_changes()
    {
        await using var run = await Run.StartAsync(); var f = await SupplierRevisionSourcesAsync(run);
        await using (var revision = MixedApprovalScope(run))
            _ = await MixedReviseSupplier(revision, f, SupplierRevisionRequest(f), AdmissionEnvelope(run, "mixed-downstream-supplier"));
        await using (var child = ServiceScope(run, CompanyA))
            _ = await SetLinkedSupplierPrice(child, f, AdmissionEnvelope(run, "mixed-downstream-child-price"), 2m);
        await using (var price = run.Store(CompanyA))
            _ = await ((IPurchasingSupplierRevisionStoreV1)price.Store).SetRevisionPriceAsync(f.Parent.Order.Id, f.Parent.Order.Lines[0].Id, 5,
                new(3m, f.Parent.Order.Lines[0].UnitId), AdmissionEnvelope(run, "mixed-downstream-parent-price"), new string('C', 64), _ => Task.CompletedTask);
        await using (var approve = MixedApprovalScope(run))
            _ = await MixedApprove(approve, f, new("6") { MixedServices = [new(f.Service.OrderId, "4", f.Service.Lines.Select(x => x.LineId).ToArray())] },
                run.Envelope("mixed-downstream-reapproval", actor: OrderApprover));
        var goods = MixedGoodsRequest(f, Guid.NewGuid(), "7");
        _ = await StageAndCommitAsync(run, CompanyA, goods, Envelope("mixed-downstream-goods", "goods-original"));
        var key = Envelope("mixed-downstream-single", "single-original");
        var single = new PurchaseReceiptAdjustmentStageV1(CompanyA, Guid.NewGuid(), f.Parent.Order.Id, goods.AcceptanceId, "8", "0.25",
            false, false, "SENTETIK correction", DateTimeOffset.UtcNow, PurchaseReceiptAdjustmentCodesV1.CommandName, AcceptanceHash("mixed-downstream-single"));
        foreach (var commit in new[] { false, true })
        {
            await using var scope = run.Factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString); await connection.OpenAsync();
            await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
            await using var owner = await PurchaseReceiptAdjustmentParticipant.EnlistAsync(connection, transaction, CompanyA,
                scope.ServiceProvider.GetRequiredService<IAuditEventCollector>(), scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>());
            var result = await owner.StageAsync(single, key); Assert.False(result.IsReplay); Assert.Equal("9", result.OrderVersion);
            if (commit) await transaction.CommitAsync(); else await transaction.RollbackAsync();
            await using var read = MixedApprovalScope(run); var state = (await read.Store.FindOrderAsync(f.Parent.Order.Id))!.Order;
            Assert.Equal(commit ? 9L : 8L, state.Version); Assert.Equal(commit ? 0.75m : 1m, state.Lines[0].AcceptedQuantity.Value);
            Assert.Equal(commit ? 1 : 0, state.ReceiptAdjustments.Count); Assert.Single(state.ServiceMemberships);
        }
        var batchKey = Envelope("mixed-downstream-batch", "batch-original");
        var batch = new PurchaseReceiptAdjustmentBatchStageV1(CompanyA, Guid.NewGuid(), f.Parent.Order.Id, "9", "SENTETIK batch correction",
            DateTimeOffset.UtcNow, "purchasing.goods-receipt.reverse-batch.v1", AcceptanceHash("mixed-downstream-batch"),
            [new(Guid.NewGuid(), goods.AcceptanceId, "0.25", false, false)]);
        await using (var scope = run.Factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString); await connection.OpenAsync();
            await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
            await using var owner = await PurchaseReceiptAdjustmentParticipant.EnlistBatchAsync(connection, transaction, CompanyA,
                scope.ServiceProvider.GetRequiredService<IAuditEventCollector>(), scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>());
            Assert.Equal("10", (await owner.StageBatchAsync(batch, batchKey)).OrderVersion); await transaction.CommitAsync();
        }
        var audit = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents");
        await using (var scope = run.Factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString); await connection.OpenAsync();
            await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
            await using var owner = await PurchaseReceiptAdjustmentParticipant.EnlistAsync(connection, transaction, CompanyA,
                scope.ServiceProvider.GetRequiredService<IAuditEventCollector>(), scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>());
            var replay = await owner.FindReplayAsync(single.CommandName, single.RequestHash, key);
            Assert.Equal((true, "9", "single-original"), (replay!.IsReplay, replay.OrderVersion, replay.OriginalCorrelationId));
            var batchOwner = Assert.IsAssignableFrom<IPurchaseReceiptAdjustmentBatchParticipantV1>(owner);
            var repeated = await batchOwner.FindBatchReplayAsync(batch.CommandName, batch.RequestHash, batchKey);
            Assert.Equal((true, "10", "batch-original"), (repeated!.IsReplay, repeated.OrderVersion, repeated.OriginalCorrelationId));
            await transaction.RollbackAsync();
        }
        Assert.Equal(audit, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));
        await using var final = MixedApprovalScope(run); var restored = (await final.Store.FindOrderAsync(f.Parent.Order.Id))!.Order;
        Assert.Equal(10L, restored.Version); Assert.Equal(0.5m, restored.Lines[0].AcceptedQuantity.Value);
        Assert.Equal(1m, Assert.Single(restored.Acceptances).Quantity.Value); Assert.Equal(2, restored.ReceiptAdjustments.Count);
        Assert.Single(restored.SupplierRevisions); Assert.Equal(1L, Assert.Single(restored.ServiceMemberships).PreparedVersion);
    }

    [SqlServerFact]
    public async Task Mixed_participant_corrupt_original_approval_is_unavailable_before_goods_write_and_same_key_can_retry_after_evidence_is_restored()
    {
        await using var run = await Run.StartAsync(); var f = await SupplierRevisionSourcesAsync(run);
        var entity = f.Parent.Order.Id.ToString("D");
        var original = await run.ScalarAsync<string>("SELECT AfterJson FROM audit.AuditEvents WHERE EntityId=@id AND EventType=N'purchasing.purchase-order.approved.v1'", ("@id", entity));
        await run.ExecuteAsync("UPDATE audit.AuditEvents SET AfterJson=N'{}' WHERE EntityId=@id AND EventType=N'purchasing.purchase-order.approved.v1'", ("@id", entity));
        var goods = MixedGoodsRequest(f, Guid.NewGuid(), "4"); var key = Envelope("mixed-corrupt-goods", "mixed-corrupt-original");
        var failure = await Assert.ThrowsAsync<PurchasingServiceException>(() => StageAndCommitAsync(run, CompanyA, goods, key));
        Assert.Equal(503, failure.Status);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAcceptanceShares WHERE OrderId=@id", ("@id", f.Parent.Order.Id)));
        await run.ExecuteAsync("UPDATE audit.AuditEvents SET AfterJson=@json WHERE EntityId=@id AND EventType=N'purchasing.purchase-order.approved.v1'", ("@json", original), ("@id", entity));
        Assert.False((await StageAndCommitAsync(run, CompanyA, goods, key)).IsReplay);
        Assert.True((await StageAndCommitAsync(run, CompanyA, goods, key)).IsReplay);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAcceptanceShares WHERE OrderId=@id", ("@id", f.Parent.Order.Id)));
    }

    private static PurchaseAcceptanceStageRequestV1 MixedGoodsRequest(MembershipSources f, Guid acceptanceId, string expected) => new(
        CompanyA, acceptanceId, f.Parent.Order.Id, f.Parent.Order.Lines[0].Id, expected, f.Parent.Order.Lines[0].ProductId,
        "7", f.Parent.Order.Lines[0].UnitId, "1", DateTimeOffset.UtcNow);
}
