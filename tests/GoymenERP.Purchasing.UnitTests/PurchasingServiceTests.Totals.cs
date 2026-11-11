using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// A2a-2 through the service: the maturity surcharge (vade farkı; ALS-D38, ALS-D75 "Satış'la aynı", ALS-D76), the order totals
/// (API-20; ALS-D74: VAT from the Settings item-type default with buyer situation Normal; withholding ALS-D78: item-type share like Sales S5, no threshold;
/// PRS-02) and "fiyat bekliyor" on the list. SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class PurchasingServiceTests
{
    [Fact]
    public async Task A_cash_price_with_a_percent_surcharge_adds_it_to_the_discounted_price_and_the_detail_shows_it()
    {
        var world = new World();

        // (41.25 − 2.5 %) = 40.21875; + 3 % of it = 41.4253125 (exact, SAT-D76); × 10 = 414.253125 → 414.25.
        var order = await world.Service().CreateOrderAsync(
            PricedOrderOf("10", new PurchaseOrderLinePriceRequest("41.25", DiscountPercent: "2.5", DeferralPercent: "3")), Metadata("k-deferral"));

        var price = order.Detail.Lines[0].Price!;
        Assert.Equal(("3", (string?)null, "41.4253125", "414.25"), (price.DeferralPercent, price.DeferralAmount, price.NetUnitPrice, price.LineAmount));
        var amount = await world.Service().CreateOrderAsync(
            PricedOrderOf("10", new PurchaseOrderLinePriceRequest("40", DiscountAmount: "1", DeferralAmount: "0.5")), Metadata("k-deferral-amount"));
        Assert.Equal(("0.5", "39.5", "395"), (amount.Detail.Lines[0].Price!.DeferralAmount, amount.Detail.Lines[0].Price!.NetUnitPrice,
            amount.Detail.Lines[0].Price!.LineAmount));
    }

    [Theory]
    [InlineData("0", "1", null, PurchasingRuleCodes.DeferralOnZeroPrice)]
    [InlineData("0", null, "0", PurchasingRuleCodes.DeferralOnZeroPrice)]
    [InlineData("10", "1", "1", PurchasingRuleCodes.DeferralKindConflict)]
    [InlineData("10", "100.5", null, PurchasingRuleCodes.DeferralPercentOutOfRange)]
    [InlineData("10", "1.0000001", null, PurchasingRuleCodes.DeferralPercentOutOfRange)]
    [InlineData("10", null, "0.123", PurchasingRuleCodes.PriceScaleExceeded)]
    public async Task Every_surcharge_rule_is_a_422_on_the_lines_price_and_nothing_is_stored(string unitPrice, string? percent, string? amount,
        string expected)
    {
        var world = new World();

        var refused = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().CreateOrderAsync(
            PricedOrderOf("5", new PurchaseOrderLinePriceRequest(unitPrice, DeferralPercent: percent, DeferralAmount: amount)),
            Metadata("k-deferral-rule-" + expected + unitPrice + percent + amount)));

        Assert.Equal((422, "validation_failed", "lines[0].price", expected), (refused.Status, refused.Code, refused.Errors[0].Field, refused.Errors[0].Code));
        Assert.Empty(world.Store.Envelopes);
    }

    [Fact]
    public async Task A_price_without_a_surcharge_keeps_the_pre_A2a_2_request_hash()
    {
        var line = new PurchaseOrderLineRequest(Guid.Parse("12121212-1212-4212-8212-121212121212"), Product, "7", KgUnit, "5", null,
            new PurchaseOrderLinePriceRequest("10"));
        var json = System.Text.Json.JsonSerializer.Serialize(new CreatePurchaseOrderRequest(Party, Account, null, Day, [line], Lira), PurchasingJson.Options);

        Assert.DoesNotContain("deferral", json, StringComparison.OrdinalIgnoreCase);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task The_totals_sum_the_line_amounts_take_VAT_and_the_withholding_share_from_the_item_type_default_with_no_threshold_gate()
    {
        var world = new World { LineTaxes = new FakeLineTaxes() };
        var order = await world.Service().CreateOrderAsync(new CreatePurchaseOrderRequest(Party, Account, null, Day,
        [
            new PurchaseOrderLineRequest(Guid.NewGuid(), Product, "7", KgUnit, "1250.5", null, new PurchaseOrderLinePriceRequest("41.25", DiscountPercent: "2.5")),
            new PurchaseOrderLineRequest(Guid.NewGuid(), Product, "7", KgUnit, "1.5", null, new PurchaseOrderLinePriceRequest("0.67")),
        ], Lira), Metadata("k-totals"));

        var totals = await world.Service().GetOrderTotalsAsync(order.Detail.Order.Id);

        // Lines: 50293.546875 → 50293.55, VAT 20 % = 10058.71; 1.5 × 0.67 = 1.005 → 1.01, VAT 0.202 → 0.20. Sums: 50294.56, 10058.91, 60353.47.
        Assert.Equal((PurchasingPriceDisclosureV1.Visible, "Computed", 0, "50294.56", "Computed", 0, "10058.91", "60353.47"),
            (totals.PriceDisclosure, totals.State, totals.UnpricedLineCount, totals.NetAmount, totals.VatState, totals.VatNotComputableLineCount,
                totals.VatAmount, totals.GrossAmount));
        // Withholding 50 % of the ROUNDED line VAT (SAT-D75): 10058.71 → 5029.355 → 5029.36; 0.20 → 0.10. Sum 5029.46; payable 60353.47 − 5029.46.
        Assert.Equal(("Computed", (string?)null, "5029.46", "55324.01"),
            (totals.WithholdingState, totals.WithholdingReason, totals.WithheldVatAmount, totals.PayableAmount));
        Assert.Equal(("Computed", "50293.55", "20", "10058.71", "60352.26", "Computed"),
            (totals.Lines[0].State, totals.Lines[0].NetAmount, totals.Lines[0].VatPercent, totals.Lines[0].VatAmount, totals.Lines[0].GrossAmount,
                totals.Lines[0].WithholdingState));
        var taxes = (FakeLineTaxes)world.LineTaxes!;
        Assert.Equal((Product, TaxBuyerSituationsV1.Normal, Day, 1), (taxes.LastProduct, taxes.LastSituation, taxes.LastOn, taxes.Calls));
        Assert.Equal(order.Detail.Order.Version, totals.Version);
    }

    [Fact]
    public async Task A_small_gross_is_withheld_too_because_the_threshold_is_no_gate()
    {
        // ALS-D78 (Sinan 2026-11-11): exactly the Sales S5 rule — no VUK 232 gate here, no Purchasing-only switch.
        var world = new World { LineTaxes = new FakeLineTaxes() };
        var created = await world.Service().CreateOrderAsync(PricedOrderOf("1", new PurchaseOrderLinePriceRequest("100")), Metadata("k-totals-small"));
        var totals = await world.Service().GetOrderTotalsAsync(created.Detail.Order.Id);
        Assert.Equal(("120", "Computed", (string?)null, "10", "110"),
            (totals.GrossAmount, totals.WithholdingState, totals.WithholdingReason, totals.WithheldVatAmount, totals.PayableAmount));
    }

    [Fact]
    public async Task An_item_type_without_withholding_is_not_applied_with_or_without_VAT()
    {
        foreach (var fault in new[] { "exempt", "no-withholding" })
        {
            var world = new World { LineTaxes = new FakeLineTaxes { Fault = fault } };
            var created = await world.Service().CreateOrderAsync(PricedOrderOf("10", new PurchaseOrderLinePriceRequest("10")), Metadata("k-totals-none-" + fault));
            var totals = await world.Service().GetOrderTotalsAsync(created.Detail.Order.Id);
            Assert.Equal(("NotApplied", (string?)null, "0", fault == "exempt" ? "100" : "120"),
                (totals.WithholdingState, totals.WithholdingReason, totals.WithheldVatAmount, totals.PayableAmount));
        }
    }

    [Fact]
    public async Task An_Exempt_withholding_state_is_unreadable_like_Sales_not_none()
    {
        var world = new World { LineTaxes = new FakeLineTaxes { Fault = "withholding-exempt" } };
        var created = await world.Service().CreateOrderAsync(PricedOrderOf("10", new PurchaseOrderLinePriceRequest("10")), Metadata("k-totals-wh-exempt"));
        var totals = await world.Service().GetOrderTotalsAsync(created.Detail.Order.Id);
        Assert.Equal(("NotComputable", PurchaseOrderTaxReasonsV1.TaxSourceInvalid, (string?)null, (string?)null),
            (totals.WithholdingState, totals.WithholdingReason, totals.WithheldVatAmount, totals.PayableAmount));
    }

    [Fact]
    public async Task A_withholding_share_outside_the_range_is_unreadable_not_none_and_one_bad_line_names_its_reason_for_the_document()
    {
        var other = Guid.Parse("33333333-3333-4333-8333-333333333333");
        foreach (var share in new decimal[] { 0m, -1m, 100.01m })
        {
            var world = new World { LineTaxes = new FakeLineTaxes { ShareOverride = share } };
            var created = await world.Service().CreateOrderAsync(PricedOrderOf("10", new PurchaseOrderLinePriceRequest("10")), Metadata("k-totals-share-" + share));
            var totals = await world.Service().GetOrderTotalsAsync(created.Detail.Order.Id);
            Assert.Equal(("NotComputable", PurchaseOrderTaxReasonsV1.TaxSourceInvalid, (string?)null, (string?)null),
                (totals.WithholdingState, totals.WithholdingReason, totals.WithheldVatAmount, totals.PayableAmount));
        }

        // Mixed order: the first line is withheld, the second has an unreadable share → the document is NotComputable with that reason (never a partial sum).
        var mixed = new World { LineTaxes = new FakeLineTaxes { InvalidShareFor = other } };
        var order = await mixed.Service().CreateOrderAsync(new CreatePurchaseOrderRequest(Party, Account, null, Day,
        [
            new PurchaseOrderLineRequest(Guid.NewGuid(), Product, "1", KgUnit, "10", null, new PurchaseOrderLinePriceRequest("100")),
            new PurchaseOrderLineRequest(Guid.NewGuid(), other, "1", KgUnit, "10", null, new PurchaseOrderLinePriceRequest("100")),
        ], Lira), Metadata("k-totals-mixed"));
        var mixedTotals = await mixed.Service().GetOrderTotalsAsync(order.Detail.Order.Id);
        Assert.Equal(("NotComputable", PurchaseOrderTaxReasonsV1.TaxSourceInvalid, (string?)null),
            (mixedTotals.WithholdingState, mixedTotals.WithholdingReason, mixedTotals.PayableAmount));
        Assert.Equal(("Computed", "NotComputable"), (mixedTotals.Lines[0].WithholdingState, mixedTotals.Lines[1].WithholdingState));
    }

    [Fact]
    public async Task A_surcharge_line_takes_VAT_on_its_amount_with_the_surcharge_and_a_product_without_an_item_type_makes_its_VAT_not_computable()
    {
        var other = Guid.Parse("99999999-9999-4999-8999-000000000002");
        var world = new World { LineTaxes = new FakeLineTaxes { UnresolvedProduct = other } };
        var order = await world.Service().CreateOrderAsync(new CreatePurchaseOrderRequest(Party, Account, null, Day,
        [
            new PurchaseOrderLineRequest(Guid.NewGuid(), Product, "7", KgUnit, "10", null, new PurchaseOrderLinePriceRequest("10", DeferralPercent: "2")),
            new PurchaseOrderLineRequest(Guid.NewGuid(), Product, "7", KgUnit, "10", null, new PurchaseOrderLinePriceRequest("10.2")),
            new PurchaseOrderLineRequest(Guid.NewGuid(), other, "7", KgUnit, "1", null, new PurchaseOrderLinePriceRequest("5")),
        ], Lira), Metadata("k-totals-vat"));

        var totals = await world.Service().GetOrderTotalsAsync(order.Detail.Order.Id);

        // ALS-D77: 10 + 2 % = 10.2 → 102, VAT 20.4 — the same as the deferred price 10.2 typed directly (ALS-D76).
        Assert.Equal(("Computed", "102", "20.4", "122.4"), (totals.Lines[0].VatState, totals.Lines[0].NetAmount, totals.Lines[0].VatAmount,
            totals.Lines[0].GrossAmount));
        Assert.Equal((totals.Lines[0].NetAmount, totals.Lines[0].VatAmount), (totals.Lines[1].NetAmount, totals.Lines[1].VatAmount));
        // The net still sums (102 + 102 + 5); one line's VAT is "hesaplanamıyor", so no document VAT, gross or payable.
        Assert.Equal(("Computed", "209", "NotComputable", 1, (string?)null, (string?)null, "NotComputed"),
            (totals.State, totals.NetAmount, totals.VatState, totals.VatNotComputableLineCount, totals.VatAmount, totals.GrossAmount, totals.WithholdingState));
        Assert.Equal(("NotComputable", TaxResolutionReasonsV1.ProductItemTypeMissing), (totals.Lines[2].VatState, totals.Lines[2].VatReason));
    }

    [Fact]
    public async Task An_unpriced_line_makes_the_totals_incomplete_and_an_order_without_a_currency_has_no_sum()
    {
        var world = new World { LineTaxes = new FakeLineTaxes() };
        var priced = await world.Service().CreateOrderAsync(new CreatePurchaseOrderRequest(Party, Account, null, Day,
        [
            new PurchaseOrderLineRequest(Guid.NewGuid(), Product, "7", KgUnit, "10", null, new PurchaseOrderLinePriceRequest("10")),
            new PurchaseOrderLineRequest(Guid.NewGuid(), Product, "7", KgUnit, "2", null),
        ], Lira), Metadata("k-totals-pending"));
        var unpriced = await world.Service().CreateOrderAsync(OrderOf(null, null, "5"), Metadata("k-totals-none"));

        var partial = await world.Service().GetOrderTotalsAsync(priced.Detail.Order.Id);
        var none = await world.Service().GetOrderTotalsAsync(unpriced.Detail.Order.Id);

        Assert.Equal(("Incomplete", 1, (string?)null, "NotComputed", (string?)null), (partial.State, partial.UnpricedLineCount, partial.NetAmount,
            partial.VatState, partial.VatAmount));
        Assert.Equal(("Unpriced", "Computed"), (partial.Lines[1].State, partial.Lines[0].State));
        Assert.Equal(("Incomplete", 1, (PurchaseOrderCurrencyView?)null), (none.State, none.UnpricedLineCount, none.Currency));
    }

    [Fact]
    public async Task Without_the_price_read_the_totals_are_withheld_and_carry_no_amount()
    {
        var world = new World { LineTaxes = new FakeLineTaxes() };
        var order = await world.Service().CreateOrderAsync(PricedOrderOf("10", new PurchaseOrderLinePriceRequest("12.5")), Metadata("k-totals-withheld"));
        world.Authority.Denied.Add(PurchasingPermissionManifestV1.OrdersPriceRead);

        var totals = await world.Service().GetOrderTotalsAsync(order.Detail.Order.Id);

        Assert.Equal((PurchasingPriceDisclosureV1.Withheld, "Withheld", "Withheld", "Withheld", (string?)null, (string?)null, 0),
            (totals.PriceDisclosure, totals.State, totals.VatState, totals.WithholdingState, totals.NetAmount, totals.VatAmount, totals.Lines.Count));
        Assert.Equal("TRY", totals.Currency!.CurrencyCode);
        Assert.Equal(0, ((FakeLineTaxes)world.LineTaxes!).Calls);
    }

    [Fact]
    public async Task The_Settings_tax_port_answers_are_mapped_fail_closed()
    {
        var absent = new World();
        var order = await absent.Service().CreateOrderAsync(PricedOrderOf("10", new PurchaseOrderLinePriceRequest("10")), Metadata("k-totals-absent"));
        var noPort = await absent.Service().GetOrderTotalsAsync(order.Detail.Order.Id);
        Assert.Equal(("NotComputable", PurchaseOrderTaxReasonsV1.TaxSourceUnavailable, "100"),
            (noPort.Lines[0].VatState, noPort.Lines[0].VatReason, noPort.NetAmount));

        absent.LineTaxes = new FakeLineTaxes { Fault = "settings_provider_unavailable" };
        Assert.Equal(PurchaseOrderTaxReasonsV1.TaxSourceUnavailable, (await absent.Service().GetOrderTotalsAsync(order.Detail.Order.Id)).Lines[0].VatReason);
        absent.LineTaxes = new FakeLineTaxes { Fault = "exempt" };
        var exempt = await absent.Service().GetOrderTotalsAsync(order.Detail.Order.Id);
        Assert.Equal(("Computed", "0", "0", "100"), (exempt.Lines[0].VatState, exempt.Lines[0].VatPercent, exempt.VatAmount, exempt.GrossAmount));

        absent.LineTaxes = new FakeLineTaxes { Fault = "settings_access_denied" };
        var denied = await Assert.ThrowsAsync<PurchasingServiceException>(() => absent.Service().GetOrderTotalsAsync(order.Detail.Order.Id));
        Assert.Equal((403, "forbidden"), (denied.Status, denied.Code));
        absent.LineTaxes = new FakeLineTaxes { Fault = "other-company" };
        var invalid = await Assert.ThrowsAsync<PurchasingServiceException>(() => absent.Service().GetOrderTotalsAsync(order.Detail.Order.Id));
        Assert.Equal((503, PurchasingProblemCodesV1.ReferenceProviderInvalid), (invalid.Status, invalid.Code));

        var missing = await Assert.ThrowsAsync<PurchasingServiceException>(() => absent.Service().GetOrderTotalsAsync(Guid.NewGuid()));
        Assert.Equal(404, missing.Status);
    }

    [Fact]
    public async Task The_list_says_fiyat_bekliyor_only_to_a_holder_of_the_price_read()
    {
        var world = new World();
        await world.Service().CreateOrderAsync(OrderOf(null, null, "5"), Metadata("k-list-unpriced"));
        await world.Service().CreateOrderAsync(PricedOrderOf("5", new PurchaseOrderLinePriceRequest("10")), Metadata("k-list-priced"));

        var visible = await world.Service().GetOrderListAsync(new PurchaseOrderListQuery());
        Assert.Equal(new bool?[] { true, false }, visible.Items.Select(item => item.PricePending).ToArray());

        world.Authority.Denied.Add(PurchasingPermissionManifestV1.OrdersPriceRead);
        var withheld = await world.Service().GetOrderListAsync(new PurchaseOrderListQuery());
        Assert.All(withheld.Items, item => Assert.Null(item.PricePending));
        var json = System.Text.Json.JsonSerializer.Serialize(withheld, PurchasingJson.Options);
        Assert.DoesNotContain("pricePending", json, StringComparison.Ordinal);
        // A detail's order summary never carries it.
        var detail = await world.Service().GetOrderDetailAsync(visible.Items[0].Id);
        Assert.Null(detail.Order.PricePending);
    }

    /// <summary>The Settings line tax port: the Steel default (KDV 20 %, withholding 5/10) for every product unless told otherwise.</summary>
    private sealed class FakeLineTaxes : ISettingsLineTaxResolverV1
    {
        public string Fault { get; init; } = "";
        public decimal? ShareOverride { get; init; }
        public Guid? InvalidShareFor { get; init; }
        public Guid? UnresolvedProduct { get; init; }
        public int Calls { get; private set; }
        public Guid LastProduct { get; private set; }
        public string? LastSituation { get; private set; }
        public DateOnly LastOn { get; private set; }

        public Task<SettingsTaxDefaultResolutionV1> ResolveAsync(Guid productId, string buyerSituation, DateOnly on, string? exemptionCode = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            (LastProduct, LastSituation, LastOn) = (productId, buyerSituation, on);
            if (Fault is "settings_provider_unavailable" or "settings_access_denied")
            {
                return Task.FromException<SettingsTaxDefaultResolutionV1>(new SettingsPrerequisiteException(Fault));
            }

            var company = Fault == "other-company" ? Guid.NewGuid() : Company;
            if (productId == UnresolvedProduct)
            {
                return Task.FromResult(new SettingsTaxDefaultResolutionV1(company, productId, null, buyerSituation, on, TaxResolutionStatesV1.Unresolved,
                    TaxResolutionReasonsV1.ProductItemTypeMissing, null, null, null, null, null, null, null, null, null, null, null, null, null, null,
                    null, null, false));
            }

            var exempt = Fault == "exempt";
            var noWithholding = exempt || Fault == "no-withholding";
            var share = productId == InvalidShareFor ? 0m : ShareOverride ?? 50m;
            return Task.FromResult(new SettingsTaxDefaultResolutionV1(company, productId, TaxItemTypesV1.Steel, buyerSituation, on,
                TaxResolutionStatesV1.Resolved, null, TaxDefaultSourcesV1.Catalog, Guid.NewGuid(), 0,
                exempt ? TaxResolutionStatesV1.Exempt : TaxResolutionStatesV1.Resolved, exempt ? null : 20m,
                TaxResolutionStatesV1.NotRequired, null, null, Fault == "withholding-exempt" ? TaxResolutionStatesV1.Exempt : noWithholding ? TaxResolutionStatesV1.NotApplied : TaxResolutionStatesV1.Resolved,
                noWithholding || Fault == "withholding-exempt" ? null : share, TaxResolutionStatesV1.NotRequired, null, null, null, 0, TaxBuyerSituationVatModesV1.Normal, false));
        }

        public Task<SettingsTaxDefaultResolutionV1> GetItemTypeDefaultAsync(string itemType, string buyerSituation, DateOnly on,
            string? exemptionCode = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<SettingsTaxBuyerSituationListResolutionV1> GetBuyerSituationsAsync(DateOnly on, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SettingsTaxCodeListResolutionV1> GetCodeListAsync(string kind, DateOnly on, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SettingsWithholdingThresholdResolutionV1> GetWithholdingThresholdAsync(DateOnly on, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
