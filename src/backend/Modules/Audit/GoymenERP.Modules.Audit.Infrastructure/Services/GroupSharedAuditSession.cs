using System.Data;
using System.Data.Common;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Audit.Infrastructure.Services;

public sealed class GroupSharedAuditSession : IGroupSharedAuditEventCollector, IAsyncDisposable
{
    private readonly AuditDbContext database;
    private readonly GroupSharedMasterCommandContext authority;
    private IReadOnlyList<GroupSharedAuditEventData>? staged;
    private bool attempted;
    private bool disposed;

    private GroupSharedAuditSession(AuditDbContext database, GroupSharedMasterCommandContext authority)
    { this.database = database; this.authority = authority; }

    public bool IsFlushed { get; private set; }
    public Guid? ProductId => staged is { Count: 1 } ? staged[0].Envelope.Subject.ProductId : null;
    public IReadOnlyList<Guid> ProductIds => staged?.Select(value => value.Envelope.Subject.ProductId).ToArray() ?? [];
    public string? IdempotencyKeyHash => staged is { Count: > 0 } ? staged[0].Envelope.IdempotencyKeyHash : null;

    public static async Task<GroupSharedAuditSession> EnlistAsync(AuditDbContext database, DbConnection connection, DbTransaction transaction,
        GroupSharedMasterCommandContext authority, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(database);
        if (connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection))
            throw new InvalidOperationException("Audit requires the same open owner connection and transaction.");
        if (database.ChangeTracker.Entries().Any() || database.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Audit enrollment requires the owner's fresh DI scope.");
        database.Database.SetDbConnection(connection, contextOwnsConnection: false);
        await database.Database.UseTransactionAsync(transaction, cancellationToken);
        return new GroupSharedAuditSession(database, authority);
    }

    public void Enqueue(GroupSharedAuditEventDraft auditEvent)
    {
        EnqueueBatch([auditEvent]);
    }

    public void EnqueueBatch(IReadOnlyList<GroupSharedAuditEventDraft> auditEvents)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(auditEvents);
        if (attempted || staged is not null)
            throw new InvalidOperationException("One Product attempt may stage its canonical audit envelope only once.");
        if (auditEvents.Count == 0)
            throw new InvalidOperationException("At least one canonical audit event is required.");
        var batch = new List<GroupSharedAuditEventData>(auditEvents.Count);
        foreach (var auditEvent in auditEvents)
        {
            ArgumentNullException.ThrowIfNull(auditEvent);
            RequireSameAuthority(auditEvent.Context);
            batch.Add(GroupSharedAuditEventFactory.Create(auditEvent));
        }
        if (batch.Select(value => value.Envelope.Subject.ProductId).Distinct().Count() != batch.Count
            || batch.Select(value => value.Envelope.IdempotencyKeyHash).Distinct(StringComparer.Ordinal).Count() != 1)
            throw new InvalidOperationException("A canonical Product audit batch requires unique products and one idempotency identity.");
        staged = batch;
    }

    public async Task FlushAsync(GroupSharedMasterCommandContext currentAuthority, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (attempted || staged is null) throw new InvalidOperationException("A staged canonical audit envelope is required.");
        attempted = true;
        RequireSameAuthority(currentAuthority);
        await database.AppendGroupSharedBatchAsync(staged.Select(AuditEvent.CreateGroupShared).ToArray(), cancellationToken);
        IsFlushed = true;
    }

    private void RequireSameAuthority(GroupSharedMasterCommandContext current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (current.Operation != authority.Operation || current.LocalActorId != authority.LocalActorId
            || current.OriginCompanyId != authority.OriginCompanyId || current.AuthorityRevision != authority.AuthorityRevision)
            throw new UnauthorizedAccessException("Product audit authority changed during the attempt.");
    }

    public ValueTask DisposeAsync()
    {
        if (disposed) return ValueTask.CompletedTask;
        disposed = true;
        // The enclosing fresh DI scope owns this context. It owns neither SQL handle.
        return ValueTask.CompletedTask;
    }
}
