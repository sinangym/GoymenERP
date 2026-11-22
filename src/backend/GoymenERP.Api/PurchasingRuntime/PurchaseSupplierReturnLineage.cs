using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.PurchasingRuntime;

/// <summary>
/// AK-8 read-only owner bridge: Inventory's label lineage (roots, exact shares, goods acceptances) adapted for Purchasing's
/// return planning. Inventory's closed refusals keep their status and code.
/// </summary>
internal sealed class PurchaseSupplierReturnLineage(IInventorySupplierReturnLineageReadPortV1 inventory)
    : IPurchaseSupplierReturnLineageReadPortV1
{
    public async Task<PurchaseSupplierReturnLineageV1> ReadAsync(Guid companyId, Guid labelId, CancellationToken cancellationToken)
    {
        InventorySupplierReturnLineageV1 lineage;
        try { lineage = await inventory.ReadAsync(companyId, labelId, cancellationToken); }
        catch (InventorySupplierReturnExitException exception)
        { throw new PurchasingServiceException(exception.Status, exception.Code, innerException: exception); }
        if (lineage.CompanyId != companyId || lineage.LabelId != labelId)
            throw new PurchasingServiceException(503, PurchaseSupplierReturnCodesV1.LineageUnavailable);
        return new PurchaseSupplierReturnLineageV1(labelId, lineage.Roots.Select(root => new PurchaseSupplierReturnLineageRootV1(
            root.Kind == InventorySupplierReturnLineageRootKindV1.PurchaseReceipt, root.GoodsAcceptanceId,
            root.Share.Numerator, root.Share.Denominator)).ToArray());
    }
}
