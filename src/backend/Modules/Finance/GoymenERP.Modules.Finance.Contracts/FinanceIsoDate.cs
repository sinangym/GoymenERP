using System.Globalization;

namespace GoymenERP.Modules.Finance.Contracts;

/// <summary>
/// The only wire form of a Finance date-only query value: ISO 8601 <c>yyyy-MM-dd</c>, exactly.
/// The framework's <see cref="DateOnly"/> binding is culture-invariant but lenient: it accepts
/// <c>01/02/2026</c> (month first), <c>2026-9-3</c>, <c>13 Sep 2026</c>, surrounding spaces and a
/// time part it silently drops. None of those is a date the client unambiguously meant, so they
/// are refused rather than reinterpreted.
/// </summary>
public static class FinanceIsoDate
{
    public const string Format = "yyyy-MM-dd";

    public static bool TryParse(string? text, out DateOnly value)
    {
        value = default;
        return text is { Length: 10 }
            && DateOnly.TryParseExact(text, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }
}
