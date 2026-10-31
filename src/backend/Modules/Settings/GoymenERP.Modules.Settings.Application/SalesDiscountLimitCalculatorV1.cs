using System.Globalization;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Application;

/// <summary>
/// One immutable limit record of a group (SAT-D36, SAT-D62). <c>IsWithdrawn</c>: withdrawn before it took effect (SAT-D84);
/// such a record is ignored everywhere.
/// </summary>
public sealed record SalesDiscountLimitVersionDataV1(
    Guid LimitGroupId,
    long Version,
    Guid ProfileId,
    string Kind,
    decimal? Percent,
    Guid? LinkedGroupId,
    decimal? ExtraPoints,
    DateOnly EffectiveFrom,
    bool IsWithdrawn = false);

/// <summary>A group and every limit record it has, in any order.</summary>
public sealed record SalesDiscountLimitGroupDataV1(
    Guid Id,
    bool IsActive,
    IReadOnlyList<SalesDiscountLimitVersionDataV1> Versions);

/// <summary>The result of <see cref="SalesDiscountLimitCalculatorV1.CheckLink"/>.</summary>
public sealed record SalesDiscountLimitLinkCheckV1(bool CreatesCycle, IReadOnlyList<Guid> Chain);

public enum SalesDiscountLimitResolutionOutcomeV1
{
    /// <summary>A limit is in force on the date.</summary>
    Defined = 1,

    /// <summary>No limit on the date (no record in force, or a linked group without one): approval.</summary>
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
/// <item><b>Versioning</b> (one key, ascending versions, non-overlapping windows): a non-withdrawn record is in force from
/// its <c>EffectiveFrom</c> until the day before the next later-dated record's <c>EffectiveFrom</c>; on a date the record
/// with the latest <c>EffectiveFrom</c> on or before it applies, and of several on that date the highest version (a
/// same-day record supersedes the earlier one; the store withdraws a superseded record that was not yet in force). Before
/// the first record nothing is in force.</item>
/// <item><b>Fixed</b>: the percent. <b>Linked</b>: the linked group's limit on the same date + the extra points, capped at
/// 100 (the discount itself is 0…100).</item>
/// <item><b>Future records</b> (SAT-D78): a record may start on a later business date; until then the previous record stays
/// in force, and from that date on it applies by itself. The timeline is ordered by date, then version, so a record may
/// start before an already scheduled one (it holds until that one starts). A linked limit is computed on the asked date,
/// so the linked group's own record of that date is used.</item>
/// <item><b>Passive groups</b> (SAT-D117): still resolve — for the people already bound and for the limits linked to
/// them; passivity only closes a group to new bindings.</item>
/// <item><b>None</b> (SAT-D79, fail-closed: "approval", not "0 + extra"): no record in force, or a linked group that is
/// unknown or has no limit itself.</item>
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

        // SAT-D117 (Sinan, 2026-10-29): a passive group still counts — for the people already bound to it and for the
        // limits linked to it; passivity only closes the group to NEW bindings (enforced by the binding write).
        var selected = RecordInForce(group, on);
        if (selected is null)
        {
            return SalesDiscountLimitResolutionV1.None;
        }

        // The record holds until the day before the next later-dated record of the timeline (SAT-D78/D84).
        var next = Active(group)
            .Where(version => version.EffectiveFrom > selected.EffectiveFrom)
            .MinBy(version => version.EffectiveFrom);
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
    /// The record of <paramref name="group"/> in force on <paramref name="on"/>: among the non-withdrawn records whose
    /// <c>EffectiveFrom</c> is on or before the date, the one with the LATEST date, and of several on that date the highest
    /// version (null before the first record). Ordering by (date, version) — not by version alone — lets a record be
    /// entered or corrected for a date before a later scheduled one (review A P2-1, SAT-D84). Activity is not considered.
    /// </summary>
    public static SalesDiscountLimitVersionDataV1? RecordInForce(SalesDiscountLimitGroupDataV1 group, DateOnly on)
    {
        ArgumentNullException.ThrowIfNull(group);
        return Active(group)
            .Where(version => version.EffectiveFrom <= on)
            .OrderBy(version => version.EffectiveFrom)
            .ThenBy(version => version.Version)
            .LastOrDefault();
    }

    /// <summary>The group's latest non-withdrawn record (the end of its timeline by date, then version), or null.</summary>
    public static SalesDiscountLimitVersionDataV1? LatestRecord(SalesDiscountLimitGroupDataV1 group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return Active(group)
            .OrderBy(version => version.EffectiveFrom)
            .ThenBy(version => version.Version)
            .LastOrDefault();
    }

    /// <summary>
    /// SAT-D78: the group's records that start AFTER <paramref name="after"/> and will take effect, earliest first. Of
    /// several records starting on the same day only the highest version is listed (a same-day correction supersedes).
    /// </summary>
    public static IReadOnlyList<SalesDiscountLimitVersionDataV1> ScheduledAfter(SalesDiscountLimitGroupDataV1 group,
        DateOnly after)
    {
        ArgumentNullException.ThrowIfNull(group);
        return Active(group)
            .Where(version => version.EffectiveFrom > after)
            .GroupBy(version => version.EffectiveFrom)
            .Select(sameDay => sameDay.MaxBy(version => version.Version)!)
            .OrderBy(version => version.EffectiveFrom)
            .ToArray();
    }

    /// <summary>
    /// EK10 + SAT-D78: checks a new linked record of <paramref name="groupId"/> → <paramref name="linkedGroupId"/> that
    /// takes effect on <paramref name="effectiveFrom"/> (see <see cref="CheckGroupLinks"/>, with the new record added as
    /// the group's highest version).
    /// </summary>
    public static SalesDiscountLimitLinkCheckV1 CheckLink(
        IReadOnlyDictionary<Guid, SalesDiscountLimitGroupDataV1> groups,
        Guid groupId,
        Guid linkedGroupId,
        DateOnly effectiveFrom)
    {
        ArgumentNullException.ThrowIfNull(groups);
        var group = groups.TryGetValue(groupId, out var existing)
            ? existing
            : new SalesDiscountLimitGroupDataV1(groupId, true, []);
        var next = group.Versions.Count == 0 ? 1 : group.Versions.Max(version => version.Version) + 1;
        var candidate = new SalesDiscountLimitVersionDataV1(groupId, next, Guid.NewGuid(), SalesDiscountLimitKindsV1.Linked,
            null, linkedGroupId, MinimumPercent, effectiveFrom);
        return CheckGroupLinks(With(groups, group with { Versions = [.. group.Versions, candidate] }), groupId, effectiveFrom);
    }

    /// <summary>
    /// EK10 + SAT-D78 + SAT-D84: <paramref name="groups"/> is the store as it WOULD be after a change of
    /// <paramref name="groupId"/>'s records that matters from <paramref name="from"/> on (a new record, a withdrawal, or a
    /// correction = both). Because records can be scheduled ahead, the link graph is checked on EVERY date it can change from
    /// then on (<paramref name="from"/> and every later record's date of any group): following the group's record in force
    /// on each date must never come back to the group (A→B→A, a self link). Only links through the changed group can be
    /// new, so this is the whole check. <c>Chain</c> is every other group reached on any of those dates (the rows the write
    /// fences).
    /// </summary>
    public static SalesDiscountLimitLinkCheckV1 CheckGroupLinks(
        IReadOnlyDictionary<Guid, SalesDiscountLimitGroupDataV1> groups,
        Guid groupId,
        DateOnly from)
    {
        ArgumentNullException.ThrowIfNull(groups);
        var dates = groups.Values
            .SelectMany(Active)
            .Select(version => version.EffectiveFrom)
            .Where(date => date > from)
            .Append(from)
            .Distinct()
            .Order();
        var chain = new List<Guid>();
        foreach (var date in dates)
        {
            var seen = new HashSet<Guid>();
            Guid? cursor = groups.TryGetValue(groupId, out var own) ? RecordInForce(own, date)?.LinkedGroupId : null;
            while (cursor is { } current)
            {
                if (current == groupId || !seen.Add(current))
                {
                    return new SalesDiscountLimitLinkCheckV1(true, chain);
                }

                if (!chain.Contains(current))
                {
                    chain.Add(current);
                }

                cursor = groups.TryGetValue(current, out var group) ? RecordInForce(group, date)?.LinkedGroupId : null;
            }
        }

        return new SalesDiscountLimitLinkCheckV1(false, chain);
    }

    /// <summary>The groups with one group replaced (a hypothetical store for <see cref="CheckGroupLinks"/>).</summary>
    public static IReadOnlyDictionary<Guid, SalesDiscountLimitGroupDataV1> With(
        IReadOnlyDictionary<Guid, SalesDiscountLimitGroupDataV1> groups, SalesDiscountLimitGroupDataV1 group)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(group);
        var copy = groups.ToDictionary(pair => pair.Key, pair => pair.Value);
        copy[group.Id] = group;
        return copy;
    }

    private static IEnumerable<SalesDiscountLimitVersionDataV1> Active(SalesDiscountLimitGroupDataV1 group) =>
        group.Versions.Where(version => !version.IsWithdrawn);

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
                || version.ProfileId == Guid.Empty)
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
