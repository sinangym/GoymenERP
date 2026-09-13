using System.Data;
using System.Data.Common;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Idempotency.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Idempotency.Infrastructure.Executions;

// Privileged owner reader. Product consumers use the existing fresh-authority resolver,
// never this raw adapter. Every call has a fresh untracked context, including race reload.
public sealed class SqlGroupSharedCommandTerminalStore(DbContextOptions<GroupSharedCommandDbContext> options)
    : IGroupSharedCommandTerminalReader
{
    public async Task<GroupSharedCommandTerminal?> ReadAsync(GroupSharedCommandKey key,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        await using var context = new GroupSharedCommandDbContext(options);
        var record = await context.Terminals.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Subject == key.Subject && x.CommandName == key.CommandName
            && x.IdempotencyKeyHash == key.IdempotencyKeyHash, cancellationToken);
        return record?.RestoreTerminal();
    }
}

// No Begin/Commit/Rollback here. The composition root owns the exact shared pair and
// may commit only after Product, canonical Audit and this terminal all flush successfully.
public sealed class GroupSharedCommandTerminalSession : IAsyncDisposable
{
    private readonly GroupSharedCommandDbContext database;
    private readonly GroupSharedMasterCommandContext authority;
    private readonly GroupSharedCommandBinding binding;
    private GroupSharedCommandTerminal? staged;
    private bool attempted;
    private bool disposed;

    private GroupSharedCommandTerminalSession(GroupSharedCommandDbContext database,
        GroupSharedMasterCommandContext authority, string keyHash, string requestHash)
    {
        this.database = database;
        this.authority = authority;
        binding = GroupSharedCommandBinding.Create(authority, keyHash, requestHash);
    }

    public bool IsFlushed { get; private set; }

    public static async Task<GroupSharedCommandTerminalSession> EnlistAsync(
        DbConnection connection, DbTransaction transaction, GroupSharedMasterCommandContext authority,
        string keyHash, string requestHash, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(authority);
        _ = GroupSharedCommandBinding.Create(authority, keyHash, requestHash);
        if (!ReferenceEquals(transaction.Connection, connection) || connection.State != ConnectionState.Open)
            throw new InvalidOperationException("The owner session requires the same open connection and transaction.");
        var options = new DbContextOptionsBuilder<GroupSharedCommandDbContext>().UseSqlServer(connection).Options;
        var database = new GroupSharedCommandDbContext(options);
        try
        {
            await database.Database.UseTransactionAsync(transaction, cancellationToken);
            return new(database, authority, keyHash, requestHash);
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public void Stage(GroupSharedCommandTerminal terminal)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(terminal);
        if (attempted || staged is not null)
            throw new InvalidOperationException("A terminal session accepts exactly one result.");
        if (terminal.Binding != binding)
            throw new InvalidOperationException("The terminal does not match the enrolled command intent.");
        staged = terminal;
    }

    public async Task FlushAsync(GroupSharedMasterCommandContext currentAuthority,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(currentAuthority);
        if (attempted || staged is null)
            throw new InvalidOperationException("A fresh session with one staged terminal is required.");
        attempted = true;
        if (currentAuthority.Operation != authority.Operation
            || currentAuthority.LocalActorId != authority.LocalActorId
            || currentAuthority.OriginCompanyId != authority.OriginCompanyId
            || currentAuthority.AuthorityRevision != authority.AuthorityRevision)
            throw new UnauthorizedAccessException("Shared authority changed during the command.");
        try
        {
            await database.InsertAsync(GroupSharedCommandTerminalRecord.FromTerminal(staged, DateTimeOffset.UtcNow), cancellationToken);
            IsFlushed = true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Only this owner's single terminal INSERT is inside this catch. The caller
            // must roll back the whole attempt before freshly authorizing/reloading the winner.
            throw new GroupSharedCommandTerminalAlreadyExistsException(exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        await database.DisposeAsync();
    }
}

public sealed class GroupSharedCommandTerminalAlreadyExistsException(Exception innerException)
    : Exception("A global terminal already exists; roll back this attempt before resolving the winner.", innerException);
