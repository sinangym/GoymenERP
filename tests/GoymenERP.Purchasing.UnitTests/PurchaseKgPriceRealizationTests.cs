using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Invoices;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Quantities;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// AK-3b (SAT-D436 TK4/TK6/TK8, SAT-D439, ALS-D121): the real kg price and the order realization derived from the invoice ↔ receipt
/// match, against the independent golden rows of <c>docs/decisions/ADET_KG_FORMUL_GOLDEN_V1.md</c> §1 (K rows) and §2 (A09, A13);
/// row ids in each test. Everything here is information only — no cost or amount is written. SENTETIK.
/// </summary>
public sealed class PurchaseKgPriceRealizationTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Supplier = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Account = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Lira = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid Dollar = Guid.Parse("66666666-6666-4666-8666-666666666667");
    private static readonly Guid PieceUnit = Guid.Parse("44444444-4444-4444-8444-444444444441");
    private static readonly Guid KgUnit = Guid.Parse("44444444-4444-4444-8444-444444444442");
    private static readonly Guid Product = Guid.Parse("55555555-5555-4555-8555-555555555555");

    // ---- builders ------------------------------------------------------------------------------------------------------

    /// <summary>An approved order: one piece line (pieces × unit price, k recorded at approval) and optional kg lines.</summary>
    private static (PurchaseOrder Order, PurchaseOrderLine Piece) PieceOrder(decimal pieces, decimal unitPrice, decimal? kgPerPiece,
        Guid? currency = null, decimal? deferralPercent = null, (decimal Kg, decimal Price)? kgLine = null)
    {
        var money = PurchaseOrderMoney.Of(currency ?? Lira, currency == Dollar ? "USD" : "TRY", Guid.NewGuid(), 1, 2);
        var order = PurchaseOrder.Create(Guid.NewGuid(), Company, Supplier, money, T0, "SENTETIK-hazirlayan");
        var piece = order.AddLine(1, Guid.NewGuid(), Product, PieceUnit, Quantity.Of(pieces, QuantityUnit.Piece), null,
            new PurchaseOrderLinePrice(unitPrice, PieceUnit, DeferralPercent: deferralPercent), T0);
        var version = 2;
        if (kgLine is { } kilograms)
        {
            order.AddLine(version++, Guid.NewGuid(), Product, KgUnit, Quantity.Of(kilograms.Kg, QuantityUnit.Kilogram), null,
                new PurchaseOrderLinePrice(kilograms.Price, KgUnit), T0);
        }

        if (kgPerPiece is { } perPiece)
        {
            order.Approve(version, "SENTETIK-onaylayan", T0, currency ?? Lira,
                new Dictionary<Guid, PurchaseOrderLineTheoreticalKilograms> { [piece.Id] = new(perPiece, "product.kg-per-unit.v1/sentetik") });
        }

        return (order, piece);
    }

    private sealed record Share(Guid OrderId, Guid OrderLineId, decimal Quantity, decimal AcceptedQuantity, decimal? MeasuredKg,
        decimal? KgPerPiece, bool UnitIsKilogram = false, bool WithoutKilogramFacts = false);

    private sealed record InvoiceLine(decimal Quantity, decimal Net, params Share[] Shares);

    private static PurchaseInvoiceAmounts Amounts(decimal net) => new(net, decimal.Round(net * 0.2m, 2), 0m);

    /// <summary>A recorded and matched supplier invoice of goods lines; <paramref name="rate"/> makes it a foreign invoice.</summary>
    private static PurchaseIncomingInvoice Matched(Guid currency, decimal? rate, params InvoiceLine[] lines)
    {
        var foreign = rate is not null;
        PurchaseInvoiceAmounts? Functional(decimal net) => foreign ? Amounts(decimal.Round(net * rate!.Value, 2)) : null;
        var invoiceLines = lines.Select((line, index) => new PurchaseInvoiceLine(Guid.NewGuid(), index + 1, Product,
            line.Shares[0].UnitIsKilogram ? KgUnit : PieceUnit, line.Quantity, "material", "resale", 20m, Amounts(line.Net), Functional(line.Net))).ToArray();
        var total = new PurchaseInvoiceAmounts(invoiceLines.Sum(line => line.Amounts.Net), invoiceLines.Sum(line => line.Amounts.Vat), 0m);
        PurchaseInvoiceAmounts? functionalTotal = foreign
            ? new(invoiceLines.Sum(line => line.FunctionalAmounts!.Net), invoiceLines.Sum(line => line.FunctionalAmounts!.Vat), 0m) : null;
        var invoice = PurchaseIncomingInvoice.Record(Guid.NewGuid(), Company, Supplier, Account, currency, Lira, 2, 2, rate,
            new(Guid.NewGuid(), 1, new string('a', 64), "SENTETIK-FTR-" + Guid.NewGuid().ToString("N")[..8], Guid.NewGuid(), new(2026, 11, 22)),
            total, functionalTotal, invoiceLines,
            [new(Guid.NewGuid(), Guid.NewGuid(), 1, new(2026, 12, 22), total.Payable, functionalTotal?.Payable)], "SENTETIK-kaydeden", T0);
        var matches = lines.SelectMany((line, index) => line.Shares.Select(share => new PurchaseInvoiceReceiptMatch(Company,
            invoiceLines[index].Id, Guid.NewGuid(), share.OrderId, share.OrderLineId, 3, Supplier, Account, currency, invoiceLines[index].UnitId,
            share.Quantity, line.Net * share.Quantity / line.Quantity, rate, new(2026, 11, 20), AcceptedQuantity: share.AcceptedQuantity,
            Kilograms: share.WithoutKilogramFacts ? null
                : new PurchaseInvoiceMatchKilograms(share.UnitIsKilogram, share.MeasuredKg, share.KgPerPiece)))).ToArray();
        invoice.Match(1, matches, new(Guid.NewGuid(), 1, 1_000_000m, 1_000_000m), "SENTETIK-eslestiren");
        return invoice;
    }

    private static PurchaseIncomingInvoice Finalized(PurchaseIncomingInvoice invoice)
    {
        if (invoice.Variances.Count != 0) invoice.ApproveVariance(invoice.Version, "SENTETIK-onaylayan", "SENTETIK fark onayi", T0);
        invoice.Finalize(invoice.Version, Guid.NewGuid(), "SENTETIK-kesinlestiren", T0);
        return invoice;
    }

    private static PurchaseIncomingInvoiceViewV1 View(PurchaseIncomingInvoice invoice) =>
        PurchaseIncomingInvoiceWireV1.View(PurchaseIncomingInvoiceSnapshot.Capture(invoice));

    private static Share PieceShare(PurchaseOrder order, PurchaseOrderLine line, decimal invoiced, decimal accepted, decimal? measuredKg,
        decimal? kgPerPiece = 12.345m) => new(order.Id, line.Id, invoiced, accepted, measuredKg, kgPerPiece);

    // ---- ALS-D121: the kilogram share of a partial invoice ----------------------------------------------------------------

    [Fact]
    public void A09_A13_a_partial_invoice_takes_the_weighed_kilograms_in_proportion_to_its_invoiced_pieces()
    {
        // Receipt R1: 100 pieces weighed 1 250,400 kg; 40 pieces invoiced -> 1 250,4 x 40 / 100 = 500,160 kg (TL and foreign alike).
        var (order, piece) = PieceOrder(100m, 250m, 12.345m);
        var invoice = Matched(Lira, null, new InvoiceLine(40m, 10000m, PieceShare(order, piece, 40m, 100m, 1250.4m)));

        var match = Assert.Single(invoice.Matches);

        Assert.Equal(500.160m, PurchaseInvoiceKilograms.Weighed(match)!.Value.Round(3));
        Assert.Equal(0, PurchaseInvoiceKilograms.Weighed(match)!.Value.CompareTo(PurchaseExactRatio.Of(500.16m)));
        Assert.Equal(PurchasePieceKilograms.InvoicedKilogramShare(1250.4m, 40m, 100m), PurchaseInvoiceKilograms.Weighed(match)!.Value.Round(3));
        Assert.Equal(493.8m, PurchaseInvoiceKilograms.Theoretical(match));
    }

    [Fact]
    public void A_non_terminating_kilogram_share_stays_exact_until_the_single_final_rounding()
    {
        // 1 000 kg over 3 pieces, 1 invoiced: 333,333... kg — the share is a rational, the kg price is rounded once (TK3).
        var (order, piece) = PieceOrder(3m, 100m, 300m);
        var invoice = Finalized(Matched(Lira, null, new InvoiceLine(1m, 100m, PieceShare(order, piece, 1m, 3m, 1000m, 300m))));

        var share = Assert.Single(PurchaseOrderRealizations.SharesOf(invoice, order.Id));

        Assert.Equal(0, share.WeighedKg!.Value.Multiply(3m).CompareTo(PurchaseExactRatio.Of(1000m)));
        Assert.Equal(0.30m, PurchaseOrderRealizations.Line(order, piece, [share], 2).RealKgPrice.Value);
        Assert.Equal(333.333m, PurchaseOrderRealizations.Line(order, piece, [share], 2).WeighedKg);
    }

    [Fact]
    public void A_piece_line_is_matched_in_whole_pieces_only()
    {
        var (order, piece) = PieceOrder(100m, 250m, 12.345m);

        var refusal = Assert.Throws<PurchasingRuleException>(() =>
            Matched(Lira, null, new InvoiceLine(2.5m, 625m, PieceShare(order, piece, 2.5m, 100m, 1250.4m))));

        Assert.Equal(PurchaseIncomingInvoice.PieceQuantityInvalid, refusal.Code);
    }

    [Fact]
    public void A_kilogram_line_may_be_matched_in_fractions_and_its_kilograms_are_its_quantity()
    {
        var (order, _) = PieceOrder(100m, 250m, 12.345m, kgLine: (500m, 21m));
        var kgLine = order.Lines[1];
        var invoice = Matched(Lira, null, new InvoiceLine(120.5m, 2530.5m, new Share(order.Id, kgLine.Id, 120.5m, 500m, null, null, UnitIsKilogram: true)));

        var match = Assert.Single(invoice.Matches);

        Assert.Equal(120.5m, PurchaseInvoiceKilograms.Theoretical(match));
        Assert.Equal(120.5m, PurchaseInvoiceKilograms.Weighed(match)!.Value.Round(3));
        Assert.Equal(("21.00", "21.00"), (View(invoice).Lines[0].KgPrice!.TheoreticalKgPrice, View(invoice).Lines[0].KgPrice!.RealKgPrice));
    }

    [Fact]
    public void Kilogram_facts_that_contradict_their_line_are_refused_as_a_source_mismatch()
    {
        var (order, piece) = PieceOrder(100m, 250m, 12.345m);

        // A kilogram line never carries the piece figures; a weighed kg is positive.
        Assert.Equal(PurchaseIncomingInvoice.SourceMismatch, Assert.Throws<PurchasingRuleException>(() => Matched(Lira, null,
            new InvoiceLine(40m, 10000m, new Share(order.Id, piece.Id, 40m, 100m, 1250.4m, 12.345m, UnitIsKilogram: true)))).Code);
        Assert.Equal(PurchaseIncomingInvoice.SourceMismatch, Assert.Throws<PurchasingRuleException>(() => Matched(Lira, null,
            new InvoiceLine(40m, 10000m, PieceShare(order, piece, 40m, 100m, 0m)))).Code);
    }

    // ---- the invoice read ------------------------------------------------------------------------------------------------

    [Fact]
    public void K10_a_purchase_invoice_shows_its_theoretical_and_real_kg_price_on_the_line_and_the_document()
    {
        // 200 pieces x 45,00 = 9 000,00; k 2,500 -> 500,000 kg theoretical; weighed at receipt 489,750 kg.
        var (order, piece) = PieceOrder(200m, 45m, 2.5m);
        var view = View(Matched(Lira, null, new InvoiceLine(200m, 9000m, PieceShare(order, piece, 200m, 200m, 489.75m, 2.5m))));

        Assert.Equal(new PurchaseKgPriceView("18.00", null, "18.38", null), Assert.Single(view.Lines).KgPrice);
        Assert.Equal(new PurchaseKgPriceView("18.00", null, "18.38", null), view.KgPrice);
    }

    [Fact]
    public void K02_K03_each_invoice_shows_its_own_kg_prices()
    {
        // Invoice 1: 40 pieces, 10 000,00, weighed 500,160 -> theoretical 20,25, real 19,99. Invoice 2: 60 pieces, 15 000,00,
        // weighed 748,900 -> theoretical 20,25, real 20,03.
        var (order, piece) = PieceOrder(100m, 250m, 12.345m);

        var first = View(Matched(Lira, null, new InvoiceLine(40m, 10000m, PieceShare(order, piece, 40m, 40m, 500.16m))));
        var second = View(Matched(Lira, null, new InvoiceLine(60m, 15000m, PieceShare(order, piece, 60m, 60m, 748.9m))));

        Assert.Equal(("20.25", "19.99"), (first.Lines[0].KgPrice!.TheoreticalKgPrice, first.Lines[0].KgPrice!.RealKgPrice));
        Assert.Equal(("20.25", "20.03"), (second.Lines[0].KgPrice!.TheoreticalKgPrice, second.Lines[0].KgPrice!.RealKgPrice));
    }

    [Fact]
    public void K16_K19_a_foreign_invoice_shows_the_kg_price_in_its_own_currency_and_uses_no_rate()
    {
        // 4 pieces x 100,00 USD = 400,00 USD; theoretical 49,380 kg; weighed 50,016 kg; the invoice rate is irrelevant (SAT-D439).
        var (order, piece) = PieceOrder(10m, 100m, 12.345m, Dollar);
        var atOneRate = View(Matched(Dollar, 34.5678m, new InvoiceLine(4m, 400m, PieceShare(order, piece, 4m, 4m, 50.016m))));
        var atAnother = View(Matched(Dollar, 41m, new InvoiceLine(4m, 400m, PieceShare(order, piece, 4m, 4m, 50.016m))));

        Assert.Equal(new PurchaseKgPriceView("8.10", null, "8.00", null), atOneRate.Lines[0].KgPrice);
        Assert.Equal(atOneRate.Lines[0].KgPrice, atAnother.Lines[0].KgPrice);
    }

    [Fact]
    public void K13_K14_a_missing_recorded_k_or_a_missing_weighing_hides_only_its_own_kg_price()
    {
        var (order, piece) = PieceOrder(100m, 250m, 12.345m);

        var notWeighed = View(Matched(Lira, null, new InvoiceLine(40m, 10000m, PieceShare(order, piece, 40m, 100m, null))));
        var noRecordedK = View(Matched(Lira, null, new InvoiceLine(40m, 10000m, PieceShare(order, piece, 40m, 100m, 1250.4m, null))));

        Assert.Equal(new PurchaseKgPriceView("20.25", null, null, PurchaseKgPriceUnavailableCodes.WeighedKgUnavailable), notWeighed.Lines[0].KgPrice);
        Assert.Equal(new PurchaseKgPriceView(null, PurchaseKgPriceUnavailableCodes.TheoreticalKgUnavailable, "19.99", null), noRecordedK.Lines[0].KgPrice);
    }

    [Fact]
    public void K17_one_line_without_kilograms_hides_the_document_level_and_keeps_every_line_level()
    {
        // A match recorded before AK-3b carries no kilogram facts: its line shows no kg price, the other line still does.
        var (order, piece) = PieceOrder(100m, 250m, 12.345m);
        var view = View(Matched(Lira, null,
            new InvoiceLine(40m, 10000m, PieceShare(order, piece, 40m, 40m, 500.16m)),
            new InvoiceLine(60m, 15000m, new Share(order.Id, piece.Id, 60m, 60m, null, null, WithoutKilogramFacts: true))));

        Assert.Equal(("20.25", "19.99"), (view.Lines[0].KgPrice!.TheoreticalKgPrice, view.Lines[0].KgPrice!.RealKgPrice));
        Assert.Equal(new PurchaseKgPriceView(null, PurchaseKgPriceUnavailableCodes.TheoreticalKgUnavailable, null,
            PurchaseKgPriceUnavailableCodes.WeighedKgUnavailable), view.Lines[1].KgPrice);
        Assert.Equal(new PurchaseKgPriceView(null, PurchaseKgPriceUnavailableCodes.NotAllLinesHaveKg, null,
            PurchaseKgPriceUnavailableCodes.NotAllLinesHaveKg), view.KgPrice);
    }

    [Fact]
    public void K18_a_service_or_expense_line_hides_the_document_level_nothing_is_computed_for_it()
    {
        // N2 is open: a document with a kg-less service line shows no document-level kg price (the goods line keeps its own).
        var goods = new PurchaseInvoiceLineKilograms(Guid.NewGuid(), true, 25000m, 1234.5m, PurchaseExactRatio.Of(1250.4m));
        var service = new PurchaseInvoiceLineKilograms(Guid.NewGuid(), false, 500m, null, null);

        var (theoretical, real) = PurchaseInvoiceKgPrice.Document([goods, service]);

        Assert.Equal((null, PurchaseKgPriceUnavailableCodes.NotAllLinesHaveKg), (theoretical.Value, theoretical.UnavailableCode));
        Assert.Equal((null, PurchaseKgPriceUnavailableCodes.NotAllLinesHaveKg), (real.Value, real.UnavailableCode));
        Assert.Equal(20.25m, PurchaseInvoiceKgPrice.Theoretical(goods).Value);
        Assert.Equal(19.99m, PurchaseInvoiceKgPrice.Real(goods).Value);
        Assert.Equal(PurchaseKgPriceUnavailableCodes.NotAllLinesHaveKg, PurchaseInvoiceKgPrice.Document([]).Real.UnavailableCode);
    }

    [Fact]
    public void An_approved_quantity_variance_leaves_the_line_without_a_kg_price_instead_of_apportioning_its_amount()
    {
        // 40 pieces invoiced but only 38 matched: the official amount does not belong to the matched kilograms.
        var (order, piece) = PieceOrder(100m, 250m, 12.345m);
        var invoice = Finalized(Matched(Lira, null, new InvoiceLine(40m, 10000m, PieceShare(order, piece, 38m, 100m, 1250.4m))));

        Assert.Equal(new PurchaseKgPriceView(null, PurchaseKgPriceUnavailableCodes.TheoreticalKgUnavailable, null,
            PurchaseKgPriceUnavailableCodes.WeighedKgUnavailable), View(invoice).Lines[0].KgPrice);
        var realization = PurchaseOrderRealizations.Line(order, piece, PurchaseOrderRealizations.SharesOf(invoice, order.Id), 2);
        Assert.Equal((38m, (decimal?)null, PurchaseKgPriceUnavailableCodes.AmountUnavailable),
            (realization.InvoicedQuantity!.Value, realization.InvoicedAmount, realization.RealKgPrice.UnavailableCode));
    }

    // ---- the order realization (TK6) ---------------------------------------------------------------------------------------

    [Fact]
    public void K01_an_order_without_a_finalised_invoice_has_a_theoretical_kg_price_and_no_real_one()
    {
        var (order, piece) = PieceOrder(100m, 250m, 12.345m);

        var line = PurchaseOrderKgPriceViews.Line(order, piece, [])!;
        var realization = PurchaseOrderKgPriceViews.LineRealization(order, piece, [])!;

        Assert.Equal(new PurchaseKgPriceView("20.25", null, null, PurchaseKgPriceUnavailableCodes.NotInvoiced), line);
        Assert.Equal(new PurchaseOrderRealizationView("0", "0.00", null, null, null, null, null, PurchaseKgPriceUnavailableCodes.NotInvoiced), realization);
    }

    [Fact]
    public void A_command_response_that_did_not_read_the_invoices_shows_no_realization_and_says_so()
    {
        var (order, piece) = PieceOrder(100m, 250m, 12.345m);

        Assert.Null(PurchaseOrderKgPriceViews.LineRealization(order, piece, null));
        Assert.Null(PurchaseOrderKgPriceViews.DocumentRealization(order, null));
        Assert.Equal(PurchaseKgPriceUnavailableCodes.RealizationNotAvailable, PurchaseOrderKgPriceViews.Line(order, piece)!.RealUnavailable);
        Assert.Equal(PurchaseKgPriceUnavailableCodes.RealizationNotAvailable, PurchaseOrderKgPriceViews.Document(order).RealUnavailable);
    }

    [Fact]
    public void K02_a_partial_invoice_realizes_its_pieces_amount_weighed_kg_real_kg_price_and_theoretical_difference()
    {
        // 40 of 100 pieces: 10 000,00 TL; weighed 500,160 kg; theoretical 493,800 kg; +6,360 kg (+1,28797... % -> 1,29); real 19,99.
        var (order, piece) = PieceOrder(100m, 250m, 12.345m);
        var shares = PurchaseOrderRealizations.SharesOf(
            Finalized(Matched(Lira, null, new InvoiceLine(40m, 10000m, PieceShare(order, piece, 40m, 40m, 500.16m)))), order.Id);

        var expected = new PurchaseOrderRealizationView("40", "10000.00", "500.16", "493.8", "6.36", "1.29", "19.99", null);
        Assert.Equal(expected, PurchaseOrderKgPriceViews.LineRealization(order, piece, shares));
        Assert.Equal(expected with { InvoicedQuantity = null }, PurchaseOrderKgPriceViews.DocumentRealization(order, shares));
        Assert.Equal(new PurchaseKgPriceView("20.25", null, "19.99", null), PurchaseOrderKgPriceViews.Line(order, piece, shares));
        Assert.Equal(new PurchaseKgPriceView("20.25", null, "19.99", null), PurchaseOrderKgPriceViews.Document(order, shares));
    }

    [Fact]
    public void K03_the_order_real_kg_price_is_total_amount_over_total_weighed_kg_never_an_average_of_invoice_kg_prices()
    {
        // 25 000 / (500,160 + 748,900 = 1 249,060) = 20,02 (the average (19,99 + 20,03) / 2 = 20,01 OLMAZ); +14,560 kg, +1,18 %.
        var (order, piece) = PieceOrder(100m, 250m, 12.345m);
        var shares = PurchaseOrderRealizations.SharesOf(
                Finalized(Matched(Lira, null, new InvoiceLine(40m, 10000m, PieceShare(order, piece, 40m, 40m, 500.16m)))), order.Id)
            .Concat(PurchaseOrderRealizations.SharesOf(
                Finalized(Matched(Lira, null, new InvoiceLine(60m, 15000m, PieceShare(order, piece, 60m, 60m, 748.9m)))), order.Id)).ToArray();

        var realization = PurchaseOrderKgPriceViews.LineRealization(order, piece, shares)!;

        Assert.Equal(new PurchaseOrderRealizationView("100", "25000.00", "1249.06", "1234.5", "14.56", "1.18", "20.02", null), realization);
        Assert.NotEqual("20.01", realization.RealKgPrice);
    }

    [Fact]
    public void K04_a_weighing_deviation_changes_the_real_kg_price_but_never_the_invoiced_amount()
    {
        // 100 pieces in one invoice, weighed 1 209,810 kg (-2 %): amount stays 25 000,00; theoretical 20,25, real 20,66.
        var (order, piece) = PieceOrder(100m, 250m, 12.345m);
        var shares = PurchaseOrderRealizations.SharesOf(
            Finalized(Matched(Lira, null, new InvoiceLine(100m, 25000m, PieceShare(order, piece, 100m, 100m, 1209.81m)))), order.Id);

        var realization = PurchaseOrderKgPriceViews.LineRealization(order, piece, shares)!;

        Assert.Equal(("25000.00", "20.66", "-24.69", "-2.00"),
            (realization.InvoicedAmount, realization.RealKgPrice, realization.DifferenceKg, realization.DeviationPercent));
        Assert.Equal("20.25", PurchaseOrderKgPriceViews.Line(order, piece, shares)!.TheoreticalKgPrice);
    }

    [Fact]
    public void K14_an_invoiced_share_without_a_weighing_shows_its_amount_and_no_real_kg_price()
    {
        var (order, piece) = PieceOrder(100m, 250m, 12.345m);
        var shares = PurchaseOrderRealizations.SharesOf(
            Finalized(Matched(Lira, null, new InvoiceLine(40m, 10000m, PieceShare(order, piece, 40m, 100m, null)))), order.Id);

        Assert.Equal(new PurchaseOrderRealizationView("40", "10000.00", null, "493.8", null, null, null,
            PurchaseKgPriceUnavailableCodes.WeighedKgUnavailable), PurchaseOrderKgPriceViews.LineRealization(order, piece, shares));
    }

    [Fact]
    public void Only_finalised_invoices_count_toward_the_realization()
    {
        // The single named rule (NEEDS-SINAN): recorded or matched invoices do not count; finance-pending and finance-accepted do.
        Assert.False(PurchaseInvoiceRealizationRule.CountsTowardRealization(PurchaseIncomingInvoiceStatus.Recorded));
        Assert.False(PurchaseInvoiceRealizationRule.CountsTowardRealization(PurchaseIncomingInvoiceStatus.Matched));
        Assert.True(PurchaseInvoiceRealizationRule.CountsTowardRealization(PurchaseIncomingInvoiceStatus.FinancePending));
        Assert.True(PurchaseInvoiceRealizationRule.CountsTowardRealization(PurchaseIncomingInvoiceStatus.FinanceAccepted));

        var (order, piece) = PieceOrder(100m, 250m, 12.345m);
        var invoice = Matched(Lira, null, new InvoiceLine(40m, 10000m, PieceShare(order, piece, 40m, 40m, 500.16m)));
        Assert.Empty(PurchaseOrderRealizations.SharesOf(invoice, order.Id));

        Finalized(invoice);
        Assert.Single(PurchaseOrderRealizations.SharesOf(invoice, order.Id));
        Assert.Empty(PurchaseOrderRealizations.SharesOf(invoice, Guid.NewGuid()));

        invoice.AcknowledgeFinance(invoice.Version, invoice.AdmissionId!.Value, T0);
        Assert.Single(PurchaseOrderRealizations.SharesOf(invoice, order.Id));
    }

    [Fact]
    public void K08_K18_the_order_document_level_follows_the_interim_rule_a_maturity_surcharge_hides_it()
    {
        // K08: a piece line and a kg line, both plain goods lines with kg -> 35 500 / 1 734,5 = 20,47.
        var (plain, _) = PieceOrder(100m, 250m, 12.345m, kgLine: (500m, 21m));
        Assert.Equal(("20.47", (string?)null), (PurchaseOrderKgPriceViews.Document(plain, []).TheoreticalKgPrice,
            PurchaseOrderKgPriceViews.Document(plain, []).TheoreticalUnavailable));

        // K18 (N2 open): a line with vade farki — the document level is hidden, the line level is still shown.
        var (deferred, piece) = PieceOrder(100m, 250m, 12.345m, deferralPercent: 2m);
        var shares = PurchaseOrderRealizations.SharesOf(
            Finalized(Matched(Lira, null, new InvoiceLine(40m, 10200m, PieceShare(deferred, piece, 40m, 40m, 500.16m)))), deferred.Id);

        var document = PurchaseOrderKgPriceViews.Document(deferred, shares);
        Assert.Equal((null, PurchaseKgPriceUnavailableCodes.NotAllLinesHaveKg, null, PurchaseKgPriceUnavailableCodes.NotAllLinesHaveKg),
            (document.TheoreticalKgPrice, document.TheoreticalUnavailable, document.RealKgPrice, document.RealUnavailable));
        Assert.NotNull(PurchaseOrderKgPriceViews.Line(deferred, piece, shares)!.TheoreticalKgPrice);
        Assert.Equal("10200.00", PurchaseOrderKgPriceViews.DocumentRealization(deferred, shares)!.InvoicedAmount);
        Assert.Null(PurchaseOrderKgPriceViews.DocumentRealization(deferred, shares)!.RealKgPrice);
    }

    // ---- golden EK1 §4 (SAT-D452): only finalised invoices, row ids in the names ---------------------------------------------

    [Fact]
    public void E16_E17_a_draft_invoice_does_not_count_until_it_is_finalised()
    {
        var (order, piece) = PieceOrder(100m, 250m, 12.345m);
        var first = Finalized(Matched(Lira, null, new InvoiceLine(40m, 10000m, PieceShare(order, piece, 40m, 40m, 500.16m))));
        var second = Matched(Lira, null, new InvoiceLine(60m, 15000m, PieceShare(order, piece, 60m, 60m, 748.9m)));
        PurchaseOrderInvoiceShare[] Shares() => [.. PurchaseOrderRealizations.SharesOf(first, order.Id), .. PurchaseOrderRealizations.SharesOf(second, order.Id)];

        // E16: invoice 2 is not finalised -> 40/100 pieces, 10 000,00, 500,160 kg, 19,99 (20,02 with the draft OLMAZ); theoretical 20,25.
        var partial = PurchaseOrderKgPriceViews.LineRealization(order, piece, Shares())!;
        Assert.Equal(("40", "10000.00", "500.16", "19.99"), (partial.InvoicedQuantity, partial.InvoicedAmount, partial.WeighedKg, partial.RealKgPrice));
        Assert.Equal("20.25", PurchaseOrderKgPriceViews.Line(order, piece, Shares())!.TheoreticalKgPrice);

        // E17: both finalised -> 100/100 pieces, 20,02.
        Finalized(second);
        var full = PurchaseOrderKgPriceViews.LineRealization(order, piece, Shares())!;
        Assert.Equal(("100", "20.02"), (full.InvoicedQuantity, full.RealKgPrice));
    }

    [Fact]
    public void E19_E20_with_only_a_draft_invoice_there_is_no_real_kg_price_and_the_theoretical_one_stays()
    {
        // E19: sales-shaped numbers; E20: purchase K10 (200 x 45,00, k 2,500, weighed 489,750) -> theoretical 18,00, real 18,38 once finalised.
        var (order, piece) = PieceOrder(200m, 45m, 2.5m);
        var invoice = Matched(Lira, null, new InvoiceLine(200m, 9000m, PieceShare(order, piece, 200m, 200m, 489.75m, 2.5m)));

        var draft = PurchaseOrderKgPriceViews.Line(order, piece, PurchaseOrderRealizations.SharesOf(invoice, order.Id))!;
        Assert.Equal(("18.00", (string?)null, PurchaseKgPriceUnavailableCodes.NotInvoiced), (draft.TheoreticalKgPrice, draft.RealKgPrice, draft.RealUnavailable));

        Finalized(invoice);
        Assert.Equal("18.38", PurchaseOrderKgPriceViews.Line(order, piece, PurchaseOrderRealizations.SharesOf(invoice, order.Id))!.RealKgPrice);
    }

    // E18 (an invoice cancelled after finalisation) and E21/E21a (a Finance rejection, SAT-D457) are sales-invoice states: the
    // purchase incoming invoice has no cancelled or rejected status, so they have no purchase fact here. The single rule
    // (PurchaseInvoiceRealizationRule) is where such a state must be excluded if it is ever added.

    [Fact]
    public void ALS_D122_a_piece_share_matched_after_a_return_shows_no_real_kg_instead_of_the_ALS_D121_figure()
    {
        var (order, piece) = PieceOrder(100m, 250m, 12.345m);
        var invoice = Matched(Lira, null, new InvoiceLine(40m, 10000m, PieceShare(order, piece, 40m, 100m, 1250.4m)));
        var returned = Assert.Single(invoice.Matches) with { ReceiptAdjustments = [new(Guid.NewGuid(), 20m, true)] };

        // Open (default): no real kg — Purchasing does not hold the returned kilograms; the theoretical kg is unaffected.
        Assert.True(PurchasePieceKilograms.RemainingShareGoldenAccepted);
        Assert.Null(PurchaseInvoiceKilograms.Weighed(returned));
        Assert.Equal(493.8m, PurchaseInvoiceKilograms.Theoretical(returned));
        // Closed (pinned by parameter): the earlier ALS-D121 figure.
        Assert.Equal(500.160m, PurchaseInvoiceKilograms.Weighed(returned, remainingShareAccepted: false)!.Value.Round(3));
        // Without a return both states agree.
        Assert.Equal(500.160m, PurchaseInvoiceKilograms.Weighed(Assert.Single(invoice.Matches))!.Value.Round(3));
    }

    // ---- wire and event shape --------------------------------------------------------------------------------------------

    [Fact]
    public void The_kilogram_facts_round_trip_through_the_invoice_history_and_older_matches_stay_readable()
    {
        var (order, piece) = PieceOrder(100m, 250m, 12.345m);
        var invoice = Finalized(Matched(Lira, null, new InvoiceLine(40m, 10000m, PieceShare(order, piece, 40m, 100m, 1250.4m))));

        var json = JsonSerializer.Serialize(PurchaseIncomingInvoiceSnapshot.Capture(invoice), PurchasingJson.Options);
        var restored = JsonSerializer.Deserialize<PurchaseIncomingInvoiceSnapshot>(json, PurchasingJson.Options)!.Restore();

        Assert.Equal(new PurchaseInvoiceMatchKilograms(false, 1250.4m, 12.345m), Assert.Single(restored.Matches).Kilograms);
        var legacy = Matched(Lira, null, new InvoiceLine(40m, 10000m, new Share(order.Id, piece.Id, 40m, 100m, null, null, WithoutKilogramFacts: true)));
        Assert.DoesNotContain("kilograms", JsonSerializer.Serialize(PurchaseIncomingInvoiceSnapshot.Capture(legacy), PurchasingJson.Options),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_finalised_event_names_the_weighed_kilograms_of_a_piece_share_and_nothing_for_a_kilogram_share()
    {
        var (order, piece) = PieceOrder(100m, 250m, 12.345m, kgLine: (500m, 21m));
        var pieceInvoice = Finalized(Matched(Lira, null, new InvoiceLine(40m, 10000m, PieceShare(order, piece, 40m, 100m, 1250.4m))));
        var kgInvoice = Finalized(Matched(Lira, null,
            new InvoiceLine(120.5m, 2530.5m, new Share(order.Id, order.Lines[1].Id, 120.5m, 500m, null, null, UnitIsKilogram: true))));

        var pieceShare = Assert.Single(Assert.Single(PurchaseIncomingInvoiceFinalizationV1.Event(pieceInvoice).Lines).GoodsCostSource!.Receipts);
        var kgEvent = PurchaseIncomingInvoiceFinalizationV1.Event(kgInvoice);

        Assert.Equal((40m, 100m, (decimal?)1250.4m), (pieceShare.MatchedQuantity, pieceShare.AcceptedQuantity, pieceShare.MeasuredQuantityKg));
        Assert.Null(Assert.Single(Assert.Single(kgEvent.Lines).GoodsCostSource!.Receipts).MeasuredQuantityKg);
        Assert.DoesNotContain("measuredQuantityKg", JsonSerializer.Serialize(kgEvent, PurchasingJson.Options), StringComparison.Ordinal);
    }
}
