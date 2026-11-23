using System.Globalization;
using System.Numerics;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Application;

/// <summary>No 133/URN-D3/D4/6960531086 (GOV-D16). Exact comparisons only: no rounding or assumed threshold.</summary>
public static class FinancePolicyRulesV1
{
    public const int AmountMaximumLength = 64;
    public const int ReasonMaximumLength = 4000;
    public const int MatrixRuleMaximumCount = 256; // Bounded command payload, not a financial threshold.

    public static bool TryAmount(string? value, out string canonical)
    {
        canonical = string.Empty;
        if (string.IsNullOrEmpty(value) || value.Length > AmountMaximumLength) return false;
        var parts = value.Split('.');
        if (parts.Length > 2 || parts.Any(x => x.Length == 0 || !x.All(char.IsAsciiDigit))) return false;
        var whole = parts[0].TrimStart('0');
        if (whole.Length == 0) whole = "0";
        var fraction = parts.Length == 2 ? parts[1].TrimEnd('0') : string.Empty;
        canonical = fraction.Length == 0 ? whole : whole + "." + fraction;
        return true;
    }

    public static int Compare(string left, string right)
    {
        if (!TryAmount(left, out var a) || !TryAmount(right, out var b)) throw new ArgumentException("Invalid exact amount.");
        static (BigInteger Value, int Scale) Number(string text)
        {
            var point = text.IndexOf('.');
            return (BigInteger.Parse(text.Replace(".", string.Empty, StringComparison.Ordinal), CultureInfo.InvariantCulture),
                point < 0 ? 0 : text.Length - point - 1);
        }
        var x = Number(a); var y = Number(b); var scale = Math.Max(x.Scale, y.Scale);
        return (x.Value * BigInteger.Pow(10, scale - x.Scale)).CompareTo(y.Value * BigInteger.Pow(10, scale - y.Scale));
    }

    public static bool MatrixThresholdReached(string amount, string threshold) => Compare(amount, threshold) >= 0;

    public static bool TryAbsoluteDifference(string? value, out string absolute) =>
        TryAmount(value is { Length: > 1 } && value[0] == '-' ? value[1..] : value, out absolute);

    /// <summary>API-33 says 'above the threshold'. The UI passes an explicit absolute difference, never computes it.</summary>
    public static bool CashDifferenceThresholdExceeded(string absoluteDifference, string threshold) => Compare(absoluteDifference, threshold) > 0;

    public static bool TryVersion(string? value, out long version) => long.TryParse(value, NumberStyles.None,
        CultureInfo.InvariantCulture, out version) && version >= 0 && version.ToString(CultureInfo.InvariantCulture) == value;

    public static bool IsWindow(DateOnly from, DateOnly? through, DateOnly today) => from >= today && (!through.HasValue || through.Value >= from);

    public static bool IsEffective(SettingsFinancePolicyStateV1 state, DateOnly from, DateOnly? through,
        DateOnly? retiredFrom, DateOnly day) => (state is SettingsFinancePolicyStateV1.Published or SettingsFinancePolicyStateV1.Retired)
        && from <= day && (!through.HasValue || day <= through.Value) && (!retiredFrom.HasValue || day < retiredFrom.Value);

    public static bool Overlaps(DateOnly firstFrom, DateOnly? firstThrough, DateOnly? firstRetiredFrom,
        DateOnly secondFrom, DateOnly? secondThrough)
    {
        var firstEnd = firstThrough ?? DateOnly.MaxValue;
        if (firstRetiredFrom.HasValue)
        {
            if (firstRetiredFrom.Value <= firstFrom) return false;
            var lastActive = firstRetiredFrom.Value.AddDays(-1);
            if (lastActive < firstEnd) firstEnd = lastActive;
        }
        return firstFrom <= (secondThrough ?? DateOnly.MaxValue) && secondFrom <= firstEnd;
    }
}
