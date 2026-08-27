using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Organization.Domain.Common;
using GoymenERP.Modules.Organization.Domain.Companies;
using GoymenERP.Modules.Organization.Domain.Locations;
using GoymenERP.Modules.Organization.Domain.Sites;
using GoymenERP.Modules.Organization.Domain.WorkCenters;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence;

public sealed class OrganizationDbContext(
    DbContextOptions<OrganizationDbContext> options,
    ICompanyContext companyContext)
    : DbContext(options)
{
    public DbSet<Company> Companies => Set<Company>();

    public DbSet<Site> Sites => Set<Site>();

    public DbSet<Location> Locations => Set<Location>();

    public DbSet<WorkCenter> WorkCenters => Set<WorkCenter>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        ConfigureCompany(modelBuilder);
        ConfigureSite(modelBuilder);
        ConfigureLocation(modelBuilder);
        ConfigureWorkCenter(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        ApplyTenantWriteGuards();
        return base.SaveChangesAsync(cancellationToken);
    }

    private static void ConfigureCompany(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Company>();
        entity.ToTable("Companies", "organization");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Code).HasMaxLength(64).IsRequired();
        entity.Property(x => x.LegalName).HasMaxLength(250).IsRequired();
        entity.Property(x => x.Version).IsConcurrencyToken();
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
        entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        entity.HasIndex(x => new { x.CompanyId, x.IsActive });
    }

    private void ApplyTenantWriteGuards()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<TenantEntity>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
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

            if (entry.State == EntityState.Modified)
            {
                entry.Entity.Touch(now);
            }
        }
    }
}
