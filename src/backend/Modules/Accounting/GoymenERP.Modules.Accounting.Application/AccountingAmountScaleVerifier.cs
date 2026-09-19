using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Accounting.Application;

/// <summary>The Accounting operation keys a Settings money profile answers for (the operation owner's keys).</summary>
public static class AccountingMoneyOperations
{
    /// <summary>
    /// The final amount of a journal line (Ö2: TL/USD/EUR final amounts carry 2 decimals). Settings'
    /// approved money profile answers only for the operation keys it lists (Business #254 5654539215);
    /// until this key is listed on the company's profile, every posting is refused with the Settings
    /// owner's own code (fail-closed), never checked against a guessed scale.
    /// </summary>
    public const string JournalAmountV1 = "accounting.journal-amount.v1";
}

/// <summary>The refusal codes this verifier adds on top of the Settings owner's own codes.</summary>
public static class AccountingAmountScaleCodes
{
    public const string ProfileCompanyMismatch = "accounting_money_profile_company_mismatch";
    public const string ProfileInvalid = "accounting_money_profile_invalid";
    public const string ProfileNotInEffect = "accounting_money_profile_not_in_effect";

    /// <summary>Ö2: an amount carries more decimals than the currency's approved scale; refused, never rounded.</summary>
    public const string ScaleExceeded = "accounting_amount_scale_exceeded";
}

/// <summary>A refused amount check: the stable code (the Settings owner's, or this verifier's). Nothing was stored.</summary>
public sealed class AccountingAmountScaleException(string code)
    : Exception($"The journal amounts were not accepted: {code}.")
{
    public string Code { get; } = code;
}

/// <summary>
/// Checks journal amounts against the company's approved money profile for the journal currency on its
/// accounting date (Ö2, ACC-V0; the README's former <c>dependency_wait</c>). The Settings owner's port is
/// the only source: nothing here guesses a scale, a profile or a currency, and nothing is rounded.
/// <list type="bullet">
/// <item>Fail-closed: every <see cref="SettingsPrerequisiteException"/> keeps the owner's code verbatim
/// (<c>settings_access_denied</c>, <c>settings_provider_unavailable</c>, the profile-not-found code, …).</item>
/// <item>The profile must belong to the server-resolved company, name the journal currency and this
/// operation, be well formed and be active on the accounting date.</item>
/// <item>An amount with more significant decimals than the profile's scale is refused
/// (<see cref="AccountingAmountScaleCodes.ScaleExceeded"/>); trailing zeros are not precision.</item>
/// </list>
/// </summary>
public sealed class AccountingAmountScaleVerifier(ISettingsPrerequisiteProvider settings)
{
    public async Task VerifyAsync(Guid companyId, Guid currencyId, DateOnly accountingDate, IEnumerable<decimal> amounts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(amounts);
        var values = amounts.ToArray();
        if (values.Length == 0)
        {
            // Nothing is valued yet (every line is pending): there is no amount to check.
            return;
        }

        MoneyProfileV1 profile;
        try
        {
            profile = await settings.GetMoneyProfileAsync(currencyId, AccountingMoneyOperations.JournalAmountV1, accountingDate,
                cancellationToken);
        }
        catch (SettingsPrerequisiteException exception)
        {
            throw new AccountingAmountScaleException(exception.Code);
        }

        if (profile is null || profile.Profile is null || profile.Profile.ProfileId == Guid.Empty || profile.Profile.Version <= 0
            || profile.Scale < 0 || profile.CurrencyId != currencyId
            || !string.Equals(profile.Operation, AccountingMoneyOperations.JournalAmountV1, StringComparison.Ordinal))
        {
            throw new AccountingAmountScaleException(AccountingAmountScaleCodes.ProfileInvalid);
        }

        if (profile.CompanyId != companyId)
        {
            throw new AccountingAmountScaleException(AccountingAmountScaleCodes.ProfileCompanyMismatch);
        }

        if (!profile.IsActive || accountingDate < profile.EffectiveFrom
            || (profile.EffectiveThrough is { } through && accountingDate > through))
        {
            throw new AccountingAmountScaleException(AccountingAmountScaleCodes.ProfileNotInEffect);
        }

        if (values.Any(value => SignificantDecimals(value) > profile.Scale))
        {
            throw new AccountingAmountScaleException(AccountingAmountScaleCodes.ScaleExceeded);
        }
    }

    /// <summary>The number of decimals that carry a non-zero digit: 12.5000 → 1, 12.5001 → 4, 1000 → 0.</summary>
    private static int SignificantDecimals(decimal value)
    {
        var decimals = 0;
        while (decimal.Round(value, decimals) != value)
        {
            decimals++;
        }

        return decimals;
    }
}
