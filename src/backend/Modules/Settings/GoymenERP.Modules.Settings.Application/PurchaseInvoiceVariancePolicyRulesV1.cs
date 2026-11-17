using System.Globalization;

namespace GoymenERP.Modules.Settings.Application;

/// <summary>Transport/storage bounds only, never a chosen approval tolerance or a rounding rule.</summary>
public static class PurchaseInvoiceVariancePolicyRulesV1
{
    public const int MaximumReasonLength = 4000;
    public const decimal MaximumValue = 999999999999999999.999999m; // SQL decimal(24,6).

    public static bool TryValue(string? text, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrEmpty(text) || text.Length > 25 || text.Any(c => !char.IsAsciiDigit(c) && c != '.')) return false;
        var parts = text.Split('.');
        if (parts.Length > 2 || parts[0].Length is 0 or > 18 || (parts[0].Length > 1 && parts[0][0] == '0')
            || (parts.Length == 2 && parts[1].Length is 0 or > 6)) return false;
        return decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)
            && IsValue(value);
    }

    public static bool IsValue(decimal value) => value >= 0m && value <= MaximumValue && decimal.Round(value, 6) == value;

    public static bool TryVersion(string? text, out long version)
    {
        version = 0;
        return !string.IsNullOrEmpty(text) && text.All(char.IsAsciiDigit) && (text.Length == 1 || text[0] != '0')
            && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out version) && version < long.MaxValue;
    }

    public static string Text(decimal value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    public static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);
}
