using System.Globalization;
using System.Numerics;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Application;

public static class CollateralTypeRulesV1
{
    public const int RateMaximumLength = 64;
    public static bool IsKind(SettingsCollateralKindV1? kind) => kind is >= SettingsCollateralKindV1.BankGuaranteedPayment
        and <= SettingsCollateralKindV1.Mortgage;

    /// <summary>Bounded exact decimal parser, matching Finance's 64-character nonnegative rate contract without a scale cap.</summary>
    public static bool TryRate(string? text, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrEmpty(text) || text.Length > RateMaximumLength) return false;
        var parts = text.Split('.');
        if (parts.Length > 2 || parts.Any(part => part.Length == 0 || !part.All(char.IsAsciiDigit))) return false;
        var digits = string.Concat(parts);
        var scale = parts.Length == 2 ? parts[1].Length : 0;
        var value = BigInteger.Parse(digits, CultureInfo.InvariantCulture);
        if (value > 100 * BigInteger.Pow(10, scale)) return false;
        var whole = parts[0].TrimStart('0');
        if (whole.Length == 0) whole = "0";
        var fraction = parts.Length == 2 ? parts[1].TrimEnd('0') : string.Empty;
        normalized = fraction.Length == 0 ? whole : whole + "." + fraction;
        return true;
    }
}
