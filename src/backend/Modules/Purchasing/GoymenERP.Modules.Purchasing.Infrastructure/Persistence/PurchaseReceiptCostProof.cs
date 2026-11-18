using System.Data;
using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

/// <summary>Purchasing-owned cost evidence; no second connection, write, commit or substitute exchange rate.</summary>
public sealed class PurchaseReceiptCostProof : IPurchaseReceiptCostProofV1
{
    private readonly PurchasingDbContext database;
    private readonly CompanyContext company;
    private readonly DbConnection connection;
    private readonly DbTransaction transaction;
    private int disposed;

    private PurchaseReceiptCostProof(PurchasingDbContext database, CompanyContext company, DbConnection connection,
        DbTransaction transaction)
    {
        this.database = database; this.company = company; this.connection = connection; this.transaction = transaction;
    }

    public static async Task<IPurchaseReceiptCostProofV1> EnlistAsync(DbConnection connection, DbTransaction transaction,
        Guid companyId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        if (companyId == Guid.Empty || connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection))
            throw Unavailable();
        var company = new CompanyContext(); company.Set(companyId);
        var database = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>().UseSqlServer(connection).Options, company);
        try
        {
            await database.Database.UseTransactionAsync(transaction, cancellationToken);
            return new PurchaseReceiptCostProof(database, company, connection, transaction);
        }
        catch { await database.DisposeAsync(); throw; }
    }

    public async Task<PurchaseReceiptCostProofV1?> ReadAsync(Guid purchaseOrderId, Guid purchaseOrderLineId, Guid acceptanceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureTransaction();
        if (purchaseOrderId == Guid.Empty || purchaseOrderLineId == Guid.Empty || acceptanceId == Guid.Empty) return null;
        // This read validates the immutable first acceptance result against its ledger row before using its source identity.
        var acceptance = await new PurchaseAcceptanceSource(database, company).ReadAsync(purchaseOrderId, acceptanceId, cancellationToken);
        EnsureTransaction();
        if (acceptance is null || acceptance.PurchaseOrderLineId != purchaseOrderLineId) return null;
        var line = await database.OrderLines.AsNoTracking().Where(row => row.CompanyId == company.CompanyId
                && row.OrderId == purchaseOrderId && row.LineId == purchaseOrderLineId)
            .Select(row => new { row.ProductId, row.ProductVersion }).SingleOrDefaultAsync(cancellationToken);
        EnsureTransaction();
        if (line is null || line.ProductId == Guid.Empty || string.IsNullOrWhiteSpace(line.ProductVersion)) throw Unavailable();
        var price = await new PurchaseOrderLineCostReadPort(database, company).GetAsync(company.CompanyId,
            purchaseOrderId, purchaseOrderLineId, cancellationToken);
        EnsureTransaction();
        var rate = await new PurchaseInvoiceCostRateReadPort(database, company).GetAsync(company.CompanyId, acceptanceId, cancellationToken);
        EnsureTransaction();
        var approvedOrderRate = await new PurchaseOrderExchangeRateReadPort(database, company).GetAsync(company.CompanyId,
            purchaseOrderId, cancellationToken);
        EnsureTransaction();
        if (price.CompanyId != company.CompanyId || price.PurchaseOrderId != purchaseOrderId
            || price.PurchaseOrderLineId != purchaseOrderLineId || price.Outcome == PurchaseOrderLineCostOutcomeV1.NotFound
            || rate.CompanyId != company.CompanyId || rate.GoodsReceiptLineId != acceptanceId
            || rate.PurchaseOrderId != purchaseOrderId || rate.PurchaseOrderLineId != purchaseOrderLineId
            || rate.Outcome == PurchaseInvoiceCostRateOutcomeV1.NotFound
            || approvedOrderRate.CompanyId != company.CompanyId || approvedOrderRate.PurchaseOrderId != purchaseOrderId
            || approvedOrderRate.Outcome == PurchaseOrderExchangeRateOutcomeV1.NotFound) throw Unavailable();
        return new PurchaseReceiptCostProofV1(company.CompanyId, purchaseOrderId, purchaseOrderLineId, acceptanceId, line.ProductId,
            line.ProductVersion, acceptance.AcceptedQuantity, acceptance.PurchaseOrderVersionAfter, acceptance.OccurredAtUtc,
            price, rate, approvedOrderRate);
    }

    private void EnsureTransaction()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection)
            || database.Database.CurrentTransaction is not { } current || !ReferenceEquals(current.GetDbTransaction(), transaction))
            throw Unavailable();
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0) await database.DisposeAsync();
    }
    private static PurchaseAcceptanceParticipantException Unavailable() => new PurchaseAcceptanceParticipantException(PurchaseAcceptanceParticipantCodes.Unavailable,
        503, "Purchase receipt cost proof is unavailable.");
}
