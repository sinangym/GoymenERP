using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// A2a-1 (TEK-01, plan API-20; ALS-D5, ALS-D18, ALS-D51/D58/D60, PRS-02) through the service: the order's currency read from the
/// Settings money profile of <c>purchasing.order.v1</c> (fail-closed while the owner has none), the manual price and discount
/// answered with the backend calculator's net and line amount, every refusal on <c>lines[i].price</c>, the content hash of an
/// unpriced order unchanged, and the price disclosure: without <c>purchasing.orders.price.read</c> every price is withheld from
/// the read and from the command's answer. SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class PurchasingServiceTests
{
    private static readonly Guid Lira = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    private static readonly Guid LiraProfile = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");

    [Fact]
    public async Task A_priced_order_records_the_currency_snapshot_and_answers_the_calculators_net_and_line_amount()
    {
        var world = new World();

        var order = await world.Service().CreateOrderAsync(PricedOrderOf("1250.5", new PurchaseOrderLinePriceRequest("41.25", DiscountPercent: "2.5")),
            Metadata("k-priced"));

        Assert.Equal(PurchasingPriceDisclosureV1.Visible, order.Detail.PriceDisclosure);
        Assert.Equal(new PurchaseOrderCurrencyView(Lira, "TRY", 2), order.Detail.Currency);
        Assert.False(order.Detail.PricePending);
        var price = Assert.Single(order.Detail.Lines).Price!;
        // net = 41.25 − 41.25 × 2.5 % = 40.21875 (exact, never rounded to the scale); amount = 1250.5 × 40.21875 = 50293.546875 → 50293.55.
        Assert.Equal(("41.25", KgUnit, "2.5", (string?)null, "40.21875", "50293.55"),
            (price.UnitPrice, price.PriceUnitId, price.DiscountPercent, price.DiscountAmount, price.NetUnitPrice, price.LineAmount));
        Assert.Equal((Lira, PurchasingMoneyOperationsV1.OrderV1, Day), (world.Money.LastCurrency, world.Money.LastOperation, world.Money.LastDay));
        var stored = (await world.Store.FindOrderAsync(order.Detail.Order.Id))!.Order;
        Assert.Equal((Lira, "TRY", LiraProfile, 3L, 2), (stored.Money!.CurrencyId, stored.Money.CurrencyCode, stored.Money.ProfileId,
            stored.Money.ProfileVersion, stored.Money.Scale));

        var detail = await world.Service().GetOrderDetailAsync(order.Detail.Order.Id);
        Assert.Equal(("50293.55", PurchasingPriceDisclosureV1.Visible), (detail.Lines[0].Price!.LineAmount, detail.PriceDisclosure));
    }

    [Fact]
    public async Task A_discount_amount_is_taken_from_the_unit_price_and_the_line_amount_rounds_half_away_from_zero_once()
    {
        var world = new World();

        var order = await world.Service().CreateOrderAsync(PricedOrderOf("0.5", new PurchaseOrderLinePriceRequest("10.01", DiscountAmount: "0.00")),
            Metadata("k-amount"));

        // 0.5 × 10.01 = 5.005 → 5.01 (half away from zero, SAT-D11), never 5.00 (banker's rounding).
        var price = order.Detail.Lines[0].Price!;
        Assert.Equal(("10.01", (string?)null, "0", "10.01", "5.01"), (price.UnitPrice, price.DiscountPercent, price.DiscountAmount, price.NetUnitPrice, price.LineAmount));
    }

    [Fact]
    public async Task An_unpriced_order_needs_no_currency_is_price_pending_and_keeps_its_content_hash()
    {
        var world = new World();

        var order = await world.Service().CreateOrderAsync(OrderOf(null, null, "5"), Metadata("k-unpriced"));

        Assert.Equal((PurchasingPriceDisclosureV1.Visible, (PurchaseOrderCurrencyView?)null, (bool?)true),
            (order.Detail.PriceDisclosure, order.Detail.Currency, order.Detail.PricePending));
        Assert.Null(order.Detail.Lines[0].Price);
        Assert.Equal(0, world.Money.Calls);
        // ALS-D18: the unpriced order is approved as usual.
        var approved = await world.AsApprover(() => world.Service().ApproveOrderAsync(order.Detail.Order.Id, new ApprovePurchaseOrderRequest("2"),
            Metadata("k-approve-unpriced")));
        Assert.Equal(("Approved", (bool?)true), (approved.Detail.Order.Status, approved.Detail.PricePending));

        // The hash of an order without a currency or a price is the hash of the pre-A2a-1 wire (no null member is written).
        var line = new PurchaseOrderLineRequest(Guid.Parse("12121212-1212-4212-8212-121212121212"), Product, "7", KgUnit, "5", null);
        var request = new CreatePurchaseOrderRequest(Party, Account, null, Day, [line]);
        var json = System.Text.Json.JsonSerializer.Serialize(new { command = PurchasingCommands.OrderCreateV1, request = (object)request }, PurchasingJson.Options);
        Assert.DoesNotContain("currencyId", json, StringComparison.Ordinal);
        Assert.DoesNotContain("price", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_the_price_read_every_price_is_withheld_from_the_read_and_from_the_commands_answer()
    {
        var world = new World();
        world.Authority.Denied.Add(PurchasingPermissionManifestV1.OrdersPriceRead);

        var order = await world.Service().CreateOrderAsync(PricedOrderOf("10", new PurchaseOrderLinePriceRequest("12.5")), Metadata("k-withheld"));

        Assert.Equal((PurchasingPriceDisclosureV1.Withheld, (bool?)null), (order.Detail.PriceDisclosure, order.Detail.PricePending));
        Assert.Null(order.Detail.Lines[0].Price);
        Assert.Equal("TRY", order.Detail.Currency!.CurrencyCode);
        var detail = await world.Service().GetOrderDetailAsync(order.Detail.Order.Id);
        Assert.Equal((PurchasingPriceDisclosureV1.Withheld, (PurchaseOrderLinePriceView?)null), (detail.PriceDisclosure, detail.Lines[0].Price));

        // The price is recorded all the same; a holder of the price read sees it.
        world.Authority.Denied.Clear();
        var visible = await world.Service().GetOrderDetailAsync(order.Detail.Order.Id);
        Assert.Equal(("12.5", "125"), (visible.Lines[0].Price!.UnitPrice, visible.Lines[0].Price!.LineAmount));
    }

    [Fact]
    public async Task An_unregistered_price_read_name_is_a_setup_problem_never_a_disclosure()
    {
        var world = new World { Bindings = new PurchasingServiceBindings(CreatePermission, ReadPermission, ApprovePermission, PriceReadPermission: null) };

        var refused = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().CreateOrderAsync(OrderOf(null, null, "5"), Metadata("k-unbound")));

        Assert.Equal((503, PurchasingProblemCodesV1.PermissionUnregistered), (refused.Status, refused.Code));
        Assert.Empty(world.Store.Envelopes);
    }

    [Fact]
    public async Task A_priced_line_without_a_currency_is_refused_before_any_owner_is_asked()
    {
        var world = new World();
        world.ResetOwnerCalls();
        var request = PricedOrderOf("5", new PurchaseOrderLinePriceRequest("10")) with { CurrencyId = null };

        var refused = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().CreateOrderAsync(request, Metadata("k-no-currency")));

        Assert.Equal((422, "validation_failed", "lines[0].price", PurchasingRuleCodes.PriceCurrencyRequired),
            (refused.Status, refused.Code, refused.Errors[0].Field, refused.Errors[0].Code));
        Assert.Equal((0, 0, 0), (world.Accounts.Calls, world.Products.Calls, world.Money.Calls));
    }

    [Theory]
    [InlineData("settings_provider_unavailable", 503, "settings_provider_unavailable")]
    [InlineData("settings_access_denied", 403, "forbidden")]
    [InlineData("settings_money_profile_not_found", 422, "unsupported_currency")]
    [InlineData("inactive", 422, "unsupported_currency")]
    [InlineData("not-yet", 422, "unsupported_currency")]
    [InlineData("other-company", 503, "reference_provider_invalid")]
    [InlineData("other-operation", 503, "reference_provider_invalid")]
    public async Task The_currency_is_the_Settings_owners_answer_and_every_other_answer_refuses_the_order(string fault, int status, string code)
    {
        var world = new World { Money = new FakeMoney { Fault = fault } };

        var refused = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().CreateOrderAsync(PricedOrderOf("5", new PurchaseOrderLinePriceRequest("10")), Metadata("k-currency-" + fault)));

        Assert.Equal((status, code), (refused.Status, refused.Code));
        Assert.Empty(world.Store.Envelopes);
    }

    [Theory]
    [InlineData("0", null, null, null, PurchasingRuleCodes.PriceNotPositive)]
    [InlineData("10.001", null, null, null, PurchasingRuleCodes.PriceScaleExceeded)]
    [InlineData("10.0000001", null, null, null, PurchasingRuleCodes.PriceScaleExceeded)]
    [InlineData("1234567890123456789", null, null, null, PurchasingRuleCodes.PriceOutOfRange)]
    [InlineData("10", "other-unit", null, null, PurchasingRuleCodes.PriceUnitConversionUnavailable)]
    [InlineData("10", null, "5", "1", PurchasingRuleCodes.DiscountKindConflict)]
    [InlineData("10", null, "100.5", null, PurchasingRuleCodes.DiscountPercentOutOfRange)]
    [InlineData("10", null, "1.0000001", null, PurchasingRuleCodes.DiscountPercentOutOfRange)]
    [InlineData("10", null, null, "10.5", PurchasingRuleCodes.DiscountExceedsPrice)]
    [InlineData("10", null, null, "1.005", PurchasingRuleCodes.PriceScaleExceeded)]
    public async Task Every_price_rule_is_a_422_on_the_lines_price_and_nothing_is_stored(string unitPrice, string? priceUnit, string? percent,
        string? amount, string expected)
    {
        var world = new World();
        Guid? unit = priceUnit is null ? null : Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");

        var refused = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().CreateOrderAsync(
            PricedOrderOf("5", new PurchaseOrderLinePriceRequest(unitPrice, unit, percent, amount)), Metadata("k-rule-" + expected + unitPrice)));

        Assert.Equal((422, "validation_failed", "lines[0].price", expected), (refused.Status, refused.Code, refused.Errors[0].Field, refused.Errors[0].Code));
        Assert.Empty(world.Store.Envelopes);
    }

    [Fact]
    public async Task The_price_unit_may_be_named_when_it_is_the_lines_entry_unit()
    {
        var world = new World();

        var order = await world.Service().CreateOrderAsync(PricedOrderOf("2", new PurchaseOrderLinePriceRequest("3", KgUnit, "100")), Metadata("k-unit"));

        Assert.Equal((KgUnit, "0", "0"), (order.Detail.Lines[0].Price!.PriceUnitId, order.Detail.Lines[0].Price!.NetUnitPrice,
            order.Detail.Lines[0].Price!.LineAmount));
    }

    [Fact]
    public async Task The_currency_candidates_are_the_owners_for_the_purchasing_operation_and_an_owner_without_one_is_a_503()
    {
        var world = new World();

        var candidates = await world.Service().GetCurrencyCandidatesAsync();

        Assert.Equal(new PurchasingCurrencyCandidateViewV1(Lira, "TRY", "TRY"), Assert.Single(candidates.Items));
        Assert.Equal(PurchasingMoneyOperationsV1.OrderV1, world.Money.LastOperation);

        var closed = new World { Money = new FakeMoney { Fault = "settings_provider_unavailable" } };
        var refused = await Assert.ThrowsAsync<PurchasingServiceException>(() => closed.Service().GetCurrencyCandidatesAsync());
        Assert.Equal((503, "settings_provider_unavailable"), (refused.Status, refused.Code));
    }

    private static CreatePurchaseOrderRequest PricedOrderOf(string quantity, PurchaseOrderLinePriceRequest price) =>
        new(Party, Account, null, Day, [new PurchaseOrderLineRequest(Guid.NewGuid(), Product, "7", KgUnit, quantity, null, price)], Lira);

    /// <summary>The Settings money port and its candidate read for the purchasing operation; GDC/TRY scale 2 unless <see cref="Fault"/>.</summary>
    private sealed class FakeMoney : ISettingsPrerequisiteProvider, ISettingsMoneyCandidateReaderV2
    {
        public string Fault { get; init; } = "";
        public int Calls { get; private set; }
        public Guid LastCurrency { get; private set; }
        public string? LastOperation { get; private set; }
        public DateOnly LastDay { get; private set; }

        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn, CancellationToken cancellationToken = default)
        {
            Calls++;
            (LastCurrency, LastOperation, LastDay) = (currencyId, operation, effectiveOn);
            return Fault switch
            {
                "settings_provider_unavailable" or "settings_access_denied" or "settings_money_profile_not_found" =>
                    Task.FromException<MoneyProfileV1>(new SettingsPrerequisiteException(Fault)),
                _ => Task.FromResult(new MoneyProfileV1(Fault == "other-company" ? Guid.NewGuid() : Company, currencyId, "TRY",
                    Fault == "other-operation" ? "sales.order-draft.v1" : operation, new ProfileVersionV1(LiraProfile, 3), 2, Fault != "inactive",
                    Fault == "not-yet" ? Day.AddDays(1) : new DateOnly(2026, 9, 1), null)),
            };
        }

        public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(string operation, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastOperation = operation;
            return Fault == "settings_provider_unavailable"
                ? Task.FromException<IReadOnlyList<MoneyCurrencyCandidateV1>>(new SettingsPrerequisiteException(Fault))
                : Task.FromResult<IReadOnlyList<MoneyCurrencyCandidateV1>>([new MoneyCurrencyCandidateV1(Lira, "TRY", "TRY")]);
        }

        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn, DateOnly? valueOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
