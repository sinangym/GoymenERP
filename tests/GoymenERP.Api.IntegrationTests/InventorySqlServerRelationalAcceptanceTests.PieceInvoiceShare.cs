using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// AK-3b (ALS-D121/D122/D124; golden ADET_KG_FORMUL_GOLDEN_V1_EK1 §5, E29) on real SQL Server: the invoice cost application's
/// kilogram share of a piece receipt with the switch on (three decimals, the last invoice takes the remainder, each invoice sees
/// the earlier ones' kilograms) and, pinned by parameter, with it off (the exact earlier share). SENTETIK owners.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    /// <summary>A piece receipt of 3 pieces (or <paramref name="pieces"/>) weighing 4 kg, stamped at 10 TL/kg (the receipt's own known cost).</summary>
    private static async Task<InventoryGoodsReceiptRequestV1> SeedPieceInvoiceCostReceipt(Run run, int pieces = 3)
    {
        var seed = await SeedGoodsReceiptPurchaseOrderAsync(run);
        var receipt = Receipt(seed, Guid.NewGuid(), "4", await run.LocationOfAsync(CompanyA)) with { InitialBlock = BlockState.Free, PieceCount = pieces };
        var receiptKey = $"SENTETIK-piece-cost-receipt-{receipt.AcceptanceId:N}";
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString); await connection.OpenAsync();
        await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
        await using var purchase = await PurchaseAcceptanceParticipant.EnlistAsync(connection, transaction, CompanyA);
        await using var inventory = await InventoryGoodsReceiptParticipant.EnlistAsync(connection, transaction, CompanyA,
            GoodsReceiptSettings(run), costSource: new SyntheticInvoiceReceiptCost(10m));
        var accepted = await purchase.StageAsync(GoodsReceiptPurchaseRequest(seed, receipt.AcceptanceId, "4"),
            GoodsReceiptPurchaseEnvelope(run, receiptKey, run.Correlation));
        receipt = receipt with { PurchaseOrderVersionAfter = accepted.PurchaseOrderVersion };
        await inventory.StageAsync(receipt, ReceiptMetadata(run, receiptKey, run.Correlation));
        await transaction.CommitAsync();
        return receipt;
    }

    /// <summary>One piece of the three invoiced; the share names the kilograms weighed for the whole acceptance (ALS-D121).</summary>
    private static WorkInboxEventV1 PieceInvoiceCostEvent(InventoryGoodsReceiptRequestV1 receipt, decimal net)
    {
        var source = new PurchaseInvoiceFinalizedPayloadV1(Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(),
            SettingsCurrencyIdentitiesV1.TurkishLira, null, net, null, CompanyA, net, 0m, 0m, null, null, null,
            [new(Guid.NewGuid(), 1, "Steel", "resale", 0m, net, 0m, 0m, null, null, null,
                GoodsCostSource: new(receipt.ProductId, receipt.PurchaseUnitId, 1m,
                [new(receipt.AcceptanceId, receipt.PurchaseOrderId, receipt.PurchaseOrderLineId,
                    long.Parse(receipt.PurchaseOrderVersionAfter, CultureInfo.InvariantCulture), 1m, 3m, MeasuredQuantityKg: 4m)]))],
            new(2026, 10, 1), "SENTETIK-finalizer", OfficialDocument: new(Guid.NewGuid(), 1, new string('a', 64), Guid.NewGuid()));
        return new(PurchaseInvoiceFinalizedEventV1.SourceName, Guid.NewGuid(), CompanyA, PurchaseInvoiceFinalizedEventV1.EventType,
            PurchaseInvoiceFinalizedEventV1.SchemaVersion, JsonSerializer.Serialize(source, PurchaseCostJson),
            GoodsReceiptTime.AddDays(7), "SENTETIK-piece-invoice-cost", null);
    }

    private static async Task ApplyPieceInvoiceCost(Run run, WorkInboxEventV1 envelope, bool remainingShareAccepted)
    {
        await using var scope = run.Factory.Services.CreateAsyncScope();
        var company = scope.ServiceProvider.GetRequiredService<ICompanyContext>(); company.Set(CompanyA);
        await using var db = new InventoryDbContext(
            new DbContextOptionsBuilder<InventoryDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString).Options, company);
        var store = new InventoryPurchaseInvoiceCostStoreV1(db, company, scope.ServiceProvider.GetRequiredService<IAuditEventCollector>(),
            scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>(), TimeProvider.System, null, remainingShareAccepted);
        await store.ApplyAsync(envelope);
    }

    private static async Task<ExactCostRatio[]> InvoicedPieceKilogramsAsync(Run run, Guid acceptanceId)
    {
        await using var verify = run.Context(CompanyA);
        var effects = await verify.PurchaseInvoiceReceiptCostEffects.Where(row => row.AcceptanceId == acceptanceId).ToListAsync();
        return [.. effects.Select(effect => JsonSerializer.Deserialize<InventoryPurchaseInvoiceCostReceiptResultV1>(effect.ResultJson, PurchaseCostJson)!
            .InvoicedKg.Value())];
    }

    [SqlServerFact]
    public async Task E29_three_invoices_of_a_piece_receipt_take_rounded_shares_and_the_last_one_takes_the_remaining_kilograms()
    {
        await using var run = await Run.StartAsync();
        var receipt = await SeedPieceInvoiceCostReceipt(run);

        // 4 kg over 3 pieces: 1,333 / 1,333 / 1,334 (the last invoice takes the remainder), each at the receipt's own 10 TL/kg.
        await ApplyPieceInvoiceCost(run, PieceInvoiceCostEvent(receipt, 13.33m), remainingShareAccepted: true);
        await ApplyPieceInvoiceCost(run, PieceInvoiceCostEvent(receipt, 13.33m), remainingShareAccepted: true);
        await ApplyPieceInvoiceCost(run, PieceInvoiceCostEvent(receipt, 13.34m), remainingShareAccepted: true);

        var shares = await InvoicedPieceKilogramsAsync(run, receipt.AcceptanceId);
        Assert.Equal([1.333m, 1.333m, 1.334m], shares.Select(share => share.Round(6)).Order());
        Assert.Equal(0, shares.Aggregate(ExactCostRatio.Of(0m), (sum, share) => sum.Add(share)).CompareTo(ExactCostRatio.Of(4m)));
        await using var verify = run.Context(CompanyA);
        // Each invoice value equals what the receipt had recognised for its share: the average stays at 10 TL/kg and no stock moved.
        var current = await verify.AverageCostLedger.SingleAsync(row => row.ProductId == receipt.ProductId);
        Assert.Equal((4m, 10m), (current.QuantityKg, current.AverageCostPerKg));
        Assert.Equal(4m, await verify.Balances.Where(row => row.LabelId == receipt.LabelId).SumAsync(row => row.QuantityKg));
        Assert.Equal(3, await verify.PurchaseInvoiceCostApplications.CountAsync());
    }

    [SqlServerFact]
    public async Task With_the_remaining_share_switch_closed_a_piece_invoice_keeps_the_exact_unrounded_share()
    {
        await using var run = await Run.StartAsync();
        var receipt = await SeedPieceInvoiceCostReceipt(run);

        await ApplyPieceInvoiceCost(run, PieceInvoiceCostEvent(receipt, 13.33m), remainingShareAccepted: false);

        var share = Assert.Single(await InvoicedPieceKilogramsAsync(run, receipt.AcceptanceId));
        // 4 x 1 / 3 kg exactly: a rational that no decimal holds.
        Assert.False(share.TryToDecimal(out _));
        Assert.Equal(0, share.Multiply(3m).CompareTo(ExactCostRatio.Of(4m)));
    }

    [SqlServerFact]
    public async Task A_piece_invoice_whose_weighed_kilograms_or_piece_count_differ_from_the_receipt_is_refused()
    {
        await using var run = await Run.StartAsync();
        var receipt = await SeedPieceInvoiceCostReceipt(run);
        var envelope = PieceInvoiceCostEvent(receipt, 13.33m);
        var wrongKg = envelope with { PayloadJson = envelope.PayloadJson.Replace("\"measuredQuantityKg\":4", "\"measuredQuantityKg\":5", StringComparison.Ordinal) };

        var refused = await Assert.ThrowsAsync<OutboxPermanentFailureException>(() => ApplyPieceInvoiceCost(run, wrongKg, remainingShareAccepted: true));

        Assert.Equal("purchase_invoice_cost_receipt_source_conflict", refused.Code);
        await using var verify = run.Context(CompanyA);
        Assert.Empty(await verify.PurchaseInvoiceCostApplications.ToListAsync());
    }
}
