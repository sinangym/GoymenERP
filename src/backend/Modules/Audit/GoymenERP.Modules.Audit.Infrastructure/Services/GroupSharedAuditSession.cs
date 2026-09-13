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
    private GroupSharedAuditEventData? staged;
    private bool attempted;
    private bool disposed;

    private GroupSharedAuditSession(AuditDbContext database, GroupSharedMasterCommandContext authority)
    { this.database = database; this.authority = authority; }

    public bool IsFlushed { get; private set; }
    public Guid? ProductId => staged?.Envelope.Subject.ProductId;
    public string? IdempotencyKeyHash => staged?.Envelope.IdempotencyKeyHash;

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
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(auditEvent);
        if (attempted || staged is not null) throw new InvalidOperationException("One Product attempt requires exactly one canonical audit event.");
        RequireSameAuthority(auditEvent.Context);
        staged = GroupSharedAuditEventFactory.Create(auditEvent);
    }

    public async Task FlushAsync(GroupSharedMasterCommandContext currentAuthority, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (attempted || staged is null) throw new InvalidOperationException("A single staged canonical audit event is required.");
        attempted = true;
        RequireSameAuthority(currentAuthority);
        await database.AppendGroupSharedAsync(AuditEvent.CreateGroupShared(staged), cancellationToken);
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
