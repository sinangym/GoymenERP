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
/// <item>the real approved Settings provider answers <c>settings_provider_unavailable</c> for the purchasing order money operation
/// today, so a priced purchase order is fail-closed. This test flips deliberately in A2a-1b, when the Settings catalog approves
/// <c>purchasing.order.v1</c> (Sinan #66 6704170096, narrow exception).</item>
/// </list>
/// SENTETIK / NOT_ENV.
/// </summary>
public sealed class PurchasingPricingGuardTests
{
    private static readonly Guid Gdc = Guid.Parse("10000000-0000-0000-0000-000000000001");

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

    [Fact]
    public async Task The_approved_Settings_provider_has_no_money_profile_for_the_purchasing_order_operation_yet()
    {
        var company = new CompanyContext();
        company.Set(Gdc);
        var provider = new ApprovedSettingsPrerequisiteProvider(company, new SyntheticUser());

        // The owner's TRY profile exists for its own operations; only the purchasing key is not approved (A2a-1b flips this).
        var sales = await provider.GetMoneyProfileAsync(SettingsCurrencyIdentitiesV1.TurkishLira, "sales.order-draft.v1", new DateOnly(2026, 11, 7));
        Assert.Equal("TRY", sales.CurrencyCode);
        var refused = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => provider.GetMoneyProfileAsync(
            SettingsCurrencyIdentitiesV1.TurkishLira, PurchasingMoneyOperationsV1.OrderV1, new DateOnly(2026, 11, 7)));
        Assert.Equal("settings_provider_unavailable", refused.Code);
        var candidates = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() =>
            ((ISettingsMoneyCandidateReaderV2)provider).GetMoneyCurrencyCandidatesAsync(PurchasingMoneyOperationsV1.OrderV1));
        Assert.Equal("settings_provider_unavailable", candidates.Code);
    }

    private static decimal? Parse(string? text) => text is null ? null : decimal.Parse(text, CultureInfo.InvariantCulture);

    private sealed class SyntheticUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => "SENTETIK-purchasing-pricing";
        public IReadOnlySet<Guid> AllowedCompanyIds => new HashSet<Guid> { Gdc };
        public bool CanAccessCompany(Guid companyId) => companyId == Gdc;
    }
}
