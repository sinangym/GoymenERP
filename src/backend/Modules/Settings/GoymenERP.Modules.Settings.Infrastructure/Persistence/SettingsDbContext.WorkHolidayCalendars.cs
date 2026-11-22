using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// YOL-D22 (W1-A): the company holiday calendar per year, insert-only and versioned like the company policy registry.
/// The version primary key is the concurrency fence; nothing is seeded.
/// </summary>
public sealed partial class SettingsDbContext
{
    internal const int WorkHolidayCalendarMinimumYear = 2000;
    internal const int WorkHolidayCalendarMaximumYear = 2100;
    internal const string WorkHolidayCalendarVersionCheck =
        "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Version] > 0 AND [Year] BETWEEN 2000 AND 2100 "
        + "AND [DayCount] BETWEEN 0 AND 60 AND LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0";
    internal const string WorkHolidayCalendarDayCheck = "[Kind] IN (1, 2) AND YEAR([Date]) = [Year]";

    internal DbSet<WorkHolidayCalendarVersionRow> WorkHolidayCalendarVersions => Set<WorkHolidayCalendarVersionRow>();

    internal DbSet<WorkHolidayCalendarDayRow> WorkHolidayCalendarDays => Set<WorkHolidayCalendarDayRow>();

    private void ConfigureWorkHolidayCalendars(ModelBuilder modelBuilder)
    {
        var version = modelBuilder.Entity<WorkHolidayCalendarVersionRow>();
        version.ToTable("WorkHolidayCalendarVersions", Schema,
            table => table.HasCheckConstraint("CK_SettingsWorkHolidayCalendarVersions_Identity", WorkHolidayCalendarVersionCheck));
        version.HasKey(x => new { x.CompanyId, x.Year, x.Version });
        version.Property(x => x.RecordedBy).HasMaxLength(128);
        version.Property(x => x.Reason).HasMaxLength(4000);
        version.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);

        var day = modelBuilder.Entity<WorkHolidayCalendarDayRow>();
        day.ToTable("WorkHolidayCalendarDays", Schema,
            table => table.HasCheckConstraint("CK_SettingsWorkHolidayCalendarDays_Day", WorkHolidayCalendarDayCheck));
        day.HasKey(x => new { x.CompanyId, x.Year, x.Version, x.Date });
        day.HasOne<WorkHolidayCalendarVersionRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.Year, x.Version }).OnDelete(DeleteBehavior.Restrict);
        day.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);
    }
}
