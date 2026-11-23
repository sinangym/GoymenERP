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

    // ---- AK-3b (ALS-D121): golden ADET_KG_FORMUL_GOLDEN_V1 §2 step 4, rows A09 and A13 ---------------------------------------

    private static InventoryPurchaseInvoiceCostSourceV1 PieceSource(decimal invoicedPieces, decimal acceptedPieces, decimal? measuredKg,
        decimal net, decimal? functionalNet = null)
    {
        var source = Source();
        var line = source.Lines![0];
        var receipt = line.GoodsCostSource!.Receipts[0] with
        {
            MatchedQuantity = invoicedPieces, AcceptedQuantity = acceptedPieces, MeasuredQuantityKg = measuredKg,
        };
        return source with
        {
            CurrencyId = functionalNet is null ? source.CurrencyId : Guid.NewGuid(), Net = net, FunctionalNet = functionalNet,
            Lines = [line with
            {
                Net = net, FunctionalNet = functionalNet,
                GoodsCostSource = line.GoodsCostSource with { InvoiceQuantity = invoicedPieces, Receipts = [receipt] },
            }],
        };
    }

    [Fact]
    public void A09_a_partial_TL_invoice_of_a_piece_receipt_takes_its_kilogram_share_and_leaves_the_receipt_cost_unchanged()
    {
        // R1: 100 pieces weighed 1 250,400 kg; 40 pieces invoiced for 10 000,00 TL -> 500,160 kg; 10 000 / 500,160 = 19,9936.
        var share = Assert.Single(Read(PieceSource(40m, 100m, 1250.4m, 10000m)).Receipts);
        Assert.Equal((40m, 100m, (decimal?)1250.4m), (share.MatchedQuantity, share.AcceptedQuantity, share.MeasuredQuantityKg));

        var invoicedKg = PurchaseInvoiceKilogramShare.Of(ExactCostRatio.Of(share.MeasuredQuantityKg!.Value), share.MatchedQuantity,
            ExactCostRatio.Of(share.AcceptedQuantity));

        Assert.Equal(0, invoicedKg.CompareTo(ExactCostRatio.Of(500.16m)));
        Assert.Equal(19.9936m, share.TlNetShare.Value().Divide(invoicedKg).Round(AverageCostPrecision.InternalScale));
        // The receipt was stamped at 25 000 / 1 250,4 per kg: the invoice value of the share equals what was recognised (no difference).
        var recognised = ExactCostRatio.Of(25000m).Divide(1250.4m).Multiply(invoicedKg);
        Assert.Equal(0, share.TlNetShare.Value().Subtract(recognised).Sign);
    }

    [Fact]
    public void A13_a_partial_foreign_invoice_of_a_piece_receipt_recognises_its_kilogram_share_at_the_invoice_TL_value()
    {
        // 40 of 100 pieces at 7,50 USD, official rate 34,5678: 10 370,34 TL over 1 250,4 x 40 / 100 = 500,160 kg -> 20,7340 TL/kg.
        var share = Assert.Single(Read(PieceSource(40m, 100m, 1250.4m, 300m, 10370.34m)).Receipts);

        var invoicedKg = PurchaseInvoiceKilogramShare.Of(ExactCostRatio.Of(1250.4m), share.MatchedQuantity, ExactCostRatio.Of(share.AcceptedQuantity));

        Assert.Equal(500.160m, invoicedKg.Round(3));
        Assert.Equal(0, share.TlNetShare.Value().CompareTo(ExactCostRatio.Of(10370.34m)));
        Assert.Equal(20.7340m, share.TlNetShare.Value().Divide(invoicedKg).Round(AverageCostPrecision.InternalScale));
        // The 60 uninvoiced pieces keep 750,240 kg pending (BS-1); nothing is costed for them here.
        Assert.Equal(750.240m, ExactCostRatio.Of(1250.4m).Subtract(invoicedKg).Round(3));
    }

    [Fact]
    public void A_piece_share_is_whole_pieces_of_a_weighed_acceptance_and_a_kilogram_share_names_no_weighed_kilograms()
    {
        Assert.Null(Assert.Single(Read(Source()).Receipts).MeasuredQuantityKg);
        Refuses(PieceSource(40m, 100m, 0m, 10000m));
        Refuses(PieceSource(40m, 100m, -1m, 10000m));
        Refuses(PieceSource(2.5m, 100m, 1250.4m, 10000m));
        Refuses(PieceSource(40m, 99.5m, 1250.4m, 10000m));
    }

    [Fact]
    public void The_kilogram_share_keeps_the_receipts_own_proportion_only_while_returns_left_proportionally()
    {
        var originalKg = ExactCostRatio.Of(1250.4m);
        // Nothing returned, or 20 of 100 pieces returned with exactly 20 % of the kg: "remaining kg x invoiced / remaining pieces"
        // is ALS-D121's "weighed kg x invoiced pieces / accepted pieces".
        Assert.True(PurchaseInvoiceKilogramShare.KeepsOriginalProportion(originalKg, 100m, originalKg, ExactCostRatio.Of(100m)));
        Assert.True(PurchaseInvoiceKilogramShare.KeepsOriginalProportion(originalKg, 100m, ExactCostRatio.Of(1000.32m), ExactCostRatio.Of(80m)));
        Assert.Equal(0, PurchaseInvoiceKilogramShare.Of(ExactCostRatio.Of(1000.32m), 40m, ExactCostRatio.Of(80m))
            .CompareTo(PurchaseInvoiceKilogramShare.Of(originalKg, 40m, ExactCostRatio.Of(100m))));
        // Golden A10's return (20 pieces weighing 249,870 kg, not 250,080): the two readings differ, so no rule is chosen.
        Assert.False(PurchaseInvoiceKilogramShare.KeepsOriginalProportion(originalKg, 100m, ExactCostRatio.Of(1000.53m), ExactCostRatio.Of(80m)));
        Assert.False(PurchaseInvoiceKilogramShare.KeepsOriginalProportion(originalKg, 0m, originalKg, ExactCostRatio.Of(100m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => PurchaseInvoiceKilogramShare.Of(ExactCostRatio.Of(0m), 1m, ExactCostRatio.Of(1m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => PurchaseInvoiceKilogramShare.Of(originalKg, -1m, ExactCostRatio.Of(1m)));
    }

    [Fact]
    public void ALS_D122_closed_by_parameter_a_non_proportional_return_keeps_the_piece_share_waiting()
    {
        // The switch is on with golden EK1; the closed state stays pinned by parameter.
        Assert.True(PurchaseInvoiceKilogramShare.PurchaseInvoiceRemainingShareGoldenAccepted);
        var originalKg = ExactCostRatio.Of(1250.4m);
        // 20 of 100 pieces returned weighing 249,870 kg: 1 000,530 kg / 80 pieces remain, not in the receipt's own proportion.
        Assert.False(PurchaseInvoiceKilogramShare.PieceShareIsDecided(originalKg, 100m, ExactCostRatio.Of(1000.53m), ExactCostRatio.Of(80m),
            remainingShareAccepted: false));
        // No return, or a proportional one: ALS-D121 and ALS-D122 agree, so the share is decided whatever the switch.
        Assert.True(PurchaseInvoiceKilogramShare.PieceShareIsDecided(originalKg, 100m, originalKg, ExactCostRatio.Of(100m), false));
        Assert.True(PurchaseInvoiceKilogramShare.PieceShareIsDecided(originalKg, 100m, ExactCostRatio.Of(1000.32m), ExactCostRatio.Of(80m), false));
    }

    [Fact]
    public void ALS_D122_open_takes_the_remaining_kilograms_over_the_remaining_pieces()
    {
        var originalKg = ExactCostRatio.Of(1250.4m);
        var remainingKg = ExactCostRatio.Of(1000.53m);
        var remainingPieces = ExactCostRatio.Of(80m);

        Assert.True(PurchaseInvoiceKilogramShare.PieceShareIsDecided(originalKg, 100m, remainingKg, remainingPieces, remainingShareAccepted: true));
        // 1 000,530 x 40 / 80 = 500,265 kg (ALS-D121's 1 250,4 x 40 / 100 = 500,160 no longer applies after the return).
        var share = PurchaseInvoiceKilogramShare.Of(remainingKg, 40m, remainingPieces);
        Assert.Equal(0, share.CompareTo(ExactCostRatio.Of(500.265m)));
        Assert.NotEqual(0, share.CompareTo(PurchaseInvoiceKilogramShare.Of(originalKg, 40m, ExactCostRatio.Of(100m))));
        // All remaining pieces invoiced take all remaining kg; nothing left is never decided.
        Assert.Equal(0, PurchaseInvoiceKilogramShare.Of(remainingKg, 80m, remainingPieces).CompareTo(remainingKg));
        Assert.False(PurchaseInvoiceKilogramShare.PieceShareIsDecided(originalKg, 100m, ExactCostRatio.Of(0m), ExactCostRatio.Of(0m), true));
    }

    [Fact]
    public void ALS_D123_D124_each_invoice_takes_its_rounded_share_of_what_currently_remains_and_the_last_one_takes_the_rest()
    {
        // Receipt 100 pieces / 1 250,400 kg; 20 pieces / 249,870 kg returned -> 80 pieces / 1 000,530 kg remain.
        var kg = ExactCostRatio.Of(1000.53m);
        var pieces = ExactCostRatio.Of(80m);

        // Invoice 1: 30 pieces -> 1 000,53 x 30 / 80 = 375,19875 -> 375,199 (half away from zero at the third decimal).
        var first = PurchaseInvoiceKilogramShare.RemainingShare(kg, pieces, 30m);
        Assert.Equal(0, first.CompareTo(ExactCostRatio.Of(375.199m)));
        // Invoice 2: 30 of the 50 remaining pieces over the CURRENT remaining 625,331 kg -> 375,1986 -> 375,199.
        kg = kg.Subtract(first); pieces = pieces.Subtract(ExactCostRatio.Of(30m));
        var second = PurchaseInvoiceKilogramShare.RemainingShare(kg, pieces, 30m);
        Assert.Equal(0, second.CompareTo(ExactCostRatio.Of(375.199m)));
        // Invoice 3 completes the receipt: it takes the remaining kg (250,132), not a rounded proportion.
        kg = kg.Subtract(second); pieces = pieces.Subtract(ExactCostRatio.Of(30m));
        var last = PurchaseInvoiceKilogramShare.RemainingShare(kg, pieces, 20m);
        Assert.Equal(0, last.CompareTo(ExactCostRatio.Of(250.132m)));
        Assert.Equal(0, first.Add(second).Add(last).CompareTo(ExactCostRatio.Of(1000.53m)));
    }

    [Fact]
    public void ALS_D124_rounds_an_exact_half_away_from_zero_and_refuses_more_than_what_remains()
    {
        // 0,001 kg over 2 pieces, 1 invoiced: 0,0005 -> 0,001.
        Assert.Equal(0, PurchaseInvoiceKilogramShare.RemainingShare(ExactCostRatio.Of(0.001m), ExactCostRatio.Of(2m), 1m)
            .CompareTo(ExactCostRatio.Of(0.001m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => PurchaseInvoiceKilogramShare.RemainingShare(ExactCostRatio.Of(10m), ExactCostRatio.Of(2m), 3m));
        Assert.Throws<ArgumentOutOfRangeException>(() => PurchaseInvoiceKilogramShare.RemainingShare(ExactCostRatio.Of(10m), ExactCostRatio.Of(2m), 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => PurchaseInvoiceKilogramShare.RemainingShare(ExactCostRatio.Of(0m), ExactCostRatio.Of(2m), 1m));
    }

    // ---- golden ADET_KG_FORMUL_GOLDEN_V1_EK1 §5 (ALS-D122/D123/D124), row ids in the names -----------------------------------

    private static readonly ExactCostRatio R1Kg = ExactCostRatio.Of(1250.4m);

    private static decimal Share(bool anyReturn, decimal remainingKg, decimal remainingPieces, decimal invoiced, decimal originalKg = 1250.4m,
        decimal originalPieces = 100m) => PurchaseInvoiceKilogramShare.PieceShare(ExactCostRatio.Of(originalKg), originalPieces, anyReturn,
            ExactCostRatio.Of(remainingKg), ExactCostRatio.Of(remainingPieces), invoiced).Round(6);

    [Fact]
    public void E22_E23_after_a_partial_return_the_invoice_takes_the_remaining_kg_over_the_remaining_pieces()
    {
        // 20 pieces / 249,870 kg returned -> 80 pieces / 1 000,530 kg; 40 pieces invoiced -> 500,265 (500,160 OLMAZ).
        var share = Share(anyReturn: true, 1000.53m, 80m, 40m);
        Assert.Equal(500.265m, share);
        Assert.NotEqual(500.160m, share);
        // E22: 10 000,00 TL / 500,265 = 19,9894. E23: 10 370,34 TL / 500,265 = 20,7297.
        Assert.Equal(19.9894m, ExactCostRatio.Of(10000m).Divide(share).Round(AverageCostPrecision.InternalScale));
        Assert.Equal(20.7297m, ExactCostRatio.Of(10370.34m).Divide(share).Round(AverageCostPrecision.InternalScale));
    }

    [Fact]
    public void E24_E25_a_proportional_return_or_no_return_gives_the_ALS_D121_share()
    {
        Assert.Equal(500.160m, Share(anyReturn: true, 1000.32m, 80m, 40m)); // E24: 20 pieces / 250,080 kg returned
        Assert.Equal(500.160m, Share(anyReturn: false, 1250.4m, 100m, 40m)); // E25 (= A13)
    }

    [Fact]
    public void E26_a_share_that_does_not_end_at_three_decimals_is_rounded_once()
    {
        // 30 pieces returned -> 70 pieces / 1 000,530 kg; 40 invoiced (not the last): 571,7314... -> 571,731; 30 pieces / 428,799 kg remain.
        var share = Share(anyReturn: true, 1000.53m, 70m, 40m);
        Assert.Equal(571.731m, share);
        Assert.Equal(428.799m, 1000.53m - share);
    }

    [Fact]
    public void E27_a_return_between_two_invoices_is_deducted_together_with_the_first_invoice()
    {
        var first = Share(anyReturn: false, 1250.4m, 100m, 40m);
        Assert.Equal(500.160m, first);
        // Then 20 pieces / 249,870 kg returned; invoice 2 takes the last 40 pieces: 1 250,4 - 249,87 - 500,16 = 500,370.
        var second = Share(anyReturn: true, 1250.4m - 249.87m - first, 100m - 20m - 40m, 40m);
        Assert.Equal(500.370m, second);
        Assert.NotEqual(500.265m, second);
    }

    [Fact]
    public void E28_after_a_return_each_invoice_rounds_its_share_of_the_current_remainder_and_the_last_takes_the_rest()
    {
        // 3 pieces / 1 000,000 kg remain after a return; three invoices of 1 piece: 333,333 / 333,334 (exact half) / 333,333.
        var first = Share(true, 1000m, 3m, 1m, originalKg: 1200m, originalPieces: 4m);
        var second = Share(true, 1000m - first, 2m, 1m, originalKg: 1200m, originalPieces: 4m);
        var third = Share(true, 1000m - first - second, 1m, 1m, originalKg: 1200m, originalPieces: 4m);
        Assert.Equal((333.333m, 333.334m, 333.333m), (first, second, third));
        Assert.Equal(1000m, first + second + third);
    }

    [Fact]
    public void E29_without_a_return_each_invoice_takes_the_original_ratio_and_the_last_takes_the_rest()
    {
        // 3 pieces / 1 000,000 kg, no return: 333,333 / 333,333 / 333,334 (differs from E28: ALS-D121 holds without a return).
        var first = Share(false, 1000m, 3m, 1m, originalKg: 1000m, originalPieces: 3m);
        var second = Share(false, 1000m - first, 2m, 1m, originalKg: 1000m, originalPieces: 3m);
        var third = Share(false, 1000m - first - second, 1m, 1m, originalKg: 1000m, originalPieces: 3m);
        Assert.Equal((333.333m, 333.333m, 333.334m), (first, second, third));
        Assert.Equal(1000m, first + second + third);
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
