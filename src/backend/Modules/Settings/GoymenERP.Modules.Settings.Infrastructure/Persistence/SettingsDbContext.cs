using GoymenERP.BuildingBlocks.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

public sealed class SettingsDbContext(DbContextOptions<SettingsDbContext> options, ICompanyContext companyContext)
    : DbContext(options)
{
    public const string Schema = "settings";
    internal DbSet<ManufacturingTolerancePolicyRow> ManufacturingTolerancePolicies =>
        Set<ManufacturingTolerancePolicyRow>();

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
    }

    public override int SaveChanges() => throw ReadOnly();
    public override int SaveChanges(bool acceptAllChangesOnSuccess) => throw ReadOnly();
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => throw ReadOnly();
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) => throw ReadOnly();

    private static InvalidOperationException ReadOnly() => new InvalidOperationException(
        "Manufacturing tolerance policies require the separately approved Settings command workflow.");
}
