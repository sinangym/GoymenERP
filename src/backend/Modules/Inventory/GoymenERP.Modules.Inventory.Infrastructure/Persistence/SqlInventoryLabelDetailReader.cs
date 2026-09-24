using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Balances;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Lots;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// Captures one tenant-filtered label and re-reads its version after related facts, refusing a
/// response assembled across a concurrent stock/document/print write.
/// </summary>
internal sealed class SqlInventoryLabelDetailReader(InventoryDbContext db) : IInventoryLabelDetailReader
{
    public async Task<InventoryLabelDetailRecord?> ReadAsync(
        Guid companyId,
        Guid labelId,
        CancellationToken cancellationToken)
    {
        var row = await db.Labels.AsNoTracking()
            .SingleOrDefaultAsync(label => label.CompanyId == companyId && label.Id == labelId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var lot = await db.Lots.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.CompanyId == companyId && candidate.Id == row.LotId,
                cancellationToken)
            ?? throw Error(503, InventoryLabelDetailCodesV1.Unavailable);
        var documents = await db.LabelDocuments.AsNoTracking()
            .Where(document => document.CompanyId == companyId && document.LabelId == labelId)
            .OrderBy(document => document.Sequence)
            .ToListAsync(cancellationToken);
        var prints = await db.LabelPrints.AsNoTracking()
            .Where(print => print.CompanyId == companyId && print.LabelId == labelId)
            .OrderBy(print => print.Sequence)
            .ToListAsync(cancellationToken);
        var balances = await db.Balances.AsNoTracking()
            .Where(balance => balance.CompanyId == companyId && balance.LabelId == labelId
                && balance.QuantityKg != 0m)
            .ToListAsync(cancellationToken);
        var finalVersion = await db.Labels.AsNoTracking()
            .Where(label => label.CompanyId == companyId && label.Id == labelId)
            .Select(label => (long?)label.Version)
            .SingleOrDefaultAsync(cancellationToken);
        if (finalVersion != row.Version)
        {
            throw Error(409, InventoryLabelDetailCodesV1.ReadConflict);
        }

        try
        {
            var label = MaterialLabel.Restore(
                row.Id,
                row.CompanyId,
                row.Code,
                row.ProductId,
                row.LotId,
                JsonSerializer.Deserialize<string[]>(row.RequiredDocumentsJson)
                    ?? throw Error(503, InventoryLabelDetailCodesV1.Unavailable),
                row.HeatNumber,
                row.CoilNumber,
                prints.Select(print => new LabelPrint(print.Sequence, print.PrintedAtUtc, print.PrintedBy, print.Reason)),
                documents.Select(document => new LabelDocumentAttachment(
                    document.Kind,
                    document.AttachedAtUtc,
                    document.AttachedBy)));
            var storedBalances = balances.Select(balance => new StockBalance(
                new StockPosition(
                    companyId,
                    new StockOwner((StockPartyKind)balance.OwnerKind, balance.OwnerId),
                    new StockCustodian((StockPartyKind)balance.CustodianKind, balance.CustodianId),
                    balance.LocationId,
                    label,
                    (BlockState)balance.Block),
                Kilograms.Of(balance.QuantityKg))).ToArray();
            return new InventoryLabelDetailRecord(
                row.Version,
                label,
                new MaterialLot(lot.Id, lot.CompanyId, lot.ProductId, lot.HeatNumber, lot.CoilNumber),
                storedBalances);
        }
        catch (InventoryLabelDetailException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw Error(503, InventoryLabelDetailCodesV1.Unavailable, exception);
        }
    }

    private static InventoryLabelDetailException Error(int statusCode, string code, Exception? exception = null) =>
        new(statusCode, code, exception);
}
