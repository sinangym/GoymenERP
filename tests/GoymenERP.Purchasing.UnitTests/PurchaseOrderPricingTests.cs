using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Quantities;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// A2a-1 (TEK-01, ALS-D5, ALS-D18, ALS-D51/D58/D60): the shared line price calculator's golden values, the order's price rules
/// (each refused with its stable code, nothing added), an unpriced order that is still approvable and receivable, a technical
/// slice that carries no price, and the stored price rebuilt through the domain. SENTETIK / NOT_ENV.
/// </summary>
public sealed class PurchaseOrderPricingTests
{
    private static readonly Guid Company = Guid.Parse("91000000-0000-0000-0000-000000000001");
    private static readonly Guid Supplier = Guid.Parse("92000000-0000-0000-0000-000000000001");
    private static readonly Guid Product = Guid.Parse("93000000-0000-0000-0000-000000000001");
    private static readonly Guid Kg = Guid.Parse("94000000-0000-0000-0000-000000000001");
    private static readonly Guid Currency = Guid.Parse("95000000-0000-0000-0000-000000000001");
    private static readonly Guid Profile = Guid.Parse("96000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset At = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);
    private static readonly PurchaseOrderMoney Lira = PurchaseOrderMoney.Of(Currency, "TRY", Profile, 1, 2);

    // ---- the shared calculator (SAT-D11, SAT-D76: the same rules as the Sales line calculator) ----------------------------------

    [Theory]
    [InlineData("100", null, null, "100")]
    [InlineData("100", "12.5", null, "87.5")]
    [InlineData("41.25", "2.5", null, "40.21875")]
    [InlineData("33.33", "33.333333", null, "22.2200001111")]
    [InlineData("100", null, "0.01", "99.99")]
    [InlineData("100", "100", null, "0")]
    [InlineData("100", null, "100", "0")]
    public void The_net_unit_price_is_exact_and_never_rounded(string price, string? percent, string? amount, string expected)
    {
        var net = CommercialLinePriceCalculator.NetUnitPrice(decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture),
            Parse(percent), Parse(amount));

        Assert.Equal(expected, CommercialLinePriceCalculator.Format(net));
    }

    [Theory]
    [InlineData("0.5", "10.01", 2, "5.01")]      // 5.005 → 5.01: half away from zero, never the banker's 5.00
    [InlineData("0.5", "10.03", 2, "5.02")]      // 5.015 → 5.02
    [InlineData("1250.5", "40.21875", 2, "50293.55")]
    [InlineData("3", "0.333333", 2, "1")]        // 0.999999 → 1.00, printed without trailing zeros
    [InlineData("0.001", "0.01", 2, "0")]        // 0.00001 → 0
    [InlineData("7", "1.5", 0, "11")]            // 10.5 → 11 at scale 0
    public void The_line_amount_is_quantity_times_the_exact_net_rounded_once_half_away_from_zero(string quantity, string price, int scale,
        string expected)
    {
        var amount = CommercialLinePriceCalculator.LineAmount(decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture), null, null, scale);

        Assert.Equal(expected, CommercialLinePriceCalculator.Format(amount));
    }

    [Fact]
    public void A_product_beyond_SystemDecimals_exact_digits_is_computed_exactly_before_the_one_rounding()
    {
        // 999999999999999.999 × (0.333333 − 33.333333 %) = 222222001111109.99977777799888889: the exact product needs more than
        // 28 significant digits, so a decimal multiplication would round it silently before the money rounding. Here it is exact,
        // then rounded once (→ 222222001111110.00).
        var amount = CommercialLinePriceCalculator.LineAmount(999999999999999.999m, 0.333333m, 33.333333m, null, 2);

        Assert.Equal("222222001111110", CommercialLinePriceCalculator.Format(amount));
        Assert.Throws<OverflowException>(() => CommercialLinePriceCalculator.LineAmount(decimal.MaxValue, 2m, null, null, 2));
    }

    [Fact]
    public void The_calculator_refuses_what_it_cannot_compute_but_chooses_no_policy()
    {
        Assert.Throws<ArgumentException>(() => CommercialLinePriceCalculator.NetUnitPrice(10m, 1m, 1m));
        Assert.Throws<ArgumentOutOfRangeException>(() => CommercialLinePriceCalculator.NetUnitPrice(10m, 101m, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => CommercialLinePriceCalculator.NetUnitPrice(10m, null, 11m));
        Assert.Throws<ArgumentOutOfRangeException>(() => CommercialLinePriceCalculator.NetUnitPrice(-1m, null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => CommercialLinePriceCalculator.LineAmount(-1m, 1m, null, null, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => CommercialLinePriceCalculator.LineAmount(1m, 1m, null, null, 29));
    }

    // ---- the order ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_priced_line_keeps_its_price_and_an_unpriced_order_is_still_approved_and_received()
    {
        var priced = PurchaseOrder.Create(Guid.NewGuid(), Company, Supplier, Lira, At);
        var line = priced.AddLine(1, Guid.NewGuid(), Product, Kg, Kilos(10m), null, new PurchaseOrderLinePrice(12.5m, Kg, 10m), At);
        Assert.Equal((12.5m, 11.25m, 112.5m), (line.Price!.UnitPrice, line.Price.NetUnitPrice, line.Price.LineAmount(line.Quantity.Value, Lira)));
        Assert.Equal(Lira, priced.Money);

        // ALS-D18: no currency, no price — approved and received as usual.
        var unpriced = PurchaseOrder.Create(Guid.NewGuid(), Company, Supplier, At);
        var open = unpriced.AddLine(1, Guid.NewGuid(), Product, Kg, Kilos(10m), At);
        unpriced.Approve(2, At);
        unpriced.RecordAcceptance(3, Guid.NewGuid(), open.Id, Kilos(4m), At);
        Assert.Equal((PurchaseOrderStatus.Approved, (PurchaseOrderLinePrice?)null, (PurchaseOrderMoney?)null), (unpriced.Status, open.Price, unpriced.Money));
    }

    [Fact]
    public void A_technical_slice_carries_no_price_its_quantity_is_priced_on_the_general_line()
    {
        var order = PurchaseOrder.Create(Guid.NewGuid(), Company, Supplier, Lira, At);
        var general = order.AddLine(1, Guid.NewGuid(), Product, Kg, Kilos(1000m), null, new PurchaseOrderLinePrice(2m, Kg), At);

        var technical = order.BreakDown(2, general.Id, Guid.NewGuid(), Guid.NewGuid(), Kilos(75m), At);

        Assert.Null(technical.Price);
        Assert.Equal(2000m, general.Price!.LineAmount(general.Quantity.Value, Lira));
    }

    [Theory]
    [InlineData("no-currency", PurchasingRuleCodes.PriceCurrencyRequired)]
    [InlineData("negative", PurchasingRuleCodes.PriceNegative)]
    [InlineData("zero-with-amount", PurchasingRuleCodes.DiscountExceedsPrice)]
    [InlineData("other-unit", PurchasingRuleCodes.PriceUnitConversionUnavailable)]
    [InlineData("scale", PurchasingRuleCodes.PriceScaleExceeded)]
    [InlineData("integer-digits", PurchasingRuleCodes.PriceOutOfRange)]
    [InlineData("both-discounts", PurchasingRuleCodes.DiscountKindConflict)]
    [InlineData("percent-over", PurchasingRuleCodes.DiscountPercentOutOfRange)]
    [InlineData("percent-negative", PurchasingRuleCodes.DiscountPercentOutOfRange)]
    [InlineData("percent-scale", PurchasingRuleCodes.DiscountPercentOutOfRange)]
    [InlineData("amount-over", PurchasingRuleCodes.DiscountExceedsPrice)]
    [InlineData("amount-negative", PurchasingRuleCodes.DiscountExceedsPrice)]
    [InlineData("amount-scale", PurchasingRuleCodes.PriceScaleExceeded)]
    public void Every_price_rule_is_refused_with_its_code_and_adds_nothing(string fault, string code)
    {
        var order = fault == "no-currency"
            ? PurchaseOrder.Create(Guid.NewGuid(), Company, Supplier, At)
            : PurchaseOrder.Create(Guid.NewGuid(), Company, Supplier, Lira, At);
        var price = fault switch
        {
            "zero-with-amount" => new PurchaseOrderLinePrice(0m, Kg, null, 0.01m),
            "negative" => new PurchaseOrderLinePrice(-1m, Kg),
            "other-unit" => new PurchaseOrderLinePrice(10m, Guid.NewGuid()),
            "scale" => new PurchaseOrderLinePrice(10.001m, Kg),
            "integer-digits" => new PurchaseOrderLinePrice(1234567890123456789m, Kg),
            "both-discounts" => new PurchaseOrderLinePrice(10m, Kg, 1m, 1m),
            "percent-over" => new PurchaseOrderLinePrice(10m, Kg, 100.01m),
            "percent-negative" => new PurchaseOrderLinePrice(10m, Kg, -1m),
            "percent-scale" => new PurchaseOrderLinePrice(10m, Kg, 1.0000001m),
            "amount-over" => new PurchaseOrderLinePrice(10m, Kg, null, 10.01m),
            "amount-negative" => new PurchaseOrderLinePrice(10m, Kg, null, -0.01m),
            "amount-scale" => new PurchaseOrderLinePrice(10m, Kg, null, 0.005m),
            _ => new PurchaseOrderLinePrice(10m, Kg),
        };

        var refused = Assert.Throws<PurchasingRuleException>(() => order.AddLine(1, Guid.NewGuid(), Product, Kg, Kilos(5m), null, price, At));

        Assert.Equal(code, refused.Code);
        Assert.Empty(order.Lines);
        Assert.Equal(1, order.Version);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("10", null)]
    [InlineData(null, "0")]
    public void An_explicit_zero_price_is_a_price_with_a_zero_amount_apart_from_no_price(string? percent, string? amount)
    {
        // Sinan (#66 6704170096): "Satırda açıkça 0 fiyat girilebilir; satır tutarı 0 olur. Boş fiyattan ayrı tutulur." On a zero
        // price a percent discount is allowed (net 0) and an amount only as 0 (a positive amount exceeds the price, fail-closed).
        var order = PurchaseOrder.Create(Guid.NewGuid(), Company, Supplier, Lira, At);
        var line = order.AddLine(1, Guid.NewGuid(), Product, Kg, Kilos(10m), null, new PurchaseOrderLinePrice(0m, Kg, Parse(percent), Parse(amount)), At);

        Assert.NotNull(line.Price);
        Assert.Equal((0m, 0m), (line.Price!.NetUnitPrice, line.Price.LineAmount(line.Quantity.Value, Lira)));
    }

    [Fact]
    public void A_net_unit_price_that_cannot_be_held_exactly_is_refused_at_write_time_not_at_read()
    {
        // #944 review A P2: at scale 6 the line amount of 123456789012345678.123457 × (1 − 33.333333 %) fits, but the exact net
        // 82304526419753082.12345692707819 (32 significant digits) needs more than System.Decimal holds; it is refused when written, never a 500 on a read.
        var wide = PurchaseOrderMoney.Of(Currency, "TRY", Profile, 1, 6);
        var order = PurchaseOrder.Create(Guid.NewGuid(), Company, Supplier, wide, At);
        var price = new PurchaseOrderLinePrice(123456789012345678.123457m, Kg, 33.333333m);
        Assert.Throws<OverflowException>(() => _ = price.NetUnitPrice);

        var refused = Assert.Throws<PurchasingRuleException>(() => order.AddLine(1, Guid.NewGuid(), Product, Kg, Kilos(1m), null, price, At));

        Assert.Equal(PurchasingRuleCodes.PriceOutOfRange, refused.Code);
        Assert.Empty(order.Lines);
    }

    [Fact]
    public void A_profile_wider_than_the_columns_still_holds_a_price_to_six_decimals()
    {
        var wide = PurchaseOrderMoney.Of(Currency, "TRY", Profile, 1, 8);
        var order = PurchaseOrder.Create(Guid.NewGuid(), Company, Supplier, wide, At);
        Assert.Equal(6, wide.PriceScale);

        order.AddLine(1, Guid.NewGuid(), Product, Kg, Kilos(1m), null, new PurchaseOrderLinePrice(1.123456m, Kg), At);
        var refused = Assert.Throws<PurchasingRuleException>(() =>
            order.AddLine(2, Guid.NewGuid(), Product, Kg, Kilos(1m), null, new PurchaseOrderLinePrice(1.1234567m, Kg), At));
        Assert.Equal(PurchasingRuleCodes.PriceScaleExceeded, refused.Code);
    }

    [Fact]
    public void The_money_snapshot_refuses_an_incomplete_profile()
    {
        Assert.Throws<ArgumentException>(() => PurchaseOrderMoney.Of(Guid.Empty, "TRY", Profile, 1, 2));
        Assert.Throws<ArgumentException>(() => PurchaseOrderMoney.Of(Currency, " ", Profile, 1, 2));
        Assert.Throws<ArgumentException>(() => PurchaseOrderMoney.Of(Currency, "TRY", Guid.Empty, 1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => PurchaseOrderMoney.Of(Currency, "TRY", Profile, 0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => PurchaseOrderMoney.Of(Currency, "TRY", Profile, 1, 29));
    }

    // ---- the stored record ----------------------------------------------------------------------------------------------------

    [Fact]
    public void A_stored_price_and_currency_are_rebuilt_through_the_domain()
    {
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var header = new PurchaseOrderRow
        {
            Id = orderId, CompanyId = Company, SupplierPartyId = Supplier, CompanyAccountId = Guid.NewGuid(), Status = 1, Version = 2,
            EffectiveOn = new DateOnly(2026, 11, 7), CreatedAtUtc = At, UpdatedAtUtc = At, CreatedBy = "SENTETIK", CorrelationId = "SENTETIK",
            CurrencyId = Currency, CurrencyCode = "TRY", MoneyProfileId = Profile, MoneyProfileVersion = 1, MoneyScale = 2,
        };
        var line = new PurchaseOrderLineRow
        {
            CompanyId = Company, OrderId = orderId, LineNo = 1, LineId = lineId, Kind = 1, ProductId = Product, ProductVersion = "7", UnitId = Kg,
            UnitIsKilogram = true, Ordered = 10m, UnitPrice = 12.5m, PriceUnitId = Kg, DiscountAmount = 0.5m,
        };

        var restored = PurchasingStore.RestoreOrder(header, [line]);

        Assert.Equal(Lira, restored.Money);
        Assert.Equal(new PurchaseOrderLinePrice(12.5m, Kg, null, 0.5m), restored.Lines[0].Price);

        // A stored technical line with a price is not a state the domain can reach: refused, never shown.
        var technical = new PurchaseOrderLineRow
        {
            CompanyId = Company, OrderId = orderId, LineNo = 2, LineId = Guid.NewGuid(), Kind = 2, ParentLineId = lineId, ProductId = Product,
            ProductVersion = "7", UnitId = Kg, UnitIsKilogram = true, Ordered = 2m, UnitPrice = 1m, PriceUnitId = Kg,
        };
        line.Technical = 2m;
        Assert.Throws<InvalidOperationException>(() => PurchasingStore.RestoreOrder(header, [line, technical]));
    }

    // ---- A2a-1c (ALS-D72): the unit-price scale --------------------------------------------------------------------------------

    [Fact]
    public void A_unit_price_scale_of_4_takes_four_decimals_refuses_a_fifth_and_still_rounds_the_line_amount_once_to_the_amount_scale()
    {
        var lira4 = PurchaseOrderMoney.Of(Currency, "TRY", Profile, 1, 2, unitPriceScale: 4);
        var order = PurchaseOrder.Create(Guid.NewGuid(), Company, Supplier, lira4, At);
        Assert.Equal((4, (int?)4, 2), (lira4.PriceScale, lira4.UnitPriceScale, lira4.Scale));

        // 41.2575 x 1250.5 = 51592.50375 -> 51592.50 (once, half away from zero, SAT-D11); the net is never rounded (SAT-D76).
        var line = order.AddLine(1, Guid.NewGuid(), Product, Kg, Kilos(1250.5m), null, new PurchaseOrderLinePrice(41.2575m, Kg), At);
        Assert.Equal((41.2575m, 51592.50m), (line.Price!.NetUnitPrice, line.Price.LineAmount(line.Quantity.Value, lira4)));
        Assert.Equal(CommercialLinePriceCalculator.LineAmount(1250.5m, 41.2575m, null, null, 2), line.Price.LineAmount(line.Quantity.Value, lira4));

        // A discount amount is a per-unit amount taken from the unit price: the same four decimals.
        order.AddLine(2, Guid.NewGuid(), Product, Kg, Kilos(1m), null, new PurchaseOrderLinePrice(10.1234m, Kg, null, 0.0001m), At);
        foreach (var price in new[] { new PurchaseOrderLinePrice(10.12345m, Kg), new PurchaseOrderLinePrice(10m, Kg, null, 0.00001m) })
        {
            var refused = Assert.Throws<PurchasingRuleException>(() => order.AddLine(order.Version, Guid.NewGuid(), Product, Kg, Kilos(1m), null, price, At));
            Assert.Equal(PurchasingRuleCodes.PriceScaleExceeded, refused.Code);
        }
    }

    [Fact]
    public void Without_a_unit_price_scale_the_unit_price_keeps_the_amount_scale_and_a_wide_one_is_capped_by_the_columns()
    {
        // A legacy (A2a-1) snapshot or a profile that answers none: the amount scale, as before.
        Assert.Equal((2, (int?)null), (Lira.PriceScale, Lira.UnitPriceScale));
        var legacy = PurchaseOrder.Create(Guid.NewGuid(), Company, Supplier, Lira, At);
        Assert.Equal(PurchasingRuleCodes.PriceScaleExceeded, Assert.Throws<PurchasingRuleException>(() =>
            legacy.AddLine(1, Guid.NewGuid(), Product, Kg, Kilos(1m), null, new PurchaseOrderLinePrice(10.123m, Kg), At)).Code);

        // A unit-price scale wider than decimal(24,6) is capped at the columns' six decimals.
        Assert.Equal(6, PurchaseOrderMoney.Of(Currency, "TRY", Profile, 1, 2, unitPriceScale: 9).PriceScale);
        Assert.Throws<ArgumentOutOfRangeException>(() => PurchaseOrderMoney.Of(Currency, "TRY", Profile, 1, 2, unitPriceScale: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => PurchaseOrderMoney.Of(Currency, "TRY", Profile, 1, 2, unitPriceScale: 29));
    }

    [Fact]
    public void A_stored_unit_price_scale_is_rebuilt_and_a_four_decimal_price_is_rebuilt_through_the_domain()
    {
        var orderId = Guid.NewGuid();
        var header = new PurchaseOrderRow
        {
            Id = orderId, CompanyId = Company, SupplierPartyId = Supplier, CompanyAccountId = Guid.NewGuid(), Status = 1, Version = 2,
            EffectiveOn = new DateOnly(2026, 11, 7), CreatedAtUtc = At, UpdatedAtUtc = At, CreatedBy = "SENTETIK", CorrelationId = "SENTETIK",
            CurrencyId = Currency, CurrencyCode = "TRY", MoneyProfileId = Profile, MoneyProfileVersion = 1, MoneyScale = 2, UnitPriceScale = 4,
        };
        var line = new PurchaseOrderLineRow
        {
            CompanyId = Company, OrderId = orderId, LineNo = 1, LineId = Guid.NewGuid(), Kind = 1, ProductId = Product, ProductVersion = "7", UnitId = Kg,
            UnitIsKilogram = true, Ordered = 10m, UnitPrice = 41.2575m, PriceUnitId = Kg,
        };

        var restored = PurchasingStore.RestoreOrder(header, [line]);

        Assert.Equal(PurchaseOrderMoney.Of(Currency, "TRY", Profile, 1, 2, 4), restored.Money);
        Assert.Equal(41.2575m, restored.Lines[0].Price!.UnitPrice);
        // The same row without the scale (a hand-edited legacy row) cannot hold a four-decimal price: refused, never shown.
        header.UnitPriceScale = null;
        var refused = Assert.Throws<PurchasingRuleException>(() => PurchasingStore.RestoreOrder(header, [line]));
        Assert.Equal(PurchasingRuleCodes.PriceScaleExceeded, refused.Code);
    }

    private static Quantity Kilos(decimal value) => Quantity.Of(value, QuantityUnit.Kilogram);

    private static decimal? Parse(string? text) =>
        text is null ? null : decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
}
