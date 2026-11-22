using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// ALS-D122 / ALS-D123 / ALS-D124 (golden ADET_KG_FORMUL_GOLDEN_V1_EK1 §5, E26–E28) on real SQL Server: after a reversal that did
/// not leave in the receipt's own kg-per-piece proportion, each invoice of a piece receipt takes the CURRENT remaining kg × invoiced
/// pieces ÷ remaining pieces (returns and earlier invoices deducted), three decimals half away from zero, and the last invoice the
/// whole remaining kg. With the switch closed the same source waits (<c>purchase_invoice_cost_piece_reversal_basis_unavailable</c>);
/// when earlier invoices and returns leave no kilograms for the remaining pieces the source waits
/// (<c>purchase_invoice_cost_piece_remaining_share_unavailable</c>). SENTETIK owners.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task After_a_non_proportional_reversal_piece_invoices_take_the_current_remaining_share_and_the_last_takes_the_rest()
    {
        await using var run = await Run.StartAsync();
        // 4 pieces / 4 kg; one piece leaves weighing 0,9 kg: 3 pieces / 3,1 kg remain (not the receipt's 1 kg per piece).
        var receipt = await SeedPieceInvoiceCostReceipt(run, pieces: 4);
        var reversal = await StagePieceReceiptReversalAsync(run, receipt,
            ReceiptReversalRequest(receipt) with { QuantityKg = "0.9", PieceCount = 1 }, SupplierReturnMetadata(run));
        var adjustment = new PurchaseInvoiceReceiptAdjustmentV1(reversal.ReversalId, 1m, false);

        // 3,1 × 1 / 3 = 1,0333… -> 1,033; then 2,067 × 1 / 2 = 1,0335 (half) -> 1,034; the last takes 1,033 (E28's shape).
        await ApplyPieceInvoiceCost(run, RemainingShareEvent(receipt, 4m, adjustment, 10.33m), remainingShareAccepted: true);
        await ApplyPieceInvoiceCost(run, RemainingShareEvent(receipt, 4m, adjustment, 10.34m), remainingShareAccepted: true);
        await ApplyPieceInvoiceCost(run, RemainingShareEvent(receipt, 4m, adjustment, 10.33m), remainingShareAccepted: true);

        var shares = await InvoicedPieceKilogramsInOrderAsync(run, receipt.AcceptanceId);
        Assert.Equal([1.033m, 1.034m, 1.033m], shares.Select(share => share.Round(6)));
        Assert.Equal(0, shares.Aggregate(ExactCostRatio.Of(0m), (sum, share) => sum.Add(share)).CompareTo(ExactCostRatio.Of(3.1m)));
        await using var verify = run.Context(CompanyA);
        Assert.Equal(3, await verify.PurchaseInvoiceCostApplications.CountAsync());
        Assert.Equal(3.1m, await verify.Balances.Where(row => row.LabelId == receipt.LabelId).SumAsync(row => row.QuantityKg));
    }

    [SqlServerFact]
    public async Task With_the_remaining_share_switch_closed_a_piece_invoice_after_a_non_proportional_reversal_waits_and_writes_nothing()
    {
        await using var run = await Run.StartAsync();
        var receipt = await SeedPieceInvoiceCostReceipt(run, pieces: 4);
        var reversal = await StagePieceReceiptReversalAsync(run, receipt,
            ReceiptReversalRequest(receipt) with { QuantityKg = "0.9", PieceCount = 1 }, SupplierReturnMetadata(run));

        var waiting = await Assert.ThrowsAsync<InvalidOperationException>(() => ApplyPieceInvoiceCost(run,
            RemainingShareEvent(receipt, 4m, new(reversal.ReversalId, 1m, false), 10.33m), remainingShareAccepted: false));

        Assert.Equal("purchase_invoice_cost_piece_reversal_basis_unavailable", waiting.Message);
        await using var verify = run.Context(CompanyA);
        Assert.Empty(await verify.PurchaseInvoiceCostApplications.ToListAsync());
        Assert.Empty(await verify.PurchaseInvoiceReceiptCostEffects.ToListAsync());
    }

    [SqlServerFact]
    public async Task A_piece_invoice_with_no_kilograms_left_after_an_earlier_invoice_and_a_reversal_waits_and_writes_nothing()
    {
        await using var run = await Run.StartAsync();
        // 4 pieces / 4 kg; invoice 1 takes 2 pieces before any return: 4 × 2 / 4 = 2 kg (ALS-D121).
        var receipt = await SeedPieceInvoiceCostReceipt(run, pieces: 4);
        await ApplyPieceInvoiceCost(run, RemainingShareEvent(receipt, 4m, null, 20m, matched: 2m), remainingShareAccepted: true);
        // One piece then leaves weighing 2 kg: the receipt keeps 3 pieces / 2 kg, the earlier invoice already took 2 kg.
        var reversal = await StagePieceReceiptReversalAsync(run, receipt,
            ReceiptReversalRequest(receipt) with { QuantityKg = "2", PieceCount = 1 }, SupplierReturnMetadata(run));

        // Current remaining (ALS-D123): 2 − 2 = 0 kg for 1 piece — no share can be derived, none is estimated.
        var waiting = await Assert.ThrowsAsync<InvalidOperationException>(() => ApplyPieceInvoiceCost(run,
            RemainingShareEvent(receipt, 4m, new(reversal.ReversalId, 1m, false), 1m), remainingShareAccepted: true));

        Assert.Equal("purchase_invoice_cost_piece_remaining_share_unavailable", waiting.Message);
        await using var verify = run.Context(CompanyA);
        Assert.Equal(1, await verify.PurchaseInvoiceCostApplications.CountAsync());
        Assert.Equal(1, await verify.PurchaseInvoiceReceiptCostEffects.CountAsync());
    }

    /// <summary>One piece of <paramref name="acceptedPieces"/> invoiced, naming the reversal Purchasing recorded for the acceptance.</summary>
    private static WorkInboxEventV1 RemainingShareEvent(InventoryGoodsReceiptRequestV1 receipt, decimal acceptedPieces,
        PurchaseInvoiceReceiptAdjustmentV1? adjustment, decimal net, decimal matched = 1m)
    {
        var source = new PurchaseInvoiceFinalizedPayloadV1(Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(),
            SettingsCurrencyIdentitiesV1.TurkishLira, null, net, null, CompanyA, net, 0m, 0m, null, null, null,
            [new(Guid.NewGuid(), 1, "Steel", "resale", 0m, net, 0m, 0m, null, null, null,
                GoodsCostSource: new(receipt.ProductId, receipt.PurchaseUnitId, matched,
                [new(receipt.AcceptanceId, receipt.PurchaseOrderId, receipt.PurchaseOrderLineId,
                    long.Parse(receipt.PurchaseOrderVersionAfter, CultureInfo.InvariantCulture), matched, acceptedPieces,
                    ReceiptAdjustments: adjustment is null ? null : [adjustment], MeasuredQuantityKg: 4m)]))],
            new(2026, 10, 1), "SENTETIK-finalizer", OfficialDocument: new(Guid.NewGuid(), 1, new string('a', 64), Guid.NewGuid()));
        return new(PurchaseInvoiceFinalizedEventV1.SourceName, Guid.NewGuid(), CompanyA, PurchaseInvoiceFinalizedEventV1.EventType,
            PurchaseInvoiceFinalizedEventV1.SchemaVersion, JsonSerializer.Serialize(source, PurchaseCostJson),
            GoodsReceiptTime.AddDays(7), "SENTETIK-piece-remaining-share", null);
    }

    /// <summary>The kilograms each invoice took, in the order the invoices were applied.</summary>
    private static async Task<ExactCostRatio[]> InvoicedPieceKilogramsInOrderAsync(Run run, Guid acceptanceId)
    {
        await using var verify = run.Context(CompanyA);
        var order = await verify.PurchaseInvoiceCostApplications.OrderBy(row => row.RecordedAtUtc)
            .Select(row => row.SourceEventId).ToListAsync();
        var effects = await verify.PurchaseInvoiceReceiptCostEffects.Where(row => row.AcceptanceId == acceptanceId).ToListAsync();
        return [.. order.Select(id => effects.Single(effect => effect.SourceEventId == id)).Select(effect =>
            JsonSerializer.Deserialize<InventoryPurchaseInvoiceCostReceiptResultV1>(effect.ResultJson, PurchaseCostJson)!.InvoicedKg.Value())];
    }
}
