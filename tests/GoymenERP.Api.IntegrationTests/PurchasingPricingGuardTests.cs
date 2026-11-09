using System.Globalization;
using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// A2a-1 (#944 review P3) guards that need more than one module, so they live in the integration project (no database):
/// <list type="bullet">
/// <item>golden parity — the shared <see cref="CommercialLinePriceCalculator"/> Purchasing uses answers exactly what the Sales line
/// calculator answers for the same price, discount and quantity (TEK-03: one rule, written twice until Sales moves; Sales types are
/// only read here);</item>
/// <item>the real approved Settings provider answers the purchasing order money operation <c>purchasing.order.v1</c> with each seed
/// company's TRY profile — scale 2 (the final amount, Ö2) and unit-price scale 4 — since A2a-1b flipped the earlier "not in the
/// catalog" pin (ALS-D63 narrow exception, Sinan #66 6704170096); the key is the Purchasing owner's own constant.</item>
/// </list>
/// SENTETIK / NOT_ENV.
/// </summary>
public sealed class PurchasingPricingGuardTests
{
    [Theory]
    [InlineData("100", null, null, "1", 2)]
    [InlineData("41.25", "2.5", null, "1250.5", 2)]
    [InlineData("10.01", null, null, "0.5", 2)]
    [InlineData("10.03", null, "0.01", "0.5", 2)]
    [InlineData("33.33", "33.333333", null, "7.125", 2)]
    [InlineData("0.333333", "33.333333", null, "999999999999999.999", 2)]
    [InlineData("7", "100", null, "3", 0)]
    [InlineData("0", "10", null, "4", 2)]
    [InlineData("1.5", null, "1.5", "7", 6)]
    public void The_shared_calculator_answers_exactly_what_the_Sales_line_calculator_answers(string price, string? percent, string? amount,
        string quantity, int scale)
    {
        var sales = new CommercialDraftFields(price, Guid.NewGuid(), percent, DiscountAmount: amount);
        decimal quantityValue = Parse(quantity)!.Value;

        decimal net = CommercialLinePriceCalculator.NetUnitPrice(Parse(price)!.Value, Parse(percent), Parse(amount));
        decimal lineAmount = CommercialLinePriceCalculator.LineAmount(quantityValue, Parse(price)!.Value, Parse(percent), Parse(amount), scale);

        Assert.Equal(SalesLinePriceCalculator.NetUnitPrice(sales), net);
        Assert.Equal(SalesLinePriceCalculator.LineAmount(sales, quantityValue, scale), lineAmount);
        Assert.Equal(SalesLinePriceCalculator.Format(net), CommercialLinePriceCalculator.Format(net));
    }

    [Theory]
    [InlineData("10000000-0000-0000-0000-000000000001")]
    [InlineData("10000000-0000-0000-0000-000000000002")]
    [InlineData("10000000-0000-0000-0000-000000000003")]
    public async Task The_approved_Settings_provider_answers_the_purchasing_order_operation_with_the_TRY_profile(string seedCompany)
    {
        var companyId = Guid.Parse(seedCompany);
        var company = new CompanyContext();
        company.Set(companyId);
        var provider = new ApprovedSettingsPrerequisiteProvider(company, new SyntheticUser(companyId));

        // A2a-1b flipped this pin: the owner's TRY profile now answers the Purchasing owner's key too.
        var profile = await provider.GetMoneyProfileAsync(SettingsCurrencyIdentitiesV1.TurkishLira, PurchasingMoneyOperationsV1.OrderV1, new DateOnly(2026, 11, 7));
        Assert.Equal((companyId, SettingsCurrencyIdentitiesV1.TurkishLira, "TRY", PurchasingMoneyOperationsV1.OrderV1, 2, (int?)4, true),
            (profile.CompanyId, profile.CurrencyId, profile.CurrencyCode, profile.Operation, profile.Scale, profile.UnitPriceScale, profile.IsActive));
        Assert.Equal(1, profile.Profile.Version);
        Assert.Contains(ApprovedMoneyProfileCatalog.Approved.Entries, entry => entry.CompanyId == companyId && entry.ProfileId == profile.Profile.ProfileId);
        var candidates = await ((ISettingsMoneyCandidateReaderV2)provider).GetMoneyCurrencyCandidatesAsync(PurchasingMoneyOperationsV1.OrderV1);
        Assert.Equal(new MoneyCurrencyCandidateV1(SettingsCurrencyIdentitiesV1.TurkishLira, "TRY", "TRY"), Assert.Single(candidates));

        // The Sales owner's answer is unchanged and carries no unit-price scale (none is decided for it).
        var sales = await provider.GetMoneyProfileAsync(SettingsCurrencyIdentitiesV1.TurkishLira, "sales.order-draft.v1", new DateOnly(2026, 11, 7));
        Assert.Equal(("TRY", 2, (int?)null), (sales.CurrencyCode, sales.Scale, sales.UnitPriceScale));
    }

    private static decimal? Parse(string? text) => text is null ? null : decimal.Parse(text, CultureInfo.InvariantCulture);

    private sealed class SyntheticUser(Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => "SENTETIK-purchasing-pricing";
        public IReadOnlySet<Guid> AllowedCompanyIds => new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => companyId == company;
    }
}
