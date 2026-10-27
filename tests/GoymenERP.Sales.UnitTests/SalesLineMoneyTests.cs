using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// TEK-03: a line's price fields are <see cref="Money"/> in the domain (draft currency, money profile scale) while the
/// wire and the row keep the exact text. The refusals and their order are unchanged. SENTETIK identities.
/// </summary>
public sealed class SalesLineMoneyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 18, 14, 0, 0, TimeSpan.FromHours(3));
    private static readonly DateOnly Day = new(2026, 9, 18);
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Currency = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid KgUnit = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid Product = Guid.Parse("99999999-9999-4999-8999-999999999999");

    private static SalesOrderDraft Draft(int scale = 2) => SalesOrderDraft.Create(Guid.NewGuid(), Company, "SO-2026-000001",
        new SalesDraftHeader(Guid.Parse("33333333-3333-4333-8333-333333333333"), Guid.Parse("44444444-4444-4444-8444-444444444444"), Currency, Day),
        new MoneyProfileSnapshot(Company, Currency, SalesOrderDraft.MoneyOperation, Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"), "3",
            Day, true, new DateOnly(2026, 9, 1), null, scale), T0);

    private static SalesDraftLineInput Line(CommercialDraftFields commercial)
    {
        var unit = new UnitProfileSnapshot(Company, KgUnit, "kg", "weight", Guid.Parse("77777777-7777-4777-8777-777777777777"), "4",
            true, 3, 3, true, new DateOnly(2026, 9, 1), null);
        var binding = new UnitBindingSnapshot(Company, KgUnit, unit.ProfileId, unit.ProfileVersion);
        var product = new ProductSnapshot(Product, "7", "GDC-001", "Sentetik bant", Day, binding, binding, binding);
        return new SalesDraftLineInput(Guid.NewGuid(), product, unit, "1", 1m, Commercial: commercial);
    }

    private static string CodeOf(Action action) => Assert.Throws<SalesRuleException>(action).Code;

    [Fact]
    public void A_priced_line_reads_as_money_in_the_draft_currency_at_the_profile_scale_while_the_text_stays_verbatim()
    {
        var draft = Draft();
        var line = draft.AddLine(1, Line(new CommercialDraftFields("42000.50", KgUnit, null, DiscountAmount: "500.5", SurchargeAmount: "10")), T0);

        var money = SalesLineMoney.ForLine(draft, line);

        Assert.Equal(Money.Of(42000.5m, Currency, 2), money.UnitPrice);
        Assert.Equal(Money.Of(500.5m, Currency, 2), money.DiscountAmount);
        Assert.Equal(Money.Of(10m, Currency, 2), money.SurchargeAmount);
        Assert.Equal((Currency, 2), (money.UnitPrice!.Value.CurrencyId, money.UnitPrice.Value.Scale));
        // The wire/row text is the person's, exactly.
        Assert.Equal(("42000.50", "500.5", "10"), (line.Commercial.UnitPrice, line.Commercial.DiscountAmount, line.Commercial.SurchargeAmount));
    }

    [Fact]
    public void An_unpriced_line_has_no_money_and_a_percent_is_never_money()
    {
        var draft = Draft();
        var unpriced = draft.AddLine(1, Line(CommercialDraftFields.None), T0);
        Assert.Equal(new SalesLineMoney(null, null, null), SalesLineMoney.ForLine(draft, unpriced));

        var percent = draft.AddLine(2, Line(new CommercialDraftFields("100", KgUnit, "10", SurchargePercent: "2")), T0);
        Assert.Equal(new SalesLineMoney(Money.Of(100m, Currency, 2), null, null), SalesLineMoney.ForLine(draft, percent));
        Assert.Throws<ArgumentException>(() => SalesLineMoney.ForLine(Draft(), percent));
    }

    [Fact]
    public void A_discount_amount_above_the_price_is_refused_and_equal_is_allowed()
    {
        Assert.Equal(SalesRuleCodes.DiscountExceedsPrice,
            CodeOf(() => Draft().AddLine(1, Line(new CommercialDraftFields("10", KgUnit, null, DiscountAmount: "10.01")), T0)));
        Assert.Equal(SalesRuleCodes.DiscountExceedsPrice,
            CodeOf(() => Draft().AddLine(1, Line(new CommercialDraftFields("9.99", KgUnit, null, DiscountAmount: "10")), T0)));
        var line = Draft().AddLine(1, Line(new CommercialDraftFields("10.00", KgUnit, null, DiscountAmount: "10")), T0);
        Assert.Equal("0", SalesLinePriceCalculator.NetUnitPriceText(line.Commercial));
    }

    [Fact]
    public void The_text_scale_rule_still_comes_first_and_trailing_zeros_on_the_wire_still_count()
    {
        // The wire rule is lexical and stricter than Money's value scale: "12.500" carries three decimals as text.
        Assert.Equal(SalesRuleCodes.PriceScaleExceeded,
            CodeOf(() => Draft().AddLine(1, Line(new CommercialDraftFields("12.500", KgUnit, null)), T0)));
        Assert.Equal(SalesRuleCodes.AdjustmentScaleExceeded,
            CodeOf(() => Draft().AddLine(1, Line(new CommercialDraftFields("12", KgUnit, null, DiscountAmount: "1.001")), T0)));
        Assert.Equal(SalesRuleCodes.AdjustmentScaleExceeded,
            CodeOf(() => Draft().AddLine(1, Line(new CommercialDraftFields("12", KgUnit, null, SurchargeAmount: "1.100")), T0)));
        // The profile's scale is the only scale: at 6 the same texts are accepted.
        var line = Draft(scale: 6).AddLine(1, Line(new CommercialDraftFields("12.500", KgUnit, null, DiscountAmount: "1.001")), T0);
        Assert.Equal("12.500", line.Commercial.UnitPrice);
    }

    [Fact]
    public void A_profile_scale_beyond_decimal_precision_allows_what_twenty_eight_decimals_allow()
    {
        var money = SalesLineMoney.From(new CommercialDraftFields("1.5", KgUnit, null), Currency, Money.MaximumScale + 2);
        Assert.Equal(Money.MaximumScale, money.UnitPrice!.Value.Scale);
    }
}
