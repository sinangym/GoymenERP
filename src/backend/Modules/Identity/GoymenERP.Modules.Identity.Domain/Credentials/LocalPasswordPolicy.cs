using System.Text;

namespace GoymenERP.Modules.Identity.Domain.Credentials;

/// <summary>
/// The configured local password policy (KIM-D4, KIM-D12; spec "Birim/formül/yuvarlama/tolerans"). Every value
/// comes from <c>Identity:LocalAccount:PasswordPolicy</c>; the type holds no default. Length is counted in Unicode
/// scalar values, so a password is never cut or padded.
/// </summary>
public sealed record LocalPasswordPolicy
{
    public LocalPasswordPolicy(int minimumLength, int maximumLength, bool blocklistEnabled, IReadOnlyList<string> blockedTerms)
    {
        ArgumentNullException.ThrowIfNull(blockedTerms);
        if (minimumLength < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumLength));
        }

        if (maximumLength < minimumLength)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumLength));
        }

        MinimumLength = minimumLength;
        MaximumLength = maximumLength;
        BlocklistEnabled = blocklistEnabled;
        BlockedTerms = blockedTerms
            .Select(LocalPasswordBlocklist.Normalize)
            .Where(term => term.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public int MinimumLength { get; }

    public int MaximumLength { get; }

    public bool BlocklistEnabled { get; }

    /// <summary>Configured terms, already normalized; the fixed and contextual terms are added per evaluation.</summary>
    public IReadOnlyList<string> BlockedTerms { get; }

    /// <summary>
    /// Returns the violated rule codes (empty when the password is acceptable). <paramref name="contextTerms"/>
    /// carries the user name and the company names (KIM-D4 list: <c>admin</c>, user name, company name derivatives).
    /// </summary>
    public IReadOnlyList<string> Evaluate(string password, IEnumerable<string> contextTerms)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(contextTerms);
        var violations = new List<string>();
        var length = password.EnumerateRunes().Count();
        if (length < MinimumLength)
        {
            violations.Add(LocalPasswordPolicyViolationCodes.TooShort);
        }

        if (length > MaximumLength)
        {
            violations.Add(LocalPasswordPolicyViolationCodes.TooLong);
        }

        if (BlocklistEnabled && LocalPasswordBlocklist.IsBlocked(password, BlockedTerms.Concat(contextTerms)))
        {
            violations.Add(LocalPasswordPolicyViolationCodes.Blocklisted);
        }

        return violations;
    }
}

/// <summary>Stable rule codes returned under <c>violations</c> of the problem response; the UI owns the Turkish text.</summary>
public static class LocalPasswordPolicyViolationCodes
{
    public const string TooShort = "password_too_short";

    public const string TooLong = "password_too_long";

    public const string Blocklisted = "password_blocklisted";
}

/// <summary>
/// The blocklist comparison. A password is blocked when it equals a term, or when its letters alone equal a term
/// (so <c>Admin2026!</c> is a derivative of <c>admin</c>). Comparison is case-insensitive and culture-invariant,
/// after Unicode compatibility normalization. The fixed term <c>admin</c> is the spec's KIM-D4 list, not a role name.
/// </summary>
public static class LocalPasswordBlocklist
{
    /// <summary>KIM-D4: the blocklist always contains <c>admin</c>.</summary>
    public const string FixedTerm = "admin";

    public static bool IsBlocked(string password, IEnumerable<string> terms)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(terms);
        var whole = Normalize(password);
        var letters = LettersOnly(whole);
        foreach (var raw in terms.Append(FixedTerm))
        {
            var term = Normalize(raw);
            if (term.Length == 0)
            {
                continue;
            }

            var termLetters = LettersOnly(term);
            if (string.Equals(whole, term, StringComparison.Ordinal)
                || (termLetters.Length > 0 && string.Equals(letters, termLetters, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    public static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Normalize(NormalizationForm.FormKC).ToLowerInvariant();

    private static string LettersOnly(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var rune in value.EnumerateRunes())
        {
            if (Rune.IsLetter(rune))
            {
                builder.Append(rune.ToString());
            }
        }

        return builder.ToString().ToLowerInvariant();
    }
}
