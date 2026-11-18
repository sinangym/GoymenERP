using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// FNS-D11/D22/D87/D102. The balance read and the economic fact are one SERIALIZABLE
/// transaction. No callback retry: a deadlock/serialization failure is unavailable,
/// leaves no command key, and the user may retry with the same key. InMemory is only
/// for explicitly synthetic service fixtures; the real ledger guard refuses it.
/// </summary>
internal static class FinanceTreasuryTransaction
{
    private sealed class Attempt { internal bool NumberReservationAttempted; }
    private static readonly ConditionalWeakTable<FinanceDbContext, Attempt> ActiveAttempts = new ConditionalWeakTable<FinanceDbContext, Attempt>();

    internal static void MarkNumberReservation(FinanceDbContext dbContext)
    {
        if (!ActiveAttempts.TryGetValue(dbContext, out var attempt))
            throw new InvalidOperationException("Belge numarası yalnız aktif kayıt işlemi içinde ayrılır.");
        attempt.NumberReservationAttempted = true;
    }

    // The marker survives a provider aborting/disconnecting the DbTransaction. Save helpers must
    // rethrow before a legacy conflict/replay conversion can hide a partially executed SQL save.
    internal static bool RequiresFullRollback(FinanceDbContext dbContext) => ActiveAttempts.TryGetValue(dbContext, out _);

    private static bool TryRegisterAttempt(FinanceDbContext dbContext, Attempt attempt)
    {
        // A competing or nested operation cannot replace the original rollback marker.
        lock (ActiveAttempts)
        {
            if (ActiveAttempts.TryGetValue(dbContext, out _)) return false;
            ActiveAttempts.Add(dbContext, attempt);
            return true;
        }
    }

    internal static async Task<T> ExecuteAsync<T>(
        FinanceDbContext dbContext, Func<Task<T>> operation, Func<Task<T>> unavailable,
        IAuditTransactionWriterV1? auditOwner, CancellationToken cancellationToken, Func<T, bool>? successful = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (dbContext.ChangeTracker.HasChanges() || auditOwner is { HasPendingDrafts: true })
            return await unavailable();
        var attempt = new Attempt();
        if (!TryRegisterAttempt(dbContext, attempt)) return await unavailable();
        IDbContextTransaction? transaction = null;
        var ownsTransaction = false;
        var completed = false;
        var rolledBack = false;
        var operationStarted = false;
        try
        {
            if (dbContext.Database.IsRelational())
            {
                transaction = dbContext.Database.CurrentTransaction;
                if (transaction is null)
                {
                    transaction = await dbContext.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable, cancellationToken);
                    ownsTransaction = true;
                }
                else if (transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
                {
                    // Do not abort or upgrade a caller transaction that this operation has not touched.
                    transaction = null;
                    return await unavailable();
                }
            }

            operationStarted = true;
            var result = await operation();
            // Settings can already have flushed its reservation/audit to this SAME transaction.
            // A subsequent denial or unavailable result must not commit a consumed number alone.
            if (attempt.NumberReservationAttempted && successful?.Invoke(result) != true)
            {
                await RollbackAsync();
                completed = true;
                return result;
            }
            if (ownsTransaction) await transaction!.CommitAsync(cancellationToken);
            completed = true;
            return result;
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException)
        {
            await RollbackAsync();
            if (operationStarted)
            {
                dbContext.ChangeTracker.Clear();
                auditOwner?.DiscardPending();
            }
            return await unavailable();
        }
        finally
        {
            ActiveAttempts.Remove(dbContext);
            if (!completed)
            {
                await RollbackAsync();
            }
            // Only an accepted attempt owns its staged drafts. Rejecting a weaker borrowed
            // transaction must preserve the caller's tracker and shared pending Audit batch.
            if (operationStarted)
            {
                dbContext.ChangeTracker.Clear();
                auditOwner?.DiscardPending();
            }
            if (ownsTransaction) await transaction!.DisposeAsync();
        }

        async Task RollbackAsync()
        {
            if (transaction is null || rolledBack) return;
            rolledBack = true;
            try { await transaction.RollbackAsync(CancellationToken.None); }
            catch (Exception exception) when (exception is DbException or InvalidOperationException)
            { /* A deadlock victim can already have been rolled back by SQL Server. */ }
        }
    }
}
