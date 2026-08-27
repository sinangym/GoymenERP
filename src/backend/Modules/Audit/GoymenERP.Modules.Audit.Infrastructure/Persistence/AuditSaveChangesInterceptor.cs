using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Modules.Audit.Infrastructure.Persistence;

public sealed class AuditSaveChangesInterceptor(AuditEventCollector collector)
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

        var invalidEntry = dbContext.ChangeTracker
            .Entries<AuditEvent>()
            .FirstOrDefault(x => x.State is EntityState.Modified or EntityState.Deleted);
        if (invalidEntry is not null)
        {
            throw new InvalidOperationException(
                "Audit kayıtları değiştirilemez veya silinemez.");
        }

        if (_eventsAttached || collector.Pending.Count == 0)
        {
            return;
        }

        foreach (var draft in collector.Pending)
        {
            var auditEvent = AuditEventFactory.Create(draft);
            dbContext.Set<AuditEvent>().Add(auditEvent);
            _attachedEvents.Add(auditEvent);
        }

        _eventsAttached = true;
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
