using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Idempotency.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

public sealed class SettingsDbContext(DbContextOptions<SettingsDbContext> options, ICompanyContext companyContext)
    : DbContext(options)
{
    public const string Schema = "settings";
    internal DbSet<ManufacturingTolerancePolicyRow> ManufacturingTolerancePolicies =>
        Set<ManufacturingTolerancePolicyRow>();
    internal DbSet<ManufacturingToleranceProfileLifecycleRow> ManufacturingToleranceProfileLifecycles =>
        Set<ManufacturingToleranceProfileLifecycleRow>();
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

        modelBuilder.ApplyConfiguration(new ManufacturingToleranceProfileLifecycleConfiguration());
        modelBuilder.Entity<ManufacturingToleranceProfileLifecycleRow>()
            .HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);
        modelBuilder.ConfigureAuditEvent(excludeFromMigrations: true);
        modelBuilder.Entity<AuditEvent>()
            .HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);
        modelBuilder.ConfigureCommandExecution(excludeFromMigrations: true);
        modelBuilder.Entity<CommandExecution>()
            .HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId);
    }

    public override int SaveChanges() => throw ReadOnly();
    public override int SaveChanges(bool acceptAllChangesOnSuccess) => throw ReadOnly();
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => throw ReadOnly();
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) => throw ReadOnly();

    internal Task<int> SaveLifecycleCommandChangesAsync(CancellationToken cancellationToken)
    {
        ApplyLifecycleCommandWriteGuards();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);
    }

    private void ApplyLifecycleCommandWriteGuards()
    {
        SettingsAuditEventTenantGuard.Validate(this, companyContext);

        var unexpected = ChangeTracker.Entries()
            .FirstOrDefault(entry => entry.State != EntityState.Unchanged
                && entry.State != EntityState.Detached
                && entry.Entity is not ManufacturingToleranceProfileLifecycleRow
                && entry.Entity is not CommandExecution
                && entry.Entity is not AuditEvent);
        if (unexpected is not null)
        {
            throw new InvalidOperationException(
                "Settings lifecycle command yolu yalnız lifecycle, idempotency ve audit kayıtlarını yazabilir.");
        }

        foreach (var entry in ChangeTracker.Entries<CommandExecution>())
        {
            if (entry.State == EntityState.Unchanged)
            {
                continue;
            }

            if (entry.State != EntityState.Added
                || !companyContext.IsResolved
                || entry.Entity.CompanyId != companyContext.CompanyId)
            {
                throw new InvalidOperationException(
                    "Settings idempotency kaydı yalnız aktif şirkete append edilebilir.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<ManufacturingToleranceProfileLifecycleRow>())
        {
            if (entry.State == EntityState.Unchanged)
            {
                continue;
            }

            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "Manufacturing tolerance lifecycle kaydı fiziksel olarak silinemez.");
            }

            if (!companyContext.IsResolved
                || entry.Entity.CompanyId == Guid.Empty
                || entry.Entity.CompanyId != companyContext.CompanyId)
            {
                throw new InvalidOperationException(
                    "Manufacturing tolerance lifecycle yazması aktif şirket bağlamıyla eşleşmelidir.");
            }

            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.Status != ManufacturingToleranceProfileStatusV1.Draft
                    || entry.Entity.StateVersion != 1)
                {
                    throw new InvalidOperationException(
                        "Yeni tolerance lifecycle kaydı yalnız ilk Draft durumu olarak eklenebilir.");
                }

                continue;
            }

            ValidateLifecycleUpdate(entry);
        }
    }

    private static void ValidateLifecycleUpdate(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<ManufacturingToleranceProfileLifecycleRow> entry)
    {
        var previousStatus = entry.Property(x => x.Status).OriginalValue;
        var currentStatus = entry.Entity.Status;
        var previousVersion = entry.Property(x => x.StateVersion).OriginalValue;
        if (previousStatus == ManufacturingToleranceProfileStatusV1.Published
            || previousVersion == long.MaxValue
            || entry.Entity.StateVersion != previousVersion + 1)
        {
            throw new InvalidOperationException(
                "Manufacturing tolerance lifecycle geçişi geçersiz veya Published kayıt immutable.");
        }

        var allowed = (previousStatus, currentStatus) switch
        {
            (ManufacturingToleranceProfileStatusV1.Draft, ManufacturingToleranceProfileStatusV1.Draft) =>
                DraftRevisionProperties,
            (ManufacturingToleranceProfileStatusV1.Draft, ManufacturingToleranceProfileStatusV1.Approved) =>
                ApprovalProperties,
            (ManufacturingToleranceProfileStatusV1.Approved, ManufacturingToleranceProfileStatusV1.Published) =>
                PublicationProperties,
            _ => [],
        };
        var modified = entry.Properties.Where(property => property.IsModified)
            .Select(property => property.Metadata.Name)
            .ToArray();
        if (allowed.Count == 0 || modified.Any(name => !allowed.Contains(name, StringComparer.Ordinal)))
        {
            throw new InvalidOperationException(
                "Manufacturing tolerance lifecycle yalnız onaylı geçiş alanlarını değiştirebilir.");
        }
    }

    private static readonly IReadOnlyList<string> DraftRevisionProperties =
    [
        nameof(ManufacturingToleranceProfileLifecycleRow.MaximumFirePercent),
        nameof(ManufacturingToleranceProfileLifecycleRow.EffectiveFrom),
        nameof(ManufacturingToleranceProfileLifecycleRow.EffectiveThrough),
        nameof(ManufacturingToleranceProfileLifecycleRow.StateVersion),
        nameof(ManufacturingToleranceProfileLifecycleRow.PreparedBy),
        nameof(ManufacturingToleranceProfileLifecycleRow.PreparedAtUtc),
        nameof(ManufacturingToleranceProfileLifecycleRow.PreparationReason),
        nameof(ManufacturingToleranceProfileLifecycleRow.PreparationCorrelationId),
        nameof(ManufacturingToleranceProfileLifecycleRow.PreparationCausationId),
        nameof(ManufacturingToleranceProfileLifecycleRow.PreparationSourceChannel),
        nameof(ManufacturingToleranceProfileLifecycleRow.PreparationClientIpAddress),
        nameof(ManufacturingToleranceProfileLifecycleRow.PreparationUserAgent),
    ];

    private static readonly IReadOnlyList<string> ApprovalProperties =
    [
        nameof(ManufacturingToleranceProfileLifecycleRow.StateVersion),
        nameof(ManufacturingToleranceProfileLifecycleRow.Status),
        nameof(ManufacturingToleranceProfileLifecycleRow.ApprovedBy),
        nameof(ManufacturingToleranceProfileLifecycleRow.ApprovedAtUtc),
        nameof(ManufacturingToleranceProfileLifecycleRow.ApprovalReason),
        nameof(ManufacturingToleranceProfileLifecycleRow.ApprovalCorrelationId),
        nameof(ManufacturingToleranceProfileLifecycleRow.ApprovalCausationId),
        nameof(ManufacturingToleranceProfileLifecycleRow.ApprovalSourceChannel),
        nameof(ManufacturingToleranceProfileLifecycleRow.ApprovalClientIpAddress),
        nameof(ManufacturingToleranceProfileLifecycleRow.ApprovalUserAgent),
    ];

    private static readonly IReadOnlyList<string> PublicationProperties =
    [
        nameof(ManufacturingToleranceProfileLifecycleRow.StateVersion),
        nameof(ManufacturingToleranceProfileLifecycleRow.Status),
        nameof(ManufacturingToleranceProfileLifecycleRow.PublishedBy),
        nameof(ManufacturingToleranceProfileLifecycleRow.PublishedAtUtc),
        nameof(ManufacturingToleranceProfileLifecycleRow.PublicationReason),
        nameof(ManufacturingToleranceProfileLifecycleRow.PublicationCorrelationId),
        nameof(ManufacturingToleranceProfileLifecycleRow.PublicationCausationId),
        nameof(ManufacturingToleranceProfileLifecycleRow.PublicationSourceChannel),
        nameof(ManufacturingToleranceProfileLifecycleRow.PublicationClientIpAddress),
        nameof(ManufacturingToleranceProfileLifecycleRow.PublicationUserAgent),
    ];

    private static InvalidOperationException ReadOnly() => new InvalidOperationException(
        "Manufacturing tolerance policies require the separately approved Settings command workflow.");
}
