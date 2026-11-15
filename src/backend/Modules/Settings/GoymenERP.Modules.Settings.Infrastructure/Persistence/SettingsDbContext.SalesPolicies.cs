using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// F-029 S4b (URUN_F029 spec §5.4(1)): the persistent Sales delivery tolerance (SAT-D5) and order-approval amount threshold
/// (SAT-D6) versions, and the per-family baseline transfer marker. Every table carries the company inside its key and the
/// company query filter; the rows are insert-only, the primary key on the version number is the concurrency fence (two
/// writers of the same next version: one wins, the other is a version conflict).
/// </summary>
public sealed partial class SettingsDbContext
{
    internal const string SalesPolicyFamilyCheck =
        "[PolicyFamily] IN ('DeliveryTolerance','ApprovalAmountThreshold','SalesQuotePolicy','EInvoiceScenario')";

    internal const string SalesPolicySourceKindCheck = "[SourceKind] IN ('CompiledBaselineV0','UserMaintained')";

    internal const string SalesDeliveryTolerancePercentCheck =
        "[WarnPercent] >= 0 AND [WarnPercent] <= [ApprovalPercent] AND [ApprovalPercent] <= [BlockPercent] AND [BlockPercent] <= 100";

    internal const string SalesApprovalThresholdAmountCheck =
        "([IsEnabled] = 1 AND [ThresholdAmount] IS NOT NULL AND [ThresholdAmount] > 0 AND [ThresholdAmountScale] IS NOT NULL "
        + "AND [ThresholdAmountScale] <= 8) OR ([IsEnabled] = 0 AND [ThresholdAmount] IS NULL AND [ThresholdAmountScale] IS NULL)";

    internal DbSet<SalesPolicyBaselineTransferRow> SalesPolicyBaselineTransfers => Set<SalesPolicyBaselineTransferRow>();

    internal DbSet<SalesDeliveryToleranceVersionRow> SalesDeliveryToleranceVersions => Set<SalesDeliveryToleranceVersionRow>();

    internal DbSet<SalesApprovalAmountThresholdVersionRow> SalesApprovalAmountThresholdVersions =>
        Set<SalesApprovalAmountThresholdVersionRow>();

    private void ConfigureSalesPolicies(ModelBuilder modelBuilder)
    {
        const string emptyGuid = "'00000000-0000-0000-0000-000000000000'";

        var transfer = modelBuilder.Entity<SalesPolicyBaselineTransferRow>();
        transfer.ToTable("SalesPolicyBaselineTransfers", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsSalesPolicyBaselineTransfers_Company", $"[CompanyId] <> {emptyGuid}");
            table.HasCheckConstraint("CK_SettingsSalesPolicyBaselineTransfers_Family", SalesPolicyFamilyCheck);
            table.HasCheckConstraint("CK_SettingsSalesPolicyBaselineTransfers_EntryCount", "[EntryCount] >= 0");
            table.HasCheckConstraint("CK_SettingsSalesPolicyBaselineTransfers_Recorded",
                "LEN(LTRIM(RTRIM([TransferredBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
        });
        transfer.HasKey(x => new { x.CompanyId, x.PolicyFamily });
        transfer.Property(x => x.PolicyFamily).HasMaxLength(32).IsUnicode(false);
        transfer.Property(x => x.TransferredBy).HasMaxLength(128);
        transfer.Property(x => x.Reason).HasMaxLength(4000);
        transfer.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);

        var tolerance = modelBuilder.Entity<SalesDeliveryToleranceVersionRow>();
        tolerance.ToTable("SalesDeliveryToleranceVersions", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsSalesDeliveryToleranceVersions_Identity",
                $"[CompanyId] <> {emptyGuid} AND [ProfileId] <> {emptyGuid} AND [Version] > 0");
            table.HasCheckConstraint("CK_SettingsSalesDeliveryToleranceVersions_Percents", SalesDeliveryTolerancePercentCheck);
            table.HasCheckConstraint("CK_SettingsSalesDeliveryToleranceVersions_Source", SalesPolicySourceKindCheck);
            table.HasCheckConstraint("CK_SettingsSalesDeliveryToleranceVersions_Recorded",
                "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
        });
        tolerance.HasKey(x => new { x.CompanyId, x.Version });
        tolerance.HasIndex(x => new { x.CompanyId, x.ProfileId }).IsUnique();
        tolerance.Property(x => x.WarnPercent).HasPrecision(7, 4);
        tolerance.Property(x => x.ApprovalPercent).HasPrecision(7, 4);
        tolerance.Property(x => x.BlockPercent).HasPrecision(7, 4);
        tolerance.Property(x => x.SourceKind).HasMaxLength(32).IsUnicode(false);
        tolerance.Property(x => x.DecisionReference).HasMaxLength(400);
        tolerance.Property(x => x.RecordedBy).HasMaxLength(128);
        tolerance.Property(x => x.Reason).HasMaxLength(4000);
        tolerance.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);

        var threshold = modelBuilder.Entity<SalesApprovalAmountThresholdVersionRow>();
        threshold.ToTable("SalesApprovalAmountThresholdVersions", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsSalesApprovalAmountThresholdVersions_Identity",
                $"[CompanyId] <> {emptyGuid} AND [CurrencyId] <> {emptyGuid} AND [ProfileId] <> {emptyGuid} AND [Version] > 0");
            table.HasCheckConstraint("CK_SettingsSalesApprovalAmountThresholdVersions_Amount", SalesApprovalThresholdAmountCheck);
            table.HasCheckConstraint("CK_SettingsSalesApprovalAmountThresholdVersions_Source", SalesPolicySourceKindCheck);
            table.HasCheckConstraint("CK_SettingsSalesApprovalAmountThresholdVersions_Recorded",
                "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
        });
        threshold.HasKey(x => new { x.CompanyId, x.CurrencyId, x.Version });
        threshold.HasIndex(x => new { x.CompanyId, x.ProfileId }).IsUnique();
        threshold.Property(x => x.ThresholdAmount).HasPrecision(28, 8);
        threshold.Property(x => x.SourceKind).HasMaxLength(32).IsUnicode(false);
        threshold.Property(x => x.DecisionReference).HasMaxLength(400);
        threshold.Property(x => x.RecordedBy).HasMaxLength(128);
        threshold.Property(x => x.Reason).HasMaxLength(4000);
        threshold.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);
    }
}
