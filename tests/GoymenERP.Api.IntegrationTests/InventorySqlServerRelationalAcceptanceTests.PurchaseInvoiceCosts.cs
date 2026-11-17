using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    private static readonly JsonSerializerOptions PurchaseCostJson = new(JsonSerializerDefaults.Web);

    [SqlServerFact]
    public async Task Purchase_invoice_cost_Down_allows_empty_storage_and_refuses_after_an_actual_invoice_application()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddPurchaseInvoiceCostApplications().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);
        var receipt = await SeedInvoiceCostReceipt(run, 10m);
        await ApplyInvoiceCost(run, InvoiceCostEvent(receipt, 4m, 60m));
        var refusal = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51926, refusal.Number);
        await using var verify = run.Context(CompanyA);
        Assert.Single(await verify.PurchaseInvoiceCostApplications.ToListAsync());
        Assert.Single(await verify.PurchaseInvoiceReceiptCostEffects.ToListAsync());
    }

    [SqlServerFact]
    public async Task The_database_refuses_rewriting_or_deleting_applied_invoice_cost_source_and_receipt_effects()
    {
        await using var run = await Run.StartAsync();
        var receipt = await SeedInvoiceCostReceipt(run, 10m);
        await ApplyInvoiceCost(run, InvoiceCostEvent(receipt, 4m, 60m));
        var sources = new[] { (Table: "PurchaseInvoiceCostApplications", Number: 51927),
            (Table: "PurchaseInvoiceReceiptCostEffects", Number: 51928) };
        foreach (var source in sources)
        {
            // Fixed test-owned identifiers, never client input. These commands exercise the SQL immutability guard.
            var update = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
                $"UPDATE [inventory].[{source.Table}] SET [ResultJson] = N'{{}}';"));
            Assert.Equal(source.Number, update.Number);
            var delete = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync($"DELETE FROM [inventory].[{source.Table}];"));
            // The parent delete is also blocked by the required receipt-effect FK before an AFTER trigger runs.
            Assert.Equal(source.Table == "PurchaseInvoiceCostApplications" ? 547 : source.Number, delete.Number);
        }
        await using var verify = run.Context(CompanyA);
        Assert.Single(await verify.PurchaseInvoiceCostApplications.ToListAsync());
        Assert.Single(await verify.PurchaseInvoiceReceiptCostEffects.ToListAsync());
        Assert.Equal(15m, (await verify.AverageCostLedger.SingleAsync(row => row.ProductId == receipt.ProductId)).AverageCostPerKg);
    }

    [SqlServerFact]
    public async Task A_late_invoice_changes_the_current_average_once_without_rewriting_the_receipt_or_physical_facts()
    {
        await using var run = await Run.StartAsync();
        var receipt = await SeedInvoiceCostReceipt(run, 10m);
        var envelope = InvoiceCostEvent(receipt, 4m, 60m);
        await ApplyInvoiceCost(run, envelope);
        await ApplyInvoiceCost(run, envelope);
        await using var verify = run.Context(CompanyA);
        var current = await verify.AverageCostLedger.SingleAsync(row => row.ProductId == receipt.ProductId);
        Assert.Equal((4m, 15m), (current.QuantityKg, current.AverageCostPerKg));
        var stamp = await verify.MovementCostStamps.SingleAsync(row => row.MovementId == receipt.MovementId
            && row.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira);
        Assert.Equal(10m, stamp.UnitCostPerKg);
        Assert.Equal(4m, await verify.Balances.Where(row => row.LabelId == receipt.LabelId).SumAsync(row => row.QuantityKg));
        Assert.Single(await verify.PurchaseInvoiceCostApplications.ToListAsync());
        Assert.Single(await verify.PurchaseInvoiceReceiptCostEffects.ToListAsync());
        Assert.False(await verify.OutboxMessages.AnyAsync(row => row.EventType == InventoryPurchaseInvoiceCostDifferenceV1.EventType));
        var changed = envelope with { PayloadJson = envelope.PayloadJson.Replace("\"net\":60", "\"net\":61", StringComparison.Ordinal) };
        await Assert.ThrowsAsync<OutboxPermanentFailureException>(() => ApplyInvoiceCost(run, changed));
    }

    [SqlServerFact]
    public async Task A_late_invoice_after_shipment_keeps_the_issue_and_emits_the_exact_unabsorbed_SMM_source_once()
    {
        await using var run = await Run.StartAsync();
        var receipt = await SeedInvoiceCostReceipt(run, 10m);
        GoymenERP.Modules.Inventory.Domain.Labels.MaterialLabel label;
        await using (var owner = run.Store(CompanyA)) label = (await owner.Labels.FindLabelAsync(receipt.LabelId))!;
        var issue = Movement(CompanyA, StockMovementKind.Issue,
            new StockSourceReference(InventoryLabelDispositionReader.DefiniteShipmentSourceKind, Guid.NewGuid(), Guid.NewGuid()),
            Out(Position(CompanyA, receipt.LocationId, label, BlockState.Free), 4m));
        await using (var owner = run.Store(CompanyA)) await owner.Store.RecordMovementAsync(issue, Metadata(run));
        var envelope = InvoiceCostEvent(receipt, 4m, 60m);
        await ApplyInvoiceCost(run, envelope);
        await ApplyInvoiceCost(run, envelope);
        await using var verify = run.Context(CompanyA);
        var message = await verify.OutboxMessages.SingleAsync(row => row.EventType == InventoryPurchaseInvoiceCostDifferenceV1.EventType);
        var result = JsonSerializer.Deserialize<InventoryPurchaseInvoiceCostApplicationResultV1>(message.PayloadJson, PurchaseCostJson)!;
        Assert.Equal(message.Id, result.CostDifferenceEventId);
        Assert.Equal(0, result.UnabsorbedTlDifference.Value().CompareTo(ExactCostRatio.Of(20m)));
        Assert.Equal(0, Assert.Single(result.Positions).RecordedPoolValueEffect.Value().Sign);
        var oldIssue = await verify.MovementCostStamps.SingleAsync(row => row.MovementId == issue.Id
            && row.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira);
        Assert.Equal(40m, oldIssue.Amount);
        Assert.Equal(2, await verify.Movements.CountAsync(row => row.Id == receipt.MovementId || row.Id == issue.Id));
    }

    [SqlServerFact]
    public async Task Partial_invoices_cannot_value_more_than_the_immutable_accepted_quantity()
    {
        await using var run = await Run.StartAsync();
        var receipt = await SeedInvoiceCostReceipt(run, 10m);
        await ApplyInvoiceCost(run, InvoiceCostEvent(receipt, 2m, 30m));
        var refused = await Assert.ThrowsAsync<OutboxPermanentFailureException>(() => ApplyInvoiceCost(run, InvoiceCostEvent(receipt, 3m, 45m)));
        Assert.Equal("purchase_invoice_cost_receipt_share_conflict", refused.Code);
        await using var verify = run.Context(CompanyA);
        Assert.Single(await verify.PurchaseInvoiceCostApplications.ToListAsync());
        Assert.Equal(12.5m, (await verify.AverageCostLedger.SingleAsync(row => row.ProductId == receipt.ProductId)).AverageCostPerKg);
    }

    [SqlServerFact]
    public async Task Concurrent_partial_invoices_share_one_acceptance_cap_and_cannot_both_commit_an_excess()
    {
        await using var run = await Run.StartAsync();
        var receipt = await SeedInvoiceCostReceipt(run, 10m);
        var events = new[] { InvoiceCostEvent(receipt, 2.5m, 37.5m), InvoiceCostEvent(receipt, 2.5m, 37.5m) };
        var failures = await Task.WhenAll(events.Select(envelope => Record.ExceptionAsync(() => ApplyInvoiceCost(run, envelope))));
        Assert.Single(failures, failure => failure is null);
        var failedIndex = Array.FindIndex(failures, failure => failure is not null);
        // A SQL deadlock loser is retried by Work; after the other commit it must see the same acceptance cap.
        var refusal = await Assert.ThrowsAsync<OutboxPermanentFailureException>(() => ApplyInvoiceCost(run, events[failedIndex]));
        Assert.Equal("purchase_invoice_cost_receipt_share_conflict", refusal.Code);
        await using var verify = run.Context(CompanyA);
        Assert.Single(await verify.PurchaseInvoiceCostApplications.ToListAsync());
        Assert.Equal(2.5m, (await verify.PurchaseInvoiceReceiptCostEffects.SingleAsync()).MatchedQuantity);
        Assert.Equal(13.125m, (await verify.AverageCostLedger.SingleAsync(row => row.ProductId == receipt.ProductId)).AverageCostPerKg);
    }

    [SqlServerFact]
    public async Task An_unknown_receipt_cost_does_not_become_a_zero_cost_late_difference()
    {
        await using var run = await Run.StartAsync();
        var receipt = await SeedInvoiceCostReceipt(run, null);
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => ApplyInvoiceCost(run, InvoiceCostEvent(receipt, 4m, 60m)));
        Assert.Equal("purchase_invoice_cost_first_recognition_pending", refused.Message);
        await using var verify = run.Context(CompanyA);
        Assert.False(await verify.PurchaseInvoiceCostApplications.AnyAsync());
        Assert.False(await verify.PurchaseInvoiceReceiptCostEffects.AnyAsync());
        Assert.False(await verify.OutboxMessages.AnyAsync(row => row.EventType == InventoryPurchaseInvoiceCostDifferenceV1.EventType));
        Assert.True(await verify.PendingMovementCosts.AnyAsync(row => row.MovementId == receipt.MovementId));
    }

    [SqlServerFact]
    public async Task A_failed_late_invoice_save_leaves_no_pool_change_no_cost_receipt_and_no_outbox_message()
    {
        await using var run = await Run.StartAsync();
        var receipt = await SeedInvoiceCostReceipt(run, 10m);
        await Assert.ThrowsAsync<PurchaseInvoiceCostSaveFault>(() => ApplyInvoiceCost(run, InvoiceCostEvent(receipt, 4m, 60m), new RejectPurchaseInvoiceCostSave()));
        await using var verify = run.Context(CompanyA);
        Assert.Equal(10m, (await verify.AverageCostLedger.SingleAsync(row => row.ProductId == receipt.ProductId)).AverageCostPerKg);
        Assert.False(await verify.PurchaseInvoiceCostApplications.AnyAsync());
        Assert.False(await verify.PurchaseInvoiceReceiptCostEffects.AnyAsync());
    }

    private static async Task<InventoryGoodsReceiptRequestV1> SeedInvoiceCostReceipt(Run run, decimal? tl)
    {
        var seed = await SeedGoodsReceiptPurchaseOrderAsync(run);
        var receipt = Receipt(seed, Guid.NewGuid(), "4", await run.LocationOfAsync(CompanyA)) with { InitialBlock = BlockState.Free };
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString); await connection.OpenAsync();
        await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
        await using var purchase = await PurchaseAcceptanceParticipant.EnlistAsync(connection, transaction, CompanyA);
        await using var inventory = await InventoryGoodsReceiptParticipant.EnlistAsync(connection, transaction, CompanyA,
            GoodsReceiptSettings(run), costSource: new SyntheticInvoiceReceiptCost(tl));
        var accepted = await purchase.StageAsync(GoodsReceiptPurchaseRequest(seed, receipt.AcceptanceId, "4"),
            GoodsReceiptPurchaseEnvelope(run, "SENTETIK-cost-receipt", run.Correlation));
        receipt = receipt with { PurchaseOrderVersionAfter = accepted.PurchaseOrderVersion };
        await inventory.StageAsync(receipt, ReceiptMetadata(run, "SENTETIK-cost-receipt", run.Correlation));
        await transaction.CommitAsync(); return receipt;
    }

    private static WorkInboxEventV1 InvoiceCostEvent(InventoryGoodsReceiptRequestV1 receipt, decimal matched, decimal net)
    {
        // Serialize the actual producer contract, not a separately constructed consumer-only DTO.
        var source = new PurchaseInvoiceFinalizedPayloadV1(Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(),
            SettingsCurrencyIdentitiesV1.TurkishLira, null, net, null, CompanyA, net, 0m, 0m, null, null, null,
            [new(Guid.NewGuid(), 1, "Steel", "resale", 0m, net, 0m, 0m, null, null, null,
                GoodsCostSource: new(receipt.ProductId, receipt.PurchaseUnitId, matched,
                [new(receipt.AcceptanceId, receipt.PurchaseOrderId, receipt.PurchaseOrderLineId,
                    long.Parse(receipt.PurchaseOrderVersionAfter, CultureInfo.InvariantCulture), matched, 4m)]))],
            new(2026, 10, 1), "SENTETIK-finalizer", OfficialDocument: new(Guid.NewGuid(), 1, new string('a', 64), Guid.NewGuid()));
        return new(PurchaseInvoiceFinalizedEventV1.SourceName, Guid.NewGuid(), CompanyA, PurchaseInvoiceFinalizedEventV1.EventType,
            PurchaseInvoiceFinalizedEventV1.SchemaVersion, JsonSerializer.Serialize(source, PurchaseCostJson),
            GoodsReceiptTime.AddDays(7), "SENTETIK-invoice-cost", null);
    }

    private static async Task ApplyInvoiceCost(Run run, WorkInboxEventV1 envelope, params IInterceptor[] interceptors)
    {
        await using var scope = run.Factory.Services.CreateAsyncScope();
        var company = scope.ServiceProvider.GetRequiredService<ICompanyContext>(); company.Set(CompanyA);
        var options = new DbContextOptionsBuilder<InventoryDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString)
            .AddInterceptors(interceptors).Options;
        await using var db = new InventoryDbContext(options, company);
        var store = new InventoryPurchaseInvoiceCostStoreV1(db, company, scope.ServiceProvider.GetRequiredService<IAuditEventCollector>(),
            scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>(), TimeProvider.System);
        await store.ApplyAsync(envelope);
    }

    private sealed class SyntheticInvoiceReceiptCost(decimal? tl) : IInventoryPurchaseReceiptCostSourceV1
    {
        public Task<InventoryPurchaseReceiptCostV1> ReadAsync(InventoryPurchaseReceiptCostSelectionV1 selection, CancellationToken cancellationToken = default) =>
            Task.FromResult(new InventoryPurchaseReceiptCostV1(selection.CompanyId, selection.AcceptanceId, selection.PurchaseOrderId,
                selection.PurchaseOrderLineId, null, null, tl, null,
                new(InventoryIssueCostCurrencyStatusV1.FunctionalCurrency, null, null, null, "SENTETIK-known-entry", null),
                "SENTETIK-known-entry", "{}", tl is null ? "SENTETIK-unknown" : null, "SENTETIK-usd-unknown"));
    }
    private sealed class PurchaseInvoiceCostSaveFault : Exception;
    private sealed class RejectPurchaseInvoiceCostSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default) => throw new PurchaseInvoiceCostSaveFault();
    }
}
