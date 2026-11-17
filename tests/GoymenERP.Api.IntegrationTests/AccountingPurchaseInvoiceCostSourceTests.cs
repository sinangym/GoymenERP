using System.Text.Json;
using GoymenERP.Api.AccountingRuntime;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.IntegrationTests;

public sealed class AccountingPurchaseInvoiceCostSourceTests
{
    [Fact]
    public void The_committed_inventory_residual_is_carried_exactly_and_stock_absorption_is_not_posted_again()
    {
        var source = Source(); var envelope = Envelope(source);
        var result = AccountingPurchaseInvoiceCostSourceV1.Read(envelope, source.CompanyId);
        var component = Assert.Single(result.Components);
        Assert.Equal(new("6", "1"), component.SignedTlAmount);
        Assert.Equal(source.Positions[0].ProductId, component.ProductId);
        Assert.Equal(source.Positions[0].LocationId, component.LocationId);
        Assert.Null(component.InvoiceLineId); Assert.Null(result.FinalizedBy);
        Assert.Contains(source.OfficialDocument.Ettn.ToString("D"), component.SourceProofJson);
    }

    [Fact]
    public void A_changed_event_identity_or_broken_owner_value_conservation_cannot_create_a_cost_proposal()
    {
        var source = Source();
        Refused(Envelope(source) with { EventId = Guid.NewGuid() });
        var position = source.Positions[0];
        var broken = source with { Positions = [position with { RecordedPoolValueEffect = new("5", "1") }] };
        Refused(Envelope(broken));
        var receipt = source.Receipts[0];
        broken = source with { Receipts = [receipt with { PreviouslyRecognizedTlValue = new("101", "1") }] };
        Refused(Envelope(broken));
        void Refused(WorkInboxEventV1 envelope) => Assert.Throws<OutboxPermanentFailureException>(() => AccountingPurchaseInvoiceCostSourceV1.Read(envelope, source.CompanyId));
    }

    [Fact]
    public void Opposite_position_residuals_are_not_netted_away_even_when_the_event_total_is_zero()
    {
        var source = Source(); var receipt = source.Receipts[0]; var position = source.Positions[0]; var location = Guid.NewGuid();
        source = source with { Receipts = [receipt, receipt with { InvoiceLineId = Guid.NewGuid(), InvoiceLineNo = 2, AcceptanceId = Guid.NewGuid(),
            LocationId = location, InvoiceTlValue = new("90", "1"), SourceDifference = new("-10", "1") }],
            Positions = [position, position with { LocationId = location, SourceDifference = new("-10", "1"), AbsorbedDifference = new("-4", "1"),
                UnabsorbedDifference = new("-6", "1"), RecordedPoolValueEffect = new("-4", "1") }], UnabsorbedTlDifference = new("0", "1") };
        var result = AccountingPurchaseInvoiceCostSourceV1.Read(Envelope(source), source.CompanyId);
        Assert.Equal(2, result.Components.Count);
        Assert.Equal(new("0", "1"), AccountingInventoryCostAmountsV1.Add(result.Components[0].SignedTlAmount, result.Components[1].SignedTlAmount));
    }

    [Fact]
    public void First_recognition_uses_one_real_invoice_line_component_with_recorded_rounding_and_two_actual_receipts()
    {
        var source = FirstSource(.01m);
        var result = AccountingPurchaseInvoiceCostSourceV1.Read(Envelope(source), source.CompanyId);
        var component = Assert.Single(result.Components);
        Assert.Equal(AccountingInventoryCostTraceV1.PurchaseFirstLineComponent(source.InitialRecognitions![0].InvoiceLineId), component.ComponentKey);
        Assert.Equal(new("1", "100"), component.SignedTlAmount);
        Assert.Equal(new("1", "100"), component.RecordedRounding!.Rounding.ExactSignedAmount);
        Assert.Null(component.LocationId);
        Assert.Null(component.InvoiceLineId);
        foreach (var receipt in source.InitialRecognitions[0].Receipts)
            Assert.Contains(receipt.AcceptanceId.ToString("D"), component.SourceProofJson);
    }

    [Fact]
    public void A_nonzero_exact_first_cost_rounded_to_zero_keeps_evidence_for_separate_zero_cost_approval()
    {
        var source = FirstSource(.004m);
        var result = AccountingPurchaseInvoiceCostSourceV1.Read(Envelope(source), source.CompanyId);
        var component = Assert.Single(result.Components);
        Assert.Equal(new("0", "1"), component.SignedTlAmount);
        Assert.Equal(new("1", "250"), component.RecordedRounding!.Rounding.ExactSignedAmount);
        Assert.Equal(new("1", "250"), component.RecordedRounding.Rounding.ExactSignedResidue);
    }

    [Fact]
    public void First_cost_rejects_per_receipt_overrounding_and_changed_physical_source_bindings()
    {
        var source = FirstSource(.01m); var line = source.InitialRecognitions![0];
        Refused(source with { InitialRecognitions = [line with { ConsumedTlAmount = .02m,
            ConsumedRoundingResidue = new("-1", "100") }], UnabsorbedTlDifference = new("1", "50") });
        Refused(source with { InitialRecognitions = [line with { Receipts = [line.Receipts[0], line.Receipts[0]] }] });
        var receipt = line.Receipts[0];
        Refused(source with { InitialRecognitions = [line with { Receipts = [receipt with { RootLabelId = Guid.NewGuid() }, line.Receipts[1]] }] });
        void Refused(InventoryPurchaseInvoiceCostApplicationResultV1 value) =>
            Assert.Throws<OutboxPermanentFailureException>(() => AccountingPurchaseInvoiceCostSourceV1.Read(Envelope(value), value.CompanyId));
    }

    private static InventoryPurchaseInvoiceCostApplicationResultV1 FirstSource(decimal exact)
    {
        var source = Source(); var lineId = Guid.NewGuid(); var productId = Guid.NewGuid();
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var receipts = Enumerable.Range(0, 2).Select(_ =>
        {
            var acceptance = Guid.NewGuid(); var movement = Guid.NewGuid(); var root = Guid.NewGuid(); var location = Guid.NewGuid();
            var physical = new InventoryPurchaseFirstCostPhysicalSourceV1(PurchaseFirstCostAllocationCalculator.Version,
                source.CompanyId, acceptance, lineId, 1, movement, root, productId, 1m, 1m, "receipt-root-proportional",
                new("0", "1"), new("1", "1"), [new(root, 1, 2, "SENTETIK-v1", new("1", "1"), [])],
                [new(Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), root, productId, "SENTETIK-v1", location,
                    1m, new("1", "1"), new("1", "1"), [], DateTimeOffset.UtcNow)]);
            var proof = JsonSerializer.Serialize(physical, json);
            var amount = InventoryExactCostRatioV1.From(ExactCostRatio.Of(exact).Divide(2m));
            return new InventoryPurchaseFirstCostReceiptV1(acceptance, movement, root, location, 1m, 1m,
                new("1", "1"), new("1", "1"), amount, new("1", "1"), amount, proof,
                AccountingInventoryCostSourceValidationV1.Sha256(proof));
        }).ToArray();
        var rounded = ExactCostRatio.Of(exact).Round(2);
        var line = new InventoryPurchaseFirstCostRecognitionV1(PurchaseFirstCostAllocationCalculator.Version,
            lineId, 1, productId, new("2", "1"), InventoryExactCostRatioV1.From(ExactCostRatio.Of(exact)),
            new("2", "1"), rounded, InventoryExactCostRatioV1.From(ExactCostRatio.Of(exact)),
            InventoryExactCostRatioV1.From(ExactCostRatio.Of(exact).Subtract(ExactCostRatio.Of(rounded))),
            SettingsCurrencyIdentitiesV1.TurkishLira, Guid.NewGuid(), 1, 2, source.InvoiceDate, receipts, []);
        return source with { Receipts = [], Positions = [], InitialRecognitions = [line],
            UnabsorbedTlDifference = InventoryExactCostRatioV1.From(ExactCostRatio.Of(rounded)) };
    }

    private static InventoryPurchaseInvoiceCostApplicationResultV1 Source()
    {
        var product = Guid.NewGuid(); var location = Guid.NewGuid();
        return new(Guid.NewGuid(), Guid.NewGuid(), 3, Guid.NewGuid(), Guid.NewGuid(), new(2026, 11, 17),
            new(Guid.NewGuid(), 2, new string('a', 64), Guid.NewGuid()), new string('b', 64),
            [new(Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), product, location, 5m, 5m,
                new("5", "1"), new("110", "1"), new("100", "1"), new("10", "1"), 1)],
            [new(product, location, new("10", "1"), new("4", "1"), new("6", "1"), new("4", "1"), new("0", "1"), 3)],
            Guid.NewGuid(), new("6", "1"));
    }
    private static WorkInboxEventV1 Envelope(InventoryPurchaseInvoiceCostApplicationResultV1 source) => new(
        AccountingInventoryCostInboxV1.InventorySourceName, source.CostDifferenceEventId!.Value, source.CompanyId,
        AccountingInventoryCostInboxV1.PurchaseDifferenceEventType, 1, JsonSerializer.Serialize(source, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
        DateTimeOffset.UtcNow, "SENTETIK-purchase-cost", null);
}
