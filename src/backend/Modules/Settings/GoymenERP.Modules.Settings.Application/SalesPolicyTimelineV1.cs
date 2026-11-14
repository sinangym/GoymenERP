using System.Globalization;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Application;

/// <summary>One stored policy version placed on its family key's timeline.</summary>
public sealed record SalesPolicyTimelineEntryV1<T>(
    T Value,
    long Version,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough,
    bool IsSuperseded);

/// <summary>
/// F-029 S4b: the deterministic timeline of a persistent Sales policy family key (the company's delivery tolerance, or one
/// currency's approval amount threshold). Stored versions are insert-only and carry only a start date; the window is derived
/// so that the answer is the same one the reviewed compiled catalog gives for the same entries:
/// <list type="bullet">
/// <item>per start date the highest version wins; a lower version on the same date is <c>Superseded</c> (never applies);</item>
/// <item>the winners, ordered by start date, each hold until the day before the next one starts; the last one is open;</item>
/// <item>the version for a date is the last one starting on or before it, or — before every start — the first one, with its
/// REAL window, so the consumer's own window check rejects it (the catalog's rule).</item>
/// </list>
/// Repeated version numbers within one key are a malformed source: <c>settings_profile_invalid</c>.
/// </summary>
public static class SalesPolicyTimelineV1
{
    public static IReadOnlyList<SalesPolicyTimelineEntryV1<T>> Build<T>(
        IEnumerable<T> versions, Func<T, long> version, Func<T, DateOnly> effectiveFrom)
    {
        ArgumentNullException.ThrowIfNull(versions);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(effectiveFrom);
        var all = versions.ToArray();
        if (all.Select(version).Distinct().Count() != all.Length || all.Any(entry => version(entry) <= 0))
        {
            throw new SettingsPrerequisiteException("settings_profile_invalid");
        }

        var winners = all
            .GroupBy(effectiveFrom)
            .Select(sameDate => sameDate.MaxBy(version)!)
            .OrderBy(effectiveFrom)
            .ToArray();
        var timeline = new List<SalesPolicyTimelineEntryV1<T>>(all.Length);
        for (var index = 0; index < winners.Length; index++)
        {
            DateOnly? through = index + 1 < winners.Length ? effectiveFrom(winners[index + 1]).AddDays(-1) : null;
            timeline.Add(new SalesPolicyTimelineEntryV1<T>(
                winners[index], version(winners[index]), effectiveFrom(winners[index]), through, IsSuperseded: false));
        }

        var winnerVersions = winners.Select(version).ToHashSet();
        timeline.AddRange(all
            .Where(entry => !winnerVersions.Contains(version(entry)))
            .OrderBy(version)
            .Select(entry => new SalesPolicyTimelineEntryV1<T>(
                entry, version(entry), effectiveFrom(entry), EffectiveThrough: null, IsSuperseded: true)));
        return timeline;
    }

    /// <summary>The version answering for the date (see the class remarks); null when the key has no version.</summary>
    public static SalesPolicyTimelineEntryV1<T>? For<T>(IReadOnlyList<SalesPolicyTimelineEntryV1<T>> timeline, DateOnly on)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        var effective = timeline.Where(entry => !entry.IsSuperseded).ToArray();
        return effective.Length == 0 ? null : effective.LastOrDefault(entry => entry.EffectiveFrom <= on) ?? effective[0];
    }

    /// <summary>The version actually in force on the date (inside its window); null before the first start.</summary>
    public static SalesPolicyTimelineEntryV1<T>? InForceOn<T>(IReadOnlyList<SalesPolicyTimelineEntryV1<T>> timeline, DateOnly on) =>
        For(timeline, on) is { } entry && entry.EffectiveFrom <= on ? entry : null;

    public static string State<T>(SalesPolicyTimelineEntryV1<T> entry, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.IsSuperseded ? SalesPolicyVersionStatesV1.Superseded
            : entry.EffectiveFrom > today ? SalesPolicyVersionStatesV1.Scheduled
            : entry.EffectiveThrough is { } through && through < today ? SalesPolicyVersionStatesV1.Ended
            : SalesPolicyVersionStatesV1.InForce;
    }

    /// <summary>The latest stored version of the key ("0" when none): the next record's expected version.</summary>
    public static long LatestVersion<T>(IReadOnlyList<SalesPolicyTimelineEntryV1<T>> timeline) =>
        timeline.Count == 0 ? 0 : timeline.Max(entry => entry.Version);

    /// <summary>
    /// A percent in its canonical form (no trailing zeros, at most <see cref="SalesPolicyInputLimitsV1.PercentMaximumScale"/>
    /// decimals), so a stored value reads back with the very same representation.
    /// </summary>
    public static bool IsCanonicalPercent(decimal value) =>
        value.Scale <= SalesPolicyInputLimitsV1.PercentMaximumScale && Canonical(value) is var canonical
        && canonical == value && canonical.Scale == value.Scale;

    /// <summary>The canonical representation of a stored percent (a decimal(7,4) column reads back with four decimals).</summary>
    public static decimal Canonical(decimal value) =>
        decimal.Parse(SalesDiscountLimitCalculatorV1.Format(value), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a threshold amount given as lossless decimal text: digits (at most
    /// <see cref="SalesPolicyInputLimitsV1.AmountMaximumIntegerDigits"/>, no leading zero other than a single "0" before a
    /// fraction) with an optional '.' fraction of 1…<see cref="SalesPolicyInputLimitsV1.AmountMaximumScale"/> digits, greater
    /// than zero. The scale is the fraction's length as written, so <see cref="FormatAmount"/> gives the same text back.
    /// Never rounds; anything else is false.
    /// </summary>
    public static bool TryParseAmount(string? text, out decimal amount, out int scale)
    {
        amount = 0m;
        scale = 0;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var dot = text.IndexOf('.', StringComparison.Ordinal);
        var integerPart = dot < 0 ? text : text[..dot];
        var fraction = dot < 0 ? string.Empty : text[(dot + 1)..];
        if (integerPart.Length is 0 || integerPart.Length > SalesPolicyInputLimitsV1.AmountMaximumIntegerDigits
            || !integerPart.All(char.IsAsciiDigit) || (integerPart.Length > 1 && integerPart[0] == '0')
            || (dot >= 0 && (fraction.Length is 0 || fraction.Length > SalesPolicyInputLimitsV1.AmountMaximumScale
                || !fraction.All(char.IsAsciiDigit))))
        {
            return false;
        }

        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0m)
        {
            return false;
        }

        amount = parsed;
        scale = fraction.Length;
        return true;
    }

    /// <summary>The amount's text with exactly <paramref name="scale"/> decimals (the spelling it was recorded with).</summary>
    public static string FormatAmount(decimal amount, int scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(scale);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(scale, SalesPolicyInputLimitsV1.AmountMaximumScale);
        return amount.ToString("F" + scale.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }
}
