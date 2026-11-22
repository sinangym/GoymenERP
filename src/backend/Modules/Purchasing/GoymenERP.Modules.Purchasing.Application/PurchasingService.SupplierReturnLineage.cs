using GoymenERP.BuildingBlocks.Quantities;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Purchasing.Application;

/// <summary>
/// AK-8 (SAT-D433, SAT-D441, SAT-D444, SAT-D445): a supplier return that names a source label other than the receipt's own label.
/// Behind <see cref="PurchaseSupplierReturnLineageGoldenV1"/>; with it closed the request is refused 503 before anything is read.
/// The lineage comes from Inventory's read port; each purchase root's supplier from Purchasing's own acceptances.
/// <list type="bullet">
/// <item><b>Lineage</b> (only the returning supplier's receipts): one receipt — the named acceptance — takes the whole return and
/// the exit is valued from that receipt (SAT-D306) through the existing invoice/exception cost path. Several receipts need one
/// atomic multi-exit return (SAT-D440/D442 distribution): not staged yet, 503.</item>
/// <item><b>Quantity</b> (any other lineage): the named acceptance of this order is the chosen receipt; the supplier price is the
/// order line's unit price (SAT-D444), so the line must be a priced kilogram line in TL. Inventory leaves the stock at the moving
/// average and refuses a readable difference (SAT-D371, 409) before anything commits.</item>
/// </list>
/// </summary>
public sealed partial class PurchasingService
{
    /// <summary>The early gate and shape checks, before replay lookup or any read.</summary>
    private void RequireReturnLineageAdmissible(CreatePurchaseSupplierReturnV1 request)
    {
        if (request.SourceLabelId is null) return;
        if (!(returnPolicy ?? PurchaseSupplierReturnPolicyV1.Default).LineageGoldenAccepted
            || request.ExpectedPurchaseOrderVersion is not null || request.CostAllocation is not null)
        {
            throw PurchaseSupplierReturnLineagePlannerV1.Unavailable();
        }
    }

    private async Task<PurchaseSupplierReturnLineagePlanV1> PlanReturnLineageAsync(Scope scope, string? permission,
        IPurchaseSupplierReturnStore returns, PurchaseOrder order, Guid sourceLabelId, Guid goodsAcceptanceId,
        CancellationToken cancellationToken)
    {
        var port = returnLineage ?? throw PurchaseSupplierReturnLineagePlannerV1.Unavailable();
        var lineage = await GuardedAsync(scope, permission, () => port.ReadAsync(scope.CompanyId, sourceLabelId, cancellationToken),
            cancellationToken);
        if (lineage is null || lineage.LabelId != sourceLabelId) throw PurchaseSupplierReturnLineagePlannerV1.Unavailable();
        var suppliers = new Dictionary<Guid, Guid?>();
        foreach (var acceptance in lineage.Roots.Where(root => root.GoodsAcceptanceId is not null)
                     .Select(root => root.GoodsAcceptanceId!.Value).Distinct())
        {
            suppliers[acceptance] = await GuardedAsync(scope, permission,
                () => returns.FindAcceptanceSupplierAsync(acceptance, cancellationToken), cancellationToken);
        }

        return PurchaseSupplierReturnLineagePlannerV1.Plan(lineage, suppliers, order.SupplierPartyId, goodsAcceptanceId);
    }

    /// <summary>
    /// SAT-D444: the quantity return's supplier price is the chosen order line's unit price per kg in TL. Golden rows price a kg
    /// line; a piece or foreign-currency line is refused (409). A discounted or deferred line has no decided "unit price" for this
    /// rule (gross or net) and stays 503 (NEEDS-SINAN).
    /// </summary>
    private static decimal QuantityReturnUnitPrice(PurchaseOrder order, Guid lineId)
    {
        var line = order.Lines.SingleOrDefault(candidate => candidate.Id == lineId) ?? throw ReturnUnavailable();
        if (order.Money is not { } money || money.CurrencyId != SettingsCurrencyIdentitiesV1.TurkishLira
            || line.Quantity.Unit != QuantityUnit.Kilogram || line.Price is not { } price || price.PriceUnitId != line.UnitId
            || price.UnitPrice <= 0m)
        {
            throw new PurchasingServiceException(409, PurchaseSupplierReturnCodesV1.LineageReceiptMismatch,
                [new("goodsAcceptanceId", PurchaseSupplierReturnCodesV1.LineageReceiptMismatch)]);
        }

        if (price.DiscountPercent is not null || price.DiscountAmount is not null || price.HasDeferral
            || price.UnitPrice >= UnitCostUpperBound || decimal.Round(price.UnitPrice, UnitCostScale) != price.UnitPrice)
        {
            throw PurchaseSupplierReturnLineagePlannerV1.Unavailable();
        }

        return price.UnitPrice;
    }
}
