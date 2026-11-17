using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task First_purchase_cost_for_material_never_shipped_does_not_fabricate_a_zero_SMM_claim()
    {
        await using var run = await Run.StartAsync();
        var receipt = await SeedInvoiceCostReceipt(run, null);
        await ApplyInvoiceCostCore(run, InvoiceCostEvent(receipt, 4m, 60m), new FirstCostPrerequisites());

        await using var verify = run.Context(CompanyA);
        var application = Assert.Single(await verify.PurchaseInvoiceCostApplications.ToListAsync());
        var result = JsonSerializer.Deserialize<InventoryPurchaseInvoiceCostApplicationResultV1>(application.ResultJson, PurchaseCostJson)!;
        Assert.Null(result.CostDifferenceEventId);
        var recognition = Assert.Single(result.InitialRecognitions!);
        var physical = JsonSerializer.Deserialize<InventoryPurchaseFirstCostPhysicalSourceV1>(
            Assert.Single(recognition.Receipts).PhysicalSourceJson, PurchaseCostJson)!;
        Assert.Empty(physical.Shipments);
        Assert.Equal(0, recognition.ConsumedExactTlAmount.Value().Sign);
        Assert.False(await verify.OutboxMessages.AnyAsync(row => row.EventType == InventoryPurchaseInvoiceCostDifferenceV1.EventType));
    }

    [SqlServerFact]
    public async Task First_purchase_cost_after_a_full_real_customer_return_emits_one_zero_claim_with_unchanged_physical_history()
    {
        await using var run = await Run.StartAsync();
        var source = await ArrangeFullyReturnedUnpricedPurchaseReceiptAsync(run);
        var before = await FirstCostZeroHistoryAsync(run, source.Receipt.ProductId);
        var envelope = InvoiceCostEvent(source.Receipt, 4m, 60m);

        await ApplyInvoiceCostCore(run, envelope, new FirstCostPrerequisites());
        await ApplyInvoiceCostCore(run, envelope, new FirstCostPrerequisites());

        await using var verify = run.Context(CompanyA);
        var application = Assert.Single(await verify.PurchaseInvoiceCostApplications.ToListAsync());
        var message = Assert.Single(await verify.OutboxMessages
            .Where(row => row.EventType == InventoryPurchaseInvoiceCostDifferenceV1.EventType).ToListAsync());
        Assert.Equal(application.ResultJson, message.PayloadJson);
        var result = JsonSerializer.Deserialize<InventoryPurchaseInvoiceCostApplicationResultV1>(message.PayloadJson, PurchaseCostJson)!;
        Assert.Equal((CompanyA, envelope.EventId), (result.CompanyId, result.SourceEventId));
        Assert.Equal(message.Id, result.CostDifferenceEventId);
        Assert.Equal(0, result.UnabsorbedTlDifference.Value().Sign);
        var recognition = Assert.Single(result.InitialRecognitions!);
        Assert.Equal((0m, 0, 0, 0), (recognition.ConsumedTlAmount, recognition.ConsumedQuantityKg.Value().Sign,
            recognition.ConsumedExactTlAmount.Value().Sign, recognition.ConsumedRoundingResidue.Value().Sign));
        Assert.Equal((SettingsCurrencyIdentitiesV1.TurkishLira, 2), (recognition.CurrencyId, recognition.MoneyScale));
        Assert.NotEqual(Guid.Empty, recognition.MoneyProfileId);
        Assert.Equal(1, recognition.MoneyProfileVersion);
        Assert.Equal(0, Assert.Single(recognition.Positions).TlValue.Value().CompareTo(ExactCostRatio.Of(60m)));
        var receipt = Assert.Single(recognition.Receipts);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(receipt.PhysicalSourceJson))), receipt.PhysicalSourceSha256);
        var physical = JsonSerializer.Deserialize<InventoryPurchaseFirstCostPhysicalSourceV1>(receipt.PhysicalSourceJson, PurchaseCostJson)!;
        Assert.Equal((CompanyA, source.Receipt.MovementId, source.Receipt.LabelId),
            (physical.CompanyId, physical.ReceiptMovementId, physical.RootLabelId));
        Assert.Equal(0, physical.NetShippedRootQuantityKg.Value().Sign);
        var shipment = Assert.Single(physical.Shipments);
        Assert.Equal((source.Shipment.Source.Id, source.Shipment.Id, source.Shipment.Source.LineId!.Value, 4m),
            (shipment.ShipmentId, shipment.MovementId, shipment.LoadingInstructionLineId, shipment.PhysicalQuantityKg));
        Assert.Equal(0, shipment.NetRootQuantityKg.Value().Sign);
        var returned = Assert.Single(shipment.Returns);
        Assert.Equal((source.Return.CustomerReturnId, source.Return.InventoryMovementId, source.Shipment.Id, 4m),
            (returned.ReturnId, returned.MovementId, returned.SourceIssueMovementId, returned.QuantityKg));
        Assert.Equal(before, await FirstCostZeroHistoryAsync(run, source.Receipt.ProductId));
        Assert.Equal(4m, await verify.Balances.Where(row => row.LabelId == source.Receipt.LabelId).SumAsync(row => row.QuantityKg));
        Assert.Single(await verify.PurchaseInvoiceReceiptCostEffects.ToListAsync());
    }

    [SqlServerFact]
    public async Task A_failed_first_purchase_zero_claim_save_rolls_back_the_application_pool_fence_and_outbox()
    {
        await using var run = await Run.StartAsync();
        var source = await ArrangeFullyReturnedUnpricedPurchaseReceiptAsync(run);
        var before = await FirstCostZeroHistoryAsync(run, source.Receipt.ProductId);
        long version;
        string pool;
        await using (var original = run.Context(CompanyA))
        {
            version = (await original.Labels.SingleAsync(row => row.Id == source.Receipt.LabelId)).Version;
            pool = JsonSerializer.Serialize(await original.AverageCostLedger.AsNoTracking()
                .Where(row => row.ProductId == source.Receipt.ProductId).ToListAsync(), PurchaseCostJson);
        }

        var envelope = InvoiceCostEvent(source.Receipt, 4m, 60m);
        await Assert.ThrowsAsync<PurchaseInvoiceCostSaveFault>(() => ApplyInvoiceCostCore(run, envelope,
            new FirstCostPrerequisites(), new RejectPurchaseInvoiceCostSave()));

        await using (var verify = run.Context(CompanyA))
        {
            Assert.False(await verify.PurchaseInvoiceCostApplications.AnyAsync());
            Assert.False(await verify.PurchaseInvoiceReceiptCostEffects.AnyAsync());
            Assert.False(await verify.OutboxMessages.AnyAsync(row => row.EventType == InventoryPurchaseInvoiceCostDifferenceV1.EventType));
            Assert.Equal(version, (await verify.Labels.SingleAsync(row => row.Id == source.Receipt.LabelId)).Version);
            Assert.Equal(pool, JsonSerializer.Serialize(await verify.AverageCostLedger.AsNoTracking()
                .Where(row => row.ProductId == source.Receipt.ProductId).ToListAsync(), PurchaseCostJson));
        }
        Assert.Equal(before, await FirstCostZeroHistoryAsync(run, source.Receipt.ProductId));
        await ApplyInvoiceCostCore(run, envelope, new FirstCostPrerequisites());
        await using var final = run.Context(CompanyA);
        Assert.Single(await final.PurchaseInvoiceCostApplications.ToListAsync());
        Assert.Single(await final.OutboxMessages.Where(row => row.EventType == InventoryPurchaseInvoiceCostDifferenceV1.EventType).ToListAsync());
    }

    private static async Task<(InventoryGoodsReceiptRequestV1 Receipt, StockMovement Shipment, InventoryCustomerReturnResultV1 Return)>
        ArrangeFullyReturnedUnpricedPurchaseReceiptAsync(Run run)
    {
        var receipt = await SeedInvoiceCostReceipt(run, null);
        GoymenERP.Modules.Inventory.Domain.Labels.MaterialLabel label;
        await using (var owner = run.Store(CompanyA)) label = (await owner.Labels.FindLabelAsync(receipt.LabelId))!;
        var shipment = new StockMovement(Guid.NewGuid(), CompanyA, StockMovementKind.Issue, GoodsReceiptTime.AddMinutes(10),
            new(InventoryLabelDispositionReader.DefiniteShipmentSourceKind, Guid.NewGuid(), Guid.NewGuid()),
            [Out(Position(CompanyA, receipt.LocationId, label, BlockState.Free), 4m)]);
        await using (var owner = run.Store(CompanyA)) await owner.Store.RecordMovementAsync(shipment, Metadata(run));
        var returned = await CommitReturnAsync(run, new InventoryCustomerReturnRequestV1(CompanyA, Guid.NewGuid(),
            shipment.Source.Id, shipment.Source.LineId!.Value, Guid.NewGuid(), "4", GoodsReceiptTime.AddMinutes(20)), Metadata(run));
        return (receipt, shipment, returned);
    }

    private static async Task<string> FirstCostZeroHistoryAsync(Run run, Guid productId)
    {
        await using var verify = run.Context(CompanyA);
        // Actual movement and issue-time stamps remain immutable; first recognition only adds its separate cost effect.
        var lines = await verify.MovementLines.AsNoTracking().Where(row => row.ProductId == productId)
            .OrderBy(row => row.MovementId).ThenBy(row => row.LineNumber).ToListAsync();
        var movementIds = lines.Select(row => row.MovementId).Distinct().ToArray();
        return JsonSerializer.Serialize(new
        {
            Movements = await verify.Movements.AsNoTracking().Where(row => movementIds.Contains(row.Id)).OrderBy(row => row.Id).ToListAsync(),
            Lines = lines,
            Stamps = await verify.MovementCostStamps.AsNoTracking().Where(row => movementIds.Contains(row.MovementId))
                .OrderBy(row => row.MovementId).ThenBy(row => row.LineNumber).ThenBy(row => row.CurrencyId).ToListAsync()
        }, PurchaseCostJson);
    }
}
