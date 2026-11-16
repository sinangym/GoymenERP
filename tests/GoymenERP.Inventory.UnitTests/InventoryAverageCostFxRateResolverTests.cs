using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// BS-1 / SAT-D295 (STK-S25): a foreign-currency purchase cost enters the TL moving average only with the supplier invoice's
/// official rate. Until the invoice is recorded the TL cost is Unavailable and the position answers the sticky
/// <see cref="AverageCostUnreadableReason.ForeignCurrency"/>: no provisional rate, no correction movement. The foreign value and
/// its TL equivalent are held together; the conversion is exact and the average core rounds once. SAT-D306 (b): a purchase return
/// whose TL cost is Unavailable is Unavailable too. Codes and numbers are pinned. SENTETIK / NOT_ENV.
/// </summary>
public sealed class InventoryAverageCostFxRateResolverTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid CompanyB = Guid.Parse("11111111-1111-4111-8111-111111111112");
    private static readonly Guid Receipt = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid OtherReceipt = Guid.Parse("44444444-4444-4444-8444-444444444445");
    private static readonly Guid Order = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid OrderLine = Guid.Parse("55555555-5555-4555-8555-555555555556");
    private static readonly Guid Usd = Guid.Parse("66666666-6666-4666-8666-666666666661");
    private static readonly Guid Eur = Guid.Parse("66666666-6666-4666-8666-666666666662");
    private static readonly Guid Lira = SettingsCurrencyIdentitiesV1.TurkishLira;

    [Fact]
    public void Codes_numbers_and_the_cost_currency_are_pinned()
    {
        Assert.Equal(Lira, InventoryAverageCostFxRateResolverV1.CostCurrencyId);
        Assert.Equal((1, 2, 3), ((int)InventoryPurchaseInvoiceCostRateStatusV1.Rate,
            (int)InventoryPurchaseInvoiceCostRateStatusV1.Unavailable, (int)InventoryPurchaseInvoiceCostRateStatusV1.NotFound));
        Assert.Equal((1, 2, 3), ((int)InventoryAverageCostFxRateStatusV1.BaseCurrency,
            (int)InventoryAverageCostFxRateStatusV1.Converted, (int)InventoryAverageCostFxRateStatusV1.Unavailable));
        Assert.Equal(["purchase_invoice_not_recorded", "purchase_invoice_rate_unreadable"],
            InventoryPurchaseInvoiceCostRateReasonsV1.All);
        Assert.Equal(
            [
                "purchase_invoice_not_recorded", "purchase_invoice_rate_unreadable", "purchase_invoice_rate_not_read",
                "purchase_receipt_not_found", "purchase_invoice_rate_mismatch", "purchase_invoice_rate_not_representable",
            ],
            InventoryAverageCostFxRateUnavailableCodesV1.All);
        Assert.Equal(3, (int)AverageCostUnreadableReason.ForeignCurrency);
        Assert.Equal("foreign_currency_undecided", InventoryAverageCostUnreadableCodesV1.ForeignCurrency);
    }

    [Fact]
    public void A_lira_cost_needs_no_rate_and_enters_the_average_as_given()
    {
        var result = InventoryAverageCostFxRateResolverV1.Resolve(CompanyA, Receipt, Lira, 80m, invoiceRate: null);

        Assert.Equal((InventoryAverageCostFxRateStatusV1.BaseCurrency, Lira, 80m, (decimal?)80m),
            (result.Status, result.SourceCurrencyId, result.SourceUnitCostPerStockKg, result.BaseUnitCostPerStockKg));
        Assert.Null(result.Rate);
        Assert.Null(result.UnavailableCode);

        var position = AverageCostCalculator.Apply(null, result.ToPurchaseReceiptMovement(100m, 0m));
        Assert.Equal((AverageCostStatus.Known, (decimal?)100m, (decimal?)80m), (position.Status, position.QuantityKg, position.AverageCostPerKg));
    }

    [Fact]
    public void A_lira_cost_ignores_any_rate_answer_even_an_unavailable_one()
    {
        var result = InventoryAverageCostFxRateResolverV1.Resolve(CompanyA, Receipt, Lira, 80m, NotRecorded(Lira));

        Assert.Equal(InventoryAverageCostFxRateStatusV1.BaseCurrency, result.Status);
        Assert.Equal(80m, result.BaseUnitCostPerStockKg);
    }

    [Fact]
    public void A_foreign_cost_converts_exactly_with_the_invoice_rate_and_keeps_both_values()
    {
        var rate = ExchangeRate.Of(Usd, Lira, 34.56789m);
        var result = InventoryAverageCostFxRateResolverV1.Resolve(CompanyA, Receipt, Usd, 1.23m, Rated(rate));

        Assert.Equal(InventoryAverageCostFxRateStatusV1.Converted, result.Status);
        Assert.Equal((Usd, 1.23m), (result.SourceCurrencyId, result.SourceUnitCostPerStockKg));
        Assert.Equal(42.5185047m, result.BaseUnitCostPerStockKg);
        Assert.Equal(rate, result.Rate);
        Assert.Null(result.UnavailableCode);
    }

    [Fact]
    public void The_converted_cost_is_rounded_once_at_the_average_never_before()
    {
        var converted = InventoryAverageCostFxRateResolverV1.Resolve(CompanyA, Receipt, Usd, 1.23m, Rated(ExchangeRate.Of(Usd, Lira, 34.56789m)));

        var first = AverageCostCalculator.Apply(null, converted.ToPurchaseReceiptMovement(100m, 0m));
        Assert.Equal((AverageCostStatus.Known, (decimal?)100m, (decimal?)42.5185m), (first.Status, first.QuantityKg, first.AverageCostPerKg));

        // (100 · 42.5185 + 100 · 42.5185047) / 200 = 42.51850235 → round4 → 42.5185: the exact TL cost blends, rounded once.
        var second = AverageCostCalculator.Apply(first, converted.ToPurchaseReceiptMovement(100m, 100m));
        Assert.Equal((AverageCostStatus.Known, (decimal?)200m, (decimal?)42.5185m), (second.Status, second.QuantityKg, second.AverageCostPerKg));
    }

    [Fact]
    public void Until_the_invoice_is_recorded_the_lira_cost_is_unavailable_and_the_position_stays_unreadable()
    {
        var known = AverageCostCalculator.Apply(null,
            InventoryAverageCostFxRateResolverV1.Resolve(CompanyA, OtherReceipt, Lira, 80m, null).ToPurchaseReceiptMovement(100m, 0m));
        Assert.Equal(AverageCostStatus.Known, known.Status);

        var pending = InventoryAverageCostFxRateResolverV1.Resolve(CompanyA, Receipt, Usd, 2.5m, NotRecorded(Usd));
        Assert.Equal((InventoryAverageCostFxRateStatusV1.Unavailable, Usd, 2.5m, "purchase_invoice_not_recorded"),
            (pending.Status, pending.SourceCurrencyId, pending.SourceUnitCostPerStockKg, pending.UnavailableCode));
        Assert.Null(pending.BaseUnitCostPerStockKg);
        Assert.Null(pending.Rate);

        var movement = pending.ToPurchaseReceiptMovement(50m, 100m);
        Assert.Equal((AverageCostMovementKind.PurchaseReceipt, false, (decimal?)2.5m),
            (movement.Kind, movement.IsBaseCurrency, movement.UnitCostPerStockKg));

        var unreadable = AverageCostCalculator.Apply(known, movement);
        Assert.Equal((AverageCostStatus.Unreadable, (AverageCostUnreadableReason?)AverageCostUnreadableReason.ForeignCurrency),
            (unreadable.Status, unreadable.UnreadableReason));

        // No provisional rate and no correction: a later lira receipt does not reopen the position by itself.
        var later = AverageCostCalculator.Apply(unreadable,
            InventoryAverageCostFxRateResolverV1.Resolve(CompanyA, OtherReceipt, Lira, 90m, null).ToPurchaseReceiptMovement(10m, 150m));
        Assert.Equal((AverageCostStatus.Unreadable, (AverageCostUnreadableReason?)AverageCostUnreadableReason.ForeignCurrency),
            (later.Status, later.UnreadableReason));
    }

    [Fact]
    public void A_purchase_return_whose_lira_cost_is_unavailable_is_unavailable_too()
    {
        var known = AverageCostPosition.Known(100m, 80m);
        var pending = InventoryAverageCostFxRateResolverV1.Resolve(CompanyA, Receipt, Usd, 2.5m, NotRecorded(Usd));

        var movement = pending.ToPurchaseReturnMovement(10m, 100m);
        Assert.Equal((AverageCostMovementKind.PurchaseReturn, false), (movement.Kind, movement.IsBaseCurrency));
        var after = AverageCostCalculator.Apply(known, movement);
        Assert.Equal((AverageCostStatus.Unreadable, (AverageCostUnreadableReason?)AverageCostUnreadableReason.ForeignCurrency),
            (after.Status, after.UnreadableReason));

        // With the invoice rate the return leaves at the converted lira cost: (100 · 80 − 10 · 85) / 90 = 79.4444…
        var converted = InventoryAverageCostFxRateResolverV1.Resolve(CompanyA, Receipt, Usd, 2.5m, Rated(ExchangeRate.Of(Usd, Lira, 34m)));
        var returned = AverageCostCalculator.Apply(known, converted.ToPurchaseReturnMovement(10m, 100m));
        Assert.Equal((AverageCostStatus.Known, (decimal?)90m, (decimal?)79.4444m), (returned.Status, returned.QuantityKg, returned.AverageCostPerKg));
    }

    public static TheoryData<string, string> UnavailableAnswers => new()
    {
        { "not-read", "purchase_invoice_rate_not_read" },
        { "not-found", "purchase_receipt_not_found" },
        { "owner-unreadable", "purchase_invoice_rate_unreadable" },
        { "another-company", "purchase_invoice_rate_mismatch" },
        { "another-receipt", "purchase_invoice_rate_mismatch" },
        { "rate-not-into-lira", "purchase_invoice_rate_mismatch" },
        { "rate-from-another-currency", "purchase_invoice_rate_mismatch" },
    };

    [Theory]
    [MemberData(nameof(UnavailableAnswers))]
    public void A_foreign_cost_without_a_trusted_invoice_rate_is_unavailable_never_guessed(string answer, string expectedCode)
    {
        InventoryPurchaseInvoiceCostRateV1? invoiceRate = answer switch
        {
            "not-read" => null,
            "not-found" => InventoryPurchaseInvoiceCostRateV1.NotFound(CompanyA, Receipt),
            "owner-unreadable" => InventoryPurchaseInvoiceCostRateV1.Unavailable(CompanyA, Receipt, null, null, null,
                InventoryPurchaseInvoiceCostRateReasonsV1.Unreadable),
            "another-company" => InventoryPurchaseInvoiceCostRateV1.Rated(CompanyB, Receipt, Order, OrderLine, Usd, ExchangeRate.Of(Usd, Lira, 34m)),
            "another-receipt" => InventoryPurchaseInvoiceCostRateV1.Rated(CompanyA, OtherReceipt, Order, OrderLine, Usd, ExchangeRate.Of(Usd, Lira, 34m)),
            "rate-not-into-lira" => Rated(ExchangeRate.Of(Usd, Eur, 0.9m)),
            "rate-from-another-currency" => InventoryPurchaseInvoiceCostRateV1.Rated(CompanyA, Receipt, Order, OrderLine, Eur,
                ExchangeRate.Of(Eur, Lira, 37m)),
            _ => throw new ArgumentOutOfRangeException(nameof(answer)),
        };

        var result = InventoryAverageCostFxRateResolverV1.Resolve(CompanyA, Receipt, Usd, 2.5m, invoiceRate);

        Assert.Equal((InventoryAverageCostFxRateStatusV1.Unavailable, expectedCode), (result.Status, result.UnavailableCode));
        Assert.Null(result.BaseUnitCostPerStockKg);
        Assert.Null(result.Rate);
        Assert.Equal((Usd, 2.5m), (result.SourceCurrencyId, result.SourceUnitCostPerStockKg));
        Assert.False(result.ToPurchaseReceiptMovement(1m, 0m).IsBaseCurrency);
    }

    [Fact]
    public void A_converted_cost_that_is_not_exactly_representable_is_refused_not_rounded()
    {
        var result = InventoryAverageCostFxRateResolverV1.Resolve(CompanyA, Receipt, Usd, 0.12345678901234567891m,
            Rated(ExchangeRate.Of(Usd, Lira, 1.1234567891m)));

        Assert.Equal((InventoryAverageCostFxRateStatusV1.Unavailable, "purchase_invoice_rate_not_representable"),
            (result.Status, result.UnavailableCode));
        Assert.Null(result.BaseUnitCostPerStockKg);
    }

    [Fact]
    public void Resolve_refuses_empty_identities_and_a_negative_cost()
    {
        Assert.Throws<ArgumentException>(() => InventoryAverageCostFxRateResolverV1.Resolve(Guid.Empty, Receipt, Usd, 1m, null));
        Assert.Throws<ArgumentException>(() => InventoryAverageCostFxRateResolverV1.Resolve(CompanyA, Guid.Empty, Usd, 1m, null));
        Assert.Throws<ArgumentException>(() => InventoryAverageCostFxRateResolverV1.Resolve(CompanyA, Receipt, Guid.Empty, 1m, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => InventoryAverageCostFxRateResolverV1.Resolve(CompanyA, Receipt, Usd, -0.01m, null));
    }

    [Fact]
    public void The_invoice_rate_answer_is_built_only_in_combinations_that_hold_together()
    {
        var notFound = InventoryPurchaseInvoiceCostRateV1.NotFound(CompanyA, Receipt);
        Assert.Equal((InventoryPurchaseInvoiceCostRateStatusV1.NotFound, CompanyA, Receipt), (notFound.Status, notFound.CompanyId, notFound.GoodsReceiptLineId));
        Assert.Null(notFound.PurchaseOrderId);
        Assert.Null(notFound.Rate);
        Assert.Null(notFound.UnavailableReason);

        var notRecorded = NotRecorded(Usd);
        Assert.Equal((InventoryPurchaseInvoiceCostRateStatusV1.Unavailable, (Guid?)Order, (Guid?)OrderLine, (Guid?)Usd, "purchase_invoice_not_recorded"),
            (notRecorded.Status, notRecorded.PurchaseOrderId, notRecorded.PurchaseOrderLineId, notRecorded.OrderCurrencyId, notRecorded.UnavailableReason));
        Assert.Null(notRecorded.Rate);

        var rated = Rated(ExchangeRate.Of(Usd, Lira, 34m));
        Assert.Equal((InventoryPurchaseInvoiceCostRateStatusV1.Rate, (Guid?)Usd), (rated.Status, rated.OrderCurrencyId));
        Assert.Null(rated.UnavailableReason);

        Assert.Throws<ArgumentException>(() => InventoryPurchaseInvoiceCostRateV1.NotFound(Guid.Empty, Receipt));
        Assert.Throws<ArgumentException>(() => InventoryPurchaseInvoiceCostRateV1.Unavailable(CompanyA, Receipt, Order, OrderLine, Usd, "provisional_rate"));
        Assert.Throws<ArgumentException>(() => InventoryPurchaseInvoiceCostRateV1.Unavailable(CompanyA, Receipt, Order, null, Usd,
            InventoryPurchaseInvoiceCostRateReasonsV1.InvoiceNotRecorded));
        Assert.Throws<ArgumentException>(() => InventoryPurchaseInvoiceCostRateV1.Unavailable(CompanyA, Receipt, Order, OrderLine, Guid.Empty,
            InventoryPurchaseInvoiceCostRateReasonsV1.InvoiceNotRecorded));
        Assert.Throws<ArgumentException>(() => InventoryPurchaseInvoiceCostRateV1.Rated(CompanyA, Receipt, Order, OrderLine, Eur,
            ExchangeRate.Of(Usd, Lira, 34m)));
        Assert.Throws<ArgumentException>(() => InventoryPurchaseInvoiceCostRateV1.Rated(CompanyA, Guid.Empty, Order, OrderLine, Usd,
            ExchangeRate.Of(Usd, Lira, 34m)));
    }

    private static InventoryPurchaseInvoiceCostRateV1 NotRecorded(Guid orderCurrency) =>
        InventoryPurchaseInvoiceCostRateV1.Unavailable(CompanyA, Receipt, Order, OrderLine, orderCurrency,
            InventoryPurchaseInvoiceCostRateReasonsV1.InvoiceNotRecorded);

    private static InventoryPurchaseInvoiceCostRateV1 Rated(ExchangeRate rate) =>
        InventoryPurchaseInvoiceCostRateV1.Rated(CompanyA, Receipt, Order, OrderLine, rate.SourceCurrencyId, rate);
}
