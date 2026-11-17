using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryPurchaseInvoiceCostSourceTests
{
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Invoice_net_is_preserved_as_exact_receipt_shares_without_tax_or_current_exchange_rate_calculation()
    {
        var source = Source();
        var receipt = source.Lines![0].GoodsCostSource!.Receipts[0];
        var second = receipt with { AcceptanceId = Guid.NewGuid(), MatchedQuantity = 2m };
        source = source with { Lines = [source.Lines[0] with { GoodsCostSource = source.Lines[0].GoodsCostSource! with
        { InvoiceQuantity = 3m, Receipts = [receipt with { MatchedQuantity = 1m }, second] } }] };
        var result = Read(source);
        Assert.Equal(2, result.Receipts.Count);
        var total = result.Receipts.Aggregate(ExactCostRatio.Of(0m), (sum, x) => sum.Add(x.TlNetShare.Value()));
        Assert.Equal(0, total.CompareTo(ExactCostRatio.Of(100.01m)));
        Assert.All(result.Receipts, x => Assert.False(x.TlNetShare.Value().TryToDecimal(out _)));
        Assert.Equal(source.OfficialDocument, result.OfficialDocument);
        Assert.Equal(source.InvoiceId, result.InvoiceId);
    }

    [Fact]
    public void Foreign_cost_uses_the_recorded_functional_net_even_if_an_unrelated_rate_would_produce_another_amount()
    {
        var source = Source() with { CurrencyId = Guid.NewGuid(), Net = 10m, FunctionalNet = 400.01m };
        source = source with { Lines = [source.Lines![0] with { Net = 10m, FunctionalNet = 400.01m }] };
        var result = Read(source);
        Assert.Equal(0, Assert.Single(result.Receipts).TlNetShare.Value().CompareTo(ExactCostRatio.Of(400.01m)));
        Refuses(source with { FunctionalNet = null });
        Refuses(source with { Lines = [source.Lines[0] with { FunctionalNet = null }] });
    }

    [Fact]
    public void Invoice_quantity_variance_is_not_an_implicit_cost_allocation_decision()
    {
        var source = Source();
        var line = source.Lines![0];
        var mismatch = source with { Lines = [line with { GoodsCostSource = line.GoodsCostSource! with { InvoiceQuantity = 5m } }] };
        var error = Refuses(mismatch);
        Assert.Equal("purchase_invoice_cost_source_quantity_mismatch", error.Code);
    }

    [Fact]
    public void Missing_legacy_document_goods_or_accepted_quantity_evidence_is_not_completed_from_current_data()
    {
        var source = Source(); var line = source.Lines![0]; var goods = line.GoodsCostSource!;
        Refuses(source with { OfficialDocument = null });
        Refuses(source with { Lines = [line with { GoodsCostSource = null }] });
        Refuses(source with { Lines = [line with { GoodsCostSource = goods with
        { Receipts = [goods.Receipts[0] with { AcceptedQuantity = 0m }] } }] });
        Refuses(source with { OfficialDocument = source.OfficialDocument! with { Sha256 = "missing" } });
    }

    [Fact]
    public void Source_amount_mismatch_or_duplicate_receipt_is_refused_instead_of_losing_or_counting_value_twice()
    {
        var source = Source(); var line = source.Lines![0]; var goods = line.GoodsCostSource!;
        Refuses(source with { Net = source.Net + 0.01m });
        Refuses(source with { Lines = [line with { GoodsCostSource = goods with
        { InvoiceQuantity = 6m, Receipts = [goods.Receipts[0], goods.Receipts[0]] } }] });
        Refuses(source with { Lines = [line, line] });
        Refuses(source with { Lines = [line with { GoodsCostSource = goods with
        { Receipts = [goods.Receipts[0] with { MatchedQuantity = 4m, AcceptedQuantity = 3m }] } }] });
    }

    [Theory]
    [InlineData("service", "Service")]
    [InlineData("expense", "Expense")]
    public void Services_and_expenses_have_no_inventory_cost_share(string purpose, string itemType)
    {
        var source = Source(); var line = source.Lines![0];
        var other = line with { LinePurpose = purpose, ItemType = itemType, GoodsCostSource = null };
        Assert.Empty(Read(source with { Lines = [other] }).Receipts);
        Refuses(source with { Lines = [other with { GoodsCostSource = line.GoodsCostSource }] });
    }

    [Fact]
    public void Another_company_or_unknown_event_never_reaches_inventory_projection()
    {
        var source = Source(); var company = new CompanyContext(); company.Set(Company);
        Assert.Throws<OutboxPermanentFailureException>(() => InventoryPurchaseInvoiceCostSourceReaderV1.Read(
            Envelope(source) with { CompanyId = Guid.NewGuid() }, company));
        Refuses(source with { CompanyId = Guid.NewGuid() });
        Assert.Throws<OutboxPermanentFailureException>(() => InventoryPurchaseInvoiceCostSourceReaderV1.Read(
            Envelope(source) with { Version = 2 }, company));
        Assert.Throws<OutboxPermanentFailureException>(() => InventoryPurchaseInvoiceCostSourceReaderV1.Read(
            Envelope(source) with { SourceName = "another-source" }, company));
    }

    private static InventoryPurchaseInvoiceCostSourceV1 Source() => new(Company, Guid.NewGuid(), 4,
        Guid.NewGuid(), SettingsCurrencyIdentitiesV1.TurkishLira, 100.01m, null, new(2026, 11, 17), "synthetic-finalizer",
        [new(Guid.NewGuid(), 1, "Product", "resale", 100.01m, null,
            new(Guid.NewGuid(), Guid.NewGuid(), 3m, [new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2, 3m, 6m)]))],
        new(Guid.NewGuid(), 1, new string('a', 64), Guid.NewGuid()));
    private static WorkInboxEventV1 Envelope(InventoryPurchaseInvoiceCostSourceV1 source) => new(
        InventoryPurchaseInvoiceCostInboxV1.SourceName, Guid.NewGuid(), Company, InventoryPurchaseInvoiceCostInboxV1.EventType,
        1, JsonSerializer.Serialize(source, Json), new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero), "synthetic-cost", null);
    private static InventoryPurchaseInvoiceCostProjectionV1 Read(InventoryPurchaseInvoiceCostSourceV1 source)
    {
        var company = new CompanyContext(); company.Set(Company);
        return InventoryPurchaseInvoiceCostSourceReaderV1.Read(Envelope(source), company);
    }
    private static OutboxPermanentFailureException Refuses(InventoryPurchaseInvoiceCostSourceV1 source) =>
        Assert.Throws<OutboxPermanentFailureException>(() => Read(source));
}
