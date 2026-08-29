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
            ValidateTrackedEvents(dbContext);

            if (_eventsAttached || collector.Pending.Count == 0)
            {
                return;
            }

            foreach (var draft in collector.Pending)
            {
                ValidateCompanyId(draft.CompanyId);
            }

            foreach (var draft in collector.Pending)
            {
                var auditEvent = AuditEventFactory.Create(draft);
                dbContext.Set<AuditEvent>().Add(auditEvent);
                _attachedEvents.Add(auditEvent);
            }

            _eventsAttached = true;
        }
        catch
        {
            // A later factory/validation failure happens before EF can reliably invoke
            // SaveChangesFailed. Restore only interceptor-owned state and keep the
            // caller's non-audit tracked changes available for an explicit retry.
            Reset(detachEvents: true, dbContext);
            throw;
        }
    }

    private void ValidateTrackedEvents(DbContext dbContext)
    {
        foreach (var entry in dbContext.ChangeTracker.Entries<AuditEvent>())
        {
            if (entry.State is EntityState.Unchanged or EntityState.Detached)
            {
                continue;
            }

            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "Audit kayıtları değiştirilemez veya silinemez.");
            }

            ValidateCompanyId(entry.Entity.CompanyId);
        }
    }

    private void ValidateCompanyId(Guid companyId)
    {
        if (companyId == Guid.Empty)
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
