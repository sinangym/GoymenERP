using System.Globalization;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Application;

/// <summary>
/// F-004: the deterministic parsing and naming rules of a unit conversion policy version, shared by the administration, the
/// provider and the tests. Every parser is lossless and closed: a value is accepted exactly as written or refused, never
/// rounded, trimmed into range or replaced by a default (BS-9/BS-13/BS-16/BS-17: every value is a Settings value).
/// </summary>
public static class UnitConversionPolicyRulesV1
{
    /// <summary>
    /// A density in g/cm³ as lossless decimal text: digits (at most
    /// <see cref="UnitConversionPolicyInputLimitsV1.DensityMaximumIntegerDigits"/>, no leading zero other than a single "0"
    /// before a fraction) with an optional '.' fraction of 1…<see cref="UnitConversionPolicyInputLimitsV1.DensityMaximumScale"/>
    /// digits, greater than zero.
    /// </summary>
    public static bool TryParseDensity(string? text, out decimal value) =>
        TryParseDecimal(text, UnitConversionPolicyInputLimitsV1.DensityMaximumIntegerDigits,
            UnitConversionPolicyInputLimitsV1.DensityMaximumScale, out value) && value > 0m;

    /// <summary>
    /// The weighbridge tolerance percent as lossless decimal text, within
    /// <see cref="UnitConversionPolicyInputLimitsV1.MinimumPercent"/>…<see cref="UnitConversionPolicyInputLimitsV1.MaximumPercent"/>
    /// with at most <see cref="UnitConversionPolicyInputLimitsV1.PercentMaximumScale"/> decimals.
    /// </summary>
    public static bool TryParseTolerancePercent(string? text, out decimal value) =>
        TryParseDecimal(text, UnitConversionPolicyInputLimitsV1.PercentMaximumIntegerDigits,
            UnitConversionPolicyInputLimitsV1.PercentMaximumScale, out value)
        && value >= UnitConversionPolicyInputLimitsV1.MinimumPercent && value <= UnitConversionPolicyInputLimitsV1.MaximumPercent;

    /// <summary>A rounding decimals count within 0…<see cref="UnitConversionPolicyInputLimitsV1.MaximumRoundingDecimals"/>.</summary>
    public static bool IsRoundingDecimals(int? decimals) =>
        decimals is >= 0 and <= UnitConversionPolicyInputLimitsV1.MaximumRoundingDecimals;

    /// <summary>A rounding mode code from <see cref="UnitConversionRoundingModesV1.All"/> (exact, case-sensitive).</summary>
    public static bool TryParseRounding(string? code, out MidpointRounding mode)
    {
        mode = default;
        switch (code)
        {
            case UnitConversionRoundingModesV1.ToEven:
                mode = MidpointRounding.ToEven;
                return true;
            case UnitConversionRoundingModesV1.AwayFromZero:
                mode = MidpointRounding.AwayFromZero;
                return true;
            case UnitConversionRoundingModesV1.ToZero:
                mode = MidpointRounding.ToZero;
                return true;
            case UnitConversionRoundingModesV1.ToNegativeInfinity:
                mode = MidpointRounding.ToNegativeInfinity;
                return true;
            case UnitConversionRoundingModesV1.ToPositiveInfinity:
                mode = MidpointRounding.ToPositiveInfinity;
                return true;
            default:
                return false;
        }
    }

    /// <summary>A tolerance boundary code from <see cref="UnitConversionToleranceBoundariesV1.All"/> (exact).</summary>
    public static bool TryParseBoundary(string? code, out SettingsWeighbridgeToleranceBoundaryV1 boundary)
    {
        boundary = code switch
        {
            UnitConversionToleranceBoundariesV1.Inclusive => SettingsWeighbridgeToleranceBoundaryV1.Inclusive,
            UnitConversionToleranceBoundariesV1.Exclusive => SettingsWeighbridgeToleranceBoundaryV1.Exclusive,
            _ => default,
        };
        return boundary != default;
    }

    /// <summary>A tolerance base code from <see cref="UnitConversionToleranceBasesV1.All"/> (exact).</summary>
    public static bool TryParseBase(string? code, out SettingsWeighbridgeToleranceBaseV1 toleranceBase)
    {
        toleranceBase = code switch
        {
            UnitConversionToleranceBasesV1.TheoreticalKg => SettingsWeighbridgeToleranceBaseV1.TheoreticalKg,
            UnitConversionToleranceBasesV1.WeighedKg => SettingsWeighbridgeToleranceBaseV1.WeighedKg,
            _ => default,
        };
        return toleranceBase != default;
    }

    /// <summary>
    /// The canonical text of a stored decimal: invariant, no exponent, no trailing zeros ("7.85", "1"), so a value read back
    /// from its fixed-scale column gives the text it was recorded with (modulo trailing zeros).
    /// </summary>
    public static string Format(decimal value) =>
        value.ToString("0.############################", CultureInfo.InvariantCulture);

    /// <summary>The canonical form of a stored decimal (the same value without trailing zeros).</summary>
    public static decimal Canonical(decimal value) =>
        decimal.Parse(Format(value), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    private static bool TryParseDecimal(string? text, int maximumIntegerDigits, int maximumScale, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var dot = text.IndexOf('.', StringComparison.Ordinal);
        var integerPart = dot < 0 ? text : text[..dot];
        var fraction = dot < 0 ? string.Empty : text[(dot + 1)..];
        if (integerPart.Length == 0 || integerPart.Length > maximumIntegerDigits || !integerPart.All(char.IsAsciiDigit)
            || (integerPart.Length > 1 && integerPart[0] == '0')
            || (dot >= 0 && (fraction.Length == 0 || fraction.Length > maximumScale || !fraction.All(char.IsAsciiDigit))))
        {
            return false;
        }

        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }
}
