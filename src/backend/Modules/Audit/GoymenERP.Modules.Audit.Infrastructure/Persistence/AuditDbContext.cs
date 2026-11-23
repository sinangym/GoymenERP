using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
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

    // A global credential has no company. This closed owner port neither resolves a fictitious company
    // nor changes the company/Product read filters or the normal permission-bearing audit writer.
    internal async Task AppendPlatformCredentialRecoveryAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        _ = auditEvent.ToLocalTwoFactorOperatorRecoveryIntegrityData();
        if (companyContext.IsResolved || groupSharedProductId.HasValue || !Database.IsRelational()
            || Database.CurrentTransaction is null || ChangeTracker.Entries().Any())
            throw new InvalidOperationException("A fresh unresolved enlisted platform recovery Audit owner is required.");
        AuditEvents.Add(auditEvent);
        await base.SaveChangesAsync(true, cancellationToken);
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

    /// <summary>The Audit owner appends the company command's batch in the host's transaction.</summary>
    internal async Task AppendCompanyBatchAsync(IReadOnlyList<AuditEvent> auditEvents,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditEvents);
        if (auditEvents.Count == 0 || !companyContext.IsResolved || groupSharedProductId.HasValue
            || !Database.IsRelational() || Database.CurrentTransaction is null
            || ChangeTracker.Entries().Any()
            || auditEvents.Any(auditEvent => auditEvent.CompanyId != companyContext.CompanyId
                || auditEvent.SubjectKind != AuditEvent.CompanySubject))
            throw new InvalidOperationException("A fresh enlisted company-bound Audit owner is required.");
        AuditEvents.AddRange(auditEvents);
        await base.SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);
    }

    /// <summary>
    /// F-023 (F023-1) standalone refused attempt write: one company-bound <c>Refused</c> row on a fresh context of the
    /// same resolved company. The only caller is <c>RefusedAttemptRecorder</c> (architecture guard).
    /// </summary>
    internal async Task AppendRefusedAttemptAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        if (!string.Equals(auditEvent.Outcome, AuditOutcomes.Refused, StringComparison.Ordinal)
            || !string.Equals(auditEvent.SubjectKind, AuditEvent.CompanySubject, StringComparison.Ordinal))
            throw new InvalidOperationException("Only a company refused attempt can be appended here.");
        if (groupSharedProductId.HasValue || !companyContext.IsResolved
            || auditEvent.CompanyId != companyContext.CompanyId || ChangeTracker.Entries().Any())
            throw new InvalidOperationException("A fresh company-bound Audit writer context is required.");
        AuditEvents.Add(auditEvent);
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
