using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Invoices;

namespace GoymenERP.Sales.UnitTests;

/// <summary>S5 API-36 (SAT-D227): the TL value of each instalment of a foreign-currency invoice. Every value is SENTETIK.</summary>
public sealed class SalesInvoiceInstalmentCalculatorTests
{
    private static readonly Guid Usd = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private static readonly Guid Try = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly DateOnly Day = new(2026, 11, 7);

    private static SalesInvoiceRateSnapshot Rate(decimal rate) => new(rate, Day, Day, Try, 2);

    private static SalesInvoiceInstalment Instalment(int no, string percent, decimal amount) =>
        new(no, Day.AddDays(30 * no), percent, Money.Of(amount, Usd, 2));

    [Fact]
    public void The_sat_d227_golden_rounds_each_instalment_and_gives_the_remainder_to_the_last()
    {
        var instalments = new[] { Instalment(1, "33.33", 33.33m), Instalment(2, "33.33", 33.33m), Instalment(3, "33.34", 33.35m) };
        var result = SalesInvoiceInstalmentCalculator.FunctionalAmounts(instalments, Rate(34.5678m), Usd, Money.Of(3457.13m, Try, 2));

        Assert.Equal([1152.14m, 1152.14m, 1152.85m], result);
        Assert.Equal(3457.13m, result.Sum());
    }

    [Fact]
    public void A_try_invoice_has_no_tl_split()
    {
        var instalments = new[] { Instalment(1, "100", 10m) };
        Assert.Equal([(decimal?)null], SalesInvoiceInstalmentCalculator.FunctionalAmounts(instalments, null, Try, null));
    }

    [Fact]
    public void A_foreign_invoice_without_a_tl_total_or_with_a_negative_remainder_is_refused()
    {
        var instalments = new[] { Instalment(1, "50", 50m), Instalment(2, "50", 50m) };
        var missing = Assert.Throws<SalesRuleException>(() => SalesInvoiceInstalmentCalculator.FunctionalAmounts(instalments, Rate(2m), Usd, null));
        Assert.Equal(SalesRuleCodes.InvoiceInstalmentFunctionalInvalid, missing.Code);
        var negative = Assert.Throws<SalesRuleException>(() =>
            SalesInvoiceInstalmentCalculator.FunctionalAmounts(instalments, Rate(2m), Usd, Money.Of(50m, Try, 2)));
        Assert.Equal(SalesRuleCodes.InvoiceInstalmentFunctionalInvalid, negative.Code);
    }

    [Fact]
    public void A_negative_last_tl_instalment_is_refused_under_sat_d233()
    {
        // 60 + 40 USD at rate 2: the first instalment is 120 TL, so a 110 TL payable leaves -10 TL for the last one.
        var instalments = new[] { Instalment(1, "60", 60m), Instalment(2, "40", 40m) };
        var refused = Assert.Throws<SalesRuleException>(() =>
            SalesInvoiceInstalmentCalculator.FunctionalAmounts(instalments, Rate(2m), Usd, Money.Of(110m, Try, 2)));
        Assert.Equal(SalesRuleCodes.InvoiceInstalmentFunctionalInvalid, refused.Code);
    }

    [Fact]
    public void A_zero_last_tl_instalment_is_refused_like_a_negative_one()
    {
        // 50 + 50 USD at rate 2: the first instalment is 100 TL, so a 100 TL payable leaves exactly 0 TL for the last one.
        var instalments = new[] { Instalment(1, "50", 50m), Instalment(2, "50", 50m) };
        var refused = Assert.Throws<SalesRuleException>(() =>
            SalesInvoiceInstalmentCalculator.FunctionalAmounts(instalments, Rate(2m), Usd, Money.Of(100m, Try, 2)));
        Assert.Equal(SalesRuleCodes.InvoiceInstalmentFunctionalInvalid, refused.Code);
    }

    [Fact]
    public void The_smallest_positive_last_tl_instalment_is_accepted()
    {
        var instalments = new[] { Instalment(1, "50", 50m), Instalment(2, "50", 50m) };
        var result = SalesInvoiceInstalmentCalculator.FunctionalAmounts(instalments, Rate(2m), Usd, Money.Of(100.01m, Try, 2));

        Assert.Equal([100m, 0.01m], result);
    }

    [Fact]
    public void An_empty_instalment_list_is_refused()
    {
        Assert.Throws<ArgumentException>(() => SalesInvoiceInstalmentCalculator.FunctionalAmounts([], Rate(2m), Usd, Money.Of(1m, Try, 2)));
    }
}
