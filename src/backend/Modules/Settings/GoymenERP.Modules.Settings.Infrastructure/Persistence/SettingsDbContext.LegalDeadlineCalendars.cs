using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// WP15 (1.16): the company legal deadline calendar, insert-only and versioned like the holiday calendar. The version primary
/// key is the concurrency fence; nothing is seeded.
/// </summary>
public sealed partial class SettingsDbContext
{
    internal const string LegalDeadlineCalendarVersionCheck =
        "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Version] > 0 AND [EntryCount] BETWEEN 0 AND 8 "
        + "AND LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0";
    internal const string LegalDeadlineCalendarEntryCheck =
        "[Obligation] BETWEEN 1 AND 8 AND [PeriodMonths] IN (1, 3, 12) AND [MonthsAfterPeriodEnd] BETWEEN 0 AND 12 "
        + "AND [DueDayOfMonth] BETWEEN 1 AND 31";

    internal DbSet<LegalDeadlineCalendarVersionRow> LegalDeadlineCalendarVersions => Set<LegalDeadlineCalendarVersionRow>();

    internal DbSet<LegalDeadlineCalendarEntryRow> LegalDeadlineCalendarEntries => Set<LegalDeadlineCalendarEntryRow>();

    private void ConfigureLegalDeadlineCalendars(ModelBuilder modelBuilder)
    {
        var version = modelBuilder.Entity<LegalDeadlineCalendarVersionRow>();
        version.ToTable("LegalDeadlineCalendarVersions", Schema,
            table => table.HasCheckConstraint("CK_SettingsLegalDeadlineCalendarVersions_Identity", LegalDeadlineCalendarVersionCheck));
        version.HasKey(x => new { x.CompanyId, x.Version });
        version.Property(x => x.RecordedBy).HasMaxLength(128);
        version.Property(x => x.Reason).HasMaxLength(4000);
        version.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);

        var entry = modelBuilder.Entity<LegalDeadlineCalendarEntryRow>();
        entry.ToTable("LegalDeadlineCalendarEntries", Schema,
            table => table.HasCheckConstraint("CK_SettingsLegalDeadlineCalendarEntries_Entry", LegalDeadlineCalendarEntryCheck));
        entry.HasKey(x => new { x.CompanyId, x.Version, x.Obligation });
        entry.HasOne<LegalDeadlineCalendarVersionRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.Version }).OnDelete(DeleteBehavior.Restrict);
        entry.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);
    }
}
