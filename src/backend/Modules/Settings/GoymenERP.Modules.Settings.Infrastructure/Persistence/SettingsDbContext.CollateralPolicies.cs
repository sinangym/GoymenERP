using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

public sealed partial class SettingsDbContext
{
    internal DbSet<BankInstitutionPolicyRow> BankInstitutionPolicies => Set<BankInstitutionPolicyRow>();
    internal DbSet<BankInstitutionEntryRow> BankInstitutionEntries => Set<BankInstitutionEntryRow>();
    internal DbSet<CollateralRatePolicyRow> CollateralRatePolicies => Set<CollateralRatePolicyRow>();

    private void ConfigureCollateralPolicies(ModelBuilder modelBuilder)
    {
        var bank = modelBuilder.Entity<BankInstitutionPolicyRow>();
        ConfigureFinancePolicyHeader(bank, "BankInstitutionPolicies");
        bank.HasIndex(x => new { x.CompanyId, x.Version }).IsUnique();
        bank.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);
        var entry = modelBuilder.Entity<BankInstitutionEntryRow>();
        entry.ToTable("BankInstitutionEntries", Schema, table =>
        {
            table.HasCheckConstraint("CK_BankInstitutionEntries_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [PolicyId] <> '00000000-0000-0000-0000-000000000000' AND [BankInstitutionId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_BankInstitutionEntries_Text", "LEN([Name])>0 AND LEN([EftCode])>0");
        });
        entry.HasKey(x => new { x.CompanyId, x.PolicyId, x.BankInstitutionId });
        entry.HasIndex(x => new { x.CompanyId, x.PolicyId, x.EftCode }).IsUnique();
        entry.Property(x => x.Name).HasMaxLength(200);
        entry.Property(x => x.EftCode).HasMaxLength(32).UseCollation("Latin1_General_100_BIN2");
        entry.HasOne<BankInstitutionPolicyRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.PolicyId }).OnDelete(DeleteBehavior.Restrict);
        entry.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);

        var rate = modelBuilder.Entity<CollateralRatePolicyRow>();
        ConfigureFinancePolicyHeader(rate, "CollateralRatePolicies", table =>
        {
            table.HasCheckConstraint("CK_CollateralRatePolicies_Scope", "([Scope]='Company' AND [ScopeKey]='Company' AND [CollateralTypeId] IS NULL AND [CollateralTypeVersion] IS NULL AND [BankInstitutionId] IS NULL AND [BankSourceVersion] IS NULL AND [MaximumRemainingDays] IS NULL) OR ([Scope] IN ('TypeBank','TypeBankRemainingDays') AND [CollateralTypeId] IS NOT NULL AND [CollateralTypeId] <> '00000000-0000-0000-0000-000000000000' AND [CollateralTypeVersion] IS NOT NULL AND LEN([CollateralTypeVersion])>0 AND [BankInstitutionId] IS NOT NULL AND [BankInstitutionId] <> '00000000-0000-0000-0000-000000000000' AND [BankSourceVersion] IS NOT NULL AND LEN([BankSourceVersion])>0 AND [ScopeKey]=[Scope]+':'+LOWER(CONVERT(varchar(36),[CollateralTypeId]))+':'+LOWER(CONVERT(varchar(36),[BankInstitutionId])) AND (([Scope]='TypeBank' AND [MaximumRemainingDays] IS NULL) OR ([Scope]='TypeBankRemainingDays' AND [MaximumRemainingDays] IS NOT NULL AND [MaximumRemainingDays]>=0)))");
            table.HasCheckConstraint("CK_CollateralRatePolicies_Rate", CollateralRateCheck);
        });
        rate.Property(x => x.ScopeKey).HasMaxLength(128).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
        rate.Property(x => x.Scope).HasMaxLength(32).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
        rate.Property(x => x.CollateralTypeVersion).HasMaxLength(128).IsUnicode(false);
        rate.Property(x => x.BankSourceVersion).HasMaxLength(128).IsUnicode(false);
        rate.Property(x => x.RatePercent).HasMaxLength(64).IsUnicode(false);
        rate.HasIndex(x => new { x.CompanyId, x.ScopeKey, x.Version }).IsUnique();
        rate.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);
    }
}
