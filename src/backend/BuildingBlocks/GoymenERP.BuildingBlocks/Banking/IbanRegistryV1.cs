using System.Collections.Frozen;

namespace GoymenERP.BuildingBlocks.Banking;

/// <summary>
/// FNS-D122 (Sinan, 2026-11-22, E-80): ISO 13616 IBAN validation with the per-country length table. A value is valid when
/// its country code is in the table, its length is exactly that country's length, it is upper-case letters/digits with two
/// check digits, and the mod-97 check is 1. A Turkish IBAN keeps its FNS-D121 rule unchanged (<c>TR</c> + 24 digits).
///
/// <para>
/// Source of the lengths: the SWIFT IBAN Registry (the ISO 13616 registration authority). Dependent territories that issue
/// IBANs under their parent's code (for example French overseas departments under <c>FR</c>, Åland under <c>FI</c>, the Crown
/// dependencies under <c>GB</c>) are covered by that code. An unknown country code is REFUSED (fail-closed): a country the
/// registry adds later is added here with its registry entry, never guessed.
/// </para>
///
/// <para>
/// Structural validity only: it is not evidence that the account exists, belongs to anyone or was approved, and nothing (no
/// bank name) is derived from it.
/// </para>
/// </summary>
public static class IbanRegistryV1
{
    public const int MinimumLength = 15;
    public const int MaximumLength = 34;
    public const string TurkeyCountryCode = "TR";

    /// <summary>Country code → total IBAN length (SWIFT IBAN Registry).</summary>
    public static FrozenDictionary<string, int> Lengths { get; } = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["AD"] = 24, ["AE"] = 23, ["AL"] = 28, ["AT"] = 20, ["AZ"] = 28, ["BA"] = 20, ["BE"] = 16, ["BG"] = 22,
        ["BH"] = 22, ["BI"] = 27, ["BR"] = 29, ["BY"] = 28, ["CH"] = 21, ["CR"] = 22, ["CY"] = 28, ["CZ"] = 24,
        ["DE"] = 22, ["DJ"] = 27, ["DK"] = 18, ["DO"] = 28, ["EE"] = 20, ["EG"] = 29, ["ES"] = 24, ["FI"] = 18,
        ["FK"] = 18, ["FO"] = 18, ["FR"] = 27, ["GB"] = 22, ["GE"] = 22, ["GI"] = 23, ["GL"] = 18, ["GR"] = 27,
        ["GT"] = 28, ["HN"] = 28, ["HR"] = 21, ["HU"] = 28, ["IE"] = 22, ["IL"] = 23, ["IQ"] = 23, ["IS"] = 26,
        ["IT"] = 27, ["JO"] = 30, ["KW"] = 30, ["KZ"] = 20, ["LB"] = 28, ["LC"] = 32, ["LI"] = 21, ["LT"] = 20,
        ["LU"] = 20, ["LV"] = 21, ["LY"] = 25, ["MC"] = 27, ["MD"] = 24, ["ME"] = 22, ["MK"] = 19, ["MN"] = 20,
        ["MR"] = 27, ["MT"] = 31, ["MU"] = 30, ["NI"] = 28, ["NL"] = 18, ["NO"] = 15, ["OM"] = 23, ["PK"] = 24,
        ["PL"] = 28, ["PS"] = 29, ["PT"] = 25, ["QA"] = 29, ["RO"] = 24, ["RS"] = 22, ["RU"] = 33, ["SA"] = 24,
        ["SC"] = 31, ["SD"] = 18, ["SE"] = 24, ["SI"] = 19, ["SK"] = 24, ["SM"] = 27, ["SO"] = 23, ["ST"] = 25,
        ["SV"] = 28, ["TL"] = 23, ["TN"] = 24, ["TR"] = 26, ["UA"] = 29, ["VA"] = 22, ["VG"] = 24, ["XK"] = 20,
        ["YE"] = 30,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The stored form: spaces, tabs and hyphens removed, upper case (invariant).</summary>
    public static string Normalize(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : new string(text.Where(character => character is not (' ' or '\t' or '-')).Select(char.ToUpperInvariant).ToArray());

    /// <summary>The refusal reason for a NORMALIZED value, or <see cref="IbanCheckV1.Valid"/>.</summary>
    public static IbanCheckV1 Check(string? normalized)
    {
        if (normalized is null || normalized.Length < 2 || !char.IsAsciiLetterUpper(normalized[0])
            || !char.IsAsciiLetterUpper(normalized[1]))
            return IbanCheckV1.Malformed;
        if (!Lengths.TryGetValue(normalized[..2], out var length)) return IbanCheckV1.UnknownCountry;
        if (normalized.Length != length) return IbanCheckV1.WrongLength;
        if (!char.IsAsciiDigit(normalized[2]) || !char.IsAsciiDigit(normalized[3])) return IbanCheckV1.Malformed;
        for (var index = 4; index < normalized.Length; index++)
        {
            if (!char.IsAsciiLetterUpper(normalized[index]) && !char.IsAsciiDigit(normalized[index])) return IbanCheckV1.Malformed;
            // FNS-D121 unchanged for Turkey: TR + 24 digits.
            if (normalized[0] == 'T' && normalized[1] == 'R' && !char.IsAsciiDigit(normalized[index])) return IbanCheckV1.Malformed;
        }

        return Mod97(normalized) == 1 ? IbanCheckV1.Valid : IbanCheckV1.CheckDigits;
    }

    public static bool IsValid(string? normalized) => Check(normalized) == IbanCheckV1.Valid;

    private static int Mod97(string normalized)
    {
        var remainder = 0;
        for (var offset = 0; offset < normalized.Length; offset++)
        {
            var character = normalized[(offset + 4) % normalized.Length];
            remainder = char.IsAsciiDigit(character)
                ? (remainder * 10 + (character - '0')) % 97
                : (remainder * 100 + (character - 'A' + 10)) % 97;
        }

        return remainder;
    }
}

/// <summary>Why a value is not a valid IBAN (FNS-D122); the order is the order the checks run.</summary>
public enum IbanCheckV1
{
    Valid = 0,
    Malformed = 1,
    UnknownCountry = 2,
    WrongLength = 3,
    CheckDigits = 4,
}
