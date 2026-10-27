using System.Globalization;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Application;

/// <summary>One immutable limit record of a group (SAT-D36, SAT-D62).</summary>
public sealed record SalesDiscountLimitVersionDataV1(
    Guid LimitGroupId,
    long Version,
    Guid ProfileId,
    string Kind,
    decimal? Percent,
    Guid? LinkedGroupId,
    decimal? ExtraPoints,
    DateOnly EffectiveFrom);

/// <summary>A group and every limit record it has, in any order.</summary>
public sealed record SalesDiscountLimitGroupDataV1(
    Guid Id,
    bool IsActive,
    IReadOnlyList<SalesDiscountLimitVersionDataV1> Versions);

public enum SalesDiscountLimitResolutionOutcomeV1
{
    /// <summary>A limit is in force on the date.</summary>
    Defined = 1,

    /// <summary>No limit on the date (no record in force, a passive group, or a linked group without one): approval.</summary>
    None = 2,

    /// <summary>The stored records contradict their own rules (a cycle, a malformed record): fail closed, never a guess.</summary>
    Invalid = 3,
}

/// <summary>
/// The limit of a group on a date. For a linked limit the window is the intersection of the group's own record window and
/// the linked group's, so the value holds over the whole window it reports.
/// </summary>
public sealed record SalesDiscountLimitResolutionV1(
    SalesDiscountLimitResolutionOutcomeV1 Outcome,
    decimal? Percent,
    SalesDiscountLimitVersionDataV1? Record,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveThrough)
{
    public static SalesDiscountLimitResolutionV1 None { get; } =
        new(SalesDiscountLimitResolutionOutcomeV1.None, null, null, null, null);

    public static SalesDiscountLimitResolutionV1 Invalid { get; } =
        new(SalesDiscountLimitResolutionOutcomeV1.Invalid, null, null, null, null);
}

/// <summary>
/// The ONE deterministic calculator of a group's discount limit (SAT-D36, SAT-D62, EK10 implementation notes), used by the
/// Settings policy read the Sales check asks and by the Settings screen's read; no UI or report re-implements it.
/// <list type="bullet">
/// <item><b>Versioning</b> (the existing Settings rule: one key, ascending versions, non-overlapping windows): a record is in
/// force from its <c>EffectiveFrom</c> until the day before the next record's later <c>EffectiveFrom</c>; on a date the
/// HIGHEST version whose <c>EffectiveFrom</c> is on or before the date applies (a same-day correction supersedes the earlier
/// one). Before the first record nothing is in force.</item>
/// <item><b>Fixed</b>: the percent. <b>Linked</b>: the linked group's limit on the same date + the extra points, capped at
/// 100 (the discount itself is 0…100).</item>
/// <item><b>None</b> (fail-closed, EK10/EK11 open item: "approval", not "0 + extra"): a passive group, no record in force, a
/// linked group that is passive, unknown or has no limit itself.</item>
/// <item><b>Invalid</b>: a cycle or a malformed record — the caller answers <c>settings_profile_invalid</c>.</item>
/// </list>
/// </summary>
public static class SalesDiscountLimitCalculatorV1
{
    public const decimal MinimumPercent = 0m;
    public const decimal MaximumPercent = 100m;

    public static SalesDiscountLimitResolutionV1 Resolve(
        IReadOnlyDictionary<Guid, SalesDiscountLimitGroupDataV1> groups,
        Guid groupId,
        DateOnly on)
    {
        ArgumentNullException.ThrowIfNull(groups);
        return Resolve(groups, groupId, on, new HashSet<Guid>());
    }

    private static SalesDiscountLimitResolutionV1 Resolve(
        IReadOnlyDictionary<Guid, SalesDiscountLimitGroupDataV1> groups,
        Guid groupId,
        DateOnly on,
        HashSet<Guid> visiting)
    {
        if (!visiting.Add(groupId))
        {
            return SalesDiscountLimitResolutionV1.Invalid;
        }

        if (!groups.TryGetValue(groupId, out var group) || group is null || group.Id != groupId)
        {
            return SalesDiscountLimitResolutionV1.None;
        }

        if (!IsWellFormed(group))
        {
            return SalesDiscountLimitResolutionV1.Invalid;
        }

        if (!group.IsActive)
        {
            return SalesDiscountLimitResolutionV1.None;
        }

        var ordered = group.Versions.OrderBy(version => version.Version).ToArray();
        var selected = ordered.LastOrDefault(version => version.EffectiveFrom <= on);
        if (selected is null)
        {
            return SalesDiscountLimitResolutionV1.None;
        }

        var next = ordered.FirstOrDefault(version =>
            version.Version > selected.Version && version.EffectiveFrom > selected.EffectiveFrom);
        DateOnly? through = next?.EffectiveFrom.AddDays(-1);
        if (selected.Kind == SalesDiscountLimitKindsV1.Fixed)
        {
            return new SalesDiscountLimitResolutionV1(SalesDiscountLimitResolutionOutcomeV1.Defined, selected.Percent,
                selected, selected.EffectiveFrom, through);
        }

        var linked = Resolve(groups, selected.LinkedGroupId!.Value, on, visiting);
        if (linked.Outcome != SalesDiscountLimitResolutionOutcomeV1.Defined)
        {
            return linked.Outcome == SalesDiscountLimitResolutionOutcomeV1.Invalid
                ? SalesDiscountLimitResolutionV1.Invalid
                : SalesDiscountLimitResolutionV1.None;
        }

        var percent = Math.Min(MaximumPercent, linked.Percent!.Value + selected.ExtraPoints!.Value);
        var from = linked.EffectiveFrom!.Value > selected.EffectiveFrom ? linked.EffectiveFrom.Value : selected.EffectiveFrom;
        DateOnly? until = (through, linked.EffectiveThrough) switch
        {
            (null, null) => null,
            (null, { } other) => other,
            ({ } own, null) => own,
            ({ } own, { } other) => own < other ? own : other,
        };
        return new SalesDiscountLimitResolutionV1(SalesDiscountLimitResolutionOutcomeV1.Defined, percent, selected, from, until);
    }

    /// <summary>
    /// True when linking <paramref name="groupId"/> to <paramref name="linkedGroupId"/> would close a cycle over the groups'
    /// CURRENT (latest) records, which are the ones in force from today on (EK10: A→B→A is refused). A self-link is a cycle.
    /// </summary>
    public static bool WouldCreateCycle(IReadOnlyDictionary<Guid, Guid?> currentLinks, Guid groupId, Guid linkedGroupId)
    {
        ArgumentNullException.ThrowIfNull(currentLinks);
        var seen = new HashSet<Guid>();
        Guid? cursor = linkedGroupId;
        while (cursor is { } current)
        {
            if (current == groupId || !seen.Add(current))
            {
                return true;
            }

            cursor = currentLinks.TryGetValue(current, out var next) ? next : null;
        }

        return false;
    }

    /// <summary>
    /// Parses a percent or extra points given as lossless decimal text: digits with an optional '.' fraction of at most
    /// <see cref="SalesDiscountLimitInputLimitsV1.PercentMaximumScale"/> places, 0…100. Never rounds; anything else is false.
    /// </summary>
    public static bool TryParsePercent(string? text, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrEmpty(text) || text.Length > 3 + 1 + SalesDiscountLimitInputLimitsV1.PercentMaximumScale)
        {
            return false;
        }

        var dot = text.IndexOf('.', StringComparison.Ordinal);
        var integerPart = dot < 0 ? text : text[..dot];
        var fraction = dot < 0 ? string.Empty : text[(dot + 1)..];
        if (integerPart.Length is 0 or > 3 || !integerPart.All(char.IsAsciiDigit)
            || (dot >= 0 && (fraction.Length is 0 || fraction.Length > SalesDiscountLimitInputLimitsV1.PercentMaximumScale
                || !fraction.All(char.IsAsciiDigit))))
        {
            return false;
        }

        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
            || parsed < MinimumPercent || parsed > MaximumPercent)
        {
            return false;
        }

        value = parsed;
        return true;
    }

    /// <summary>The canonical text of a stored percent: invariant, no exponent, no trailing zeros ("35", "37.5").</summary>
    public static string Format(decimal value)
    {
        var text = value.ToString("0.############################", CultureInfo.InvariantCulture);
        return text;
    }

    private static bool IsWellFormed(SalesDiscountLimitGroupDataV1 group)
    {
        if (group.Versions is null)
        {
            return false;
        }

        var ordered = group.Versions.OrderBy(version => version.Version).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var version = ordered[index];
            if (version is null || version.LimitGroupId != group.Id || version.Version != index + 1
                || version.ProfileId == Guid.Empty
                || (index > 0 && version.EffectiveFrom < ordered[index - 1].EffectiveFrom))
            {
                return false;
            }

            var valid = version.Kind switch
            {
                SalesDiscountLimitKindsV1.Fixed => version.Percent is >= MinimumPercent and <= MaximumPercent
                    && version.LinkedGroupId is null && version.ExtraPoints is null,
                SalesDiscountLimitKindsV1.Linked => version.Percent is null
                    && version.LinkedGroupId is { } linked && linked != Guid.Empty && linked != group.Id
                    && version.ExtraPoints is >= MinimumPercent and <= MaximumPercent,
                _ => false,
            };
            if (!valid)
            {
                return false;
            }
        }

        return true;
    }
}
