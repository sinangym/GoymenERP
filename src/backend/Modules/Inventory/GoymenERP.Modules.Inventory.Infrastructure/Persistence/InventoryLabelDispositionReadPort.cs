using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// F-009 D1 (URT-D31): the Inventory-owned answer to <see cref="IInventoryLabelDispositionReadPortV1"/>. It reads only
/// under the server-resolved company (a different <c>companyId</c> argument reads as NotFound, never as another
/// company's label), untracked, and writes nothing. The figures are <see cref="InventoryLabelDispositionReader"/>'s, the
/// same computation the output-reversal participant enforces.
/// </summary>
internal sealed class InventoryLabelDispositionReadPort(InventoryDbContext dbContext, ICompanyContext companyContext)
    : IInventoryLabelDispositionReadPortV1
{
    public async Task<InventoryLabelDispositionReadV1> GetAsync(
        Guid companyId, Guid labelId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            // Without a server-resolved company there is no tenant filter to read under: refuse, never read unscoped.
            throw new InvalidOperationException("Etiket hareket dökümü yalnız çözümlenmiş şirket bağlamında okunur.");
        }

        if (companyId != companyContext.CompanyId)
        {
            return InventoryLabelDispositionReadV1.NotFound;
        }

        var snapshot = await InventoryLabelDispositionReader.ReadAsync(dbContext, companyId, labelId,
            cancellationToken);
        return snapshot.Status switch
        {
            InventoryLabelDispositionStatusV1.Value => new InventoryLabelDispositionReadV1(
                InventoryLabelDispositionStatusV1.Value, ToContract(companyId, snapshot)),
            InventoryLabelDispositionStatusV1.NotFound => InventoryLabelDispositionReadV1.NotFound,
            _ => InventoryLabelDispositionReadV1.Unreadable,
        };
    }

    internal static InventoryLabelDispositionV1 ToContract(Guid companyId, InventoryLabelDispositionSnapshot snapshot)
    {
        var output = snapshot.Output is { } origin
            ? new InventoryLabelOutputOriginV1(origin.ManufacturingOrderId, origin.OutputId,
                origin.ReceiptMovementId, Kg(origin.ProducedKg), Kg(origin.ReversedKg),
                Kg(origin.FreeUnallocatedAtPositionKg), Kg(origin.BlockedAtPositionKg))
            : null;
        return new InventoryLabelDispositionV1(companyId, snapshot.LabelId, snapshot.ProductId,
            snapshot.LabelVersion.ToString(CultureInfo.InvariantCulture),
            snapshot.PhysicalKg > 0m ? InventoryLabelStockStateV1.InStock : InventoryLabelStockStateV1.OutOfStock,
            output, Kg(snapshot.ShippedKg), Kg(snapshot.CustomerReturnedKg), Kg(snapshot.ManufacturingIssuedKg),
            Kg(snapshot.ManufacturingReturnedKg), Kg(snapshot.TransferDispatchedKg), Kg(snapshot.TransferAcceptedKg),
            Kg(snapshot.ScrappedKg), Kg(snapshot.CountAdjustedInKg), Kg(snapshot.CountAdjustedOutKg),
            Kg(snapshot.OtherInKg), Kg(snapshot.OtherOutKg), Kg(snapshot.PhysicalKg), Kg(snapshot.BlockedKg),
            Kg(snapshot.AllocatedKg));
    }

    private static string Kg(decimal kilograms) => InventoryManufacturingPlanAllocationContractV1.Format(kilograms);
}
