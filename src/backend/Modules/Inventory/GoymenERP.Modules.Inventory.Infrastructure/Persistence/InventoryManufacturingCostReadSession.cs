using System.Data;
using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>Read-only Inventory owner view of the host's uncommitted physical facts. Borrows both SQL handles;
/// disposing never commits, rolls back or closes them. No public writer or filter bypass is enabled.</summary>
public sealed class InventoryManufacturingCostReadSession : IInventoryManufacturingCostReadPortV1,
    IInventoryFirstReceiptExchangeRateReadPortV1, IAsyncDisposable
{
    private readonly InventoryDbContext db;
    private readonly DbConnection connection;
    private readonly DbTransaction transaction;
    private readonly ICompanyContext company;
    private readonly Guid companyId;
    private readonly InventoryManufacturingCostReadPort costs;
    private readonly InventoryFirstReceiptExchangeRateReadPort rates;
    private bool disposed;
    private InventoryManufacturingCostReadSession(InventoryDbContext db, DbConnection connection,
        DbTransaction transaction, ICompanyContext company)
    {
        this.db = db; this.connection = connection; this.transaction = transaction; this.company = company;
        companyId = company.CompanyId; costs = new(db, company); rates = new(db, company);
    }
    public IInventoryManufacturingCostReadPortV1 CostMovements => this;
    public IInventoryFirstReceiptExchangeRateReadPortV1 FirstReceiptRates => this;
    public static async Task<InventoryManufacturingCostReadSession> EnlistAsync(DbConnection connection,
        DbTransaction transaction, ICompanyContext company, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection); ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(company); cancellationToken.ThrowIfCancellationRequested();
        if (!company.IsResolved || company.CompanyId == Guid.Empty) throw new UnauthorizedAccessException();
        if (connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection))
            throw new InvalidOperationException("Inventory cost reads require the active owner's SQL transaction.");
        var db = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer(connection, contextOwnsConnection: false).Options, company);
        try
        {
            await db.Database.UseTransactionAsync(transaction, cancellationToken);
            return new(db, connection, transaction, company);
        }
        catch { await db.DisposeAsync(); throw; }
    }
    async Task<InventoryManufacturingCostReadV1> IInventoryManufacturingCostReadPortV1.GetAsync(Guid companyId,
        Guid manufacturingOrderId, CancellationToken cancellationToken)
    {
        RequireActive(companyId);
        var result = await costs.GetAsync(companyId, manufacturingOrderId, cancellationToken);
        RequireActive(companyId); return result;
    }
    async Task<InventoryFirstReceiptExchangeRateV1> IInventoryFirstReceiptExchangeRateReadPortV1.GetAsync(Guid companyId,
        Guid labelId, CancellationToken cancellationToken)
    {
        RequireActive(companyId);
        var result = await rates.GetAsync(companyId, labelId, cancellationToken);
        RequireActive(companyId); return result;
    }
    private void RequireActive(Guid asked)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (asked != companyId || !company.IsResolved || company.CompanyId != companyId)
            throw new UnauthorizedAccessException();
        if (connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection)
            || db.Database.CurrentTransaction is not { } current || !ReferenceEquals(current.GetDbTransaction(), transaction)
            || db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Inventory borrowed cost read transaction is no longer valid.");
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return; disposed = true; await db.DisposeAsync();
    }
}
