namespace GoymenERP.Modules.Counterparty.Domain.Parties;

/// <summary>Which official number a <see cref="TaxIdentityNumber"/> is; decided by its length alone.</summary>
public enum TaxIdentityKind
{
    /// <summary>Vergi Kimlik Numarası: 10 digits, Gelir İdaresi check digit.</summary>
    Vkn = 1,

    /// <summary>T.C. Kimlik Numarası: 11 digits, first digit not zero, NVİ check digits 10 and 11.</summary>
    Tckn = 2,
}

/// <summary>
/// AT-009: a counterparty's VKN (10 digits) or TCKN (11 digits), accepted ONLY when the official check digits hold
/// (user decision 2026-09-30: an invalid number is refused, never stored with a warning). The canonical text is the
/// bare digits; surrounding whitespace is trimmed, anything else (spaces inside, dashes, letters, non-ASCII digits)
/// is refused rather than cleaned, so what is stored is exactly what was typed and checked.
/// </summary>
public readonly record struct TaxIdentityNumber
{
    private TaxIdentityNumber(string value, TaxIdentityKind kind)
    {
        Value = value;
        Kind = kind;
    }

    public string Value { get; }

    public TaxIdentityKind Kind { get; }

    public override string ToString() => Value;

    public static TaxIdentityNumber Parse(string? value) =>
        TryParse(value, out var number, out var problem)
            ? number
            : throw new ArgumentException(problem, nameof(value));

    public static bool TryParse(string? value, out TaxIdentityNumber number, out string problem)
    {
        number = default;
        var text = value?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            problem = "Vergi/kimlik numarası boş olamaz.";
            return false;
        }

        foreach (var character in text)
        {
            if (character is < '0' or > '9')
            {
                problem = "Vergi/kimlik numarası yalnız rakamlardan oluşmalıdır (VKN 10, TCKN 11 hane).";
                return false;
            }
        }

        switch (text.Length)
        {
            case 10 when IsValidVkn(text):
                number = new TaxIdentityNumber(text, TaxIdentityKind.Vkn);
                problem = string.Empty;
                return true;
            case 10:
                problem = "VKN geçersiz: kontrol hanesi tutmuyor.";
                return false;
            case 11 when IsValidTckn(text):
                number = new TaxIdentityNumber(text, TaxIdentityKind.Tckn);
                problem = string.Empty;
                return true;
            case 11:
                problem = "TCKN geçersiz: ilk hane 0 olamaz ve kontrol haneleri tutmalıdır.";
                return false;
            default:
                problem = "Vergi/kimlik numarası VKN için 10, TCKN için 11 hane olmalıdır.";
                return false;
        }
    }

    /// <summary>
    /// Gelir İdaresi VKN check: for the first nine digits d(i), i = 1..9, t = (d(i) + 10 - i) mod 10; the term is 9 when
    /// t is 9, otherwise (t × 2^(10 - i)) mod 9. The tenth digit is (10 - (Σ terms mod 10)) mod 10.
    /// </summary>
    public static bool IsValidVkn(string digits)
    {
        if (digits is null || digits.Length != 10 || !AllAsciiDigits(digits))
        {
            return false;
        }

        var sum = 0;
        for (var i = 1; i <= 9; i++)
        {
            var t = (digits[i - 1] - '0' + 10 - i) % 10;
            sum += t == 9 ? 9 : t * (1 << (10 - i)) % 9;
        }

        return (10 - sum % 10) % 10 == digits[9] - '0';
    }

    /// <summary>
    /// NVİ TCKN check: 11 digits, the first not zero; digit 10 = ((d1+d3+d5+d7+d9) × 7 − (d2+d4+d6+d8)) mod 10 (taken
    /// non-negative) and digit 11 = (d1 + … + d10) mod 10.
    /// </summary>
    public static bool IsValidTckn(string digits)
    {
        if (digits is null || digits.Length != 11 || !AllAsciiDigits(digits) || digits[0] == '0')
        {
            return false;
        }

        var d = new int[11];
        for (var i = 0; i < 11; i++)
        {
            d[i] = digits[i] - '0';
        }

        var odd = d[0] + d[2] + d[4] + d[6] + d[8];
        var even = d[1] + d[3] + d[5] + d[7];
        var tenth = ((odd * 7 - even) % 10 + 10) % 10;
        if (tenth != d[9])
        {
            return false;
        }

        var firstTen = 0;
        for (var i = 0; i < 10; i++)
        {
            firstTen += d[i];
        }

        return firstTen % 10 == d[10];
    }

    private static bool AllAsciiDigits(string text)
    {
        foreach (var character in text)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
