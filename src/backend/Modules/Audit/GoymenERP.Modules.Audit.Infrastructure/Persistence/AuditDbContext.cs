using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Domain.Events;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Audit.Infrastructure.Persistence;

public sealed class AuditDbContext(
    DbContextOptions<AuditDbContext> options,
    ICompanyContext companyContext)
    : DbContext(options)
{
    private Guid? groupSharedProductId;
    private Guid? QueryCompanyId => groupSharedProductId.HasValue ? null : companyContext.CompanyId;
    private string? QueryProductId => groupSharedProductId?.ToString("D");

    internal void BindGroupSharedRead(GroupSharedMasterReadContext authority, Guid productId)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (productId == Guid.Empty) throw new ArgumentException("A Product identity is required.", nameof(productId));
        if (companyContext.IsResolved || groupSharedProductId.HasValue || ChangeTracker.Entries().Any())
            throw new InvalidOperationException("A fresh unresolved owner read scope is required.");
        groupSharedProductId = productId;
    }

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ConfigureAuditEvent(excludeFromMigrations: false);
        modelBuilder.Entity<AuditEvent>()
            .HasQueryFilter(x => groupSharedProductId.HasValue
                ? x.SubjectKind == AuditEvent.GroupSharedProductSubject && x.EntityType == AuditEvent.GroupSharedProductEntityType && x.EntityId == QueryProductId
                : x.SubjectKind == AuditEvent.CompanySubject && x.CompanyId == QueryCompanyId);
    }

    internal async Task AppendGroupSharedAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        await AppendGroupSharedBatchAsync([auditEvent], cancellationToken);
    }

    internal async Task AppendGroupSharedBatchAsync(IReadOnlyList<AuditEvent> auditEvents,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditEvents);
        if (auditEvents.Count == 0)
            throw new InvalidOperationException("At least one canonical group-shared audit event is required.");
        foreach (var auditEvent in auditEvents) _ = auditEvent.ToGroupSharedIntegrityData();
        if (!Database.IsRelational() || Database.CurrentTransaction is null || groupSharedProductId.HasValue
            || ChangeTracker.Entries().Any())
            throw new InvalidOperationException("A fresh enlisted Audit writer context is required.");
        AuditEvents.AddRange(auditEvents);
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
