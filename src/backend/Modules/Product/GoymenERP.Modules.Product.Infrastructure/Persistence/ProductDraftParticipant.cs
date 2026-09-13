using System.Data;
using System.Data.Common;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

/// <summary>
/// Product's participant in the shared host's one-command transaction. It borrows
/// the already-open connection/transaction and owns only its fresh Product context.
/// The accepted host runner must flush canonical audit + matching global terminal
/// before commit, and roll back the whole attempt on any exception. An enlisted
/// pair is not evidence that those other owners have completed their writes.
/// </summary>
public sealed class ProductDraftParticipant : IAsyncDisposable
{
    private readonly ProductDbContext context;
    private readonly DbConnection connection;
    private readonly DbTransaction transaction;
    private readonly GroupSharedMasterCommandContext authority;
    private readonly TimeProvider clock;
    private readonly Func<CancellationToken, Task>? onStaged;
    private int started;
    private bool disposed;
    private ProductDraftWriteResult? staged;

    private ProductDraftParticipant(ProductDbContext context, DbConnection connection, DbTransaction transaction,
        GroupSharedMasterCommandContext authority, TimeProvider clock, Func<CancellationToken, Task>? onStaged)
    {
        this.context = context;
        this.connection = connection;
        this.transaction = transaction;
        this.authority = authority;
        this.clock = clock;
        this.onStaged = onStaged;
    }

    public static async Task<ProductDraftParticipant> EnlistAsync(DbConnection connection, DbTransaction transaction,
        GroupSharedMasterCommandContext authority, CancellationToken cancellationToken, TimeProvider? clock = null,
        Func<CancellationToken, Task>? onStaged = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(authority);
        cancellationToken.ThrowIfCancellationRequested();
        if (connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection))
            throw new InvalidOperationException("Product requires the owner's open connection and its active transaction.");

        var options = new DbContextOptionsBuilder<ProductDbContext>()
            .UseSqlServer(connection, contextOwnsConnection: false).Options;
        var context = new ProductDbContext(options);
        try
        {
            await context.Database.UseTransactionAsync(transaction, cancellationToken);
            return new(context, connection, transaction, authority, clock ?? TimeProvider.System, onStaged);
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }

    /// <summary>Flush Product only. The returned result is uncommitted until the host finishes all owners.</summary>
    public async Task<ProductDraftWriteResult> ExecuteAsync(ProductCommandIntent intent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intent);
        EnsureActive();
        if (Interlocked.Exchange(ref started, 1) != 0)
            throw new InvalidOperationException("A Product participant can execute only one command attempt.");
        staged = await ProductDraftOperation.StageAsync(context, authority, intent, clock, cancellationToken);
        // The host may relay this point to its existing attempt observer. It lets
        // SQL tests pause after the actual read/validation but before any Product
        // flush, without exposing this context, transaction or request payload.
        // An exception/cancellation leaves the whole attempt to the owner's rollback.
        if (onStaged is not null) await onStaged(cancellationToken);
        try
        {
            await context.FlushEnlistedAsync(this, cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception) when (exception.Entries.Any(entry => entry.Entity is ProductRow))
        {
            // Read only the database version, never reload/apply the losing edit.
            // The tracked row already contains the attempted v+1, which is NOT
            // evidence of the winner's current version (it may have advanced again).
            var expected = staged.VersionBefore
                ?? throw new InvalidOperationException("A Product edit conflict has no original version.");
            var current = await context.Products.AsNoTracking()
                .Where(row => row.Id == staged.ProductId).Select(row => (long?)row.Version)
                .SingleOrDefaultAsync(cancellationToken);
            if (current is null) throw new ProductOperationException(ProductApiV1.NotFound, 404);
            if (current < 1) throw new InvalidOperationException("The stored Product version is invalid.");
            // The owner still rolls back/disposes and rechecks current authority
            // before releasing error metadata. Counter concurrency remains an
            // owner whole-attempt retry rather than a Product version conflict.
            throw new ProductVersionConflictException(expected, current.Value);
        }
        return staged;
    }

    internal void ValidateFlush(ProductDbContext candidate)
    {
        EnsureActive();
        if (!ReferenceEquals(candidate, context) || staged is null)
            throw new InvalidOperationException("The Product context has no matching staged participant.");
        var changes = context.ChangeTracker.Entries().Where(entry => entry.State is not (EntityState.Unchanged or EntityState.Detached)).ToArray();
        var rows = changes.Where(entry => entry.Entity is ProductRow).ToArray();
        if (rows.Length != 1 || ((ProductRow)rows[0].Entity).Id != staged.ProductId)
            throw new InvalidOperationException("One Product command must write exactly its one Product row.");

        if (authority.Operation == GroupSharedMasterOperation.DraftCreate)
        {
            if (rows[0].State != EntityState.Added || changes.Length != 3
                || changes.Count(entry => entry.Entity is ProductCodeCounter && entry.State is (EntityState.Added or EntityState.Modified)) != 1
                || changes.Count(entry => entry.Entity is ProductCodeRegistration registration && entry.State == EntityState.Added
                    && registration.ProductId == staged.ProductId && registration.Code == staged.Response.Card.Product.Code && registration.IsCurrent) != 1)
                throw new InvalidOperationException("Product create must stage one Product, one code and one prefix counter.");
        }
        else if (changes.Length != 1 || rows[0].State != EntityState.Modified)
        {
            throw new InvalidOperationException("Product basic edit may modify only its existing Product row.");
        }
    }

    private void EnsureActive()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection)
            || context.Database.CurrentTransaction is not { } enlisted || !ReferenceEquals(enlisted.GetDbTransaction(), transaction))
            throw new InvalidOperationException("The owner's Product transaction is no longer active.");
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        // EF borrows both handles. Disposing this context never commits, rolls
        // back or closes the transaction/connection owned by the shared host.
        await context.DisposeAsync();
    }
}
