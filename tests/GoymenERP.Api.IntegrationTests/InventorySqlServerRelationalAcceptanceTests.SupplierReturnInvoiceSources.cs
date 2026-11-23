using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Api.AccountingRuntime;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Supplier_return_before_first_cost_keeps_remaining_receipt_invoicable_and_conserves_two_partial_invoice_values()
    {
        await using var run = await Run.StartAsync();
        var receipt = await SeedInvoiceCostReceipt(run, null);
        var returned = CombinedReversalWrite(receipt) with { IsSupplierReturn = true, OriginalUnitCostTryPerKg = "10" };
        await RunCombinedReversalHostAsync(run, returned, GoodsReceiptPurchaseEnvelope(run, "B2-before-first", "B2-before-first"));
        var history = await FirstCostZeroHistoryAsync(run, receipt.ProductId);
        var first = await InvoiceWithCurrentReturnEvidenceAsync(run, receipt, 1m, 20m);
        var second = await InvoiceWithCurrentReturnEvidenceAsync(run, receipt, 1m, 30m);
        await ApplyInvoiceCostCore(run, first, new FirstCostPrerequisites());
        await ApplyInvoiceCostCore(run, first, new FirstCostPrerequisites());
        await ApplyInvoiceCostCore(run, second, new FirstCostPrerequisites());
        await using var verify = run.Context(CompanyA);
        var pool = await verify.AverageCostLedger.SingleAsync(x => x.ProductId == receipt.ProductId);
        Assert.Equal((2m, 25m), (pool.QuantityKg, pool.AverageCostPerKg));
        Assert.Equal(2m, await LabelBlockKgAsync(run, receipt.LabelId, BlockState.Free));
        Assert.Equal(2, await verify.PurchaseInvoiceCostApplications.CountAsync());
        Assert.Equal(history, await FirstCostZeroHistoryAsync(run, receipt.ProductId));
        foreach (var row in await verify.PurchaseInvoiceCostApplications.ToListAsync())
        {
            var result = JsonSerializer.Deserialize<InventoryPurchaseInvoiceCostApplicationResultV1>(row.ResultJson, PurchaseCostJson)!;
            var recognition = Assert.Single(result.InitialRecognitions!);
            var proof = JsonSerializer.Deserialize<InventoryPurchaseFirstCostPhysicalSourceV1>(Assert.Single(recognition.Receipts).PhysicalSourceJson, PurchaseCostJson)!;
            Assert.Equal((returned.Request.AdjustmentId, 2m, 2m),
                (Assert.Single(proof.ReceiptReversals!).ReversalId, proof.ReceiptReversals![0].UndoQuantity, proof.ReceiptReversals[0].QuantityKg));
            Assert.Equal(0, recognition.ConsumedQuantityKg.Value().Sign);
            Assert.Equal(0, recognition.InvoiceTlValue.Value().CompareTo(Assert.Single(recognition.Positions).TlValue.Value()));
        }
    }

    [SqlServerFact]
    public async Task Supplier_return_after_invoice_source_was_frozen_refuses_retryably_without_poisoning_the_event()
    {
        await using var run = await Run.StartAsync();
        var receipt = await SeedInvoiceCostReceipt(run, null);
        var delayed = await InvoiceWithCurrentReturnEvidenceAsync(run, receipt, 2m, 20m);
        var returned = CombinedReversalWrite(receipt) with { IsSupplierReturn = true, OriginalUnitCostTryPerKg = "10" };
        await RunCombinedReversalHostAsync(run, returned, GoodsReceiptPurchaseEnvelope(run, "B2-delay", "B2-delay"));
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => ApplyInvoiceCostCore(run, delayed, new FirstCostPrerequisites()));
        Assert.Equal("purchase_invoice_cost_reversal_source_unavailable", failure.Message);
        await using (var verify = run.Context(CompanyA)) Assert.Empty(await verify.PurchaseInvoiceCostApplications.ToListAsync());
        await ApplyInvoiceCostCore(run, await InvoiceWithCurrentReturnEvidenceAsync(run, receipt, 2m, 20m), new FirstCostPrerequisites());
        await using var final = run.Context(CompanyA);
        Assert.Single(await final.PurchaseInvoiceCostApplications.ToListAsync());
    }

    [SqlServerFact]
    public Task Explicit_pending_supplier_return_preserves_the_invoiced_cohort_and_later_invoice_basis() =>
        ExplicitReturnSourceAsync(false, "1", 2m, 40m, 3m, 16.6667m);

    [SqlServerFact]
    public Task Explicit_invoiced_supplier_return_preserves_the_pending_cohort_and_later_invoice_basis() =>
        ExplicitReturnSourceAsync(true, "0.5", 3m, 60m, 3.5m, 18.5714m);

    private static async Task ExplicitReturnSourceAsync(
        bool invoiced, string returnedKg, decimal nextMatched, decimal nextValue, decimal finalKg, decimal finalAverage)
    {
        await using var run = await Run.StartAsync();
        var receipt = await SeedInvoiceCostReceipt(run, null);
        var initial = InvoiceCostEvent(receipt, 1m, 10m);
        await ApplyInvoiceCostCore(run, initial, new FirstCostPrerequisites());
        var preview = await ReturnSourcesAsync(run, receipt);
        var allocation = new PurchaseSupplierReturnCostAllocationV1(preview.SourceVersion,
            new(false, invoiced ? "0" : returnedKg, invoiced ? "0" : returnedKg), invoiced
                ? [new(initial.EventId, false, returnedKg, returnedKg)] : []);
        var write = CombinedReversalWrite(receipt) with { IsSupplierReturn = true, OriginalUnitCostTryPerKg = "10",
            CostAllocation = allocation, Request = CombinedReversalWrite(receipt).Request with { UndoQuantity = returnedKg, MeasuredUndoQuantityKg = returnedKg } };
        await RunCombinedReversalHostAsync(run, write, GoodsReceiptPurchaseEnvelope(run, "B2-explicit", "B2-explicit"));
        var afterReturn = await ReturnSourcesAsync(run, receipt);
        Assert.Equal(0, afterReturn.PendingQuantityKg.Value().CompareTo(ExactCostRatio.Of(invoiced ? 3m : 2m)));
        Assert.Equal(0, Assert.Single(afterReturn.InvoicedSources).AvailableQuantityKg.Value().CompareTo(ExactCostRatio.Of(invoiced ? .5m : 1m)));
        var history = await FirstCostZeroHistoryAsync(run, receipt.ProductId);
        var later = await InvoiceWithCurrentReturnEvidenceAsync(run, receipt, nextMatched, nextValue);
        await ApplyInvoiceCostCore(run, later, new FirstCostPrerequisites());
        await using var verify = run.Context(CompanyA);
        var pool = await verify.AverageCostLedger.SingleAsync(x => x.ProductId == receipt.ProductId);
        Assert.Equal((finalKg, finalAverage), (pool.QuantityKg, pool.AverageCostPerKg));
        Assert.Equal(history, await FirstCostZeroHistoryAsync(run, receipt.ProductId));
        var result = JsonSerializer.Deserialize<InventoryPurchaseInvoiceCostApplicationResultV1>(
            (await verify.PurchaseInvoiceCostApplications.SingleAsync(x => x.SourceEventId == later.EventId)).ResultJson, PurchaseCostJson)!;
        var first = Assert.Single(result.InitialRecognitions!);
        var proof = JsonSerializer.Deserialize<InventoryPurchaseFirstCostPhysicalSourceV1>(Assert.Single(first.Receipts).PhysicalSourceJson, PurchaseCostJson)!;
        Assert.Equal("explicit-supplier-return-source", proof.AllocationBasis);
        Assert.NotNull(proof.PendingBasis);
        Assert.Equal(0, first.InvoicedPhysicalKg.Value().CompareTo(ExactCostRatio.Of(nextMatched)));
        Assert.Equal(0, first.InvoiceTlValue.Value().CompareTo(Assert.Single(first.Positions).TlValue.Value()));
    }

    [SqlServerFact]
    public async Task Explicit_supplier_return_stale_source_version_rolls_back_both_owners_and_retries_with_fresh_selection()
    {
        await using var run = await Run.StartAsync();
        var receipt = await SeedInvoiceCostReceipt(run, null);
        await ApplyInvoiceCostCore(run, InvoiceCostEvent(receipt, 1m, 10m), new FirstCostPrerequisites());
        var old = await ReturnSourcesAsync(run, receipt);
        await ApplyInvoiceCostCore(run, InvoiceCostEvent(receipt, 1m, 10m), new FirstCostPrerequisites());
        var write = CombinedReversalWrite(receipt) with { IsSupplierReturn = true, OriginalUnitCostTryPerKg = "10",
            CostAllocation = new(old.SourceVersion, new(false, "1", "1"), []),
            Request = CombinedReversalWrite(receipt).Request with { UndoQuantity = "1", MeasuredUndoQuantityKg = "1" } };
        var envelope = GoodsReceiptPurchaseEnvelope(run, "B2-stale", "B2-stale");
        var refused = await Assert.ThrowsAsync<PurchasingServiceException>(() => RunCombinedReversalHostAsync(run, write, envelope));
        Assert.Equal((409, InventorySupplierReturnExitCodesV1.ConcurrentChange), (refused.Status, refused.Code));
        await using (var verify = run.Context(CompanyA))
        { Assert.Empty(await verify.GoodsReceiptReversals.ToListAsync()); Assert.Equal(4m, await LabelBlockKgAsync(run, receipt.LabelId, BlockState.Free)); }
        var fresh = await ReturnSourcesAsync(run, receipt);
        await RunCombinedReversalHostAsync(run, write with { CostAllocation = write.CostAllocation! with { ExpectedSourceVersion = fresh.SourceVersion } }, envelope);
        Assert.Equal(3m, await LabelBlockKgAsync(run, receipt.LabelId, BlockState.Free));
    }

    [SqlServerFact]
    public async Task Explicit_supplier_return_preview_after_partial_shipment_keeps_exact_sources_but_the_host_preserves_the_fulfilled_label_guard()
    {
        await using var run = await Run.StartAsync();
        var receipt = await SeedInvoiceCostReceipt(run, null);
        await ApplyInvoiceCostCore(run, InvoiceCostEvent(receipt, 1m, 10m), new FirstCostPrerequisites());
        GoymenERP.Modules.Inventory.Domain.Labels.MaterialLabel label;
        await using (var owner = run.Store(CompanyA)) label = (await owner.Labels.FindLabelAsync(receipt.LabelId))!;
        var shipment = Movement(CompanyA, StockMovementKind.Issue,
            new StockSourceReference(InventoryLabelDispositionReader.DefiniteShipmentSourceKind, Guid.NewGuid(), Guid.NewGuid()),
            Out(Position(CompanyA, receipt.LocationId, label, BlockState.Free), 1m));
        await using (var owner = run.Store(CompanyA)) await owner.Store.RecordMovementAsync(shipment, Metadata(run));
        var preview = await ReturnSourcesAsync(run, receipt);
        Assert.False(preview.PendingWholePairAvailable);
        Assert.False(Assert.Single(preview.InvoicedSources).WholePairAvailable);
        Assert.Equal(0, preview.PendingQuantityKg.Value().CompareTo(ExactCostRatio.Of(2.25m)));
        Assert.Equal(0, preview.PendingUndoQuantity.Value().CompareTo(ExactCostRatio.Of(3m))); // Independent owner ceiling, not inferred surviving units.
        var history = await FirstCostZeroHistoryAsync(run, receipt.ProductId);
        long labelVersion;
        await using (var before = run.Context(CompanyA)) labelVersion = (await before.Labels.SingleAsync(x => x.Id == receipt.LabelId)).Version;
        var write = CombinedReversalWrite(receipt) with { IsSupplierReturn = true, OriginalUnitCostTryPerKg = "10",
            CostAllocation = new(preview.SourceVersion, new(false, "1", "1"), []),
            Request = CombinedReversalWrite(receipt).Request with { UndoQuantity = "1", MeasuredUndoQuantityKg = "1" } };
        // F-017 spec section 3.7 preserves this host gate even when the remaining source preview is exact.
        var refused = await Assert.ThrowsAsync<PurchasingServiceException>(() => RunCombinedReversalHostAsync(run, write,
            GoodsReceiptPurchaseEnvelope(run, "B2-shipped", "B2-shipped")));
        Assert.Equal((409, InventoryGoodsReceiptReversalCodesV1.AlreadyFulfilled), (refused.Status, refused.Code));
        Assert.Empty((await ReturnPurchaseSourceAsync(run, receipt)).ReceiptAdjustments);
        Assert.Equal(history, await FirstCostZeroHistoryAsync(run, receipt.ProductId));
        Assert.Equal(0, await SupplierReturnExitEffectsAsync(run, "B2-shipped"));
        Assert.Equal(3m, await LabelBlockKgAsync(run, receipt.LabelId, BlockState.Free));
        await using var verify = run.Context(CompanyA);
        Assert.Equal(labelVersion, (await verify.Labels.SingleAsync(x => x.Id == receipt.LabelId)).Version);
        Assert.Empty(await verify.GoodsReceiptReversals.ToListAsync());
        Assert.Empty(await verify.FutureSupplyConversionReversals.ToListAsync());
        Assert.False(await verify.Movements.AnyAsync(x => x.Id == write.Request.InventoryMovementId));
        Assert.False(await verify.Commands.AnyAsync(x => x.CorrelationId == "B2-shipped"));
        Assert.False(await verify.OutboxMessages.AnyAsync(x => x.CorrelationId == "B2-shipped"));
        var pool = await verify.AverageCostLedger.SingleAsync(x => x.ProductId == receipt.ProductId);
        Assert.Equal((.75m, 10m), (pool.QuantityKg, pool.AverageCostPerKg));
        await using var purchasing = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString).Options, Company(CompanyA));
        var order = await purchasing.Orders.SingleAsync(x => x.Id == receipt.PurchaseOrderId);
        Assert.Equal(long.Parse(receipt.PurchaseOrderVersionAfter, System.Globalization.CultureInfo.InvariantCulture), order.Version);
        Assert.Equal(4m, (await purchasing.OrderLines.SingleAsync(x => x.OrderId == receipt.PurchaseOrderId
            && x.LineId == receipt.PurchaseOrderLineId)).Accepted);
    }

    [SqlServerFact]
    public async Task Explicit_pending_return_before_partial_shipment_preserves_issue_cost_and_later_consumed_cost()
    {
        await using var run = await Run.StartAsync();
        var receipt = await SeedInvoiceCostReceipt(run, null);
        await ApplyInvoiceCostCore(run, InvoiceCostEvent(receipt, 1m, 10m), new FirstCostPrerequisites());
        var preview = await ReturnSourcesAsync(run, receipt);
        var write = CombinedReversalWrite(receipt) with { IsSupplierReturn = true, OriginalUnitCostTryPerKg = "10",
            CostAllocation = new(preview.SourceVersion, new(false, "1", "1"), []),
            Request = CombinedReversalWrite(receipt).Request with { UndoQuantity = "1", MeasuredUndoQuantityKg = "1" } };
        await RunCombinedReversalHostAsync(run, write, GoodsReceiptPurchaseEnvelope(run, "B2-before-shipped", "B2-before-shipped"));
        GoymenERP.Modules.Inventory.Domain.Labels.MaterialLabel label;
        await using (var owner = run.Store(CompanyA)) label = (await owner.Labels.FindLabelAsync(receipt.LabelId))!;
        var shipment = Movement(CompanyA, StockMovementKind.Issue,
            new StockSourceReference(InventoryLabelDispositionReader.DefiniteShipmentSourceKind, Guid.NewGuid(), Guid.NewGuid()),
            Out(Position(CompanyA, receipt.LocationId, label, BlockState.Free), 1.5m));
        await using (var owner = run.Store(CompanyA)) await owner.Store.RecordMovementAsync(shipment, Metadata(run));
        var afterShipment = await ReturnSourcesAsync(run, receipt);
        Assert.Equal(0, afterShipment.PendingQuantityKg.Value().CompareTo(ExactCostRatio.Of(1m)));
        Assert.Equal(0, afterShipment.PendingUndoQuantity.Value().CompareTo(ExactCostRatio.Of(2m)));
        var history = await FirstCostZeroHistoryAsync(run, receipt.ProductId);
        var later = await InvoiceWithCurrentReturnEvidenceAsync(run, receipt, 2m, 40m);
        await ApplyInvoiceCostCore(run, later, new FirstCostPrerequisites());
        Assert.Equal(history, await FirstCostZeroHistoryAsync(run, receipt.ProductId));
        await using var verify = run.Context(CompanyA);
        var pool = await verify.AverageCostLedger.SingleAsync(x => x.ProductId == receipt.ProductId);
        Assert.Equal((1.5m, 16.6667m), (pool.QuantityKg, pool.AverageCostPerKg));
        var message = await verify.OutboxMessages.SingleAsync(x => x.EventType == InventoryPurchaseInvoiceCostDifferenceV1.EventType);
        var result = JsonSerializer.Deserialize<InventoryPurchaseInvoiceCostApplicationResultV1>(message.PayloadJson, PurchaseCostJson)!;
        var recognized = Assert.Single(result.InitialRecognitions!);
        Assert.Equal(20m, recognized.ConsumedTlAmount);
        Assert.Equal(0, recognized.ConsumedQuantityKg.Value().CompareTo(ExactCostRatio.Of(1m)));
        Assert.Equal(0, Assert.Single(recognized.Positions).TlValue.Value().CompareTo(ExactCostRatio.Of(20m)));
        var accounting = AccountingPurchaseInvoiceCostSourceV1.Read(new(AccountingInventoryCostInboxV1.InventorySourceName,
            message.Id, CompanyA, message.EventType, 1, message.PayloadJson, message.OccurredAtUtc, message.CorrelationId, message.CausationId), CompanyA);
        Assert.Equal(new AccountingInventoryCostRatioV1("20", "1"), Assert.Single(accounting.Components).SignedTlAmount);
    }

    [SqlServerFact]
    public async Task Whole_source_selection_keeps_nonrepresentable_partial_kg_exact_and_returns_a_representable_total()
    {
        await using var run = await Run.StartAsync();
        var receipt = await SeedInvoiceCostReceipt(run, null);
        var initial = InvoiceCostEvent(receipt, 1.333333m, 4m);
        await ApplyInvoiceCostCore(run, initial, new FirstCostPrerequisites());
        var preview = await ReturnSourcesAsync(run, receipt);
        Assert.True(preview.PendingWholePairAvailable);
        Assert.True(Assert.Single(preview.InvoicedSources).WholePairAvailable);
        Assert.Equal(0, preview.InvoicedSources[0].AvailableQuantityKg.Value().CompareTo(ExactCostRatio.Of(1.333333m)));
        var write = CombinedReversalWrite(receipt) with { IsSupplierReturn = true, OriginalUnitCostTryPerKg = "3",
            CostAllocation = new(preview.SourceVersion, new(true, null, null), [new(initial.EventId, true, null, null)]),
            Request = CombinedReversalWrite(receipt).Request with { UndoQuantity = "4", MeasuredUndoQuantityKg = "4" } };
        var envelope = GoodsReceiptPurchaseEnvelope(run, "B2-whole", "B2-whole");
        var first = await RunCombinedReversalHostAsync(run, write, envelope);
        Assert.False(first.Stock.IsReplay);
        Assert.True((await RunCombinedReversalHostAsync(run, write, envelope)).Stock.IsReplay);
        Assert.Equal(0m, await LabelBlockKgAsync(run, receipt.LabelId, BlockState.Free));
        await using var verify = run.Context(CompanyA);
        var stamp = await verify.MovementCostStamps.SingleAsync(x => x.MovementId == write.Request.InventoryMovementId
            && x.CurrencyId == GoymenERP.Modules.Settings.Application.Contracts.V1.SettingsCurrencyIdentitiesV1.TurkishLira);
        var proof = InventorySupplierReturnCostSourceReader.ReadProof(stamp.EvidenceJson)!;
        Assert.Equal(0, proof.Selected.Aggregate(ExactCostRatio.Of(0m), (sum, x) => sum.Add(x.QuantityKg.Value())).CompareTo(ExactCostRatio.Of(4m)));
        Assert.Equal(0, proof.Selected.Aggregate(ExactCostRatio.Of(0m), (sum, x) => sum.Add(x.UndoQuantity.Value())).CompareTo(ExactCostRatio.Of(4m)));
        Assert.Single(await verify.GoodsReceiptReversals.ToListAsync());
    }

    [SqlServerFact]
    public async Task A_single_full_known_cost_invoice_remains_returnable_and_second_explicit_return_preserves_replay()
    {
        await using var run = await Run.StartAsync();
        var receipt = await SeedInvoiceCostReceipt(run, 10m);
        var invoice = InvoiceCostEvent(receipt, 4m, 40m);
        await ApplyInvoiceCost(run, invoice);
        var preview = await ReturnSourcesAsync(run, receipt);
        Assert.Equal(0, preview.PendingQuantityKg.Value().Sign);
        Assert.Equal(invoice.EventId, Assert.Single(preview.InvoicedSources).SourceEventId);
        await AssertSupplierReturnRefused(run, SupplierReturnRequest(receipt, BlockState.Free, "1", "10") with
            { PurchaseSource = await ReturnPurchaseSourceAsync(run, receipt) }, SupplierReturnMetadata(run),
            409, InventorySupplierReturnExitCodesV1.CostAllocationRequired);
        Assert.Empty((await ReturnPurchaseSourceAsync(run, receipt)).ReceiptAdjustments);
        var firstWrite = CombinedReversalWrite(receipt) with { IsSupplierReturn = true, OriginalUnitCostTryPerKg = "10",
            CostAllocation = null, // Existing command has exactly one complete invoice source, no mixture to infer.
            Request = CombinedReversalWrite(receipt).Request with { UndoQuantity = "1", MeasuredUndoQuantityKg = "1" } };
        var firstEnvelope = GoodsReceiptPurchaseEnvelope(run, "B2-known-first", "B2-known-first");
        var first = await RunCombinedReversalHostAsync(run, firstWrite, firstEnvelope);
        var next = await ReturnSourcesAsync(run, receipt);
        Assert.True(Assert.Single(next.InvoicedSources).WholePairAvailable);
        var secondWrite = CombinedReversalWrite(receipt) with { IsSupplierReturn = true, OriginalUnitCostTryPerKg = "10",
            CostAllocation = new(next.SourceVersion, new(false, "0", "0"), [new(invoice.EventId, false, "1", "1")]),
            Request = CombinedReversalWrite(receipt).Request with { ExpectedPurchaseOrderVersion = first.Purchase.OrderVersion,
                UndoQuantity = "1", MeasuredUndoQuantityKg = "1" } };
        await RunCombinedReversalHostAsync(run, secondWrite, GoodsReceiptPurchaseEnvelope(run, "B2-known-second", "B2-known-second"));
        Assert.True((await RunCombinedReversalHostAsync(run, firstWrite, firstEnvelope)).Stock.IsReplay);
        Assert.Equal(2m, await LabelBlockKgAsync(run, receipt.LabelId, BlockState.Free));
        await using var verify = run.Context(CompanyA);
        var pool = await verify.AverageCostLedger.SingleAsync(x => x.ProductId == receipt.ProductId);
        Assert.Equal((2m, 10m), (pool.QuantityKg, pool.AverageCostPerKg));
        Assert.Equal(2, await verify.GoodsReceiptReversals.CountAsync());
    }

    [SqlServerFact]
    public async Task Supplier_return_cannot_exhaust_purchase_units_while_uninvoiced_source_kg_remain()
    {
        await using var run = await Run.StartAsync();
        var receipt = await SeedInvoiceCostReceipt(run, null);
        await ApplyInvoiceCostCore(run, InvoiceCostEvent(receipt, 2m, 20m), new FirstCostPrerequisites());
        var preview = await ReturnSourcesAsync(run, receipt);
        var write = CombinedReversalWrite(receipt) with { IsSupplierReturn = true, OriginalUnitCostTryPerKg = "10",
            CostAllocation = new(preview.SourceVersion, new(false, "0.5", "2"), []),
            Request = CombinedReversalWrite(receipt).Request with { UndoQuantity = "2", MeasuredUndoQuantityKg = "0.5" } };
        var failure = await Assert.ThrowsAsync<PurchasingServiceException>(() => RunCombinedReversalHostAsync(run, write,
            GoodsReceiptPurchaseEnvelope(run, "B2-empty-purchase", "B2-empty-purchase")));
        Assert.Equal((400, InventorySupplierReturnExitCodesV1.InvalidRequest), (failure.Status, failure.Code));
        Assert.Empty((await ReturnPurchaseSourceAsync(run, receipt)).ReceiptAdjustments);
        Assert.Equal(4m, await LabelBlockKgAsync(run, receipt.LabelId, BlockState.Free));
        await using var verify = run.Context(CompanyA);
        Assert.Empty(await verify.GoodsReceiptReversals.ToListAsync());
        Assert.Single(await verify.Movements.ToListAsync());
        Assert.Equal(0, await SupplierReturnExitEffectsAsync(run, "B2-empty-purchase"));
    }

    [SqlServerFact]
    public async Task Legacy_known_cost_partial_invoice_without_causal_source_evidence_stays_unavailable_without_inventing_attribution()
    {
        await using var run = await Run.StartAsync();
        var receipt = await SeedInvoiceCostReceipt(run, 10m);
        await ApplyInvoiceCost(run, InvoiceCostEvent(receipt, 1m, 10m));
        var failure = await Assert.ThrowsAsync<InventorySupplierReturnExitException>(() => ReturnSourcesAsync(run, receipt));
        Assert.Equal((503, InventorySupplierReturnExitCodesV1.ReceiptTraceInvalid), (failure.Status, failure.Code));
        await using var verify = run.Context(CompanyA);
        Assert.Empty(await verify.GoodsReceiptReversals.ToListAsync());
        Assert.Single(await verify.PurchaseInvoiceCostApplications.ToListAsync());
        Assert.Equal(4m, await LabelBlockKgAsync(run, receipt.LabelId, BlockState.Free));
    }

    [SqlServerFact]
    public async Task A_sole_receipt_cost_source_stays_unambiguous_across_free_and_blocked_positions()
    {
        await using var run = await Run.StartAsync();
        var receipt = await SeedInvoiceCostReceipt(run, null);
        GoymenERP.Modules.Inventory.Domain.Labels.MaterialLabel label;
        await using (var owner = run.Store(CompanyA)) label = (await owner.Labels.FindLabelAsync(receipt.LabelId))!;
        var block = Movement(CompanyA, StockMovementKind.Block,
            Out(Position(CompanyA, receipt.LocationId, label, BlockState.Free), 1m),
            In(Position(CompanyA, receipt.LocationId, label, BlockState.Blocked), 1m));
        await using (var owner = run.Store(CompanyA)) await owner.Store.RecordMovementAsync(block, Metadata(run));
        var pending = await ReturnSourcesAsync(run, receipt);
        Assert.Empty(pending.InvoicedSources);
        Assert.Equal(0, pending.PendingQuantityKg.Value().CompareTo(ExactCostRatio.Of(4m)));
        Assert.True(pending.PendingWholePairAvailable);
        var invoice = InvoiceCostEvent(receipt, 4m, 40m);
        await ApplyInvoiceCostCore(run, invoice, new FirstCostPrerequisites());
        var priced = await ReturnSourcesAsync(run, receipt);
        Assert.Equal(0, priced.PendingQuantityKg.Value().Sign);
        var source = Assert.Single(priced.InvoicedSources);
        Assert.Equal(invoice.EventId, source.SourceEventId);
        Assert.Equal(0, source.AvailableQuantityKg.Value().CompareTo(ExactCostRatio.Of(4m)));
        Assert.True(source.WholePairAvailable);
        Assert.Equal(3m, await LabelBlockKgAsync(run, receipt.LabelId, BlockState.Free));
        Assert.Equal(1m, await LabelBlockKgAsync(run, receipt.LabelId, BlockState.Blocked));
    }

    [SqlServerFact]
    public async Task Explicit_return_source_evidence_without_the_owner_reversal_witness_is_unavailable()
    {
        await using var run = await Run.StartAsync();
        var receipt = await SeedInvoiceCostReceipt(run, null);
        await ApplyInvoiceCostCore(run, InvoiceCostEvent(receipt, 1m, 10m), new FirstCostPrerequisites());
        var preview = await ReturnSourcesAsync(run, receipt);
        // Deliberately commit only Inventory's participant to model an incomplete historical owner witness.
        await StageSupplierReturnAsync(run, SupplierReturnRequest(receipt, BlockState.Free, "1", "10") with
        {
            CostAllocation = new(preview.SourceVersion, new(false, "1", "1"), []),
            PurchaseSource = await ReturnPurchaseSourceAsync(run, receipt), UndoQuantity = "1",
        });
        var failure = await Assert.ThrowsAsync<InventorySupplierReturnExitException>(() => ReturnSourcesAsync(run, receipt));
        Assert.Equal((503, InventorySupplierReturnExitCodesV1.ReceiptTraceInvalid), (failure.Status, failure.Code));
        Assert.Empty((await ReturnPurchaseSourceAsync(run, receipt)).ReceiptAdjustments);
        await using var verify = run.Context(CompanyA);
        Assert.Empty(await verify.GoodsReceiptReversals.ToListAsync());
        Assert.Equal(3m, await LabelBlockKgAsync(run, receipt.LabelId, BlockState.Free));
    }

    [SqlServerFact]
    public async Task Explicit_return_source_quantities_must_match_the_owner_undo_witness()
    {
        await using var run = await Run.StartAsync();
        var receipt = await SeedInvoiceCostReceipt(run, null);
        await ApplyInvoiceCostCore(run, InvoiceCostEvent(receipt, 1m, 10m), new FirstCostPrerequisites());
        var preview = await ReturnSourcesAsync(run, receipt);
        var write = CombinedReversalWrite(receipt) with { IsSupplierReturn = true, OriginalUnitCostTryPerKg = "10",
            CostAllocation = new(preview.SourceVersion, new(false, "1", "1"), []),
            Request = CombinedReversalWrite(receipt).Request with { UndoQuantity = "1", MeasuredUndoQuantityKg = "1" } };
        await RunCombinedReversalHostAsync(run, write, GoodsReceiptPurchaseEnvelope(run, "B2-owner-proof", "B2-owner-proof"));
        var actualOwner = await ReturnPurchaseSourceAsync(run, receipt);
        var undo = Assert.Single(actualOwner.ReceiptAdjustments);
        await using var verify = run.Context(CompanyA);
        var reader = new InventorySupplierReturnCostSourceReader(verify, Company(CompanyA));
        var failure = await Assert.ThrowsAsync<InventorySupplierReturnExitException>(() => reader.GetAsync(CompanyA,
            receipt.AcceptanceId, actualOwner with { ReceiptAdjustments = [undo with { UndoQuantity = .5m }] }));
        Assert.Equal((503, InventorySupplierReturnExitCodesV1.ReceiptTraceInvalid), (failure.Status, failure.Code));
        var valid = await reader.GetAsync(CompanyA, receipt.AcceptanceId, actualOwner);
        Assert.Equal(0, valid.PendingUndoQuantity.Value().CompareTo(ExactCostRatio.Of(2m)));
        Assert.Single(await verify.GoodsReceiptReversals.ToListAsync());
        Assert.Equal(3m, await LabelBlockKgAsync(run, receipt.LabelId, BlockState.Free));
    }

    private static async Task<InventorySupplierReturnPurchaseSourceV1> ReturnPurchaseSourceAsync(Run run, InventoryGoodsReceiptRequestV1 receipt)
    {
        await using var scope = run.Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
        var owner = await scope.ServiceProvider.GetRequiredService<PurchaseSupplierReturnPurchaseSourceReader>()
            .ReadAsync(CompanyA, receipt.PurchaseOrderId, receipt.AcceptanceId);
        return new(owner.AcceptedQuantity, owner.SourceVersion,
            owner.ReceiptAdjustments.Select(x => new InventoryPurchaseInvoiceReceiptAdjustmentV1(x.AdjustmentId, x.UndoQuantity, x.IsSupplierReturn)).ToArray());
    }

    private static async Task<InventorySupplierReturnCostSourcesV1> ReturnSourcesAsync(Run run, InventoryGoodsReceiptRequestV1 receipt)
    {
        var owner = await ReturnPurchaseSourceAsync(run, receipt);
        await using var db = run.Context(CompanyA);
        return await new InventorySupplierReturnCostSourceReader(db, Company(CompanyA)).GetAsync(CompanyA, receipt.AcceptanceId, owner);
    }

    private static async Task<WorkInboxEventV1> InvoiceWithCurrentReturnEvidenceAsync(Run run, InventoryGoodsReceiptRequestV1 receipt,
        decimal matched, decimal net)
    {
        var owner = await ReturnPurchaseSourceAsync(run, receipt);
        var envelope = InvoiceCostEvent(receipt, matched, net);
        var payload = JsonSerializer.Deserialize<PurchaseInvoiceFinalizedPayloadV1>(envelope.PayloadJson, PurchaseCostJson)!;
        var line = Assert.Single(payload.Lines);
        var cost = line.GoodsCostSource!;
        var updated = cost with { Receipts = cost.Receipts.Select(x => x with { ReceiptAdjustments = owner.ReceiptAdjustments
            .Select(a => new PurchaseInvoiceReceiptAdjustmentV1(a.AdjustmentId, a.UndoQuantity, a.IsSupplierReturn)).ToArray() }).ToArray() };
        return envelope with { PayloadJson = JsonSerializer.Serialize(payload with { Lines = [line with { GoodsCostSource = updated }] }, PurchaseCostJson) };
    }
}
