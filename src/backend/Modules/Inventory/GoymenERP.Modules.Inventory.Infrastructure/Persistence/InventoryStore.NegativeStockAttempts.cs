using System.Data;
using System.Data.Common;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Balances;
using GoymenERP.Modules.Inventory.Domain.Movements;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

public sealed partial class InventoryStore
{
    private DbTransaction? negativeValidationTransaction;
    private readonly Dictionary<(Guid ProductId, Guid LocationId), long> negativeCountBoundaries = [];

    internal async Task LockPhysicalValidationAsync(CancellationToken token)
    {
        RequireResolvedCompany();
        var transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("Physical validation requires an owner transaction.");
        if (ReferenceEquals(transaction, negativeValidationTransaction)) return;
        await InventoryNegativeStockValidationGuard.AcquireAsync(dbContext, companyContext.CompanyId, token);
        await RefreshUnchangedPhysicalRowsAsync(token);
        negativeValidationTransaction = transaction;
        negativeCountBoundaries.Clear();
    }

    private async Task RefreshUnchangedPhysicalRowsAsync(CancellationToken token)
    {
        // A reused scoped store may retain snapshots from a completed transaction. A new guard does not
        // refresh EF's identity map. Read filtered snapshots explicitly; never clear/detach a caller's staged work,
        // and never use Reload (which could query an attached identity outside this company's filter).
        var labels = dbContext.ChangeTracker.Entries<LabelRow>().Where(entry => entry.State == EntityState.Unchanged).ToArray();
        var balances = dbContext.ChangeTracker.Entries<BalanceRow>().Where(entry => entry.State == EntityState.Unchanged).ToArray();
        if (labels.Any(entry => entry.Entity.CompanyId != companyContext.CompanyId)
            || balances.Any(entry => entry.Entity.CompanyId != companyContext.CompanyId))
            throw new InvalidOperationException("Only this company's tracked physical rows may be refreshed.");
        if (labels.Length > 0)
        {
            var ids = labels.Select(entry => entry.Entity.Id).ToArray();
            var actual = await dbContext.Labels.AsNoTracking().Where(row => ids.Contains(row.Id)).ToDictionaryAsync(row => row.Id, token);
            foreach (var entry in labels)
            {
                if (!actual.TryGetValue(entry.Entity.Id, out var row))
                    throw new InvalidOperationException("A tracked physical label must still exist in this company.");
                entry.CurrentValues.SetValues(row); entry.OriginalValues.SetValues(row);
            }
        }
        if (balances.Length > 0)
        {
            var ids = balances.Select(entry => entry.Entity.Id).ToArray();
            var actual = await dbContext.Balances.AsNoTracking().Where(row => ids.Contains(row.Id)).ToDictionaryAsync(row => row.Id, token);
            foreach (var entry in balances)
            {
                if (!actual.TryGetValue(entry.Entity.Id, out var row))
                    throw new InvalidOperationException("A tracked physical balance must still exist in this company.");
                entry.CurrentValues.SetValues(row); entry.OriginalValues.SetValues(row);
            }
        }
    }

    internal async Task ObservePhysicalCountBoundaryAsync(Guid productId, Guid locationId, CancellationToken token)
    {
        await LockPhysicalValidationAsync(token);
        var key = (productId, locationId);
        if (negativeCountBoundaries.ContainsKey(key)) return;
        var version = await dbContext.NegativeStockCountBoundaries.AsNoTracking()
            .Where(row => row.CompanyId == companyContext.CompanyId && row.ProductId == productId && row.LocationId == locationId)
            .MaxAsync(row => (long?)row.Version, token) ?? 0L;
        negativeCountBoundaries.Add(key, version);
    }

    private async Task<T> WithinPhysicalValidationTransactionAsync<T>(Func<Task<T>> action, CancellationToken token)
    {
        if (dbContext.Database.CurrentTransaction is not null)
            return await action(); // The host alone rolls back/commits; replay does not need the physical guard.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(token);
        try
        {
            var result = await action();
            await transaction.CommitAsync(token);
            return result;
        }
        catch (Exception exception)
        {
            // No independent attempt until rollback is positively confirmed. Failure remains unavailable.
            await transaction.RollbackAsync(CancellationToken.None);
            await transaction.DisposeAsync();
            dbContext.ChangeTracker.Clear();
            if (negativeStockAttempts is not null) await RecordPhysicalRefusalAfterRollbackAsync(exception);
            throw;
        }
    }

    internal InventoryRuleException PhysicalStockRefusal(StockMovement movement, StockShortfall shortfall,
        InventoryCommandMetadata metadata, string labelCode, InventoryRuleException? original = null)
    {
        var lineNumber = shortfall.LineNumber;
        if (lineNumber is null || lineNumber > movement.Lines.Count
            || movement.Lines[lineNumber.Value - 1].Direction != StockDirection.Out
            || movement.Lines[lineNumber.Value - 1].Position != shortfall.Position
            || movement.Lines[lineNumber.Value - 1].Quantity != shortfall.Requested
            || movement.CompanyId != shortfall.Position.CompanyId || movement.Kind != shortfall.MovementKind
            || movement.Source != shortfall.Source)
            throw new InvalidOperationException("Only the actual failing outbound line supplies physical refusal evidence.");
        if (!negativeCountBoundaries.TryGetValue((shortfall.Position.ProductId, shortfall.Position.LocationId), out var boundary)
            || dbContext.Database.CurrentTransaction?.GetDbTransaction() is not { } transaction
            || !ReferenceEquals(transaction, negativeValidationTransaction))
            throw new InvalidOperationException("Physical refusal requires the locked pre-validation count boundary.");
        var evidence = new InventoryNegativeStockAttemptEvidenceV1(movement.Id, PayloadHash(movement), shortfall,
            labelCode, timeProvider.GetUtcNow(), metadata with { RoleCodes = metadata.RoleCodes.ToArray() }, boundary);
        original ??= new InventoryRuleException(shortfall, "Hareket fiziksel stoğu eksiye düşürür.");
        return new InventoryRuleException(shortfall, original.Message,
            new InventoryNegativeStockAttemptEvidenceException(evidence, original));
    }

    private async Task RecordPhysicalRefusalAfterRollbackAsync(Exception refusal)
    {
        if (InventoryNegativeStockAttemptEvidenceException.FindEvidence(refusal) is null) return;
        if (negativeStockAttempts is null)
            throw new InventoryNegativeStockAttemptException(503, InventoryNegativeStockAttemptCodesV1.Unavailable, refusal);
        await negativeStockAttempts.RecordAfterRollbackAsync(refusal, CancellationToken.None);
    }

    private async Task ResolveNegativeStockAttemptsAsync(StockCountRow count, DateTimeOffset now,
        InventoryCommandMetadata metadata, CancellationToken cancellationToken)
    {
        // SAT-D339: product+warehouse, across labels. Capture the exact approved count as immutable boundary proof.
        if (dbContext.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Count resolution requires the count's serializable transaction.");
        await ObservePhysicalCountBoundaryAsync(count.ProductId, count.LocationId, cancellationToken);
        var boundaryVersion = checked(negativeCountBoundaries[(count.ProductId, count.LocationId)] + 1);
        dbContext.NegativeStockCountBoundaries.Add(new()
        {
            CompanyId = count.CompanyId, ProductId = count.ProductId, LocationId = count.LocationId,
            Version = boundaryVersion, StockCountId = count.Id, DecidedAtUtc = now, DecidedBy = metadata.ActorId,
        });
        var ids = await dbContext.NegativeStockAttempts
            .Where(row => row.CompanyId == count.CompanyId && row.ProductId == count.ProductId
                && row.LocationId == count.LocationId
                && !dbContext.NegativeStockAttemptResolutions.Any(resolution => resolution.CompanyId == row.CompanyId
                    && resolution.AttemptId == row.Id))
            .Select(row => row.Id).ToArrayAsync(cancellationToken);
        foreach (var id in ids)
            dbContext.NegativeStockAttemptResolutions.Add(new NegativeStockAttemptResolutionRow
            {
                CompanyId = count.CompanyId, AttemptId = id, StockCountId = count.Id,
                DecidedAtUtc = now, DecidedBy = metadata.ActorId,
                CountBoundaryVersion = boundaryVersion,
            });
    }
}
