using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// API-01/02 (Üretim U1-a; URT-D6, D7, D9, D10): the Manufacturing parameter profile registry (header + typed value rows) and
/// the tolerance profile lifecycle, company-scoped with the company inside every key and foreign key. Published rows never
/// change (the guard triggers created by <c>AddManufacturingParameterProfiles</c>); EF is told about every trigger so it
/// writes without an OUTPUT clause.
/// </summary>
public sealed partial class SettingsDbContext
{
    internal const string ParameterProfileGuardTrigger = "TR_ManufacturingParameterProfiles_Guard";
    internal const string ParameterValueGuardTrigger = "TR_ManufacturingParameterProfileValues_InsertOnly";
    internal const string ToleranceVersionGuardTrigger = "TR_ManufacturingToleranceProfileVersions_Guard";
    internal const string TolerancePolicyTrigger = "TR_ManufacturingTolerancePolicies_NoOverlap";

    internal DbSet<ManufacturingParameterProfileRow> ManufacturingParameterProfiles => Set<ManufacturingParameterProfileRow>();

    internal DbSet<ManufacturingParameterProfileValueRow> ManufacturingParameterProfileValues =>
        Set<ManufacturingParameterProfileValueRow>();

    internal DbSet<ManufacturingToleranceProfileVersionRow> ManufacturingToleranceProfileVersions =>
        Set<ManufacturingToleranceProfileVersionRow>();

    private void ConfigureManufacturingProfiles(ModelBuilder modelBuilder)
    {
        const string emptyGuid = "'00000000-0000-0000-0000-000000000000'";
        const string processes = "('Slitting','ColdRolling','TubeProfile','Paint','CutToLength')";
        const string binaryCollation = "Latin1_General_100_BIN2";

        // The published registry (20261010095724) gains URT-D6's mass-balance tolerance and the trigger declaration.
        var policy = modelBuilder.Entity<ManufacturingTolerancePolicyRow>();
        policy.ToTable(table =>
        {
            table.HasTrigger(TolerancePolicyTrigger);
            table.HasCheckConstraint("CK_SettingsMfgTolerance_MassBalance",
                "[MassBalanceDifferenceTolerancePercent] IS NULL OR ([MassBalanceDifferenceTolerancePercent] >= 0 "
                + "AND [MassBalanceDifferenceTolerancePercent] <= 100)");
        });
        policy.Property(x => x.MassBalanceDifferenceTolerancePercent).HasPrecision(9, 6);

        var profile = modelBuilder.Entity<ManufacturingParameterProfileRow>();
        profile.ToTable("ManufacturingParameterProfiles", Schema, table =>
        {
            table.HasTrigger(ParameterProfileGuardTrigger);
            table.HasCheckConstraint("CK_SettingsMfgParameterProfiles_Company", $"[CompanyId] <> {emptyGuid}");
            table.HasCheckConstraint("CK_SettingsMfgParameterProfiles_Profile", $"[ProfileId] <> {emptyGuid}");
            table.HasCheckConstraint("CK_SettingsMfgParameterProfiles_WorkCenter",
                $"[WorkCenterId] IS NULL OR [WorkCenterId] <> {emptyGuid}");
            table.HasCheckConstraint("CK_SettingsMfgParameterProfiles_Process", $"[Process] IN {processes}");
            table.HasCheckConstraint("CK_SettingsMfgParameterProfiles_Versions", "[ProfileVersion] > 0 AND [StateVersion] > 0");
            table.HasCheckConstraint("CK_SettingsMfgParameterProfiles_Window",
                "[EffectiveThrough] IS NULL OR [EffectiveThrough] >= [EffectiveFrom]");
            table.HasCheckConstraint("CK_SettingsMfgParameterProfiles_Lifecycle", LifecycleCheck);
        });
        profile.HasKey(x => new { x.CompanyId, x.ProfileId });
        profile.HasIndex(x => new { x.CompanyId, x.Process, x.WorkCenterId, x.ProfileVersion }).IsUnique().HasFilter(null);
        profile.HasIndex(x => new { x.CompanyId, x.Process, x.WorkCenterId, x.Status, x.EffectiveFrom });
        profile.Property(x => x.Process).HasMaxLength(32).IsUnicode(false);
        ConfigureLifecycleColumns(profile);
        profile.HasQueryFilter(x => CompanyContext.IsResolved && x.CompanyId == CompanyContext.CompanyId);

        var value = modelBuilder.Entity<ManufacturingParameterProfileValueRow>();
        value.ToTable("ManufacturingParameterProfileValues", Schema, table =>
        {
            table.HasTrigger(ParameterValueGuardTrigger);
            table.HasCheckConstraint("CK_SettingsMfgParameterValues_Company", $"[CompanyId] <> {emptyGuid}");
            table.HasCheckConstraint("CK_SettingsMfgParameterValues_Key",
                "LEN([ParameterKey]) BETWEEN 1 AND 64 AND [ParameterKey] NOT LIKE '%[^A-Za-z0-9]%'");
            table.HasCheckConstraint("CK_SettingsMfgParameterValues_Unit", "LEN(LTRIM(RTRIM([Unit]))) > 0");
            table.HasCheckConstraint("CK_SettingsMfgParameterValues_Shape",
                "([NumericValue] IS NOT NULL AND [ChoiceValue] IS NULL) OR "
                + "([NumericValue] IS NULL AND [ChoiceValue] IS NOT NULL AND LEN([ChoiceValue]) > 0)");
        });
        value.HasKey(x => new { x.CompanyId, x.ProfileId, x.ParameterKey });
        value.Property(x => x.ParameterKey).HasMaxLength(64).IsUnicode(false).UseCollation(binaryCollation);
        value.Property(x => x.Unit).HasMaxLength(16).IsUnicode(false);
        value.Property(x => x.NumericValue).HasPrecision(18, 6);
        value.Property(x => x.ChoiceValue).HasMaxLength(32).IsUnicode(false);
        value.HasOne<ManufacturingParameterProfileRow>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ProfileId })
            .OnDelete(DeleteBehavior.Restrict);
        value.HasQueryFilter(x => CompanyContext.IsResolved && x.CompanyId == CompanyContext.CompanyId);

        var tolerance = modelBuilder.Entity<ManufacturingToleranceProfileVersionRow>();
        tolerance.ToTable("ManufacturingToleranceProfileVersions", Schema, table =>
        {
            table.HasTrigger(ToleranceVersionGuardTrigger);
            table.HasCheckConstraint("CK_SettingsMfgToleranceVersions_Company", $"[CompanyId] <> {emptyGuid}");
            table.HasCheckConstraint("CK_SettingsMfgToleranceVersions_Profile", $"[ProfileId] <> {emptyGuid}");
            table.HasCheckConstraint("CK_SettingsMfgToleranceVersions_Process", $"[Process] IN {processes}");
            table.HasCheckConstraint("CK_SettingsMfgToleranceVersions_Versions", "[ProfileVersion] > 0 AND [StateVersion] > 0");
            table.HasCheckConstraint("CK_SettingsMfgToleranceVersions_Percents",
                "[MaximumFirePercent] >= 0 AND [MaximumFirePercent] <= 100 "
                + "AND [MassBalanceDifferenceTolerancePercent] >= 0 AND [MassBalanceDifferenceTolerancePercent] <= 100");
            table.HasCheckConstraint("CK_SettingsMfgToleranceVersions_Window",
                "[EffectiveThrough] IS NULL OR [EffectiveThrough] >= [EffectiveFrom]");
            table.HasCheckConstraint("CK_SettingsMfgToleranceVersions_Lifecycle", LifecycleCheck);
        });
        tolerance.HasKey(x => new { x.CompanyId, x.ProfileId });
        tolerance.HasIndex(x => new { x.CompanyId, x.Process, x.ProfileVersion }).IsUnique();
        tolerance.Property(x => x.Process).HasMaxLength(32).IsUnicode(false);
        tolerance.Property(x => x.MaximumFirePercent).HasPrecision(9, 6);
        tolerance.Property(x => x.MassBalanceDifferenceTolerancePercent).HasPrecision(9, 6);
        ConfigureLifecycleColumns(tolerance);
        tolerance.HasQueryFilter(x => CompanyContext.IsResolved && x.CompanyId == CompanyContext.CompanyId);
    }

    /// <summary>
    /// A registry row may only be ADDED, and only beside its own lifecycle version (same company and profile id) moving from
    /// Approved to Published in the same save; any other write of the registry is refused before SQL.
    /// </summary>
    private bool IsToleranceRegistryPublication(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, ManufacturingTolerancePolicyRow policy) =>
        entry.State == EntityState.Added
        && ChangeTracker.Entries<ManufacturingToleranceProfileVersionRow>().Any(version =>
            version.State == EntityState.Modified
            && version.Entity.CompanyId == policy.CompanyId && version.Entity.ProfileId == policy.ProfileId
            && version.Entity.ProfileVersion == policy.Version
            && version.Entity.Status == ManufacturingProfileStatusesV1.Published
            && Equals(version.Property(row => row.Status).OriginalValue, ManufacturingProfileStatusesV1.Approved));

    /// <summary>Values are insert-only; a published header or tolerance version never changes (the DB triggers agree).</summary>
    private static void ApplyManufacturingProfileWriteGuard(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
    {
        if (entry.State != EntityState.Modified)
        {
            return;
        }

        var published = entry.Entity switch
        {
            ManufacturingParameterProfileValueRow => true,
            ManufacturingParameterProfileRow or ManufacturingToleranceProfileVersionRow =>
                Equals(entry.Property("Status").OriginalValue, ManufacturingProfileStatusesV1.Published),
            _ => false,
        };
        if (published)
        {
            throw new InvalidOperationException("Yayımlanmış üretim profili ve değerleri değiştirilemez; yeni sürüm hazırlanır.");
        }
    }

    /// <summary>
    /// The lifecycle shape of D-MFG-PROFILE-AUTH-01=A as data: a Draft carries no approval or publication, an Approved row an
    /// approval by someone other than the preparer, a Published row both, each by someone other than the preparer.
    /// </summary>
    private const string LifecycleCheck =
        "LEN(LTRIM(RTRIM([PreparedBy]))) > 0 AND LEN(LTRIM(RTRIM([PreparationReason]))) > 0 AND ("
        + "([Status] = 'Draft' AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL AND [ApprovalReason] IS NULL "
        + "AND [PublishedBy] IS NULL AND [PublishedAtUtc] IS NULL AND [PublicationReason] IS NULL) OR "
        + "([Status] = 'Approved' AND LEN(LTRIM(RTRIM([ApprovedBy]))) > 0 AND [ApprovedBy] <> [PreparedBy] "
        + "AND [ApprovedAtUtc] IS NOT NULL AND LEN(LTRIM(RTRIM([ApprovalReason]))) > 0 "
        + "AND [PublishedBy] IS NULL AND [PublishedAtUtc] IS NULL AND [PublicationReason] IS NULL) OR "
        + "([Status] = 'Published' AND LEN(LTRIM(RTRIM([ApprovedBy]))) > 0 AND [ApprovedBy] <> [PreparedBy] "
        + "AND [ApprovedAtUtc] IS NOT NULL AND LEN(LTRIM(RTRIM([ApprovalReason]))) > 0 "
        + "AND LEN(LTRIM(RTRIM([PublishedBy]))) > 0 AND [PublishedBy] <> [PreparedBy] "
        + "AND [PublishedAtUtc] IS NOT NULL AND LEN(LTRIM(RTRIM([PublicationReason]))) > 0))";

    private static void ConfigureLifecycleColumns<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> entity)
        where T : class
    {
        entity.Property<long>("StateVersion").IsConcurrencyToken();
        entity.Property<string>("Status").HasMaxLength(16).IsUnicode(false);
        entity.Property<string>("PreparedBy").HasMaxLength(200);
        entity.Property<string>("PreparationReason").HasMaxLength(500);
        entity.Property<string?>("ApprovedBy").HasMaxLength(200);
        entity.Property<string?>("ApprovalReason").HasMaxLength(500);
        entity.Property<string?>("PublishedBy").HasMaxLength(200);
        entity.Property<string?>("PublicationReason").HasMaxLength(500);
    }
}
