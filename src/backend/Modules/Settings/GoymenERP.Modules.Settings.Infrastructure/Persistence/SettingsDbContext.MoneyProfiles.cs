using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

public sealed partial class SettingsDbContext
{
    internal const string MoneyProfileScaleCheck = "[Scale] BETWEEN 0 AND 28 AND ([UnitPriceScale] IS NULL OR [UnitPriceScale] BETWEEN 0 AND 28)";
    internal DbSet<MoneyProfileVersionRow> MoneyProfileVersions => Set<MoneyProfileVersionRow>();
    internal DbSet<MoneyProfileBaselineTransferRow> MoneyProfileBaselineTransfers => Set<MoneyProfileBaselineTransferRow>();

    private void ConfigureMoneyProfiles(ModelBuilder modelBuilder)
    {
        const string empty = "'00000000-0000-0000-0000-000000000000'";
        var transfer = modelBuilder.Entity<MoneyProfileBaselineTransferRow>();
        transfer.ToTable("MoneyProfileBaselineTransfers", Schema, table =>
            table.HasCheckConstraint("CK_SettingsMoneyProfileBaselineTransfers_Identity",
                $"[CompanyId] <> {empty} AND [CurrencyId] <> {empty} AND LEN(LTRIM(RTRIM([Operation]))) > 0"));
        transfer.HasKey(row => new { row.CompanyId, row.CurrencyId, row.Operation });
        transfer.Property(row => row.Operation).HasMaxLength(128).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
        transfer.HasQueryFilter(row => companyContext.IsResolved && row.CompanyId == companyContext.CompanyId);
        var profile = modelBuilder.Entity<MoneyProfileVersionRow>();
        profile.ToTable("MoneyProfileVersions", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsMoneyProfileVersions_Identity", $"[CompanyId] <> {empty} AND [CurrencyId] <> {empty} AND [ProfileId] <> {empty} AND [Version] > 0");
            table.HasCheckConstraint("CK_SettingsMoneyProfileVersions_Scale", MoneyProfileScaleCheck);
            table.HasCheckConstraint("CK_SettingsMoneyProfileVersions_Codes", "LEN(LTRIM(RTRIM([Operation]))) > 0 AND LEN(LTRIM(RTRIM([CurrencyCode]))) > 0");
            table.HasCheckConstraint("CK_SettingsMoneyProfileVersions_Source", SalesPolicySourceKindCheck);
            table.HasCheckConstraint("CK_SettingsMoneyProfileVersions_Recorded", "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
        });
        profile.HasKey(row => new { row.CompanyId, row.CurrencyId, row.Operation, row.Version });
        profile.HasOne<MoneyProfileBaselineTransferRow>().WithMany()
            .HasForeignKey(row => new { row.CompanyId, row.CurrencyId, row.Operation }).OnDelete(DeleteBehavior.Restrict);
        profile.Property(row => row.Operation).HasMaxLength(128).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
        profile.Property(row => row.CurrencyCode).HasMaxLength(32);
        profile.Property(row => row.SourceKind).HasMaxLength(32).IsUnicode(false);
        profile.Property(row => row.DecisionReference).HasMaxLength(400);
        profile.Property(row => row.RecordedBy).HasMaxLength(128);
        profile.Property(row => row.Reason).HasMaxLength(4000);
        profile.HasQueryFilter(row => companyContext.IsResolved && row.CompanyId == companyContext.CompanyId);
    }
}
