using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Idempotency.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

public sealed class SettingsDbContext(DbContextOptions<SettingsDbContext> options, ICompanyContext companyContext)
    : DbContext(options)
{
    public const string Schema = "settings";
    internal DbSet<ManufacturingTolerancePolicyRow> ManufacturingTolerancePolicies =>
        Set<ManufacturingTolerancePolicyRow>();

    internal DbSet<SalesDiscountLimitGroupRow> SalesDiscountLimitGroups => Set<SalesDiscountLimitGroupRow>();

    internal DbSet<SalesDiscountLimitVersionRow> SalesDiscountLimitVersions => Set<SalesDiscountLimitVersionRow>();

    internal DbSet<SalesDiscountLimitWithdrawalRow> SalesDiscountLimitWithdrawals => Set<SalesDiscountLimitWithdrawalRow>();

    internal DbSet<SalesDiscountLimitMembershipRow> SalesDiscountLimitMemberships =>
        Set<SalesDiscountLimitMembershipRow>();

    internal DbSet<SalesDiscountLimitMembershipGroupRow> SalesDiscountLimitMembershipGroups =>
        Set<SalesDiscountLimitMembershipGroupRow>();

    internal DbSet<TaxItemTypeDefaultSeriesRow> TaxItemTypeDefaultSeries => Set<TaxItemTypeDefaultSeriesRow>();

    internal DbSet<TaxItemTypeDefaultRecordRow> TaxItemTypeDefaultRecords => Set<TaxItemTypeDefaultRecordRow>();

    internal DbSet<TaxItemTypeDefaultWithdrawalRow> TaxItemTypeDefaultWithdrawals => Set<TaxItemTypeDefaultWithdrawalRow>();

    internal DbSet<TaxCodeListRow> TaxCodeLists => Set<TaxCodeListRow>();

    internal DbSet<TaxCodeListVersionRow> TaxCodeListVersions => Set<TaxCodeListVersionRow>();

    internal DbSet<TaxCodeListEntryRow> TaxCodeListEntries => Set<TaxCodeListEntryRow>();

    internal DbSet<TaxCodeListWithdrawalRow> TaxCodeListWithdrawals => Set<TaxCodeListWithdrawalRow>();

    internal DbSet<TaxWithholdingThresholdRow> TaxWithholdingThresholds => Set<TaxWithholdingThresholdRow>();

    internal DbSet<TaxWithholdingThresholdRecordRow> TaxWithholdingThresholdRecords => Set<TaxWithholdingThresholdRecordRow>();

    internal DbSet<TaxWithholdingThresholdWithdrawalRow> TaxWithholdingThresholdWithdrawals =>
        Set<TaxWithholdingThresholdWithdrawalRow>();

    internal DbSet<CommandExecution> CommandExecutions => Set<CommandExecution>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        var policy = modelBuilder.Entity<ManufacturingTolerancePolicyRow>();
        policy.ToTable("ManufacturingTolerancePolicies", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsMfgTolerance_Company", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_SettingsMfgTolerance_Profile", "[ProfileId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_SettingsMfgTolerance_Process", "[Process] IN ('Slitting','ColdRolling','TubeProfile','Paint','CutToLength')");
            table.HasCheckConstraint("CK_SettingsMfgTolerance_Version", "[Version] > 0");
            table.HasCheckConstraint("CK_SettingsMfgTolerance_Percent", "[MaximumFirePercent] >= 0");
            table.HasCheckConstraint("CK_SettingsMfgTolerance_Window", "[EffectiveThrough] IS NULL OR [EffectiveThrough] >= [EffectiveFrom]");
            table.HasCheckConstraint("CK_SettingsMfgTolerance_Approval", "[ApprovalStatus] = 'Approved' AND LEN(LTRIM(RTRIM([ApprovedBy]))) > 0 AND [ApprovedAtUtc] > '0001-01-01T00:00:00+00:00' AND LEN(LTRIM(RTRIM([ApprovalReason]))) > 0");
        });
        policy.HasKey(x => new { x.CompanyId, x.ProfileId });
        policy.HasIndex(x => new { x.CompanyId, x.Process, x.Version }).IsUnique();
        policy.HasIndex(x => new { x.CompanyId, x.Process, x.EffectiveFrom });
        policy.Property(x => x.Process).HasMaxLength(32).IsUnicode(false);
        policy.Property(x => x.MaximumFirePercent).HasPrecision(9, 6);
        policy.Property(x => x.ApprovalStatus).HasMaxLength(16).IsUnicode(false);
        policy.Property(x => x.ApprovedBy).HasMaxLength(200);
        policy.Property(x => x.ApprovalReason).HasMaxLength(500);
        policy.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);

        ConfigureSalesDiscountLimits(modelBuilder);
        ConfigureTaxMaster(modelBuilder);

        // The platform audit and command-result tables are their owners' (excluded from Settings' migrations); Settings
        // writes them in the same SaveChanges as its own rows and reads them only through the company filter.
        modelBuilder.ConfigureAuditEvent(excludeFromMigrations: true);
        modelBuilder.Entity<AuditEvent>()
            .HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
        modelBuilder.ConfigureCommandExecution(excludeFromMigrations: true);
        modelBuilder.Entity<CommandExecution>()
            .HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    /// <summary>
    /// SAT-D61…D70: limit groups, their immutable limit records and the person → group bindings, all company-scoped with
    /// the company inside every key and every foreign key, so a record can never point at another company's group.
    /// </summary>
    private void ConfigureSalesDiscountLimits(ModelBuilder modelBuilder)
    {
        const string emptyGuid = "'00000000-0000-0000-0000-000000000000'";
        const string binaryCollation = "Latin1_General_100_BIN2";

        var group = modelBuilder.Entity<SalesDiscountLimitGroupRow>();
        group.ToTable("SalesDiscountLimitGroups", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsDiscountLimitGroups_Company", $"[CompanyId] <> {emptyGuid}");
            table.HasCheckConstraint("CK_SettingsDiscountLimitGroups_Id", $"[Id] <> {emptyGuid}");
            table.HasCheckConstraint("CK_SettingsDiscountLimitGroups_Code",
                "LEN([Code]) BETWEEN 1 AND 32 AND [Code] NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([Code], 1) LIKE '[A-Z0-9]'");
            table.HasCheckConstraint("CK_SettingsDiscountLimitGroups_Name", "LEN(LTRIM(RTRIM([Name]))) > 0");
            table.HasCheckConstraint("CK_SettingsDiscountLimitGroups_Version", "[Version] > 0 AND [LimitVersion] >= 0");
            table.HasCheckConstraint("CK_SettingsDiscountLimitGroups_Actors",
                "LEN(LTRIM(RTRIM([CreatedBy]))) > 0 AND LEN(LTRIM(RTRIM([UpdatedBy]))) > 0");
        });
        group.HasKey(x => new { x.CompanyId, x.Id });
        group.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        group.HasIndex(x => new { x.CompanyId, x.Name }).IsUnique();
        group.Property(x => x.Code).HasMaxLength(32).IsUnicode(false).UseCollation(binaryCollation);
        group.Property(x => x.Name).HasMaxLength(100);
        group.Property(x => x.Version).IsConcurrencyToken();
        group.Property(x => x.CreatedBy).HasMaxLength(128);
        group.Property(x => x.UpdatedBy).HasMaxLength(128);
        group.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);

        var limit = modelBuilder.Entity<SalesDiscountLimitVersionRow>();
        limit.ToTable("SalesDiscountLimitVersions", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsDiscountLimitVersions_Company", $"[CompanyId] <> {emptyGuid}");
            table.HasCheckConstraint("CK_SettingsDiscountLimitVersions_Profile", $"[ProfileId] <> {emptyGuid}");
            table.HasCheckConstraint("CK_SettingsDiscountLimitVersions_Version", "[Version] > 0");
            table.HasCheckConstraint("CK_SettingsDiscountLimitVersions_Kind", "[Kind] IN ('Fixed', 'Linked')");
            table.HasCheckConstraint("CK_SettingsDiscountLimitVersions_Percent",
                "[Percent] IS NULL OR ([Percent] >= 0 AND [Percent] <= 100)");
            table.HasCheckConstraint("CK_SettingsDiscountLimitVersions_ExtraPoints",
                "[ExtraPoints] IS NULL OR ([ExtraPoints] >= 0 AND [ExtraPoints] <= 100)");
            table.HasCheckConstraint("CK_SettingsDiscountLimitVersions_Shape",
                "([Kind] = 'Fixed' AND [Percent] IS NOT NULL AND [LinkedGroupId] IS NULL AND [ExtraPoints] IS NULL) OR "
                + "([Kind] = 'Linked' AND [Percent] IS NULL AND [LinkedGroupId] IS NOT NULL AND [ExtraPoints] IS NOT NULL "
                + "AND [LinkedGroupId] <> [LimitGroupId])");
            table.HasCheckConstraint("CK_SettingsDiscountLimitVersions_Recorded",
                "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
        });
        limit.HasKey(x => new { x.CompanyId, x.LimitGroupId, x.Version });
        limit.HasIndex(x => x.ProfileId).IsUnique();
        limit.HasIndex(x => new { x.CompanyId, x.LinkedGroupId });
        limit.Property(x => x.Kind).HasMaxLength(16).IsUnicode(false);
        limit.Property(x => x.Percent).HasPrecision(7, 4);
        limit.Property(x => x.ExtraPoints).HasPrecision(7, 4);
        limit.Property(x => x.RecordedBy).HasMaxLength(128);
        limit.Property(x => x.Reason).HasMaxLength(4000);
        limit.HasOne<SalesDiscountLimitGroupRow>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.LimitGroupId })
            .OnDelete(DeleteBehavior.Restrict);
        limit.HasOne<SalesDiscountLimitGroupRow>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.LinkedGroupId })
            .OnDelete(DeleteBehavior.Restrict);
        limit.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);

        // SAT-D84: a not-yet-effective record's withdrawal (insert-only; the record row itself never changes).
        var withdrawal = modelBuilder.Entity<SalesDiscountLimitWithdrawalRow>();
        withdrawal.ToTable("SalesDiscountLimitWithdrawals", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsDiscountLimitWithdrawals_Company", $"[CompanyId] <> {emptyGuid}");
            table.HasCheckConstraint("CK_SettingsDiscountLimitWithdrawals_Version", "[LimitVersion] > 0");
            table.HasCheckConstraint("CK_SettingsDiscountLimitWithdrawals_Replacement",
                "[ReplacedByVersion] IS NULL OR [ReplacedByVersion] > [LimitVersion]");
            table.HasCheckConstraint("CK_SettingsDiscountLimitWithdrawals_Recorded",
                "LEN(LTRIM(RTRIM([WithdrawnBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
        });
        withdrawal.HasKey(x => new { x.CompanyId, x.LimitGroupId, x.LimitVersion });
        withdrawal.Property(x => x.WithdrawnBy).HasMaxLength(128);
        withdrawal.Property(x => x.Reason).HasMaxLength(4000);
        withdrawal.HasOne<SalesDiscountLimitVersionRow>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.LimitGroupId, x.LimitVersion })
            .OnDelete(DeleteBehavior.Restrict);
        withdrawal.HasOne<SalesDiscountLimitVersionRow>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.LimitGroupId, x.ReplacedByVersion })
            .OnDelete(DeleteBehavior.Restrict);
        withdrawal.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);

        var membership = modelBuilder.Entity<SalesDiscountLimitMembershipRow>();
        membership.ToTable("SalesDiscountLimitMemberships", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsDiscountLimitMemberships_Company", $"[CompanyId] <> {emptyGuid}");
            table.HasCheckConstraint("CK_SettingsDiscountLimitMemberships_User", $"[UserId] <> {emptyGuid}");
            table.HasCheckConstraint("CK_SettingsDiscountLimitMemberships_Version", "[Version] > 0");
            table.HasCheckConstraint("CK_SettingsDiscountLimitMemberships_Actor", "LEN(LTRIM(RTRIM([UpdatedBy]))) > 0");
        });
        membership.HasKey(x => new { x.CompanyId, x.UserId });
        membership.Property(x => x.Version).IsConcurrencyToken();
        membership.Property(x => x.UpdatedBy).HasMaxLength(128);
        membership.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);

        var bound = modelBuilder.Entity<SalesDiscountLimitMembershipGroupRow>();
        bound.ToTable("SalesDiscountLimitMembershipGroups", Schema, table =>
            table.HasCheckConstraint("CK_SettingsDiscountLimitMembershipGroups_Version", "[MembershipVersion] > 0"));
        bound.HasKey(x => new { x.CompanyId, x.UserId, x.MembershipVersion, x.LimitGroupId });
        bound.HasIndex(x => new { x.CompanyId, x.LimitGroupId });
        bound.HasOne<SalesDiscountLimitMembershipRow>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.UserId })
            .OnDelete(DeleteBehavior.Restrict);
        bound.HasOne<SalesDiscountLimitGroupRow>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.LimitGroupId })
            .OnDelete(DeleteBehavior.Restrict);
        bound.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);
    }

    /// <summary>
    /// MOD-03 tax master (SAT-D93/D133/D135/D139): the per-item-type default series and their immutable records, code list
    /// versions and the withholding threshold, with insert-only withdrawals, all company-scoped with the company inside every
    /// key and foreign key. Nothing is seeded (the SAT-D139 defaults are the reviewed source catalog, not rows).
    /// </summary>
    private void ConfigureTaxMaster(ModelBuilder modelBuilder)
    {
        const string emptyGuid = "'00000000-0000-0000-0000-000000000000'";
        const string binaryCollation = "Latin1_General_100_BIN2";

        var series = modelBuilder.Entity<TaxItemTypeDefaultSeriesRow>();
        series.ToTable("TaxItemTypeDefaultSeries", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsTaxItemTypeDefaultSeries_Company", $"[CompanyId] <> {emptyGuid}");
            table.HasCheckConstraint("CK_SettingsTaxItemTypeDefaultSeries_ItemType", "[ItemType] IN ('Steel', 'Scrap', 'Service')");
            table.HasCheckConstraint("CK_SettingsTaxItemTypeDefaultSeries_Version", "[Version] > 0 AND [LastRecordVersion] > 0");
            table.HasCheckConstraint("CK_SettingsTaxItemTypeDefaultSeries_Actor", "LEN(LTRIM(RTRIM([UpdatedBy]))) > 0");
        });
        series.HasKey(x => new { x.CompanyId, x.ItemType });
        series.Property(x => x.ItemType).HasMaxLength(16).IsUnicode(false).UseCollation(binaryCollation);
        series.Property(x => x.Version).IsConcurrencyToken();
        series.Property(x => x.UpdatedBy).HasMaxLength(128);
        series.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);

        var record = modelBuilder.Entity<TaxItemTypeDefaultRecordRow>();
        record.ToTable("TaxItemTypeDefaultRecords", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsTaxItemTypeDefaultRecords_Id", $"[Id] <> {emptyGuid}");
            table.HasCheckConstraint("CK_SettingsTaxItemTypeDefaultRecords_Version", "[RecordVersion] > 0");
            // VAT applies with a 0…100 rate and no exemption code, or not ("KDV yok") with an optional exemption code;
            // withholding only with VAT: a rate above 0 up to 100 and an optional code, or neither.
            table.HasCheckConstraint("CK_SettingsTaxItemTypeDefaultRecords_Vat",
                "([VatApplies] = 1 AND [VatPercent] IS NOT NULL AND [VatPercent] >= 0 AND [VatPercent] <= 100 "
                + "AND [ExemptionCode] IS NULL) OR ([VatApplies] = 0 AND [VatPercent] IS NULL)");
            table.HasCheckConstraint("CK_SettingsTaxItemTypeDefaultRecords_Withholding",
                "([WithholdingApplies] = 1 AND [VatApplies] = 1 AND [WithholdingPercent] IS NOT NULL "
                + "AND [WithholdingPercent] > 0 AND [WithholdingPercent] <= 100) "
                + "OR ([WithholdingApplies] = 0 AND [WithholdingPercent] IS NULL AND [WithholdingCode] IS NULL)");
            table.HasCheckConstraint("CK_SettingsTaxItemTypeDefaultRecords_Codes",
                "([ExemptionCode] IS NULL OR (LEN([ExemptionCode]) BETWEEN 1 AND 16 AND [ExemptionCode] NOT LIKE '%[^A-Z0-9]%')) "
                + "AND ([WithholdingCode] IS NULL OR (LEN([WithholdingCode]) BETWEEN 1 AND 16 "
                + "AND [WithholdingCode] NOT LIKE '%[^A-Z0-9]%'))");
            table.HasCheckConstraint("CK_SettingsTaxItemTypeDefaultRecords_Recorded",
                "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
        });
        record.HasKey(x => new { x.CompanyId, x.Id });
        record.HasIndex(x => new { x.CompanyId, x.ItemType, x.RecordVersion }).IsUnique();
        record.Property(x => x.ItemType).HasMaxLength(16).IsUnicode(false).UseCollation(binaryCollation);
        record.Property(x => x.VatPercent).HasPrecision(7, 4);
        record.Property(x => x.WithholdingPercent).HasPrecision(7, 4);
        record.Property(x => x.ExemptionCode).HasMaxLength(16).IsUnicode(false).UseCollation(binaryCollation);
        record.Property(x => x.WithholdingCode).HasMaxLength(16).IsUnicode(false).UseCollation(binaryCollation);
        record.Property(x => x.RecordedBy).HasMaxLength(128);
        record.Property(x => x.Reason).HasMaxLength(4000);
        record.HasOne<TaxItemTypeDefaultSeriesRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ItemType })
            .OnDelete(DeleteBehavior.Restrict);
        record.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);

        var recordWithdrawal = modelBuilder.Entity<TaxItemTypeDefaultWithdrawalRow>();
        recordWithdrawal.ToTable("TaxItemTypeDefaultWithdrawals", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsTaxItemTypeDefaultWithdrawals_Replacement",
                "[ReplacedByRecordId] IS NULL OR [ReplacedByRecordId] <> [RecordId]");
            table.HasCheckConstraint("CK_SettingsTaxItemTypeDefaultWithdrawals_Recorded",
                "LEN(LTRIM(RTRIM([WithdrawnBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
        });
        recordWithdrawal.HasKey(x => new { x.CompanyId, x.RecordId });
        recordWithdrawal.Property(x => x.WithdrawnBy).HasMaxLength(128);
        recordWithdrawal.Property(x => x.Reason).HasMaxLength(4000);
        recordWithdrawal.HasOne<TaxItemTypeDefaultRecordRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.RecordId })
            .OnDelete(DeleteBehavior.Restrict);
        recordWithdrawal.HasOne<TaxItemTypeDefaultRecordRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ReplacedByRecordId })
            .OnDelete(DeleteBehavior.Restrict);
        recordWithdrawal.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);

        var list = modelBuilder.Entity<TaxCodeListRow>();
        list.ToTable("TaxCodeLists", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsTaxCodeLists_Company", $"[CompanyId] <> {emptyGuid}");
            table.HasCheckConstraint("CK_SettingsTaxCodeLists_Kind", "[Kind] IN ('Exemption', 'Withholding')");
            table.HasCheckConstraint("CK_SettingsTaxCodeLists_Version", "[Version] > 0 AND [LastListVersion] > 0");
            table.HasCheckConstraint("CK_SettingsTaxCodeLists_Actor", "LEN(LTRIM(RTRIM([UpdatedBy]))) > 0");
        });
        list.HasKey(x => new { x.CompanyId, x.Kind });
        list.Property(x => x.Kind).HasMaxLength(16).IsUnicode(false).UseCollation(binaryCollation);
        list.Property(x => x.Version).IsConcurrencyToken();
        list.Property(x => x.UpdatedBy).HasMaxLength(128);
        list.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);

        var listVersion = modelBuilder.Entity<TaxCodeListVersionRow>();
        listVersion.ToTable("TaxCodeListVersions", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsTaxCodeListVersions_Version", "[ListVersion] > 0");
            table.HasCheckConstraint("CK_SettingsTaxCodeListVersions_Recorded",
                "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
        });
        listVersion.HasKey(x => new { x.CompanyId, x.Kind, x.ListVersion });
        listVersion.Property(x => x.Kind).HasMaxLength(16).IsUnicode(false).UseCollation(binaryCollation);
        listVersion.Property(x => x.RecordedBy).HasMaxLength(128);
        listVersion.Property(x => x.Reason).HasMaxLength(4000);
        listVersion.HasOne<TaxCodeListRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.Kind })
            .OnDelete(DeleteBehavior.Restrict);
        listVersion.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);

        var entry = modelBuilder.Entity<TaxCodeListEntryRow>();
        entry.ToTable("TaxCodeListEntries", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsTaxCodeListEntries_Code",
                "LEN([Code]) BETWEEN 1 AND 16 AND [Code] NOT LIKE '%[^A-Z0-9]%'");
            table.HasCheckConstraint("CK_SettingsTaxCodeListEntries_Name", "LEN(LTRIM(RTRIM([Name]))) > 0");
        });
        entry.HasKey(x => new { x.CompanyId, x.Kind, x.ListVersion, x.Code });
        entry.Property(x => x.Kind).HasMaxLength(16).IsUnicode(false).UseCollation(binaryCollation);
        entry.Property(x => x.Code).HasMaxLength(16).IsUnicode(false).UseCollation(binaryCollation);
        entry.Property(x => x.Name).HasMaxLength(200);
        entry.HasOne<TaxCodeListVersionRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.Kind, x.ListVersion })
            .OnDelete(DeleteBehavior.Restrict);
        entry.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);

        var listWithdrawal = modelBuilder.Entity<TaxCodeListWithdrawalRow>();
        listWithdrawal.ToTable("TaxCodeListWithdrawals", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsTaxCodeListWithdrawals_Replacement",
                "[ReplacedByListVersion] IS NULL OR [ReplacedByListVersion] > [ListVersion]");
            table.HasCheckConstraint("CK_SettingsTaxCodeListWithdrawals_Recorded",
                "LEN(LTRIM(RTRIM([WithdrawnBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
        });
        listWithdrawal.HasKey(x => new { x.CompanyId, x.Kind, x.ListVersion });
        listWithdrawal.Property(x => x.Kind).HasMaxLength(16).IsUnicode(false).UseCollation(binaryCollation);
        listWithdrawal.Property(x => x.WithdrawnBy).HasMaxLength(128);
        listWithdrawal.Property(x => x.Reason).HasMaxLength(4000);
        listWithdrawal.HasOne<TaxCodeListVersionRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.Kind, x.ListVersion })
            .OnDelete(DeleteBehavior.Restrict);
        listWithdrawal.HasOne<TaxCodeListVersionRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.Kind, x.ReplacedByListVersion })
            .OnDelete(DeleteBehavior.Restrict);
        listWithdrawal.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);

        // VUK 232 withholding amount threshold (SAT-D93; Sinan 2026-10-31): dated, starts empty, an entered amount and currency.
        var threshold = modelBuilder.Entity<TaxWithholdingThresholdRow>();
        threshold.ToTable("TaxWithholdingThresholds", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsTaxWithholdingThresholds_Company", $"[CompanyId] <> {emptyGuid}");
            table.HasCheckConstraint("CK_SettingsTaxWithholdingThresholds_Version", "[Version] > 0 AND [LastRecordVersion] > 0");
            table.HasCheckConstraint("CK_SettingsTaxWithholdingThresholds_Actor", "LEN(LTRIM(RTRIM([UpdatedBy]))) > 0");
        });
        threshold.HasKey(x => x.CompanyId);
        threshold.Property(x => x.Version).IsConcurrencyToken();
        threshold.Property(x => x.UpdatedBy).HasMaxLength(128);
        threshold.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);

        var thresholdRecord = modelBuilder.Entity<TaxWithholdingThresholdRecordRow>();
        thresholdRecord.ToTable("TaxWithholdingThresholdRecords", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsTaxWithholdingThresholdRecords_Id", $"[Id] <> {emptyGuid}");
            table.HasCheckConstraint("CK_SettingsTaxWithholdingThresholdRecords_Version", "[RecordVersion] > 0");
            table.HasCheckConstraint("CK_SettingsTaxWithholdingThresholdRecords_Amount", "[Amount] > 0");
            table.HasCheckConstraint("CK_SettingsTaxWithholdingThresholdRecords_Currency",
                "LEN([CurrencyCode]) = 3 AND [CurrencyCode] NOT LIKE '%[^A-Z]%'");
            table.HasCheckConstraint("CK_SettingsTaxWithholdingThresholdRecords_Recorded",
                "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
        });
        thresholdRecord.HasKey(x => new { x.CompanyId, x.Id });
        thresholdRecord.HasIndex(x => new { x.CompanyId, x.RecordVersion }).IsUnique();
        thresholdRecord.Property(x => x.Amount).HasPrecision(19, 4);
        thresholdRecord.Property(x => x.CurrencyCode).HasMaxLength(3).IsUnicode(false).IsFixedLength()
            .UseCollation(binaryCollation);
        thresholdRecord.Property(x => x.RecordedBy).HasMaxLength(128);
        thresholdRecord.Property(x => x.Reason).HasMaxLength(4000);
        thresholdRecord.HasOne<TaxWithholdingThresholdRow>().WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        thresholdRecord.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);

        var thresholdWithdrawal = modelBuilder.Entity<TaxWithholdingThresholdWithdrawalRow>();
        thresholdWithdrawal.ToTable("TaxWithholdingThresholdWithdrawals", Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsTaxWithholdingThresholdWithdrawals_Replacement",
                "[ReplacedByRecordId] IS NULL OR [ReplacedByRecordId] <> [RecordId]");
            table.HasCheckConstraint("CK_SettingsTaxWithholdingThresholdWithdrawals_Recorded",
                "LEN(LTRIM(RTRIM([WithdrawnBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
        });
        thresholdWithdrawal.HasKey(x => new { x.CompanyId, x.RecordId });
        thresholdWithdrawal.Property(x => x.WithdrawnBy).HasMaxLength(128);
        thresholdWithdrawal.Property(x => x.Reason).HasMaxLength(4000);
        thresholdWithdrawal.HasOne<TaxWithholdingThresholdRecordRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.RecordId })
            .OnDelete(DeleteBehavior.Restrict);
        thresholdWithdrawal.HasOne<TaxWithholdingThresholdRecordRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ReplacedByRecordId })
            .OnDelete(DeleteBehavior.Restrict);
        thresholdWithdrawal.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);
    }

    public override int SaveChanges() => SaveChanges(acceptAllChangesOnSuccess: true);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyWriteGuards();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyWriteGuards();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Fail-closed write fence: Manufacturing tolerance policies stay read-only here (their approved command workflow is
    /// separate); nothing is ever deleted; limit records, their withdrawals, binding group rows, tax rate records, code list
    /// versions and their withdrawals are immutable once written; every Settings
    /// row written belongs to this context's resolved company (a multi-company binding write uses one context per company).
    /// </summary>
    private void ApplyWriteGuards()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Unchanged or EntityState.Detached)
            {
                continue;
            }

            if (entry.Entity is ManufacturingTolerancePolicyRow)
            {
                throw new InvalidOperationException(
                    "Manufacturing tolerance policies require the separately approved Settings command workflow.");
            }

            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException("Ayarlar kayıtları silinmez; pasife alınır veya yeni sürüm yazılır.");
            }

            if (entry.State == EntityState.Modified
                && entry.Entity is SalesDiscountLimitVersionRow or SalesDiscountLimitWithdrawalRow
                    or SalesDiscountLimitMembershipGroupRow)
            {
                throw new InvalidOperationException("İskonto limit kaydı ve grup bağı satırı değiştirilemez; yeni sürüm yazılır.");
            }

            if (entry.State == EntityState.Modified
                && entry.Entity is TaxItemTypeDefaultRecordRow or TaxItemTypeDefaultWithdrawalRow or TaxCodeListVersionRow
                    or TaxCodeListEntryRow or TaxCodeListWithdrawalRow or TaxWithholdingThresholdRecordRow
                    or TaxWithholdingThresholdWithdrawalRow)
            {
                throw new InvalidOperationException("Vergi oranı kaydı ve kod listesi sürümü değiştirilemez; yeni kayıt yazılır.");
            }

            var companyId = entry.Entity switch
            {
                SalesDiscountLimitGroupRow row => row.CompanyId,
                SalesDiscountLimitVersionRow row => row.CompanyId,
                SalesDiscountLimitWithdrawalRow row => row.CompanyId,
                SalesDiscountLimitMembershipRow row => row.CompanyId,
                SalesDiscountLimitMembershipGroupRow row => row.CompanyId,
                TaxItemTypeDefaultSeriesRow row => row.CompanyId,
                TaxItemTypeDefaultRecordRow row => row.CompanyId,
                TaxItemTypeDefaultWithdrawalRow row => row.CompanyId,
                TaxCodeListRow row => row.CompanyId,
                TaxCodeListVersionRow row => row.CompanyId,
                TaxCodeListEntryRow row => row.CompanyId,
                TaxCodeListWithdrawalRow row => row.CompanyId,
                TaxWithholdingThresholdRow row => row.CompanyId,
                TaxWithholdingThresholdRecordRow row => row.CompanyId,
                TaxWithholdingThresholdWithdrawalRow row => row.CompanyId,
                _ => (Guid?)null,
            };
            if (companyId is { } owner && (!companyContext.IsResolved || owner != companyContext.CompanyId))
            {
                throw new InvalidOperationException("Ayarlar kaydı yalnız etkin şirket bağlamında yazılır.");
            }
        }
    }
}
