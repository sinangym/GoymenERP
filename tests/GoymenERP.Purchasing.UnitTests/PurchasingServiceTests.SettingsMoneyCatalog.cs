using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// A2a-1b (ALS-D63 narrow exception, Sinan #66 6704170096): the service composed with the REAL approved Settings provider, not a
/// fake, for the seed companies. The Settings catalog now answers <c>purchasing.order.v1</c> with each company's TRY profile, so a
/// priced order opens and records that profile as its money snapshot. A2a-1c (ALS-D72): the profile's unit-price scale of 4 for this
/// operation is snapshotted with the order, so a unit price takes four decimals and a fifth is refused; the line amount is still
/// rounded once to the amount scale (2). SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class PurchasingServiceTests
{
    private static readonly Guid SeedGdc = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid SeedGsp = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid SeedGin = Guid.Parse("10000000-0000-0000-0000-000000000003");
    private static readonly DateOnly CatalogDay = new(2026, 11, 7);

    [Theory]
    [InlineData("gdc")]
    [InlineData("gsp")]
    [InlineData("gin")]
    public async Task A_priced_order_opens_with_the_approved_Settings_TRY_profile_of_the_company(string company)
    {
        var (companyId, entry) = SeedOf(company);
        var world = SeedWorld(companyId);

        var order = await world.Service().CreateOrderAsync(
            SeedPricedOrderOf("1250.5", new PurchaseOrderLinePriceRequest("41.25", DiscountPercent: "2.5")), Metadata("k-seed-" + company));

        Assert.Equal(new PurchaseOrderCurrencyView(SettingsCurrencyIdentitiesV1.TurkishLira, "TRY", 2), order.Detail.Currency);
        Assert.Equal((companyId, false), (order.Detail.Order.CompanyId, order.Detail.PricePending));
        var price = Assert.Single(order.Detail.Lines).Price!;
        Assert.Equal(("41.25", "40.21875", "50293.55"), (price.UnitPrice, price.NetUnitPrice, price.LineAmount));
        var stored = (await world.Store.FindOrderAsync(order.Detail.Order.Id))!.Order.Money!;
        Assert.Equal((SettingsCurrencyIdentitiesV1.TurkishLira, "TRY", entry.ProfileId, 1L, 2, (int?)4),
            (stored.CurrencyId, stored.CurrencyCode, stored.ProfileId, stored.ProfileVersion, stored.Scale, stored.UnitPriceScale));
    }

    [Theory]
    [InlineData("gdc")]
    [InlineData("gsp")]
    [InlineData("gin")]
    public async Task The_purchasing_currency_candidates_are_TRY_from_the_approved_Settings_catalog(string company)
    {
        var world = SeedWorld(SeedOf(company).CompanyId);

        var candidates = await world.Service().GetCurrencyCandidatesAsync();

        Assert.Equal(new PurchasingCurrencyCandidateViewV1(SettingsCurrencyIdentitiesV1.TurkishLira, "TRY", "TRY"), Assert.Single(candidates.Items));
    }

    [Fact]
    public async Task A_currency_without_an_approved_profile_is_unsupported_and_nothing_is_stored()
    {
        var world = SeedWorld(SeedGdc);
        var request = SeedPricedOrderOf("5", new PurchaseOrderLinePriceRequest("10")) with { CurrencyId = Guid.Parse("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee") };

        var refused = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().CreateOrderAsync(request, Metadata("k-seed-usd")));

        Assert.Equal((422, PurchasingProblemCodesV1.UnsupportedCurrency), (refused.Status, refused.Code));
        Assert.Empty(world.Store.Envelopes);
    }

    [Fact]
    public async Task A_four_decimal_unit_price_is_accepted_and_the_line_amount_is_rounded_once_to_the_amount_scale()
    {
        // A2a-1c flipped the A2a-1b pin: ALS-D72 "birim fiyat en fazla 4 ondalik; satir tutari bir kez kurusa yuvarlanir".
        var world = SeedWorld(SeedGdc);

        var order = await world.Service().CreateOrderAsync(
            SeedPricedOrderOf("1250.5", new PurchaseOrderLinePriceRequest("10.1234", DiscountAmount: "0.0004")), Metadata("k-seed-four"));

        // net = 10.1234 - 0.0004 = 10.123 (never rounded); amount = 1250.5 x 10.123 = 12658.8115 -> 12658.81.
        var price = order.Detail.Lines[0].Price!;
        Assert.Equal(("10.1234", "0.0004", "10.123", "12658.81"), (price.UnitPrice, price.DiscountAmount, price.NetUnitPrice, price.LineAmount));
        Assert.Equal(new PurchaseOrderCurrencyView(SettingsCurrencyIdentitiesV1.TurkishLira, "TRY", 2), order.Detail.Currency);
        var detail = await world.Service().GetOrderDetailAsync(order.Detail.Order.Id);
        Assert.Equal(("10.1234", "12658.81"), (detail.Lines[0].Price!.UnitPrice, detail.Lines[0].Price!.LineAmount));
    }

    [Theory]
    [InlineData("10.12345", null)]
    [InlineData("10", "0.00001")]
    public async Task A_fifth_decimal_on_the_unit_price_or_the_discount_amount_is_refused_and_nothing_is_stored(string unitPrice, string? discountAmount)
    {
        var world = SeedWorld(SeedGdc);

        var refused = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().CreateOrderAsync(
            SeedPricedOrderOf("5", new PurchaseOrderLinePriceRequest(unitPrice, DiscountAmount: discountAmount)), Metadata("k-seed-five-" + unitPrice)));

        Assert.Equal((422, "lines[0].price", PurchasingRuleCodes.PriceScaleExceeded), (refused.Status, refused.Errors[0].Field, refused.Errors[0].Code));
        Assert.Empty(world.Store.Envelopes);
    }

    private static (Guid CompanyId, ApprovedMoneyProfileEntry Entry) SeedOf(string company) => company switch
    {
        "gdc" => (SeedGdc, ApprovedMoneyProfileCatalog.GdcTurkishLiraV1),
        "gsp" => (SeedGsp, ApprovedMoneyProfileCatalog.GspTurkishLiraV1),
        _ => (SeedGin, ApprovedMoneyProfileCatalog.GinTurkishLiraV1),
    };

    /// <summary>A world in a seed company whose money owner is the real approved Settings provider (the same context and actor).</summary>
    private static World SeedWorld(Guid companyId)
    {
        var world = new World
        {
            Accounts = new FakeAccounts { OwnerCompany = companyId },
            Settings = new FakeSettings { OwnerCompany = companyId },
        };
        world.Company.Set(companyId);
        world.User.Allowed.Add(companyId);
        var provider = new ApprovedSettingsPrerequisiteProvider(world.Company, world.User);
        world.MoneyOwner = provider;
        world.CandidateOwner = provider;
        return world;
    }

    private static CreatePurchaseOrderRequest SeedPricedOrderOf(string quantity, PurchaseOrderLinePriceRequest price) =>
        new(Party, Account, null, CatalogDay, [new PurchaseOrderLineRequest(Guid.NewGuid(), Product, "7", KgUnit, quantity, null, price)],
            SettingsCurrencyIdentitiesV1.TurkishLira);
}
