using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.SecurityEvents.Domain.Events;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.SecurityEvents.Infrastructure.Persistence;

/// <summary>
/// Append-only security event store. Normal SaveChanges is refused; the module writer appends through
/// <see cref="AppendAsync"/>. Reads see only company-scoped rows of the resolved company; platform rows
/// (CompanyId null) are invisible in a company context.
/// </summary>
public sealed class SecurityEventsDbContext(
    DbContextOptions<SecurityEventsDbContext> options,
    ICompanyContext companyContext)
    : DbContext(options)
{
    private Guid QueryCompanyId => companyContext.CompanyId;

    public DbSet<SecurityEventRecord> SecurityEvents => Set<SecurityEventRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ConfigureSecurityEvent(excludeFromMigrations: false);
        modelBuilder.Entity<SecurityEventRecord>()
            .HasQueryFilter(x => x.ScopeKind == SecurityEventScopeKindsV1.Company
                && x.CompanyId != null
                && x.CompanyId == QueryCompanyId);
    }

    internal async Task AppendAsync(SecurityEventRecord securityEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(securityEvent);
        if (ChangeTracker.Entries().Any())
        {
            throw new InvalidOperationException("A fresh security event writer context is required.");
        }

        SecurityEvents.Add(securityEvent);
        await base.SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);
    }

    public override int SaveChanges()
    {
        throw AppendOnlyException();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        throw AppendOnlyException();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        throw AppendOnlyException();
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        throw AppendOnlyException();
    }

    private static InvalidOperationException AppendOnlyException()
    {
        return new InvalidOperationException(
            "Güvenlik olayı kayıtları normal uygulama erişimiyle eklenemez, değiştirilemez veya silinemez.");
    }
}
