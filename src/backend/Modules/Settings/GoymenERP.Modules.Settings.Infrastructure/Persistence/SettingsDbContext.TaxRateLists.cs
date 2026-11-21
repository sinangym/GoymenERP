using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// F-029 S9 (SAT-D181, EK26): the company's VAT rate and withholding ratio lists. The same shape as the code lists: a list row
/// per kind (the edit fence, its <c>Version</c> the concurrency token), immutable whole versions with their entries (restrict,
/// so a version never loses its entries) and insert-only withdrawals. Every table carries the company inside its key and the
/// company query filter; codes are compared ordinally (binary collation). The tables start EMPTY (SAT-D148: nothing is
/// seeded). The percent is stored exactly (scale 4, the tax master's input scale); a withholding ratio above 0, a VAT rate
/// without a withholding code.
/// </summary>
public sealed partial class SettingsDbContext
{
    internal DbSet<TaxRateListRow> TaxRateLists => Set<TaxRateListRow>();

    internal DbSet<TaxRateListVersionRow> TaxRateListVersions => Set<TaxRateListVersionRow>();

    internal DbSet<TaxRateListEntryRow> TaxRateListEntries => Set<TaxRateListEntryRow>();

    internal DbSet<TaxRateListWithdrawalRow> TaxRateListWithdrawals => Set<TaxRateListWithdrawalRow>();

    private void ConfigureTaxRateLists(ModelBuilder modelBuilder)
    {
        const string emptyGuid = "'00000000-0000-0000-0000-000000000000'";
        const string binaryCollation = "Latin1_General_100_BIN2";

        var list = modelBuilder.Entity<TaxRateListRow>();
        list.ToTable("TaxRateLists", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsTaxRateLists_Company", $"[CompanyId] <> {emptyGuid}");
            table.HasCheckConstraint("CK_SettingsTaxRateLists_Kind", "[Kind] IN ('VatRate', 'WithholdingRatio')");
            table.HasCheckConstraint("CK_SettingsTaxRateLists_Version", "[Version] > 0 AND [LastListVersion] > 0");
            table.HasCheckConstraint("CK_SettingsTaxRateLists_Actor", "LEN(LTRIM(RTRIM([UpdatedBy]))) > 0");
        });
        list.HasKey(x => new { x.CompanyId, x.Kind });
        list.Property(x => x.Kind).HasMaxLength(16).IsUnicode(false).UseCollation(binaryCollation);
        list.Property(x => x.Version).IsConcurrencyToken();
        list.Property(x => x.UpdatedBy).HasMaxLength(128);
        list.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);

        var version = modelBuilder.Entity<TaxRateListVersionRow>();
        version.ToTable("TaxRateListVersions", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsTaxRateListVersions_Version", "[ListVersion] > 0");
            table.HasCheckConstraint("CK_SettingsTaxRateListVersions_Recorded",
                "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
        });
        version.HasKey(x => new { x.CompanyId, x.Kind, x.ListVersion });
        version.Property(x => x.Kind).HasMaxLength(16).IsUnicode(false).UseCollation(binaryCollation);
        version.Property(x => x.RecordedBy).HasMaxLength(128);
        version.Property(x => x.Reason).HasMaxLength(4000);
        version.HasOne<TaxRateListRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.Kind })
            .OnDelete(DeleteBehavior.Restrict);
        version.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);

        var entry = modelBuilder.Entity<TaxRateListEntryRow>();
        entry.ToTable("TaxRateListEntries", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsTaxRateListEntries_Code",
                "LEN([Code]) BETWEEN 1 AND 16 AND [Code] NOT LIKE '%[^A-Z0-9/]%'");
            table.HasCheckConstraint("CK_SettingsTaxRateListEntries_Name", "LEN(LTRIM(RTRIM([Name]))) > 0");
            table.HasCheckConstraint("CK_SettingsTaxRateListEntries_Percent", "[Percent] >= 0 AND [Percent] <= 100");
            table.HasCheckConstraint("CK_SettingsTaxRateListEntries_KindRule",
                "([Kind] = 'VatRate' AND [TaxCode] IS NULL) OR ([Kind] = 'WithholdingRatio' AND [Percent] > 0)");
            table.HasCheckConstraint("CK_SettingsTaxRateListEntries_TaxCode",
                "[TaxCode] IS NULL OR (LEN([TaxCode]) BETWEEN 1 AND 16 AND [TaxCode] NOT LIKE '%[^A-Z0-9]%')");
        });
        entry.HasKey(x => new { x.CompanyId, x.Kind, x.ListVersion, x.Code });
        entry.Property(x => x.Kind).HasMaxLength(16).IsUnicode(false).UseCollation(binaryCollation);
        entry.Property(x => x.Code).HasMaxLength(16).IsUnicode(false).UseCollation(binaryCollation);
        entry.Property(x => x.Name).HasMaxLength(200);
        entry.Property(x => x.Percent).HasPrecision(7, 4);
        entry.Property(x => x.TaxCode).HasMaxLength(16).IsUnicode(false).UseCollation(binaryCollation);
        entry.HasOne<TaxRateListVersionRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.Kind, x.ListVersion })
            .OnDelete(DeleteBehavior.Restrict);
        entry.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);

        var withdrawal = modelBuilder.Entity<TaxRateListWithdrawalRow>();
        withdrawal.ToTable("TaxRateListWithdrawals", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsTaxRateListWithdrawals_Replacement",
                "[ReplacedByListVersion] IS NULL OR [ReplacedByListVersion] > [ListVersion]");
            table.HasCheckConstraint("CK_SettingsTaxRateListWithdrawals_Recorded",
                "LEN(LTRIM(RTRIM([WithdrawnBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
        });
        withdrawal.HasKey(x => new { x.CompanyId, x.Kind, x.ListVersion });
        withdrawal.Property(x => x.Kind).HasMaxLength(16).IsUnicode(false).UseCollation(binaryCollation);
        withdrawal.Property(x => x.WithdrawnBy).HasMaxLength(128);
        withdrawal.Property(x => x.Reason).HasMaxLength(4000);
        withdrawal.HasOne<TaxRateListVersionRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.Kind, x.ListVersion })
            .OnDelete(DeleteBehavior.Restrict);
        withdrawal.HasOne<TaxRateListVersionRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.Kind, x.ReplacedByListVersion })
            .OnDelete(DeleteBehavior.Restrict);
        withdrawal.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);
    }
}
