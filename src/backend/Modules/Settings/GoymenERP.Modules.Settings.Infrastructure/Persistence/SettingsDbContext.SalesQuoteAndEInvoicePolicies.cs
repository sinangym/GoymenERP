using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// F-029 S5/S6 (URUN_F029 spec §5.4(2)): the persistent Sales quote loss reason lists (SAT-D25), default quote validity
/// (SAT-D39), e-Fatura scenario code lists and default scenarios (SAT-D102, SAT-D120). The same shape as the S4b tables:
/// every table carries the company inside its key and the company query filter, the rows are insert-only, the primary key
/// on the version number is the concurrency fence, and the list entries are children of their list version (restrict, so a
/// list version never loses its entries). Codes are compared ordinally (binary collation), as the catalogs compare them.
/// </summary>
public sealed partial class SettingsDbContext
{
    internal const string EInvoiceScenarioCodeShapeCheck = "[Code] LIKE '[A-Z]%' AND [Code] NOT LIKE '%[^A-Z0-9_]%'";

    internal const string EInvoiceScenarioDefaultShapeCheck =
        "[ScenarioCode] LIKE '[A-Z]%' AND [ScenarioCode] NOT LIKE '%[^A-Z0-9_]%'";

    internal DbSet<SalesQuoteLossReasonListVersionRow> SalesQuoteLossReasonListVersions =>
        Set<SalesQuoteLossReasonListVersionRow>();

    internal DbSet<SalesQuoteLossReasonEntryRow> SalesQuoteLossReasonEntries => Set<SalesQuoteLossReasonEntryRow>();

    internal DbSet<SalesQuoteDefaultValidityVersionRow> SalesQuoteDefaultValidityVersions =>
        Set<SalesQuoteDefaultValidityVersionRow>();

    internal DbSet<EInvoiceScenarioCodeListVersionRow> EInvoiceScenarioCodeListVersions =>
        Set<EInvoiceScenarioCodeListVersionRow>();

    internal DbSet<EInvoiceScenarioCodeRow> EInvoiceScenarioCodes => Set<EInvoiceScenarioCodeRow>();

    internal DbSet<EInvoiceScenarioDefaultVersionRow> EInvoiceScenarioDefaultVersions =>
        Set<EInvoiceScenarioDefaultVersionRow>();

    private void ConfigureSalesQuoteAndEInvoicePolicies(ModelBuilder modelBuilder)
    {
        const string emptyGuid = "'00000000-0000-0000-0000-000000000000'";
        const string binaryCollation = "Latin1_General_100_BIN2";
        const string identityCheck = $"[CompanyId] <> {emptyGuid} AND [ProfileId] <> {emptyGuid} AND [Version] > 0";
        const string recordedCheck = "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0";

        var lossReasonList = modelBuilder.Entity<SalesQuoteLossReasonListVersionRow>();
        lossReasonList.ToTable("SalesQuoteLossReasonListVersions", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsSalesQuoteLossReasonListVersions_Identity", identityCheck);
            table.HasCheckConstraint("CK_SettingsSalesQuoteLossReasonListVersions_Source", SalesPolicySourceKindCheck);
            table.HasCheckConstraint("CK_SettingsSalesQuoteLossReasonListVersions_Recorded", recordedCheck);
        });
        lossReasonList.HasKey(x => new { x.CompanyId, x.Version });
        lossReasonList.HasIndex(x => new { x.CompanyId, x.ProfileId }).IsUnique();
        lossReasonList.Property(x => x.SourceKind).HasMaxLength(32).IsUnicode(false);
        lossReasonList.Property(x => x.DecisionReference).HasMaxLength(400);
        lossReasonList.Property(x => x.RecordedBy).HasMaxLength(128);
        lossReasonList.Property(x => x.Reason).HasMaxLength(4000);
        lossReasonList.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);

        var lossReason = modelBuilder.Entity<SalesQuoteLossReasonEntryRow>();
        lossReason.ToTable("SalesQuoteLossReasonEntries", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsSalesQuoteLossReasonEntries_Identity",
                $"[CompanyId] <> {emptyGuid} AND [ReasonId] <> {emptyGuid} AND [ListVersion] > 0 AND [Position] >= 0");
            table.HasCheckConstraint("CK_SettingsSalesQuoteLossReasonEntries_Code",
                "LEN(LTRIM(RTRIM([Code]))) > 0 AND DATALENGTH([Code]) = DATALENGTH(LTRIM(RTRIM([Code])))");
            table.HasCheckConstraint("CK_SettingsSalesQuoteLossReasonEntries_Label",
                "LEN(LTRIM(RTRIM([Label]))) > 0 AND DATALENGTH([Label]) = DATALENGTH(LTRIM(RTRIM([Label])))");
        });
        lossReason.HasKey(x => new { x.CompanyId, x.ListVersion, x.Position });
        lossReason.HasIndex(x => new { x.CompanyId, x.ListVersion, x.ReasonId }).IsUnique();
        lossReason.HasIndex(x => new { x.CompanyId, x.ListVersion, x.Code }).IsUnique();
        lossReason.Property(x => x.Code)
            .HasMaxLength(ApprovedSalesQuotePolicyCatalog.MaximumReasonCodeLength)
            .UseCollation(binaryCollation);
        lossReason.Property(x => x.Label).HasMaxLength(ApprovedSalesQuotePolicyCatalog.MaximumReasonLabelLength);
        lossReason.HasOne<SalesQuoteLossReasonListVersionRow>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ListVersion })
            .OnDelete(DeleteBehavior.Restrict);
        lossReason.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);

        var validity = modelBuilder.Entity<SalesQuoteDefaultValidityVersionRow>();
        validity.ToTable("SalesQuoteDefaultValidityVersions", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsSalesQuoteDefaultValidityVersions_Identity", identityCheck);
            table.HasCheckConstraint("CK_SettingsSalesQuoteDefaultValidityVersions_Days", "[ValidityDays] > 0");
            table.HasCheckConstraint("CK_SettingsSalesQuoteDefaultValidityVersions_Source", SalesPolicySourceKindCheck);
            table.HasCheckConstraint("CK_SettingsSalesQuoteDefaultValidityVersions_Recorded", recordedCheck);
        });
        validity.HasKey(x => new { x.CompanyId, x.Version });
        validity.HasIndex(x => new { x.CompanyId, x.ProfileId }).IsUnique();
        validity.Property(x => x.SourceKind).HasMaxLength(32).IsUnicode(false);
        validity.Property(x => x.DecisionReference).HasMaxLength(400);
        validity.Property(x => x.RecordedBy).HasMaxLength(128);
        validity.Property(x => x.Reason).HasMaxLength(4000);
        validity.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);

        var codeList = modelBuilder.Entity<EInvoiceScenarioCodeListVersionRow>();
        codeList.ToTable("EInvoiceScenarioCodeListVersions", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsEInvoiceScenarioCodeListVersions_Identity", identityCheck);
            table.HasCheckConstraint("CK_SettingsEInvoiceScenarioCodeListVersions_Source", SalesPolicySourceKindCheck);
            table.HasCheckConstraint("CK_SettingsEInvoiceScenarioCodeListVersions_Recorded", recordedCheck);
        });
        codeList.HasKey(x => new { x.CompanyId, x.Version });
        codeList.HasIndex(x => new { x.CompanyId, x.ProfileId }).IsUnique();
        codeList.Property(x => x.SourceKind).HasMaxLength(32).IsUnicode(false);
        codeList.Property(x => x.DecisionReference).HasMaxLength(400);
        codeList.Property(x => x.RecordedBy).HasMaxLength(128);
        codeList.Property(x => x.Reason).HasMaxLength(4000);
        codeList.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);

        var code = modelBuilder.Entity<EInvoiceScenarioCodeRow>();
        code.ToTable("EInvoiceScenarioCodes", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsEInvoiceScenarioCodes_Identity",
                $"[CompanyId] <> {emptyGuid} AND [ListVersion] > 0 AND [Position] >= 0");
            table.HasCheckConstraint("CK_SettingsEInvoiceScenarioCodes_Code", EInvoiceScenarioCodeShapeCheck);
        });
        code.HasKey(x => new { x.CompanyId, x.ListVersion, x.Position });
        code.HasIndex(x => new { x.CompanyId, x.ListVersion, x.Code }).IsUnique();
        code.Property(x => x.Code)
            .HasMaxLength(EInvoiceScenarioCodeFormatV1.MaximumLength)
            .IsUnicode(false)
            .UseCollation(binaryCollation);
        code.HasOne<EInvoiceScenarioCodeListVersionRow>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ListVersion })
            .OnDelete(DeleteBehavior.Restrict);
        code.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);

        var scenarioDefault = modelBuilder.Entity<EInvoiceScenarioDefaultVersionRow>();
        scenarioDefault.ToTable("EInvoiceScenarioDefaultVersions", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsEInvoiceScenarioDefaultVersions_Identity", identityCheck);
            table.HasCheckConstraint("CK_SettingsEInvoiceScenarioDefaultVersions_Code", EInvoiceScenarioDefaultShapeCheck);
            table.HasCheckConstraint("CK_SettingsEInvoiceScenarioDefaultVersions_Source", SalesPolicySourceKindCheck);
            table.HasCheckConstraint("CK_SettingsEInvoiceScenarioDefaultVersions_Recorded", recordedCheck);
        });
        scenarioDefault.HasKey(x => new { x.CompanyId, x.Version });
        scenarioDefault.HasIndex(x => new { x.CompanyId, x.ProfileId }).IsUnique();
        scenarioDefault.Property(x => x.ScenarioCode)
            .HasMaxLength(EInvoiceScenarioCodeFormatV1.MaximumLength)
            .IsUnicode(false)
            .UseCollation(binaryCollation);
        scenarioDefault.Property(x => x.SourceKind).HasMaxLength(32).IsUnicode(false);
        scenarioDefault.Property(x => x.DecisionReference).HasMaxLength(400);
        scenarioDefault.Property(x => x.RecordedBy).HasMaxLength(128);
        scenarioDefault.Property(x => x.Reason).HasMaxLength(4000);
        scenarioDefault.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);
    }
}
