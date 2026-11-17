using System.Data;
using System.Globalization;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Allocations;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using static GoymenERP.Modules.Inventory.Application.InventorySubcontractDeliveryContractV1;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

public sealed partial class InventorySubcontractDeliveryParticipant
{
    public async Task<InventorySubcontractLabelReadV1?> ReadLabelAsync(Guid labelId, CancellationToken cancellationToken = default)
    {
        RequireReadActive(); cancellationToken.ThrowIfCancellationRequested();
        if (labelId == Guid.Empty) throw Refused(400, Invalid);
        var label = await context.Labels.AsNoTracking().SingleOrDefaultAsync(row => row.Id == labelId, cancellationToken);
        if (label is null) { RequireReadActive(); return null; }
        if (label.CompanyId != companyId || label.Version < 1 || label.ProductId == Guid.Empty || !Text(label.ProductVersion, 128))
            throw Refused(503, SourceMismatch);
        var balances = await context.Balances.AsNoTracking().Where(row => row.LabelId == labelId && row.QuantityKg > 0m)
            .OrderBy(row => row.Id).ToListAsync(cancellationToken);
        var allocations = await context.Allocations.AsNoTracking().Where(row => row.LabelId == labelId && row.Status == (int)StockAllocationStatus.Active)
            .OrderBy(row => row.Id).ToListAsync(cancellationToken);
        if (balances.Any(row => row.CompanyId != companyId || row.ProductId != label.ProductId || row.QuantityKg < 0m
                || !Enum.IsDefined((StockPartyKind)row.OwnerKind) || !Enum.IsDefined((StockPartyKind)row.CustodianKind) || !Enum.IsDefined((BlockState)row.Block))
            || allocations.Any(row => row.CompanyId != companyId || row.RemainingKg < 0m)) throw Refused(503, SourceMismatch);
        var positions = balances.Select(balance =>
        {
            var held = allocations.Where(row => row.LocationId == balance.LocationId && row.OwnerKind == balance.OwnerKind && row.OwnerId == balance.OwnerId
                && row.CustodianKind == balance.CustodianKind && row.CustodianId == balance.CustodianId && row.Block == balance.Block).Sum(row => row.RemainingKg);
            return new InventorySubcontractLabelPositionV1(((StockPartyKind)balance.OwnerKind).ToString(), balance.OwnerId,
                ((StockPartyKind)balance.CustodianKind).ToString(), balance.CustodianId, balance.LocationId, ((BlockState)balance.Block).ToString(),
                Kg(balance.QuantityKg), Kg(held), Kg(StockAllocation.Unreserved(Kilograms.Of(balance.QuantityKg), Kilograms.Of(held)).Value));
        }).ToArray();
        var relevant = allocations.Where(row => row.DemandKind == InventoryAllocationDemandKindsV1.SubcontractServiceOrderLine).ToArray();
        if (relevant.Any(row => row.DemandId is null || row.DemandLineId is null || row.DemandProductId is null || !Text(row.DemandProductVersion, 128)))
            throw Refused(503, SourceMismatch);
        var views = relevant.Select(View).ToArray();
        var material = await MaterialAsync(label, cancellationToken);
        RequireReadActive();
        var version = Hash(new { companyId, labelId, label.Version, label.ProductId, label.ProductVersion, positions, allocations = views });
        return new(companyId, labelId, label.Code, label.Version.ToString(CultureInfo.InvariantCulture), label.ProductId,
            label.ProductVersion!, material.IsReleasableForProductionOrShipment, positions, views, version);
        static string Kg(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private void RequireReadActive()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection)
            || transaction.IsolationLevel != IsolationLevel.Serializable || context.Database.CurrentTransaction is not { } enlisted
            || !ReferenceEquals(enlisted.GetDbTransaction(), transaction) || context.ChangeTracker.HasChanges() || auditWriter.HasPendingDrafts)
            throw Refused(503, Unavailable);
    }
}
