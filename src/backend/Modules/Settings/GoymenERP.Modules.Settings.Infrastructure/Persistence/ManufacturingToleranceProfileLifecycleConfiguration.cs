using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

internal sealed class ManufacturingToleranceProfileLifecycleConfiguration
    : IEntityTypeConfiguration<ManufacturingToleranceProfileLifecycleRow>
{
    public const string Table = "ManufacturingToleranceProfileLifecycles";
    public const string PublicationWindowIndex = "IX_SettingsMfgToleranceLifecycle_PublicationWindow";
    public const string IdentityCollation = "Latin1_General_100_BIN2";

    public void Configure(EntityTypeBuilder<ManufacturingToleranceProfileLifecycleRow> entity)
    {
        entity.ToTable(Table, SettingsDbContext.Schema, table =>
        {
            table.HasCheckConstraint("CK_SettingsMfgToleranceLifecycle_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [ProfileId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_SettingsMfgToleranceLifecycle_Process",
                "[Process] IN ('Slitting','ColdRolling','TubeProfile','Paint','CutToLength')");
            table.HasCheckConstraint("CK_SettingsMfgToleranceLifecycle_Versions",
                "[ProfileVersion] > 0 AND [StateVersion] > 0");
            table.HasCheckConstraint("CK_SettingsMfgToleranceLifecycle_Percent",
                "[MaximumFirePercent] >= 0");
            table.HasCheckConstraint("CK_SettingsMfgToleranceLifecycle_Window",
                "[EffectiveThrough] IS NULL OR [EffectiveThrough] >= [EffectiveFrom]");
            table.HasCheckConstraint("CK_SettingsMfgToleranceLifecycle_Status",
                "[Status] IN (1,2,3)");
            table.HasCheckConstraint("CK_SettingsMfgToleranceLifecycle_Separation",
                "([ApprovedBy] IS NULL OR [ApprovedBy] <> [PreparedBy]) AND ([PublishedBy] IS NULL OR [PublishedBy] <> [PreparedBy])");
            table.HasCheckConstraint("CK_SettingsMfgToleranceLifecycle_Approval",
                "([Status] = 1 AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL AND [ApprovalReason] IS NULL AND [ApprovalCorrelationId] IS NULL AND [ApprovalCausationId] IS NULL AND [ApprovalSourceChannel] IS NULL AND [ApprovalClientIpAddress] IS NULL AND [ApprovalUserAgent] IS NULL) OR ([Status] IN (2,3) AND [ApprovedBy] IS NOT NULL AND [ApprovedAtUtc] IS NOT NULL AND [ApprovalReason] IS NOT NULL AND [ApprovalCorrelationId] IS NOT NULL AND [ApprovalSourceChannel] IS NOT NULL)");
            table.HasCheckConstraint("CK_SettingsMfgToleranceLifecycle_Publication",
                "([Status] IN (1,2) AND [PublishedBy] IS NULL AND [PublishedAtUtc] IS NULL AND [PublicationReason] IS NULL AND [PublicationCorrelationId] IS NULL AND [PublicationCausationId] IS NULL AND [PublicationSourceChannel] IS NULL AND [PublicationClientIpAddress] IS NULL AND [PublicationUserAgent] IS NULL) OR ([Status] = 3 AND [PublishedBy] IS NOT NULL AND [PublishedAtUtc] IS NOT NULL AND [PublicationReason] IS NOT NULL AND [PublicationCorrelationId] IS NOT NULL AND [PublicationSourceChannel] IS NOT NULL)");
        });

        entity.HasKey(x => new { x.CompanyId, x.ProfileId });
        entity.HasIndex(x => new { x.CompanyId, x.Process, x.ProfileVersion }).IsUnique();
        entity.HasIndex(x => new { x.CompanyId, x.Process, x.Status, x.EffectiveFrom, x.EffectiveThrough })
            .HasDatabaseName(PublicationWindowIndex);
        entity.Property(x => x.Process).HasMaxLength(32).IsUnicode(false).IsRequired();
        entity.Property(x => x.MaximumFirePercent).HasPrecision(9, 6);
        entity.Property(x => x.StateVersion).IsConcurrencyToken();
        entity.Property(x => x.Status).HasConversion<int>();
        ConfigureRequiredEvidence(entity, nameof(ManufacturingToleranceProfileLifecycleRow.PreparedBy), 200,
            binary: true);
        ConfigureRequiredEvidence(entity, nameof(ManufacturingToleranceProfileLifecycleRow.PreparationReason), 500);
        ConfigureRequiredEvidence(entity, nameof(ManufacturingToleranceProfileLifecycleRow.PreparationCorrelationId), 128);
        ConfigureRequiredEvidence(entity, nameof(ManufacturingToleranceProfileLifecycleRow.PreparationSourceChannel), 32);
        ConfigureOptionalEvidence(entity, nameof(ManufacturingToleranceProfileLifecycleRow.PreparationCausationId), 128);
        ConfigureOptionalEvidence(entity, nameof(ManufacturingToleranceProfileLifecycleRow.PreparationClientIpAddress), 64);
        ConfigureOptionalEvidence(entity, nameof(ManufacturingToleranceProfileLifecycleRow.PreparationUserAgent), 512);
        ConfigureOptionalEvidence(entity, nameof(ManufacturingToleranceProfileLifecycleRow.ApprovedBy), 200,
            binary: true);
        ConfigureOptionalEvidence(entity, nameof(ManufacturingToleranceProfileLifecycleRow.ApprovalReason), 500);
        ConfigureOptionalEvidence(entity, nameof(ManufacturingToleranceProfileLifecycleRow.ApprovalCorrelationId), 128);
        ConfigureOptionalEvidence(entity, nameof(ManufacturingToleranceProfileLifecycleRow.ApprovalCausationId), 128);
        ConfigureOptionalEvidence(entity, nameof(ManufacturingToleranceProfileLifecycleRow.ApprovalSourceChannel), 32);
        ConfigureOptionalEvidence(entity, nameof(ManufacturingToleranceProfileLifecycleRow.ApprovalClientIpAddress), 64);
        ConfigureOptionalEvidence(entity, nameof(ManufacturingToleranceProfileLifecycleRow.ApprovalUserAgent), 512);
        ConfigureOptionalEvidence(entity, nameof(ManufacturingToleranceProfileLifecycleRow.PublishedBy), 200,
            binary: true);
        ConfigureOptionalEvidence(entity, nameof(ManufacturingToleranceProfileLifecycleRow.PublicationReason), 500);
        ConfigureOptionalEvidence(entity, nameof(ManufacturingToleranceProfileLifecycleRow.PublicationCorrelationId), 128);
        ConfigureOptionalEvidence(entity, nameof(ManufacturingToleranceProfileLifecycleRow.PublicationCausationId), 128);
        ConfigureOptionalEvidence(entity, nameof(ManufacturingToleranceProfileLifecycleRow.PublicationSourceChannel), 32);
        ConfigureOptionalEvidence(entity, nameof(ManufacturingToleranceProfileLifecycleRow.PublicationClientIpAddress), 64);
        ConfigureOptionalEvidence(entity, nameof(ManufacturingToleranceProfileLifecycleRow.PublicationUserAgent), 512);
    }

    private static void ConfigureRequiredEvidence(
        EntityTypeBuilder<ManufacturingToleranceProfileLifecycleRow> entity,
        string property,
        int maximumLength,
        bool binary = false)
    {
        var builder = entity.Property<string>(property).HasMaxLength(maximumLength).IsRequired();
        if (binary)
        {
            builder.UseCollation(IdentityCollation);
        }
    }

    private static void ConfigureOptionalEvidence(
        EntityTypeBuilder<ManufacturingToleranceProfileLifecycleRow> entity,
        string property,
        int maximumLength,
        bool binary = false)
    {
        var builder = entity.Property<string?>(property).HasMaxLength(maximumLength);
        if (binary)
        {
            builder.UseCollation(IdentityCollation);
        }
    }
}
