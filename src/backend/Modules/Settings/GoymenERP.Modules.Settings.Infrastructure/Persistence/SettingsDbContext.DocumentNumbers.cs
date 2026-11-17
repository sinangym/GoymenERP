using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

public sealed partial class SettingsDbContext
{
    internal DbSet<DocumentNumberFormatRow> DocumentNumberFormats => Set<DocumentNumberFormatRow>();
    internal DbSet<DocumentNumberCounterRow> DocumentNumberCounters => Set<DocumentNumberCounterRow>();
    internal DbSet<DocumentNumberReservationRow> DocumentNumberReservations => Set<DocumentNumberReservationRow>();
    internal DbSet<DocumentNumberCancellationRow> DocumentNumberCancellations => Set<DocumentNumberCancellationRow>();
    private void ConfigureDocumentNumbers(ModelBuilder model)
    {
        const string identity = "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Series] IN (1,2,3,4)";
        var format = model.Entity<DocumentNumberFormatRow>();
        format.ToTable("DocumentNumberFormats", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsDocumentNumberFormats_Identity", identity + " AND [Version] > 0");
            table.HasCheckConstraint("CK_SettingsDocumentNumberFormats_Format", "LEN([Prefix]) BETWEEN 1 AND 16 AND [Prefix] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9]%' AND [SequenceDigits] BETWEEN 1 AND 18");
            table.HasCheckConstraint("CK_SettingsDocumentNumberFormats_Recorded", "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
        });
        format.HasKey(row => new { row.CompanyId, row.Series, row.Version });
        format.Property(row => row.Prefix).HasMaxLength(16).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
        format.Property(row => row.RecordedBy).HasMaxLength(128);
        format.Property(row => row.Reason).HasMaxLength(4000);
        format.HasQueryFilter(row => companyContext.IsResolved && row.CompanyId == companyContext.CompanyId);
        var counter = model.Entity<DocumentNumberCounterRow>();
        counter.ToTable("DocumentNumberCounters", Schema, table => table.HasCheckConstraint("CK_SettingsDocumentNumberCounters_Identity", identity + " AND [Year] BETWEEN 1 AND 9999 AND [LastSequence] > 0"));
        counter.HasKey(row => new { row.CompanyId, row.Series, row.Year });
        counter.Property(row => row.RowVersion).IsRowVersion();
        counter.HasQueryFilter(row => companyContext.IsResolved && row.CompanyId == companyContext.CompanyId);
        var reservation = model.Entity<DocumentNumberReservationRow>();
        reservation.ToTable("DocumentNumberReservations", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsDocumentNumberReservations_Identity", identity + " AND [DocumentId] <> '00000000-0000-0000-0000-000000000000' AND [Year] = YEAR([BusinessDate]) AND [Sequence] > 0 AND [FormatVersion] > 0");
            table.HasCheckConstraint("CK_SettingsDocumentNumberReservations_Recorded", "LEN([Number]) > 0 AND LEN([KeyHash]) = 64 AND LEN([RequestHash]) = 64 AND LEN(LTRIM(RTRIM([RecordedBy]))) > 0");
        });
        reservation.HasKey(row => new { row.CompanyId, row.Series, row.DocumentId });
        reservation.HasIndex(row => new { row.CompanyId, row.Series, row.Year, row.Sequence }).IsUnique();
        reservation.HasIndex(row => new { row.CompanyId, row.Number }).IsUnique();
        reservation.HasIndex(row => new { row.CompanyId, row.KeyHash }).IsUnique();
        reservation.Property(row => row.Number).HasMaxLength(64).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
        reservation.Property(row => row.KeyHash).HasMaxLength(64).IsUnicode(false);
        reservation.Property(row => row.RequestHash).HasMaxLength(64).IsUnicode(false);
        reservation.Property(row => row.RecordedBy).HasMaxLength(128);
        reservation.HasOne<DocumentNumberFormatRow>().WithMany().HasForeignKey(row => new { row.CompanyId, row.Series, row.FormatVersion }).OnDelete(DeleteBehavior.Restrict);
        reservation.HasOne<DocumentNumberCounterRow>().WithMany().HasForeignKey(row => new { row.CompanyId, row.Series, row.Year }).OnDelete(DeleteBehavior.Restrict);
        reservation.HasQueryFilter(row => companyContext.IsResolved && row.CompanyId == companyContext.CompanyId);
        var cancellation = model.Entity<DocumentNumberCancellationRow>();
        cancellation.ToTable("DocumentNumberCancellations", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsDocumentNumberCancellations_Identity", identity + " AND [DocumentId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_SettingsDocumentNumberCancellations_Recorded", "LEN([KeyHash]) = 64 AND LEN([RequestHash]) = 64 AND LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
        });
        cancellation.HasKey(row => new { row.CompanyId, row.Series, row.DocumentId });
        cancellation.HasIndex(row => new { row.CompanyId, row.KeyHash }).IsUnique();
        cancellation.Property(row => row.KeyHash).HasMaxLength(64).IsUnicode(false);
        cancellation.Property(row => row.RequestHash).HasMaxLength(64).IsUnicode(false);
        cancellation.Property(row => row.RecordedBy).HasMaxLength(128);
        cancellation.Property(row => row.Reason).HasMaxLength(4000);
        cancellation.HasOne<DocumentNumberReservationRow>().WithOne().HasForeignKey<DocumentNumberCancellationRow>(row => new { row.CompanyId, row.Series, row.DocumentId }).OnDelete(DeleteBehavior.Restrict);
        cancellation.HasQueryFilter(row => companyContext.IsResolved && row.CompanyId == companyContext.CompanyId);
    }
}
