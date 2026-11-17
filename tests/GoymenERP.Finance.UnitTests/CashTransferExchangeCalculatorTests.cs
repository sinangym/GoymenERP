using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Finance.UnitTests;

public sealed class CashTransferExchangeCalculatorTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Usd = SettingsCurrencyIdentitiesV1.UnitedStatesDollar;
    private static readonly Guid Try = SettingsCurrencyIdentitiesV1.TurkishLira;
    private static readonly Guid Eur = SettingsCurrencyIdentitiesV1.Euro;
    private static readonly DateOnly Day = new(2026, 11, 17);
    private static readonly ProfileReference Profile = new("66666666-6666-4666-8666-666666666666", "1");

    [Fact]
    public void The_actual_two_amounts_determine_the_quote_and_TCMB_deviation_never_changes_either_money_leg()
    {
        var quote = Calculate(Usd, "100.00", "34", Try, "3300.00", "1");
        Assert.Equal(("33.00", "34.00", "-1.00"), (quote.TransactionRate, quote.ComparisonRate, quote.RateDeviation));
        Assert.Equal(new CashTransferRateRatioV1("33", "1"), quote.ExactTransactionRate);
        Assert.Equal(new MoneyText("100.00", Usd), quote.SourceMoney);
        Assert.Equal(new MoneyText("3300.00", Try), quote.TargetMoney);
        // The benchmark difference is quote information, never an accounting amount or an extra treasury movement.
    }

    [Fact]
    public void Cross_currency_benchmark_keeps_its_exact_ratio_and_actual_quote_rounds_once_half_away_from_zero()
    {
        var quote = Calculate(Eur, "2.00", "4", Usd, "2.01", "3");
        Assert.Equal(("1.01", "1.33", "-0.33"), (quote.TransactionRate, quote.ComparisonRate, quote.RateDeviation));
        Assert.Equal(new CashTransferRateRatioV1("201", "200"), quote.ExactTransactionRate);
        Assert.Equal(new CashTransferRateRatioV1("4", "3"), quote.ExactComparisonRate);
    }

    [Fact]
    public void Very_small_positive_quotes_keep_exact_evidence_even_when_the_display_rounds_to_zero()
    {
        var quote = Calculate(Try, "1000", "1", Usd, "1", "34");
        Assert.Equal("0.00", quote.TransactionRate);
        Assert.Equal(new CashTransferRateRatioV1("1", "1000"), quote.ExactTransactionRate);
        Assert.True(CashTransferExchangeCalculatorV1.SameRate("0.001", quote.ExactTransactionRate));
        Assert.False(CashTransferExchangeCalculatorV1.SameRate("0.01", quote.ExactTransactionRate));
        Assert.False(CashTransferExchangeCalculatorV1.SameRate("0", quote.ExactTransactionRate));
        Assert.False(CashTransferExchangeCalculatorV1.SameRate("1e-3", quote.ExactTransactionRate));
    }

    [Fact]
    public void The_separate_treasury_profile_is_an_empty_human_authoring_target_not_an_automatic_monetary_baseline()
    {
        Assert.Equal(FinanceMoneyOperations.CashTransferV1, MoneyProfileAuthoringCatalogV1.CashTransferOperation);
        var company = ApprovedUnitProfileCatalog.GdcCompanyId;
        Assert.Contains((Try, FinanceMoneyOperations.CashTransferV1), MoneyProfileAuthoringCatalogV1.Targets(company));
        Assert.DoesNotContain(ApprovedMoneyProfileCatalog.Approved.Entries,
            row => row.CompanyId == company && row.Operations.Contains(FinanceMoneyOperations.CashTransferV1));
    }

    private static CashTransferExchangeSnapshotV1 Calculate(Guid sourceCurrency, string source, string sourceRate,
        Guid targetCurrency, string target, string targetRate) => CashTransferExchangeCalculatorV1.Calculate(
            Company, Day, new(source, sourceCurrency), new(target, targetCurrency), Profile, Profile,
            Comparison(sourceCurrency, sourceRate), Comparison(targetCurrency, targetRate));

    private static CashTransferComparisonSourceV1 Comparison(Guid currency, string rate) => currency == Try
        ? new(currency, rate, Day, "FunctionalCurrency", null, null, null, null)
        : new(currency, rate, Day, "TCMB", Guid.NewGuid(), "1", Day.AddDays(-1), "SENTETIK / NOT_ENV");
}
