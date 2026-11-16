using System.Globalization;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Application;

/// <summary>
/// URT-D89: the deterministic input rules of a work center capacity version, shared by the administration, the provider and
/// the tests. Every parser is lossless and closed: a value is accepted exactly as written or refused, never rounded, trimmed
/// into range or replaced by a default. Which capacity or buffer a work center has is a Settings value, never chosen here.
/// </summary>
public static class WorkCenterCapacityRulesV1
{
    /// <summary>
    /// The input capacity in kg/hour as lossless decimal text: digits (at most
    /// <see cref="WorkCenterCapacityInputLimitsV1.CapacityMaximumIntegerDigits"/>, no leading zero other than a single "0"
    /// before a fraction) with an optional '.' fraction of
    /// 1…<see cref="WorkCenterCapacityInputLimitsV1.CapacityMaximumScale"/> digits, greater than zero.
    /// </summary>
    public static bool TryParseCapacity(string? text, out decimal value) =>
        TryParseDecimal(text, WorkCenterCapacityInputLimitsV1.CapacityMaximumIntegerDigits,
            WorkCenterCapacityInputLimitsV1.CapacityMaximumScale, out value) && value > 0m;

    /// <summary>
    /// The safety buffer value as lossless decimal text: digits (at most
    /// <see cref="WorkCenterCapacityInputLimitsV1.SafetyBufferMaximumIntegerDigits"/>) with an optional '.' fraction of
    /// 1…<see cref="WorkCenterCapacityInputLimitsV1.SafetyBufferMaximumScale"/> digits, greater than zero (no buffer is
    /// expressed by omitting it, not by zero).
    /// </summary>
    public static bool TryParseSafetyBuffer(string? text, out decimal value) =>
        TryParseDecimal(text, WorkCenterCapacityInputLimitsV1.SafetyBufferMaximumIntegerDigits,
            WorkCenterCapacityInputLimitsV1.SafetyBufferMaximumScale, out value) && value > 0m;

    /// <summary>One of the published buffer units, spelled exactly (no case folding, no trimming).</summary>
    public static bool IsSafetyBufferUnit(string? value) =>
        value is not null && WorkCenterSafetyBufferUnitsV1.All.Contains(value, StringComparer.Ordinal);

    /// <summary>
    /// The buffer pair: both omitted (null) is no buffer; a value with a known unit is a buffer; anything else (one without
    /// the other, an empty string, a malformed value or an unknown unit) is refused.
    /// </summary>
    public static bool TryParseSafetyBufferPair(string? valueText, string? unit, out decimal? value, out string? normalizedUnit)
    {
        value = null;
        normalizedUnit = null;
        if (valueText is null && unit is null)
        {
            return true;
        }

        if (valueText is null || unit is null || !IsSafetyBufferUnit(unit) || !TryParseSafetyBuffer(valueText, out var parsed))
        {
            return false;
        }

        value = parsed;
        normalizedUnit = unit;
        return true;
    }

    /// <summary>
    /// The canonical text of a stored decimal: invariant, no exponent, no trailing zeros ("1250.5", "1"), so a value read back
    /// from its fixed-scale column gives the text it was recorded with (modulo trailing zeros).
    /// </summary>
    public static string Format(decimal value) =>
        value.ToString("0.############################", CultureInfo.InvariantCulture);

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
