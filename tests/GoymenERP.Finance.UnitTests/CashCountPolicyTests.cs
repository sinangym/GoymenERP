using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;

namespace GoymenERP.Finance.UnitTests;

public sealed class CashCountPolicyTests
{
    private static readonly Guid CompanyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CountId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TreasuryAccountId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid CurrencyId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid CounterId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly DateTimeOffset CountedAtUtc = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Equal_count_is_balanced_and_creates_no_money_event()
    {
        var result = Prepare("12500.00", "12500", reason: null);

        Assert.Equal(CashCountDifferenceDirectionV1.Balanced, result.Direction);
        Assert.Equal(new MoneyText("0", CurrencyId), result.Difference);
        Assert.False(result.RequiresApproval);
        Assert.False(result.CreatesMoneyEvent);
        Assert.Null(result.DifferenceReason);
        Assert.Equal(1, result.Version);
    }

    [Fact]
    public void Lower_physical_count_is_short_and_requires_a_reason_and_approval()
    {
        var result = Prepare("12500", "12000", "  Sayim tutanagi ile eksik.  ");

        Assert.Equal(CashCountDifferenceDirectionV1.Short, result.Direction);
        Assert.Equal(new MoneyText("500", CurrencyId), result.Difference);
        Assert.Equal("Sayim tutanagi ile eksik.", result.DifferenceReason);
        Assert.True(result.RequiresApproval);
        Assert.False(result.CreatesMoneyEvent);
    }

    [Fact]
    public void Higher_physical_count_is_over_without_becoming_a_money_event()
    {
        var result = Prepare("100", "112.50", "Fazla sayim arastirilacak.");

        Assert.Equal(CashCountDifferenceDirectionV1.Over, result.Direction);
        Assert.Equal(new MoneyText("12.5", CurrencyId), result.Difference);
        Assert.True(result.RequiresApproval);
        Assert.False(result.CreatesMoneyEvent);
    }

    [Fact]
    public void Nonzero_difference_without_reason_is_refused()
    {
        AssertCode(
            CashCountPolicyCodes.DifferenceReasonRequired,
            "differenceReason",
            () => Prepare("100", "99", " "));
    }

    [Fact]
    public void Currency_mismatch_is_refused_without_conversion()
    {
        var otherCurrency = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var request = Request("100", "100") with
        {
            CountedBalance = new MoneyText("100", otherCurrency)
        };

        AssertCode(
            CashCountPolicyCodes.CurrencyMismatch,
            "countedBalance",
            () => CashCountPolicy.Prepare(CompanyId, CounterId, request, CountedAtUtc));
    }

    [Theory]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("1,00")]
    [InlineData(".5")]
    [InlineData("5.")]
    [InlineData("1.2.3")]
    [InlineData("NaN")]
    public void Invalid_balance_text_is_refused_without_locale_or_sign_coercion(string amount)
    {
        AssertCode(
            CashCountPolicyCodes.InvalidRequest,
            "bookBalance",
            () => Prepare(amount, "0"));
    }

    [Fact]
    public void Identity_currency_and_utc_inputs_fail_closed()
    {
        var request = Request("0", "0");

        AssertCode(
            CashCountPolicyCodes.InvalidRequest,
            "companyId",
            () => CashCountPolicy.Prepare(Guid.Empty, CounterId, request, CountedAtUtc));
        AssertCode(
            CashCountPolicyCodes.InvalidRequest,
            "treasuryAccountId",
            () => CashCountPolicy.Prepare(
                CompanyId,
                CounterId,
                request with { TreasuryAccountId = Guid.Empty },
                CountedAtUtc));
        AssertCode(
            CashCountPolicyCodes.InvalidRequest,
            "bookBalance",
            () => CashCountPolicy.Prepare(
                CompanyId,
                CounterId,
                request with { BookBalance = new MoneyText("0", Guid.Empty) },
                CountedAtUtc));
        AssertCode(
            CashCountPolicyCodes.InvalidRequest,
            "countedAtUtc",
            () => CashCountPolicy.Prepare(
                CompanyId,
                CounterId,
                request,
                CountedAtUtc.ToOffset(TimeSpan.FromHours(3))));
    }

    private static CashCountDraftV1 Prepare(
        string bookBalance,
        string countedBalance,
        string? reason = null) =>
        CashCountPolicy.Prepare(
            CompanyId,
            CounterId,
            Request(bookBalance, countedBalance, reason),
            CountedAtUtc);

    private static PrepareCashCountV1 Request(
        string bookBalance,
        string countedBalance,
        string? reason = null) =>
        new(
            CountId,
            TreasuryAccountId,
            new MoneyText(bookBalance, CurrencyId),
            new MoneyText(countedBalance, CurrencyId),
            reason);

    private static void AssertCode(string code, string field, Action action)
    {
        var error = Assert.Throws<CashCountPolicyException>(action);
        Assert.Equal(code, error.Code);
        Assert.Equal(field, error.Field);
    }
}
