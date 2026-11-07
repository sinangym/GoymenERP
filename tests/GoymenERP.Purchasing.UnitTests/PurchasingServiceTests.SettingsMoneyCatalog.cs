using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// A2a-1b (ALS-D63 narrow exception, Sinan #66 6704170096): the service composed with the REAL approved Settings provider, not a
/// fake, for the seed companies. The Settings catalog now answers <c>purchasing.order.v1</c> with each company's TRY profile, so a
/// priced order opens and records that profile as its money snapshot. The profile also publishes a unit-price scale of 4 for this
/// operation; the Purchasing pricing path does not read it yet (the stored snapshot has no column for it), so a unit price is still
/// held to the amount scale — pinned below so the follow-up flips it deliberately. SENTETIK / NOT_ENV.
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
        Assert.Equal((SettingsCurrencyIdentitiesV1.TurkishLira, "TRY", entry.ProfileId, 1L, 2),
            (stored.CurrencyId, stored.CurrencyCode, stored.ProfileId, stored.ProfileVersion, stored.Scale));
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
    public async Task A_four_decimal_unit_price_is_still_held_to_the_amount_scale_until_Purchasing_reads_the_unit_price_scale()
    {
        // The Settings answer carries UnitPriceScale 4 for purchasing.order.v1 (A2a-1b); the order snapshot has no column for it yet,
        // so the pricing path keeps min(amount scale, 6) = 2. This pin flips in the follow-up that persists and honours it.
        var world = SeedWorld(SeedGdc);
        var answered = await world.MoneyOwner!.GetMoneyProfileAsync(SettingsCurrencyIdentitiesV1.TurkishLira, PurchasingMoneyOperationsV1.OrderV1, CatalogDay);
        Assert.Equal((2, (int?)4), (answered.Scale, answered.UnitPriceScale));

        var refused = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().CreateOrderAsync(
            SeedPricedOrderOf("5", new PurchaseOrderLinePriceRequest("10.1234")), Metadata("k-seed-four")));

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
