namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>
/// SAT-D102 (<c>docs/decisions/SINAN_KARARLARI_2026-10-28_SATIS_EK13.md</c>): the Settings-owned e-Fatura scenario
/// policy of the server-resolved company. Two effective-dated keys:
/// <list type="bullet">
/// <item><b>The scenario code list</b> — the UBL-TR profile codes (e.g. TEMELFATURA / TICARIFATURA) a Cari card may
/// choose from as its per-customer override. The codes are Settings data, never a constant of a consumer.</item>
/// <item><b>The default scenario</b> — used for a customer without an override. Its starting value is EMPTY: Sinan
/// enters it (SAT-D102, "Boş kalsın, ben gireyim"); until then every read answers
/// <see cref="SettingsPrerequisiteCodesV1.EInvoiceScenarioDefaultNotFound"/> and an invoice draft must ask for the
/// scenario (fail-closed).</item>
/// </list>
/// Every company the actor can access has these keys; an empty key is a NOT FOUND answer, never a default, a guess or
/// <c>settings_provider_unavailable</c>. Failures are <see cref="SettingsPrerequisiteException"/>s, never nulls:
/// <c>settings_access_denied</c> before anything about the source is revealed;
/// <see cref="SettingsPrerequisiteCodesV1.EInvoiceScenarioCodeListNotFound"/> /
/// <see cref="SettingsPrerequisiteCodesV1.EInvoiceScenarioDefaultNotFound"/> when no version is in force on the date;
/// <c>settings_profile_invalid</c> for a malformed source. A result is reference metadata, not an authorization.
/// </summary>
public interface ISettingsEInvoiceScenarioPolicyProviderV1
{
    /// <summary>The company's e-Fatura scenario code list in force on the date.</summary>
    Task<EInvoiceScenarioCodeListV1> GetEInvoiceScenarioCodesAsync(
        DateOnly effectiveOn, CancellationToken cancellationToken = default);

    /// <summary>
    /// The company's default e-Fatura scenario in force on the date. A served default is always one of the codes of
    /// the list in force on the same date (a source that breaks this is <c>settings_profile_invalid</c>).
    /// </summary>
    Task<EInvoiceScenarioDefaultV1> GetEInvoiceScenarioDefaultAsync(
        DateOnly effectiveOn, CancellationToken cancellationToken = default);
}

/// <summary>One version of the company's scenario code list; <see cref="Codes"/> is non-empty, distinct and well formed.</summary>
public sealed record EInvoiceScenarioCodeListV1(
    Guid CompanyId,
    ProfileVersionV1 Profile,
    IReadOnlyList<string> Codes,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough);

/// <summary>One version of the company's default scenario.</summary>
public sealed record EInvoiceScenarioDefaultV1(
    Guid CompanyId,
    ProfileVersionV1 Profile,
    string ScenarioCode,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough);

/// <summary>
/// The lexical shape of a scenario code (a UBL-TR <c>ProfileID</c> token): 1..32 characters, an ASCII upper-case
/// letter first, then upper-case letters, digits or underscores. It is a format check only: WHICH codes exist is the
/// Settings code list's business, never this type's.
/// </summary>
public static class EInvoiceScenarioCodeFormatV1
{
    public const int MaximumLength = 32;

    public static bool IsWellFormed(string? code)
    {
        if (string.IsNullOrEmpty(code) || code.Length > MaximumLength || code[0] is < 'A' or > 'Z')
        {
            return false;
        }

        foreach (var character in code)
        {
            if (character is not ((>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_'))
            {
                return false;
            }
        }

        return true;
    }
}
