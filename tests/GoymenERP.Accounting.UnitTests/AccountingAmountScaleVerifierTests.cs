using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Accounting.UnitTests;

/// <summary>
/// ACC-V0 (Ö2): journal amounts are checked against the company's approved money profile for the journal
/// currency and Accounting's operation key. Nothing is rounded, and nothing is guessed. The profiles here
/// are SYNTHETIC test sources, not Settings' approved data.
/// </summary>
public sealed class AccountingAmountScaleVerifierTests
{
    private static readonly Guid Company = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    private static readonly Guid Currency = Guid.Parse("5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a71");
    private static readonly DateOnly OnDate = new(2026, 9, 15);

    [Fact]
    public async Task Amounts_at_the_profile_scale_pass_and_trailing_zeros_are_not_precision()
    {
        var verifier = new AccountingAmountScaleVerifier(SyntheticSettings.Scale(Company, 2));

        await verifier.VerifyAsync(Company, Currency, OnDate, [100m, 1250.5m, 1250.50m, 0.01m, 7.000m]);
    }

    [Fact]
    public async Task A_third_decimal_is_refused_never_rounded()
    {
        var verifier = new AccountingAmountScaleVerifier(SyntheticSettings.Scale(Company, 2));

        var error = await Assert.ThrowsAsync<AccountingAmountScaleException>(
            () => verifier.VerifyAsync(Company, Currency, OnDate, [100m, 1250.505m]));
        Assert.Equal(AccountingAmountScaleCodes.ScaleExceeded, error.Code);
    }

    [Fact]
    public async Task No_known_amount_needs_no_profile()
    {
        // Every line is pending (an unknown amount stays pending, never zero): there is nothing to check.
        var verifier = new AccountingAmountScaleVerifier(SyntheticSettings.Refusing("settings_provider_unavailable"));

        await verifier.VerifyAsync(Company, Currency, OnDate, []);
    }

    [Theory]
    [InlineData("settings_provider_unavailable")]
    [InlineData("settings_money_profile_not_found")]
    [InlineData("settings_access_denied")]
    public async Task A_Settings_refusal_keeps_its_own_code_fail_closed(string code)
    {
        var verifier = new AccountingAmountScaleVerifier(SyntheticSettings.Refusing(code));

        var error = await Assert.ThrowsAsync<AccountingAmountScaleException>(
            () => verifier.VerifyAsync(Company, Currency, OnDate, [100m]));
        Assert.Equal(code, error.Code);
    }

    [Theory]
    [InlineData("other-company", AccountingAmountScaleCodes.ProfileCompanyMismatch)]
    [InlineData("other-currency", AccountingAmountScaleCodes.ProfileInvalid)]
    [InlineData("other-operation", AccountingAmountScaleCodes.ProfileInvalid)]
    [InlineData("no-version", AccountingAmountScaleCodes.ProfileInvalid)]
    [InlineData("inactive", AccountingAmountScaleCodes.ProfileNotInEffect)]
    [InlineData("not-yet", AccountingAmountScaleCodes.ProfileNotInEffect)]
    [InlineData("expired", AccountingAmountScaleCodes.ProfileNotInEffect)]
    public async Task A_profile_that_does_not_fit_the_journal_is_refused(string fault, string code)
    {
        var profile = new MoneyProfileV1(Company, Currency, "SYN", AccountingMoneyOperations.JournalAmountV1,
            new ProfileVersionV1(Guid.NewGuid(), 1), 2, IsActive: true, EffectiveFrom: new DateOnly(2026, 1, 1), EffectiveThrough: null);
        profile = fault switch
        {
            "other-company" => profile with { CompanyId = Guid.NewGuid() },
            "other-currency" => profile with { CurrencyId = Guid.NewGuid() },
            "other-operation" => profile with { Operation = "finance.manual-bank-receipt.v1" },
            "no-version" => profile with { Profile = new ProfileVersionV1(Guid.NewGuid(), 0) },
            "inactive" => profile with { IsActive = false },
            "not-yet" => profile with { EffectiveFrom = OnDate.AddDays(1) },
            _ => profile with { EffectiveThrough = OnDate.AddDays(-1) },
        };
        var verifier = new AccountingAmountScaleVerifier(new SyntheticSettings(profile, null));

        var error = await Assert.ThrowsAsync<AccountingAmountScaleException>(
            () => verifier.VerifyAsync(Company, Currency, OnDate, [100m]));
        Assert.Equal(code, error.Code);
    }
}

/// <summary>A SYNTHETIC Settings source for unit tests: one money profile, or one refusal code.</summary>
internal sealed class SyntheticSettings(MoneyProfileV1? profile, string? refusal) : ISettingsPrerequisiteProvider
{
    public static SyntheticSettings Scale(Guid company, int scale) =>
        new(new MoneyProfileV1(company, Guid.Empty, "SYN", AccountingMoneyOperations.JournalAmountV1,
            new ProfileVersionV1(Guid.Parse("5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a99"), 1), scale, IsActive: true,
            EffectiveFrom: new DateOnly(2026, 1, 1), EffectiveThrough: null), null);

    public static SyntheticSettings Refusing(string code) => new(null, code);

    public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn,
        CancellationToken cancellationToken = default)
    {
        if (refusal is not null)
        {
            return Task.FromException<MoneyProfileV1>(new SettingsPrerequisiteException(refusal));
        }

        // A scale-only source answers for whatever currency and operation it is asked (the caller's own).
        return Task.FromResult(profile!.CurrencyId == Guid.Empty ? profile with { CurrencyId = currencyId, Operation = operation } : profile);
    }

    public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn, DateOnly? valueOn,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
