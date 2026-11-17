using System.Data;
using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>Read-only owner evidence. No status filter, query-filter bypass, request-intent inference or write.</summary>
public sealed class InventoryHistoricalSalesLinksV1(InventoryDbContext database, ICompanyContext company)
    : IInventoryHistoricalSalesLinksV1, IAsyncDisposable
{
    private DbTransaction? borrowedTransaction;
    private int disposed;

    /// <summary>Notification evidence at the command's instant; the caller owns locks and commit/rollback.</summary>
    public static async Task<InventoryHistoricalSalesLinksV1> EnlistAsync(DbConnection connection, DbTransaction transaction,
        Guid companyId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        if (companyId == Guid.Empty || connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection)
            || transaction.IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Historical link evidence requires the open serializable owner transaction.");
        var company = new CompanyContext();
        company.Set(companyId);
        var database = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>().UseSqlServer(connection).Options, company);
        try
        {
            await database.Database.UseTransactionAsync(transaction, cancellationToken);
            return new InventoryHistoricalSalesLinksV1(database, company) { borrowedTransaction = transaction };
        }
        catch { await database.DisposeAsync(); throw; }
    }

    public async Task<InventoryHistoricalSalesLinksAnswerV1> ReadAsync(Guid purchaseOrderId, Guid purchaseOrderLineId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureBorrowedTransaction();
        var companyId = company.IsResolved ? company.CompanyId : Guid.Empty;
        InventoryHistoricalSalesLinksAnswerV1 Unavailable() => new(InventoryHistoricalSalesLinksOutcomeV1.Unavailable,
            companyId, purchaseOrderId, purchaseOrderLineId, []);
        if (companyId == Guid.Empty || purchaseOrderId == Guid.Empty || purchaseOrderLineId == Guid.Empty) return Unavailable();
        try
        {
            var rows = await database.FutureSupplyAllocations.AsNoTracking()
                .Where(row => row.CompanyId == companyId && row.PurchaseOrderId == purchaseOrderId
                    && row.PurchaseOrderLineId == purchaseOrderLineId)
                .Select(row => new { row.CompanyId, row.SourceKind, row.DemandKind, row.DemandId, row.DemandLineId, row.Version })
                .ToListAsync(cancellationToken);
            EnsureBorrowedTransaction();
            if (!company.IsResolved || company.CompanyId != companyId
                || rows.Any(row => row.CompanyId != companyId || row.SourceKind != InventoryFutureAllocationKindsV1.PurchaseOrderLine
                    || row.DemandKind != InventoryFutureAllocationKindsV1.SalesOrderLine || row.DemandId == Guid.Empty
                    || row.DemandLineId == Guid.Empty || row.Version < 1)) return Unavailable();
            var lines = rows.Select(row => new InventoryHistoricalSalesLineV1(row.DemandId, row.DemandLineId))
                .Distinct().OrderBy(row => row.SalesOrderId).ThenBy(row => row.SalesOrderLineId).ToArray();
            return new(InventoryHistoricalSalesLinksOutcomeV1.Available, companyId, purchaseOrderId, purchaseOrderLineId, lines);
        }
        // Error categories only: this reader opens no connection and emits no SQL text.
        catch (DbException) { return Unavailable(); }
        catch (InvalidOperationException) { return Unavailable(); }
    }

    private void EnsureBorrowedTransaction()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (borrowedTransaction is { } transaction && (transaction.Connection is not { State: ConnectionState.Open }
            || database.Database.CurrentTransaction is not { } current || !ReferenceEquals(current.GetDbTransaction(), transaction)))
            throw new InvalidOperationException("The borrowed historical link transaction is no longer available.");
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0 && borrowedTransaction is not null) await database.DisposeAsync();
    }
}
