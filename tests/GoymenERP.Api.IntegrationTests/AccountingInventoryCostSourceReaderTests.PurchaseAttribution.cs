using System.Text.Json;
using GoymenERP.Api.AccountingRuntime;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Inbox;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingInventoryCostSourceReaderTests
{
    [Fact]
    public async Task Original_unavailable_sales_cost_uses_one_actual_purchase_posted_share_without_creating_new_unposted_money()
    {
        var f = new Fixture(); var line = f.Inventory.Value.Lines[0];
        f.Inventory.Value = f.Inventory.Value with { Lines = [line with { Tl = line.Tl with {
            Status = InventoryMovementCostStampStatusV1.Unavailable, Amount = null, UnitCostRatio = null, Reason = "unknown-at-shipment" } }] };
        var envelope = f.Envelope(); var purchase = new PurchasePostedPort(f);
        var reader = new AccountingInventoryCostSourceReaderV1(f.Company, f.Logistics, f.Inventory,
            salesClaims: new FinalizedClaims(f, envelope), postedPurchasing: purchase);
        var result = await reader.ReadAsync(envelope); var component = Assert.Single(result.Components);
        Assert.Equal(new("10001", "100"), component.SignedTlAmount); Assert.Null(component.UnpostedTlAmount);
        Assert.Null(component.AlreadyPostedComponents); Assert.Equal(purchase.Share, Assert.Single(component.AlreadyPostedPurchaseComponents!));
        Assert.True(component.HasPostedComponents);
        purchase.Pending = true;
        Assert.Equal("sales_cost_original_tl_unavailable", (await Assert.ThrowsAsync<AccountingInvoiceJournalRetryException>(() => reader.ReadAsync(envelope))).Reason);
        purchase.Pending = false; purchase.AfterRead = () => f.Company.CompanyId = Guid.NewGuid();
        Assert.Equal("company_context_mismatch", (await Assert.ThrowsAsync<AccountingInvoiceJournalRetryException>(() => reader.ReadAsync(envelope))).Reason);
    }

    [Fact]
    public async Task Partial_purchase_coverage_cannot_claim_the_uninvoiced_physical_remainder_as_zero_cost()
    {
        var f = new Fixture(); var line = f.Inventory.Value.Lines[0];
        f.Inventory.Value = f.Inventory.Value with { Lines = [line with { Tl = line.Tl with {
            Status = InventoryMovementCostStampStatusV1.Unavailable, Amount = null, UnitCostRatio = null, Reason = "partial-first-cost" } }] };
        var envelope = f.Envelope(); var purchase = new PurchasePostedPort(f, new("1", "2"));
        var reader = new AccountingInventoryCostSourceReaderV1(f.Company, f.Logistics, f.Inventory,
            salesClaims: new FinalizedClaims(f, envelope), postedPurchasing: purchase);
        Assert.Equal("sales_cost_original_tl_unavailable", (await Assert.ThrowsAsync<AccountingInvoiceJournalRetryException>(() => reader.ReadAsync(envelope))).Reason);
    }

    private sealed class PurchasePostedPort : IPostedPurchaseShipmentCostReaderV1
    {
        public PostedPurchaseCostShareV1 Share { get; }
        public bool Pending { get; set; }
        public Action? AfterRead { get; set; }
        public PurchasePostedPort(Fixture f, AccountingInventoryCostRatioV1? fraction = null)
        {
            var json = new JsonSerializerOptions(JsonSerializerDefaults.Web); var stock = f.Inventory.Value; var line = stock.Lines[0];
            var at = DateTimeOffset.Parse("2026-11-17T09:00:00Z"); var sourceEvent = Guid.NewGuid(); var sourceLine = Guid.NewGuid();
            var exact = new AccountingInventoryCostRatioV1("10001", "100");
            Assert.True(AccountingInventoryCostRoundingV1Rules.TryRound(exact, SettingsCurrencyIdentitiesV1.TurkishLira, 2, out _, out var rounding));
            var approval = new PurchaseFirstCostApprovalV1(f.Company.CompanyId, sourceEvent, sourceLine, Guid.NewGuid(), 3,
                Guid.NewGuid(), "Posted", "100.01", SettingsCurrencyIdentitiesV1.TurkishLira, Guid.NewGuid(), 1, 2, Guid.NewGuid(), Guid.NewGuid(),
                new(2026, 11, 17), Guid.NewGuid(), Guid.NewGuid(), at,
                new(SettingsCurrencyIdentitiesV1.TurkishLira, Guid.NewGuid(), 1, 2, rounding!), new string('A', 64), "");
            approval = approval with { Fingerprint = AccountingInventoryCostSourceValidationV1.Sha256(JsonSerializer.Serialize(approval, json)) };
            var physical = new PurchaseFirstCostShipmentV1(stock.ShipmentId, stock.MovementId, line.MovementLineNumber,
                line.LoadingInstructionLineId, line.ProductId, line.LabelId, line.QuantityKg, new("1", "1"),
                AccountingInventoryCostAmountsV1.FromDecimal(line.QuantityKg), at, null, [], "{\"synthetic\":true}");
            var candidate = new PurchaseFirstCostCandidateV1(f.Company.CompanyId, sourceEvent, sourceLine, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                line.ProductId, fraction ?? new("1", "1"), physical.NetRootQuantityKg, exact, physical, approval.SourceProofSha256, new string('B', 64), "");
            candidate = candidate with { Fingerprint = AccountingInventoryCostSourceValidationV1.Sha256(JsonSerializer.Serialize(candidate, json)) };
            var value = new PostedPurchaseCostShareV1(approval, stock.ShipmentId, stock.MovementId, line.MovementLineNumber,
                line.ProductId, line.QuantityKg, "100.01", exact, new("0", "1"), [candidate], "");
            Share = value with { Fingerprint = AccountingInventoryCostSourceValidationV1.Sha256(JsonSerializer.Serialize(value, json)) };
        }
        public async Task<PurchaseShipmentCostAttributionReadV1> ReadProvenSharesAsync(Guid companyId, Guid shipmentId, Guid movementId,
            int movementLineNumber, Guid productId, decimal quantityKg, Func<CancellationToken, Task> freshConsumerFence,
            CancellationToken cancellationToken = default)
        {
            await freshConsumerFence(cancellationToken); AfterRead?.Invoke(); await freshConsumerFence(cancellationToken);
            return new(companyId, shipmentId, movementId, movementLineNumber, productId, quantityKg, Pending, [Share]);
        }
    }
}
