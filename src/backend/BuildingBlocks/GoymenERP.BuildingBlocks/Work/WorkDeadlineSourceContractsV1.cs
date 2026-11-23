namespace GoymenERP.BuildingBlocks.Work;

/// <summary>
/// WP15 (DALGA_PLANI_v2 1.16): the caller of one Work "yaklaşan süreler" read. The host captures it from the resolved request
/// and hands it to every deadline source unchanged. A source answers only for <see cref="CompanyId"/> and only under its own
/// authority.
/// </summary>
public sealed record WorkDeadlineSourceContextV1(Guid CompanyId, string UserId, string CorrelationId);

/// <summary>Whether a deadline has a recorded definition. An undefined deadline has no due date and no running counter.</summary>
public static class WorkDeadlineStatesV1
{
    public const string Defined = "defined";
    public const string Undefined = "undefined";
}

/// <summary>How a defined deadline's shown due date relates to its original (legal) date (YOL-D32).</summary>
public static class WorkDeadlineAdjustmentsV1
{
    public const string None = "none";
    public const string MovedToBusinessDay = "moved-to-business-day";
    public const string HolidayCalendarUndefined = "holiday-calendar-undefined";

    public static bool IsKnown(string? value) => value is None or MovedToBusinessDay or HolidayCalendarUndefined;
}

/// <summary>
/// One deadline as its owner lists it. <see cref="Code"/> is the owner's English code (<c>{module}.{deadline}</c>);
/// <see cref="State"/> is <see cref="WorkDeadlineStatesV1"/>; for a defined deadline the owner computed
/// <see cref="DueDate"/> (business date) and <see cref="DaysRemaining"/> with its own calculator, Work never computes them;
/// <see cref="PeriodStart"/>/<see cref="PeriodEnd"/> name the period it is due for when the deadline is periodic.
/// <see cref="DefinitionVersion"/> is the owner's version text of the definition in force (null when undefined).
/// <see cref="Link"/> is the owner's screen for the definition (relative application path). For a defined deadline
/// <see cref="OriginalDueDate"/> is the date the definition gives and <see cref="DueDateAdjustment"/> says whether
/// <see cref="DueDate"/> was moved from it (<see cref="WorkDeadlineAdjustmentsV1"/>); both are null for an undefined one.
/// </summary>
public sealed record WorkDeadlineItemV1(
    string Module,
    string Kind,
    string Code,
    string State,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    DateOnly? DueDate,
    int? DaysRemaining,
    string? DefinitionVersion,
    string Link,
    DateOnly? OriginalDueDate = null,
    string? DueDateAdjustment = null);

public enum WorkDeadlineSourceOutcomeV1
{
    Found,
    Denied,
    Unavailable,
}

/// <summary>A source's answer: items (found), nothing because the caller may not see them (denied), or a failure (unavailable).</summary>
public sealed record WorkDeadlineSourceResultV1(WorkDeadlineSourceOutcomeV1 Outcome, IReadOnlyList<WorkDeadlineItemV1> Items, bool HasMore)
{
    public static WorkDeadlineSourceResultV1 Found(IReadOnlyList<WorkDeadlineItemV1> items, bool hasMore) =>
        new(WorkDeadlineSourceOutcomeV1.Found, items ?? throw new ArgumentNullException(nameof(items)), hasMore);

    public static WorkDeadlineSourceResultV1 Denied() => new(WorkDeadlineSourceOutcomeV1.Denied, [], false);

    public static WorkDeadlineSourceResultV1 Unavailable() => new(WorkDeadlineSourceOutcomeV1.Unavailable, [], false);
}

/// <summary>
/// WP15 (1.16): an owner module's list of upcoming deadlines for the Work view. Unlike <see cref="IWorkInboxSourceV1"/> it is not
/// an approval: there is no maker, no decision and no approval duration. The owner applies its own read permission, reads only
/// the context company, computes due dates with its own calculator and writes nothing. A source with no definition answers its
/// deadlines as <see cref="WorkDeadlineStatesV1.Undefined"/>, never with an assumed date (fail closed).
/// </summary>
public interface IWorkDeadlineSourceV1
{
    string Module { get; }

    string Kind { get; }

    Task<WorkDeadlineSourceResultV1> ListUpcomingAsync(WorkDeadlineSourceContextV1 context, int maxCount,
        CancellationToken cancellationToken = default);
}
