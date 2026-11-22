namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// WP15 (1.16): one insert-only version of a company's legal deadline calendar. No version is "not defined" (every counter
/// shows "tanımsız"); a version with no entries is an explicit empty calendar. Nothing is seeded: the values wait for the
/// financial advisor's confirmation.
/// </summary>
internal sealed class LegalDeadlineCalendarVersionRow
{
    public Guid CompanyId { get; set; }
    public long Version { get; set; }
    public int EntryCount { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>One obligation of a calendar version (<see cref="Application.Contracts.V1.LegalDeadlineObligationV1"/>).</summary>
internal sealed class LegalDeadlineCalendarEntryRow
{
    public Guid CompanyId { get; set; }
    public long Version { get; set; }
    public int Obligation { get; set; }
    public int PeriodMonths { get; set; }
    public int MonthsAfterPeriodEnd { get; set; }
    public int DueDayOfMonth { get; set; }
}
