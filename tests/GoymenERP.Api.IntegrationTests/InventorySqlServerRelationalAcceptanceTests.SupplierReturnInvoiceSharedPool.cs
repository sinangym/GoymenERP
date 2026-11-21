using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task A_split_after_an_explicit_return_makes_the_shared_pool_unreadable_without_stopping_the_products_movements()
    {
        await using var run = await Run.StartAsync();
        var receipt = await SeedInvoiceCostReceipt(run, null);
        await ApplyInvoiceCostCore(run, InvoiceCostEvent(receipt, 1m, 10m), new FirstCostPrerequisites());
        var preview = await ReturnSourcesAsync(run, receipt);
        var returned = CombinedReversalWrite(receipt) with
        {
            IsSupplierReturn = true, OriginalUnitCostTryPerKg = "10",
            CostAllocation = new PurchaseSupplierReturnCostAllocationV1(preview.SourceVersion,
                new PurchaseSupplierReturnSourceSelectionV1(false, "1", "1"), []),
            Request = CombinedReversalWrite(receipt).Request with { UndoQuantity = "1", MeasuredUndoQuantityKg = "1" },
        };
        await RunCombinedReversalHostAsync(run, returned, GoodsReceiptPurchaseEnvelope(run, "B2-split-pool", "B2-split-pool"));

        // The split breaks the sole-label lineage the supplier-return attribution needs (K3-REVIEW-B P1-2).
        var left = Guid.NewGuid();
        await RepackFirstFxAsync(run, InventoryLabelTransformationKindV1.Split, [receipt.LabelId], [(left, 1m), (Guid.NewGuid(), 2m)]);
        await using (var read = run.Context(CompanyA))
        {
            var failure = await Assert.ThrowsAsync<InventorySupplierReturnExitException>(() => ReturnSourcesAsync(run, receipt));
            Assert.Equal((503, InventorySupplierReturnExitCodesV1.ReceiptTraceInvalid), (failure.Status, failure.Code));
            var pool = await InventoryStore.ReadCostPoolAsync(read, Company(CompanyA), receipt.LocationId,
                receipt.ProductId, SettingsCurrencyIdentitiesV1.TurkishLira, default);
            Assert.False(pool.SourceReadable);
            Assert.False(pool.IsConsistent);
        }

        GoymenERP.Modules.Inventory.Domain.Labels.MaterialLabel label;
        await using (var owner = run.Store(CompanyA)) label = (await owner.Labels.FindLabelAsync(left))!;
        var shipment = Movement(CompanyA, StockMovementKind.Issue,
            new StockSourceReference(InventoryLabelDispositionReader.DefiniteShipmentSourceKind, Guid.NewGuid(), Guid.NewGuid()),
            Out(Position(CompanyA, receipt.LocationId, label, BlockState.Free), 1m));
        await using (var owner = run.Store(CompanyA)) await owner.Store.RecordMovementAsync(shipment, Metadata(run));

        await using var verify = run.Context(CompanyA);
        Assert.True(await verify.Movements.AnyAsync(x => x.Id == shipment.Id));
        Assert.All(await verify.MovementCostStamps.Where(x => x.MovementId == shipment.Id).ToListAsync(),
            stamp => Assert.Null(stamp.Amount));
        Assert.Equal(0m, await LabelBlockKgAsync(run, left, BlockState.Free));
    }

    [SqlServerFact]
    public async Task Two_invoice_lines_in_one_pool_after_an_explicit_return_commit_their_source_shares_atomically_and_replay()
    {
        await using var run = await Run.StartAsync();
        var first = await SeedInvoiceCostReceipt(run, null);
        var second = await SeedInvoiceCostReceipt(run, null);
        Assert.Equal((first.ProductId, first.LocationId), (second.ProductId, second.LocationId));
        var originalInvoice = InvoiceCostEvent(first, 1m, 10m);
        await ApplyInvoiceCostCore(run, originalInvoice, new FirstCostPrerequisites());
        var preview = await ReturnSourcesAsync(run, first);
        var returned = CombinedReversalWrite(first) with
        {
            IsSupplierReturn = true, OriginalUnitCostTryPerKg = "10",
            CostAllocation = new PurchaseSupplierReturnCostAllocationV1(preview.SourceVersion,
                new PurchaseSupplierReturnSourceSelectionV1(false, "1", "1"), []),
            Request = CombinedReversalWrite(first).Request with { UndoQuantity = "1", MeasuredUndoQuantityKg = "1" },
        };
        await RunCombinedReversalHostAsync(run, returned, GoodsReceiptPurchaseEnvelope(run, "B2-shared-return", "B2-shared-return"));
        var immutableHistory = await FirstCostZeroHistoryAsync(run, first.ProductId);
        var envelope = await InvoiceWithCurrentReturnEvidenceAsync(run, first, 2m, 20m);
        var firstPayload = JsonSerializer.Deserialize<PurchaseInvoiceFinalizedPayloadV1>(envelope.PayloadJson, PurchaseCostJson)!;
        var secondPayload = JsonSerializer.Deserialize<PurchaseInvoiceFinalizedPayloadV1>(
            InvoiceCostEvent(second, 4m, 40m).PayloadJson, PurchaseCostJson)!;
        var combined = firstPayload with
        {
            Net = 60m, Payable = 60m,
            Lines = [Assert.Single(firstPayload.Lines), Assert.Single(secondPayload.Lines) with { LineNo = 2 }],
        };
        envelope = envelope with { PayloadJson = JsonSerializer.Serialize(combined, PurchaseCostJson) };

        await Assert.ThrowsAsync<PurchaseInvoiceCostSaveFault>(() =>
            ApplyInvoiceCostCore(run, envelope, new FirstCostPrerequisites(), new RejectPurchaseInvoiceCostSave()));
        await using (var failed = run.Context(CompanyA))
        {
            Assert.Single(await failed.PurchaseInvoiceCostApplications.ToListAsync());
            Assert.Single(await failed.PurchaseInvoiceReceiptCostEffects.ToListAsync());
            var pool = await failed.AverageCostLedger.SingleAsync(x => x.ProductId == first.ProductId);
            Assert.Equal((1m, 10m), (pool.QuantityKg, pool.AverageCostPerKg));
            Assert.Single(await failed.GoodsReceiptReversals.ToListAsync());
        }
        Assert.Equal(immutableHistory, await FirstCostZeroHistoryAsync(run, first.ProductId));
        Assert.Equal(0, (await ReturnSourcesAsync(run, first)).PendingQuantityKg.Value().CompareTo(ExactCostRatio.Of(2m)));

        await ApplyInvoiceCostCore(run, envelope, new FirstCostPrerequisites());
        await ApplyInvoiceCostCore(run, envelope, new FirstCostPrerequisites());
        await using var verify = run.Context(CompanyA);
        Assert.Equal(2, await verify.PurchaseInvoiceCostApplications.CountAsync());
        Assert.Equal(3, await verify.PurchaseInvoiceReceiptCostEffects.CountAsync());
        Assert.Equal(2, await verify.PurchaseInvoiceReceiptCostEffects.CountAsync(x => x.SourceEventId == envelope.EventId));
        var application = await verify.PurchaseInvoiceCostApplications.SingleAsync(x => x.SourceEventId == envelope.EventId);
        var result = JsonSerializer.Deserialize<InventoryPurchaseInvoiceCostApplicationResultV1>(application.ResultJson, PurchaseCostJson)!;
        Assert.Equal(2, result.InitialRecognitions!.Count);
        Assert.Equal(0, result.InitialRecognitions.Aggregate(ExactCostRatio.Of(0m),
            (sum, x) => sum.Add(x.InvoiceTlValue.Value())).CompareTo(ExactCostRatio.Of(60m)));
        Assert.Equal(0, result.InitialRecognitions.Aggregate(ExactCostRatio.Of(0m),
            (sum, x) => sum.Add(x.InvoicedPhysicalKg.Value())).CompareTo(ExactCostRatio.Of(6m)));
        Assert.All(result.InitialRecognitions, x => Assert.Equal(0, x.ConsumedQuantityKg.Value().Sign));
        var exact = await InventoryStore.ReadCostPoolAsync(verify, Company(CompanyA), first.LocationId,
            first.ProductId, SettingsCurrencyIdentitiesV1.TurkishLira, default);
        Assert.True(exact.IsConsistent);
        Assert.True(exact.IsComplete);
        Assert.Equal(0, exact.ExactPosition!.QuantityKg.CompareTo(ExactCostRatio.Of(7m)));
        Assert.Equal(0, exact.ExactPosition.Value.CompareTo(ExactCostRatio.Of(70m)));
        Assert.Equal(3m, await LabelBlockKgAsync(run, first.LabelId, BlockState.Free));
        Assert.Equal(4m, await LabelBlockKgAsync(run, second.LabelId, BlockState.Free));
        Assert.Equal(immutableHistory, await FirstCostZeroHistoryAsync(run, first.ProductId));
    }
}
