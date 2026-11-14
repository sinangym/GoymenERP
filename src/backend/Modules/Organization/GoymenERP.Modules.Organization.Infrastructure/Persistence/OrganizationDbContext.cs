using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Organization.Domain.Common;
using GoymenERP.Modules.Organization.Domain.Companies;
using GoymenERP.Modules.Organization.Domain.Locations;
using GoymenERP.Modules.Organization.Domain.Sites;
using GoymenERP.Modules.Organization.Domain.WorkCenters;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Idempotency.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence;

public sealed class OrganizationDbContext(
    DbContextOptions<OrganizationDbContext> options,
    ICompanyContext companyContext,
    OrganizationBootstrapWriteScope bootstrapWriteScope)
    : DbContext(options)
{
    public DbSet<Company> Companies => Set<Company>();

    public DbSet<Site> Sites => Set<Site>();

    public DbSet<Location> Locations => Set<Location>();

    public DbSet<WorkCenter> WorkCenters => Set<WorkCenter>();

    public DbSet<WorkCenterCalendar> WorkCenterCalendars => Set<WorkCenterCalendar>();

    public DbSet<WorkCenterCalendarShiftRecord> WorkCenterCalendarShifts => Set<WorkCenterCalendarShiftRecord>();

    public DbSet<WorkCenterCalendarClosedDateRecord> WorkCenterCalendarClosedDates =>
        Set<WorkCenterCalendarClosedDateRecord>();

    public DbSet<CommandExecution> CommandExecutions => Set<CommandExecution>();

    internal const string WorkCenterCalendarVersionCheck = "[Version] >= 1";
    internal const string WorkCenterCalendarShiftRevisionCheck = "[Revision] >= 1";
    internal const string WorkCenterCalendarShiftDayCheck = "[Day] BETWEEN 0 AND 6";
    internal const string WorkCenterCalendarShiftWindowCheck = "[StartTime] < [EndTime]";
    internal const string WorkCenterCalendarClosedDateRevisionCheck = "[Revision] >= 1";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        ConfigureCompany(modelBuilder);
        ConfigureSite(modelBuilder);
        ConfigureLocation(modelBuilder);
        ConfigureWorkCenter(modelBuilder);
        ConfigureWorkCenterCalendar(modelBuilder);
        modelBuilder.ConfigureAuditEvent(excludeFromMigrations: true);
        modelBuilder.Entity<AuditEvent>()
            .HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
        modelBuilder.ConfigureCommandExecution(excludeFromMigrations: true);
        modelBuilder.Entity<CommandExecution>()
            .HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    public override int SaveChanges()
    {
        return SaveChanges(acceptAllChangesOnSuccess: true);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyTenantWriteGuards();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ApplyTenantWriteGuards();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private static void ConfigureCompany(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Company>();
        entity.ToTable("Companies", "organization", table =>
        {
            table.HasCheckConstraint(
                "CK_Companies_Vkn_Format",
                "[Vkn] IS NULL OR (LEN([Vkn]) = 10 AND [Vkn] COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9]%')");
            table.HasCheckConstraint(
                "CK_Companies_Placeholder_InvoiceDisabled",
                "[InvoiceCapable] = 0 OR ([IsPlaceholder] = 0 AND [Vkn] IS NOT NULL)");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Code).HasMaxLength(64).IsRequired();
        entity.Property(x => x.LegalName).HasMaxLength(250).IsRequired();
        entity.Property(x => x.Vkn).HasMaxLength(10);
        entity.Property(x => x.InvoiceCapable).HasDefaultValue(false);
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasIndex(x => x.Code).IsUnique();
    }

    private void ConfigureSite(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Site>();
        ConfigureTenantEntity(entity, "Sites");
        entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
        entity.HasOne<Company>()
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureLocation(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Location>();
        ConfigureTenantEntity(entity, "Locations");
        entity.ToTable("Locations", "organization", table =>
        {
            table.HasCheckConstraint(
                "CK_Locations_InternalOperatorParty",
                "[IsExternal] = 1 OR [OperatorPartyId] IS NULL");
            table.HasCheckConstraint(
                "CK_Locations_OperatorParty_NotEmpty",
                "[OperatorPartyId] IS NULL OR [OperatorPartyId] <> '00000000-0000-0000-0000-000000000000'");
        });
        entity.HasOne<Site>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SiteId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureWorkCenter(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<WorkCenter>();
        ConfigureTenantEntity(entity, "WorkCenters");
        entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32);
        entity.HasOne<Location>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.LocationId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    /// <summary>
    /// B-15: one calendar head per work centre (unique on the company and work centre) and append-only revision rows.
    /// Every table carries the company, its composite foreign keys stay inside the company, and the query filter fences
    /// reads like every other Organization table.
    /// </summary>
    private void ConfigureWorkCenterCalendar(ModelBuilder modelBuilder)
    {
        var calendar = modelBuilder.Entity<WorkCenterCalendar>();
        calendar.ToTable("WorkCenterCalendars", "organization", table =>
            table.HasCheckConstraint("CK_WorkCenterCalendars_Version", WorkCenterCalendarVersionCheck));
        calendar.HasKey(x => x.Id);
        calendar.Property(x => x.Id).ValueGeneratedNever();
        calendar.HasAlternateKey(x => new { x.CompanyId, x.Id });
        calendar.Property(x => x.Version).IsConcurrencyToken();
        calendar.Property(x => x.RowVersion).IsRowVersion();
        calendar.HasIndex(x => new { x.CompanyId, x.WorkCenterId }).IsUnique();
        calendar.HasOne<WorkCenter>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.WorkCenterId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        calendar.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var shift = modelBuilder.Entity<WorkCenterCalendarShiftRecord>();
        shift.ToTable("WorkCenterCalendarShifts", "organization", table =>
        {
            table.HasCheckConstraint("CK_WorkCenterCalendarShifts_Revision", WorkCenterCalendarShiftRevisionCheck);
            table.HasCheckConstraint("CK_WorkCenterCalendarShifts_Day", WorkCenterCalendarShiftDayCheck);
            table.HasCheckConstraint("CK_WorkCenterCalendarShifts_Window", WorkCenterCalendarShiftWindowCheck);
        });
        shift.HasKey(x => x.Id);
        shift.Property(x => x.Id).ValueGeneratedNever();
        shift.Property(x => x.StartTime).HasColumnType("time(0)");
        shift.Property(x => x.EndTime).HasColumnType("time(0)");
        shift.HasIndex(x => new { x.CompanyId, x.CalendarId, x.Revision, x.Day, x.StartTime }).IsUnique();
        shift.HasOne<WorkCenterCalendar>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.CalendarId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        shift.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var closedDate = modelBuilder.Entity<WorkCenterCalendarClosedDateRecord>();
        closedDate.ToTable("WorkCenterCalendarClosedDates", "organization", table =>
            table.HasCheckConstraint(
                "CK_WorkCenterCalendarClosedDates_Revision",
                WorkCenterCalendarClosedDateRevisionCheck));
        closedDate.HasKey(x => x.Id);
        closedDate.Property(x => x.Id).ValueGeneratedNever();
        closedDate.HasIndex(x => new { x.CompanyId, x.CalendarId, x.Revision, x.Date }).IsUnique();
        closedDate.HasOne<WorkCenterCalendar>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.CalendarId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        closedDate.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private static void ConfigureTenantEntity<TEntity>(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> entity,
        string tableName)
        where TEntity : TenantEntity
    {
        entity.ToTable(tableName, "organization");
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.CompanyId, x.Id });
        entity.Property(x => x.Code).HasMaxLength(64).IsRequired();
        entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        entity.HasIndex(x => new { x.CompanyId, x.IsActive });
    }

    private void ApplyTenantWriteGuards()
    {
        OrganizationAuditEventTenantGuard.Validate(this, companyContext);

        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<TenantEntity>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (entry.Entity.CompanyId == Guid.Empty)
            {
                throw new InvalidOperationException("Kritik organizasyon kaydı CompanyId olmadan yazılamaz.");
            }

            if (entry.State == EntityState.Modified
                && entry.Property(x => x.CompanyId).IsModified)
            {
                throw new InvalidOperationException("Bir organizasyon kaydının CompanyId değeri değiştirilemez.");
            }

            if (!bootstrapWriteScope.IsActive)
            {
                if (!companyContext.IsResolved)
                {
                    throw new InvalidOperationException(
                        "Organizasyon yazma işlemi için aktif şirket bağlamı zorunludur.");
                }

                if (entry.Entity.CompanyId != companyContext.CompanyId)
                {
                    throw new InvalidOperationException(
                        "Organizasyon kaydının CompanyId değeri aktif şirket bağlamıyla eşleşmelidir.");
                }
            }

            if (entry.State == EntityState.Modified)
            {
                entry.Entity.Touch(now);
            }
        }

        foreach (var entry in ChangeTracker.Entries<ICompanyOwnedRecord>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (entry.State == EntityState.Deleted
                || (entry.State == EntityState.Modified && entry.Entity is not WorkCenterCalendar))
            {
                // A calendar head is never deleted and a revision row is append-only: a change is a new revision.
                throw new InvalidOperationException(
                    "İş merkezi takvimi kayıtları silinemez; takvim satırları değiştirilemez.");
            }

            if (entry.Entity.CompanyId == Guid.Empty)
            {
                throw new InvalidOperationException("Kritik organizasyon kaydı CompanyId olmadan yazılamaz.");
            }

            if (entry.State == EntityState.Modified
                && entry.Property(nameof(ICompanyOwnedRecord.CompanyId)).IsModified)
            {
                throw new InvalidOperationException("Bir organizasyon kaydının CompanyId değeri değiştirilemez.");
            }

            if (!bootstrapWriteScope.IsActive)
            {
                if (!companyContext.IsResolved)
                {
                    throw new InvalidOperationException(
                        "Organizasyon yazma işlemi için aktif şirket bağlamı zorunludur.");
                }

                if (entry.Entity.CompanyId != companyContext.CompanyId)
                {
                    throw new InvalidOperationException(
                        "Organizasyon kaydının CompanyId değeri aktif şirket bağlamıyla eşleşmelidir.");
                }
            }
        }


        foreach (var entry in ChangeTracker.Entries<CommandExecution>())
        {
            if (entry.State == EntityState.Unchanged)
            {
                continue;
            }

            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "Idempotency kayıtları değiştirilemez veya silinemez.");
            }

            if (!companyContext.IsResolved || entry.Entity.CompanyId != companyContext.CompanyId)
            {
                throw new InvalidOperationException(
                    "Idempotency kaydı aktif şirket bağlamıyla eşleşmelidir.");
            }
        }
    }
}
