using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Finance.UnitTests;

/// <summary>FNS-D24 shape, profile and commit rules. SENTETIK; no owner read.</summary>
public sealed class CashTransferValidatorTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Lira = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Euro = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private static readonly Guid Source = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Target = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid Profile = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly DateOnly OccurredOn = new(2026, 11, 10);

    [Fact]
    public void Same_currency_transfer_and_exchange_pass_the_shape_rules()
    {
        Assert.Empty(CashTransferValidator.ValidateShape(SameCurrency()));
        Assert.Empty(CashTransferValidator.ValidateShape(Exchange()));
    }

    [Fact]
    public void Same_currency_transfer_refuses_two_currencies_two_amounts_and_a_rate()
    {
        Assert.Equal(["cash_transfer_currency_mismatch"], Codes(SameCurrency() with
        {
            TargetMoney = new MoneyText("1000.00", Euro),
        }));
        Assert.Equal(["cash_transfer_amount_mismatch"], Codes(SameCurrency() with
        {
            TargetMoney = new MoneyText("1000", Lira),
        }));
        Assert.Equal(["cash_transfer_rate_not_allowed"], Codes(SameCurrency() with { Rate = "1" }));
    }

    [Fact]
    public void Exchange_refuses_one_currency_a_missing_rate_and_a_malformed_rate()
    {
        Assert.Equal(["cash_transfer_currency_same"], Codes(Exchange() with
        {
            TargetMoney = new MoneyText("3650.00", Euro),
        }));
        Assert.Equal(["cash_transfer_rate_required"], Codes(Exchange() with { Rate = null }));
        Assert.Equal(["cash_transfer_rate_invalid"], Codes(Exchange() with { Rate = "36,5" }));
    }

    [Fact]
    public void Same_account_is_refused_only_when_the_kind_is_also_the_same()
    {
        Assert.Equal(["cash_transfer_same_account"], Codes(SameCurrency() with
        {
            TargetKind = CashTransferAccountKind.Bank,
            TargetAccountId = Source,
        }));
        Assert.Empty(Codes(SameCurrency() with { TargetAccountId = Source }));
    }

    [Fact]
    public void Missing_identities_unknown_kinds_texts_and_value_date_are_refused()
    {
        Assert.Contains("identity_required", Codes(SameCurrency() with { SourceAccountId = Guid.Empty }));
        Assert.Equal(["identity_required"], Codes(SameCurrency() with { SourceMoney = null! }));
        Assert.Contains("cash_transfer_kind_invalid", Codes(SameCurrency() with { Kind = (CashTransferKind)9 }));
        Assert.Contains("cash_transfer_account_kind_invalid",
            Codes(SameCurrency() with { SourceKind = (CashTransferAccountKind)9 }));
        Assert.Equal(["cash_transfer_description_invalid"], Codes(SameCurrency() with
        {
            Description = new string('x', CashTransferValidator.DescriptionMaximumLength + 1),
        }));
        Assert.Equal(["cash_transfer_reference_invalid"], Codes(SameCurrency() with { Reference = "  " }));
        Assert.Equal(["cash_transfer_reference_invalid"], Codes(SameCurrency() with
        {
            Reference = new string('r', CashTransferValidator.ReferenceMaximumLength + 1),
        }));
        Assert.Equal(["value_date_before_occurred_date"], Codes(SameCurrency() with
        {
            ValueOn = OccurredOn.AddDays(-1),
        }));
        Assert.Empty(Codes(SameCurrency() with { ValueOn = OccurredOn }));
    }

    [Theory]
    [InlineData("36.5", true)]
    [InlineData("0.0000000001", true)]
    [InlineData("123456789012345678", true)]
    [InlineData("0", false)]
    [InlineData("-1", false)]
    [InlineData("36,5", false)]
    [InlineData("1e3", false)]
    [InlineData(" 36.5", false)]
    [InlineData("0.00000000001", false)]
    [InlineData("1234567890123456789", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Rate_text_is_an_exact_positive_decimal_within_the_storage_bounds(string? value, bool expected)
    {
        Assert.Equal(expected, CashTransferValidator.IsRateText(value));
    }

    [Fact]
    public void Commit_needs_the_transfer_confirmation()
    {
        Assert.Empty(CashTransferValidator.ValidateCommit(new CommitCashTransferRequest(SameCurrency(), true)));
        var issue = Assert.Single(CashTransferValidator.ValidateCommit(
            new CommitCashTransferRequest(SameCurrency(), false)));
        Assert.Equal(("transferConfirmed", "cash_transfer_not_confirmed"), (issue.Field, issue.Code));
    }

    [Fact]
    public void Leg_profile_matches_only_the_named_active_effective_profile_of_the_company_currency_and_operation()
    {
        Assert.Empty(Leg(MoneyProfile()));
        Assert.Equal(["settings_provider_unavailable"], Leg(null));
        Assert.Equal(["settings_profile_invalid"], Leg(MoneyProfile() with { Profile = new ProfileVersionV1(Guid.Empty, 1) }));
        Assert.Equal(["profile_mismatch"], Leg(MoneyProfile() with { CompanyId = Guid.NewGuid() }));
        Assert.Equal(["profile_mismatch"], Leg(MoneyProfile() with { CurrencyId = Euro }));
        Assert.Equal(["profile_mismatch"], Leg(MoneyProfile() with { IsActive = false }));
        Assert.Equal(["profile_mismatch"], Leg(MoneyProfile() with { Operation = "another.operation" }));
        Assert.Equal(["profile_mismatch"], Leg(MoneyProfile() with { Profile = new ProfileVersionV1(Profile, 2) }));
        Assert.Equal(["profile_mismatch"], Leg(MoneyProfile() with { EffectiveFrom = OccurredOn.AddDays(1) }));
        Assert.Equal(["profile_mismatch"], Leg(MoneyProfile() with { EffectiveThrough = OccurredOn.AddDays(-1) }));
    }

    [Fact]
    public void Leg_amount_over_the_profile_scale_is_refused_and_never_rounded()
    {
        var issues = CashTransferValidator.ValidateLegProfile("sourceProfile", "sourceMoney.amount",
            new MoneyText("1000.001", Lira), new ProfileReference(Profile.ToString("D"), "1"), OccurredOn, Company,
            MoneyProfile(), FinanceMoneyOperations.ManualBankPaymentV1);
        var issue = Assert.Single(issues);
        Assert.Equal("sourceMoney.amount", issue.Field);
    }

    private static string[] Codes(CashTransferPreparation preparation) =>
        CashTransferValidator.ValidateShape(preparation).Select(issue => issue.Code).ToArray();

    private static string[] Leg(MoneyProfileV1? profile) =>
        CashTransferValidator.ValidateLegProfile("sourceProfile", "sourceMoney.amount",
                new MoneyText("1000.00", Lira), new ProfileReference(Profile.ToString("D"), "1"), OccurredOn, Company,
                profile, FinanceMoneyOperations.ManualBankPaymentV1)
            .Select(issue => issue.Code).ToArray();

    private static MoneyProfileV1 MoneyProfile() => new(Company, Lira, "TRY",
        FinanceMoneyOperations.ManualBankPaymentV1, new ProfileVersionV1(Profile, 1), 2, true,
        new DateOnly(2026, 1, 1), null);

    private static CashTransferPreparation SameCurrency() => new(
        CashTransferKind.SameCurrency,
        CashTransferAccountKind.Bank, Source, new MoneyText("1000.00", Lira), new ProfileReference(Profile.ToString("D"), "1"),
        CashTransferAccountKind.Cash, Target, new MoneyText("1000.00", Lira), new ProfileReference(Profile.ToString("D"), "1"),
        null, OccurredOn, null, null, "SENTETIK virman");

    private static CashTransferPreparation Exchange() => new(
        CashTransferKind.CurrencyExchange,
        CashTransferAccountKind.Cash, Target, new MoneyText("100.00", Euro), new ProfileReference(Profile.ToString("D"), "1"),
        CashTransferAccountKind.Bank, Source, new MoneyText("3650.00", Lira), new ProfileReference(Profile.ToString("D"), "1"),
        "36.5", OccurredOn, null, null, "SENTETIK doviz bozdurma");
}
