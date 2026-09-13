using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Modules.Audit.Infrastructure.Persistence;

public sealed class AuditSaveChangesInterceptor(
    AuditEventCollector collector,
    ICompanyContext companyContext)
    : SaveChangesInterceptor
{
    private readonly List<AuditEvent> _attachedEvents = [];
    private bool _eventsAttached;

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Prepare(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Prepare(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Reset(detachEvents: false, eventData.Context);
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        Reset(detachEvents: false, eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        Reset(detachEvents: true, eventData.Context);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Reset(detachEvents: true, eventData.Context);
        return Task.CompletedTask;
    }

    public override void SaveChangesCanceled(DbContextEventData eventData)
    {
        Reset(detachEvents: true, eventData.Context);
    }

    public override Task SaveChangesCanceledAsync(
        DbContextEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Reset(detachEvents: true, eventData.Context);
        return Task.CompletedTask;
    }

    private void Prepare(DbContext? dbContext)
    {
        if (dbContext is null)
        {
            return;
        }

        try
        {
            PrepareCore(dbContext);
        }
        catch
        {
            Reset(detachEvents: true, dbContext);
            throw;
        }
    }

    private void PrepareCore(DbContext dbContext)
    {
        var auditEntries = dbContext.ChangeTracker
            .Entries<AuditEvent>()
            .ToList();

        var invalidEntry = auditEntries
            .FirstOrDefault(x => x.State is EntityState.Modified or EntityState.Deleted);
        if (invalidEntry is not null)
        {
            throw new InvalidOperationException(
                "Audit kayıtları değiştirilemez veya silinemez.");
        }

        foreach (var entry in auditEntries.Where(x => x.State == EntityState.Added))
        {
            ValidateCompany(entry.Entity.CompanyId);
        }

        if (_eventsAttached || collector.Pending.Count == 0)
        {
            return;
        }

        foreach (var draft in collector.Pending)
        {
            ValidateCompany(draft.CompanyId);
        }

        var auditEvents = collector.Pending
            .Select(AuditEventFactory.Create)
            .ToList();
        _attachedEvents.AddRange(auditEvents);
        dbContext.Set<AuditEvent>().AddRange(auditEvents);

        _eventsAttached = true;
    }

    private void ValidateCompany(Guid? companyId)
    {
        if (!companyId.HasValue || companyId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Audit kaydı CompanyId olmadan yazılamaz.");
        }

        if (!companyContext.IsResolved)
        {
            throw new InvalidOperationException(
                "Audit kaydı yazmak için aktif şirket bağlamı zorunludur.");
        }

        if (companyId != companyContext.CompanyId)
        {
            throw new InvalidOperationException(
                "Audit kaydı aktif şirket bağlamıyla eşleşmelidir.");
        }
    }

    private void Reset(bool detachEvents, DbContext? dbContext)
    {
        if (detachEvents && dbContext is not null)
        {
            foreach (var auditEvent in _attachedEvents)
            {
                var entry = dbContext.Entry(auditEvent);
                if (entry.State != EntityState.Detached)
                {
                    entry.State = EntityState.Detached;
                }
            }
        }

        collector.Clear();
        _attachedEvents.Clear();
        _eventsAttached = false;
    }
}
