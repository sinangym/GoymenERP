using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// API-98/B-14 (KIM-D49, KIM-D51): the company's reference lists (expense types, service types, paint colors, scrap kinds).
/// Both tables carry the company inside their key and the company query filter; rows are insert-only. The entry's code is
/// unique within the company's list (binary collation: exactly as written) and immutable; the version primary key on
/// (company, entry, version) is the concurrency fence. The kind check holds only the enabled kinds (the operation type list
/// is not opened). Nothing is seeded: every list starts empty.
/// </summary>
public sealed partial class SettingsDbContext
{
    internal const string ReferenceListKindCheck =
        "[Kind] IN ('" + SettingsReferenceListKindsV1.ExpenseTypeCode + "','" + SettingsReferenceListKindsV1.ServiceTypeCode
        + "','" + SettingsReferenceListKindsV1.PaintColorCode + "','" + SettingsReferenceListKindsV1.ScrapKindCode + "')";

    internal const string ReferenceListCodeCheck =
        "LEN([Code]) BETWEEN 1 AND 32 AND [Code] NOT LIKE '%[^A-Z0-9._-]%' AND LEFT([Code], 1) LIKE '[A-Z0-9]'";

    internal DbSet<ReferenceListEntryRow> ReferenceListEntries => Set<ReferenceListEntryRow>();

    internal DbSet<ReferenceListEntryVersionRow> ReferenceListEntryVersions => Set<ReferenceListEntryVersionRow>();

    private void ConfigureReferenceLists(ModelBuilder modelBuilder)
    {
        const string emptyGuid = "'00000000-0000-0000-0000-000000000000'";
        const string binaryCollation = "Latin1_General_100_BIN2";

        var entry = modelBuilder.Entity<ReferenceListEntryRow>();
        entry.ToTable("ReferenceListEntries", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsReferenceListEntries_Identity",
                $"[CompanyId] <> {emptyGuid} AND [EntryId] <> {emptyGuid}");
            table.HasCheckConstraint("CK_SettingsReferenceListEntries_Kind", ReferenceListKindCheck);
            table.HasCheckConstraint("CK_SettingsReferenceListEntries_Code", ReferenceListCodeCheck);
            table.HasCheckConstraint("CK_SettingsReferenceListEntries_Created", "LEN(LTRIM(RTRIM([CreatedBy]))) > 0");
        });
        entry.HasKey(x => new { x.CompanyId, x.EntryId });
        entry.HasIndex(x => new { x.CompanyId, x.Kind, x.Code }).IsUnique();
        entry.Property(x => x.Kind).HasMaxLength(16).IsUnicode(false).UseCollation(binaryCollation);
        entry.Property(x => x.Code).HasMaxLength(SettingsReferenceListInputLimitsV1.CodeMaximumLength).IsUnicode(false)
            .UseCollation(binaryCollation);
        entry.Property(x => x.CreatedBy).HasMaxLength(128);
        entry.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);

        var version = modelBuilder.Entity<ReferenceListEntryVersionRow>();
        version.ToTable("ReferenceListEntryVersions", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsReferenceListEntryVersions_Identity",
                $"[CompanyId] <> {emptyGuid} AND [EntryId] <> {emptyGuid} AND [Version] > 0");
            table.HasCheckConstraint("CK_SettingsReferenceListEntryVersions_Name", "LEN(LTRIM(RTRIM([Name]))) > 0");
            table.HasCheckConstraint("CK_SettingsReferenceListEntryVersions_Recorded",
                "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
        });
        version.HasKey(x => new { x.CompanyId, x.EntryId, x.Version });
        version.Property(x => x.Version).ValueGeneratedNever();
        version.Property(x => x.Name).HasMaxLength(SettingsReferenceListInputLimitsV1.NameMaximumLength);
        version.Property(x => x.RecordedBy).HasMaxLength(128);
        version.Property(x => x.Reason).HasMaxLength(SettingsReferenceListInputLimitsV1.ReasonMaximumLength);
        version.HasOne<ReferenceListEntryRow>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.EntryId })
            .OnDelete(DeleteBehavior.Restrict);
        version.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);
    }
}
