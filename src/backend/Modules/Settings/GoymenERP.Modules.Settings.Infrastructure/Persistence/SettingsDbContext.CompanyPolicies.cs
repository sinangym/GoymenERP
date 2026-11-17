using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// B-13 (ADR-SET-002): the company policy registry and the FNS-D97 credit account mapping. Both tables carry the company
/// inside their key and the company query filter; rows are insert-only and the primary key on the version number is the
/// concurrency fence. The database seeds and defaults nothing: "not recorded" is the absence of a row.
/// </summary>
public sealed partial class SettingsDbContext
{
    internal DbSet<CompanyPolicyVersionRow> CompanyPolicyVersions => Set<CompanyPolicyVersionRow>();

    internal DbSet<FinanceCreditAccountMappingVersionRow> FinanceCreditAccountMappingVersions =>
        Set<FinanceCreditAccountMappingVersionRow>();

    private void ConfigureCompanyPolicies(ModelBuilder modelBuilder)
    {
        const string emptyGuid = "'00000000-0000-0000-0000-000000000000'";
        const string binaryCollation = "Latin1_General_100_BIN2";
        const string noTax = "[VatApplies] IS NULL AND [VatPercent] IS NULL AND [ExemptionCode] IS NULL "
            + "AND [WithholdingApplies] IS NULL AND [WithholdingPercent] IS NULL AND [WithholdingCode] IS NULL";

        var policy = modelBuilder.Entity<CompanyPolicyVersionRow>();
        policy.ToTable("CompanyPolicyVersions", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsCompanyPolicyVersions_Identity", $"[CompanyId] <> {emptyGuid} AND [Version] > 0");
            table.HasCheckConstraint("CK_SettingsCompanyPolicyVersions_Key",
                "[PolicyKey] IN ('finance.opening-period', 'manufacturing.due-date-buffer', "
                + "'sales.subcontract-tax-default', 'purchasing.subcontract-tax-default', 'sales.contract-discount-limit', 'manufacturing.include-scrap-cost', 'work.notification-active-days')");
            // Each key fills only its own columns: a boolean (opening period), whole working days ≥ 0 (due-date buffer), or
            // the SAT-D381 tax default shape (VAT above 0 up to 100 without an exemption code, or no VAT and no withholding;
            // withholding above 0 up to 100 with an optional code, or neither).
            table.HasCheckConstraint("CK_SettingsCompanyPolicyVersions_Value",
                "([PolicyKey] IN ('finance.opening-period', 'manufacturing.include-scrap-cost') AND [PercentValue] IS NULL AND [BooleanValue] IS NOT NULL AND [WholeNumberValue] IS NULL AND "
                + noTax + ") "
                + "OR ([PolicyKey] IN ('manufacturing.due-date-buffer', 'work.notification-active-days') AND [PercentValue] IS NULL AND [BooleanValue] IS NULL "
                + "AND [WholeNumberValue] IS NOT NULL AND [WholeNumberValue] >= 0 AND " + noTax + ") "
                + "OR ([PolicyKey] IN ('sales.subcontract-tax-default', 'purchasing.subcontract-tax-default') "
                + "AND [PercentValue] IS NULL AND [BooleanValue] IS NULL AND [WholeNumberValue] IS NULL "
                + "AND [VatApplies] IS NOT NULL AND [WithholdingApplies] IS NOT NULL "
                + "AND (([VatApplies] = 1 AND [VatPercent] IS NOT NULL AND [VatPercent] > 0 AND [VatPercent] <= 100 "
                + "AND [ExemptionCode] IS NULL) OR ([VatApplies] = 0 AND [VatPercent] IS NULL AND [WithholdingApplies] = 0)) "
                + "AND (([WithholdingApplies] = 1 AND [WithholdingPercent] IS NOT NULL AND [WithholdingPercent] > 0 "
                + "AND [WithholdingPercent] <= 100) "
                + "OR ([WithholdingApplies] = 0 AND [WithholdingPercent] IS NULL AND [WithholdingCode] IS NULL))) "
                + "OR ([PolicyKey] = 'sales.contract-discount-limit' AND [BooleanValue] IS NULL AND [WholeNumberValue] IS NULL "
                + "AND [PercentValue] IS NOT NULL AND [PercentValue] >= 0 AND [PercentValue] <= 100 AND " + noTax + ")");
            table.HasCheckConstraint("CK_SettingsCompanyPolicyVersions_Codes",
                "([ExemptionCode] IS NULL OR (LEN([ExemptionCode]) BETWEEN 1 AND 16 AND [ExemptionCode] NOT LIKE '%[^A-Z0-9]%')) "
                + "AND ([WithholdingCode] IS NULL OR (LEN([WithholdingCode]) BETWEEN 1 AND 16 "
                + "AND [WithholdingCode] NOT LIKE '%[^A-Z0-9]%'))");
            table.HasCheckConstraint("CK_SettingsCompanyPolicyVersions_Recorded",
                "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
        });
        policy.HasKey(x => new { x.CompanyId, x.PolicyKey, x.Version });
        policy.Property(x => x.PolicyKey).HasMaxLength(64).IsUnicode(false).UseCollation(binaryCollation);
        policy.Property(x => x.PercentValue).HasPrecision(7, 4);
        policy.Property(x => x.VatPercent).HasPrecision(7, 4);
        policy.Property(x => x.WithholdingPercent).HasPrecision(7, 4);
        policy.Property(x => x.ExemptionCode).HasMaxLength(16).IsUnicode(false).UseCollation(binaryCollation);
        policy.Property(x => x.WithholdingCode).HasMaxLength(16).IsUnicode(false).UseCollation(binaryCollation);
        policy.Property(x => x.RecordedBy).HasMaxLength(128);
        policy.Property(x => x.Reason).HasMaxLength(4000);
        policy.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);

        var mapping = modelBuilder.Entity<FinanceCreditAccountMappingVersionRow>();
        mapping.ToTable("FinanceCreditAccountMappingVersions", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsFinanceCreditAccountMappingVersions_Identity",
                $"[CompanyId] <> {emptyGuid} AND [Version] > 0 AND [Purpose] IN (1, 2, 3)");
            table.HasCheckConstraint("CK_SettingsFinanceCreditAccountMappingVersions_Recorded",
                "LEN(LTRIM(RTRIM([LedgerAccountCode]))) > 0 AND LEN(LTRIM(RTRIM([RecordedBy]))) > 0 "
                + "AND LEN(LTRIM(RTRIM([Reason]))) > 0");
        });
        mapping.HasKey(x => new { x.CompanyId, x.Purpose, x.Version });
        mapping.Property(x => x.LedgerAccountCode).HasMaxLength(32);
        mapping.Property(x => x.RecordedBy).HasMaxLength(128);
        mapping.Property(x => x.Reason).HasMaxLength(4000);
        mapping.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);
    }
}
