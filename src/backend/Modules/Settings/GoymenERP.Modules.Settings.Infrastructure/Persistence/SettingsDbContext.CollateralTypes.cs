using GoymenERP.Modules.Settings.Application;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

public sealed partial class SettingsDbContext
{
    internal const string CollateralKindCheck = "[Kind] IN ('BankGuaranteedPayment','Dbs','GuaranteeLetter','CreditCard','Vinov','Mortgage')";
    internal const string CollateralRateCheck = "LEN([RatePercent]) BETWEEN 1 AND 64 AND [RatePercent] NOT LIKE '%[^0-9.]%' AND LEFT([RatePercent], 1) <> '.' AND RIGHT([RatePercent], 1) <> '.' AND LEN([RatePercent]) - LEN(REPLACE([RatePercent], '.', '')) <= 1 AND (TRY_CONVERT(int, LEFT([RatePercent], CHARINDEX('.', [RatePercent] + '.') - 1)) BETWEEN 0 AND 99 OR [RatePercent] = '100')";
    internal DbSet<CollateralTypeRow> CollateralTypes => Set<CollateralTypeRow>();
    internal DbSet<CollateralTypeVersionRow> CollateralTypeVersions => Set<CollateralTypeVersionRow>();

    private void ConfigureCollateralTypes(ModelBuilder modelBuilder)
    {
        var entry = modelBuilder.Entity<CollateralTypeRow>();
        entry.ToTable("CollateralTypes", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsCollateralTypes_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [EntryId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_SettingsCollateralTypes_Kind", CollateralKindCheck);
            table.HasCheckConstraint("CK_SettingsCollateralTypes_Code", ReferenceListCodeCheck);
            table.HasCheckConstraint("CK_SettingsCollateralTypes_Created", "LEN(LTRIM(RTRIM([CreatedBy]))) > 0");
        });
        entry.HasKey(x => new { x.CompanyId, x.EntryId });
        entry.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        entry.Property(x => x.Kind).HasMaxLength(32).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
        entry.Property(x => x.Code).HasMaxLength(32).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
        entry.Property(x => x.CreatedBy).HasMaxLength(128);
        entry.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);

        var version = modelBuilder.Entity<CollateralTypeVersionRow>();
        version.ToTable("CollateralTypeVersions", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsCollateralTypeVersions_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [EntryId] <> '00000000-0000-0000-0000-000000000000' AND [Version] > 0");
            table.HasCheckConstraint("CK_SettingsCollateralTypeVersions_Name", "LEN(LTRIM(RTRIM([Name]))) > 0");
            table.HasCheckConstraint("CK_SettingsCollateralTypeVersions_Recorded", "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
            // Exact percentage validation is shared by commands and reader; decimal SQL conversion would round valid text.
            table.HasCheckConstraint("CK_SettingsCollateralTypeVersions_RateText", CollateralRateCheck);
        });
        version.HasKey(x => new { x.CompanyId, x.EntryId, x.Version });
        version.Property(x => x.Version).ValueGeneratedNever();
        version.Property(x => x.Name).HasMaxLength(200);
        version.Property(x => x.RatePercent).HasMaxLength(CollateralTypeRulesV1.RateMaximumLength).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
        version.Property(x => x.RecordedBy).HasMaxLength(128);
        version.Property(x => x.Reason).HasMaxLength(4000);
        version.HasOne<CollateralTypeRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.EntryId }).OnDelete(DeleteBehavior.Restrict);
        version.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);
    }
}
