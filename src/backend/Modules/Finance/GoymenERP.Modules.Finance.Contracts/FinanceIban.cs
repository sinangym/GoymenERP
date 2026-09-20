namespace GoymenERP.Modules.Finance.Contracts;

/// <summary>
/// The structural validity of an IBAN, and nothing more (K2, Sinan #66 <c>5748258299</c>).
///
/// <para>
/// WHAT THIS IS NOT, in K2's own words: <b>"IBAN biçim/checksum doğrulaması banka mülkiyeti/onayı
/// yerine geçmez"</b>. A value that passes here is well-formed arithmetic, not evidence that the
/// account exists, that it belongs to this company, or that anyone approved it. Ownership is what Ö4's
/// propose≠approve gate is for; this class must never be cited as a substitute.
/// </para>
///
/// <para>
/// AND IT DERIVES NOTHING. K2 says <b>"eksik banka verisi icat edilmez"</b>. An IBAN contains a bank
/// identifier, so it is tempting to fill a missing bank name from it — that is exactly the invention
/// the decision forbids, and nothing here exposes the bank code for that purpose. The bank name comes
/// from the person entering the card.
/// </para>
///
/// <para>
/// VALIDATED ON THE TEXT, before any conversion — the Ö2 discipline applied to a different field. The
/// check is ISO 13616 mod-97: the first four characters move to the end, each letter becomes two
/// digits (A=10 … Z=35), and the resulting number modulo 97 must be 1. That number is far too long for
/// any integer type, so it is computed digit by digit rather than parsed, which also means no input
/// length can overflow anything.
/// </para>
///
/// <para>
/// NO COUNTRY LENGTH TABLE, deliberately. Per-country lengths are real, but an incomplete table
/// silently rejects valid foreign IBANs — the failure would look like a validation rule and actually be
/// missing data, which is the same class of defect as inventing bank data. The generic bounds (15..34,
/// ISO 13616) plus mod-97 are what is checked; a country table can be added later WITH its source.
/// </para>
/// </summary>
public static class FinanceIban
{
    /// <summary>ISO 13616 allows 15..34 characters; no registered country is outside that.</summary>
    public const int MinimumLength = 15;
    public const int MaximumLength = 34;

    /// <summary>
    /// The stored form: no spaces, upper case. Grouping into blocks of four is a display concern and is
    /// never stored, so two people entering the same account store the same characters.
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        Span<char> buffer = stackalloc char[text.Length];
        var length = 0;
        foreach (var character in text)
        {
            if (character is ' ' or '\t' or '-')
            {
                continue;
            }

            buffer[length++] = char.ToUpperInvariant(character);
        }

        return new string(buffer[..length]);
    }

    /// <summary>
    /// Whether the NORMALIZED text is a structurally valid IBAN. Callers normalize first and store what
    /// they validated; validating one form and storing another is how a stored value stops matching the
    /// rule that admitted it.
    /// </summary>
    public static bool IsStructurallyValid(string? normalized)
    {
        if (normalized is null || normalized.Length < MinimumLength || normalized.Length > MaximumLength)
        {
            return false;
        }

        // Country code: two letters. Check digits: two digits. Then the account part, letters or digits.
        if (!char.IsAsciiLetterUpper(normalized[0]) || !char.IsAsciiLetterUpper(normalized[1])
            || !char.IsAsciiDigit(normalized[2]) || !char.IsAsciiDigit(normalized[3]))
        {
            return false;
        }

        for (var index = 4; index < normalized.Length; index++)
        {
            if (!char.IsAsciiLetterUpper(normalized[index]) && !char.IsAsciiDigit(normalized[index]))
            {
                return false;
            }
        }

        return Mod97(normalized) == 1;
    }

    /// <summary>
    /// ISO 13616 mod-97 over the rearranged string, accumulated digit by digit so the value never needs
    /// a numeric type that could overflow: a 34-character IBAN expands past 60 digits.
    /// </summary>
    private static int Mod97(string normalized)
    {
        var remainder = 0;
        for (var offset = 0; offset < normalized.Length; offset++)
        {
            // The first four characters are moved to the end (ISO 13616), so read from index 4 and wrap.
            var character = normalized[(offset + 4) % normalized.Length];
            if (char.IsAsciiDigit(character))
            {
                remainder = ((remainder * 10) + (character - '0')) % 97;
                continue;
            }

            // A=10 … Z=35, contributing two digits.
            var value = character - 'A' + 10;
            remainder = ((remainder * 100) + value) % 97;
        }

        return remainder;
    }
}
