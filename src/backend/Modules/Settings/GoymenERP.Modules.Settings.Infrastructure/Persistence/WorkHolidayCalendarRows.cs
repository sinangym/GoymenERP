namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// YOL-D22 (W1-A): one insert-only version of a company's official holiday / eve list for one calendar year. A year with
/// no version is "not defined" (the Work inbox then deducts weekends only and warns); a version with no days is an
/// explicit empty list.
/// </summary>
internal sealed class WorkHolidayCalendarVersionRow
{
    public Guid CompanyId { get; set; }
    public int Year { get; set; }
    public long Version { get; set; }
    public int DayCount { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>One day of a holiday calendar version: the date and whether it is an official holiday (1) or an eve (2).</summary>
internal sealed class WorkHolidayCalendarDayRow
{
    public Guid CompanyId { get; set; }
    public int Year { get; set; }
    public long Version { get; set; }
    public DateOnly Date { get; set; }
    public int Kind { get; set; }
}
