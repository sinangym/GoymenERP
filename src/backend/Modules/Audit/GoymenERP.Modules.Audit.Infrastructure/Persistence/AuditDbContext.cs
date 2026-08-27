using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Audit.Infrastructure.Persistence;

public sealed class AuditDbContext(
    DbContextOptions<AuditDbContext> options,
    ICompanyContext companyContext)
    : DbContext(options)
{
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ConfigureAuditEvent(excludeFromMigrations: false);
        modelBuilder.Entity<AuditEvent>()
            .HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    public override int SaveChanges()
    {
        throw AppendOnlyException();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        throw AppendOnlyException();
    }

    public override Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        throw AppendOnlyException();
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        throw AppendOnlyException();
    }

    private static InvalidOperationException AppendOnlyException()
    {
        return new InvalidOperationException(
            "Audit kayıtları normal uygulama erişimiyle eklenemez, değiştirilemez veya silinemez.");
    }
}
