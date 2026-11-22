using System.Globalization;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.PurchasingRuntime;

/// <summary>
/// AK-8 / AK-8b read-only owner bridge: Inventory's label lineage plan (roots in receipt order, exact shares, the SAT-D440 /
/// SAT-D442 distribution, each receipt's open, invoiced and pending kilograms, the label's stock and whether its moving average
/// is readable) adapted for Purchasing's return planning. Price-free. Inventory's closed refusals keep their status and code.
/// A remaining part that is not a three-decimal kilogram is cut DOWN to three decimals: the preparer is never offered more than
/// what is left.
/// </summary>
internal sealed class PurchaseSupplierReturnLineage(IInventorySupplierReturnLineageReadPortV1 inventory)
    : IPurchaseSupplierReturnLineageReadPortV1
{
    public async Task<PurchaseSupplierReturnLineageV1> PlanAsync(Guid companyId, Guid labelId, string quantityKg, int? pieceCount,
        CancellationToken cancellationToken)
    {
        InventorySupplierReturnLineagePlanV1 plan;
        try { plan = await inventory.PlanAsync(companyId, labelId, quantityKg, pieceCount, cancellationToken); }
        catch (InventorySupplierReturnExitException exception)
        { throw new PurchasingServiceException(exception.Status, exception.Code, innerException: exception); }
        var lineage = plan.Lineage;
        if (lineage.CompanyId != companyId || lineage.LabelId != labelId
            || plan.Receipts.Any(receipt => receipt.CompanyId != companyId))
            throw new PurchasingServiceException(503, PurchaseSupplierReturnCodesV1.LineageUnavailable);
        return new PurchaseSupplierReturnLineageV1(labelId, lineage.Roots.Select(root => new PurchaseSupplierReturnLineageRootV1(
            root.Kind == InventorySupplierReturnLineageRootKindV1.PurchaseReceipt, root.GoodsAcceptanceId,
            root.Share.Numerator, root.Share.Denominator,
            plan.Receipts.SingleOrDefault(receipt => receipt.GoodsAcceptanceId == root.GoodsAcceptanceId) is { } state ? Map(state) : null))
            .ToArray(), plan.TracksPieces, plan.FreeKg, plan.BlockedKg, plan.FreePieces, plan.BlockedPieces, plan.AverageCostReadable);
    }

    public async Task<PurchaseSupplierReturnReceiptStateV1> ReadReceiptAsync(Guid companyId, Guid goodsAcceptanceId,
        CancellationToken cancellationToken)
    {
        InventorySupplierReturnReceiptStateV1 state;
        try { state = await inventory.ReadReceiptAsync(companyId, goodsAcceptanceId, cancellationToken); }
        catch (InventorySupplierReturnExitException exception)
        { throw new PurchasingServiceException(exception.Status, exception.Code, innerException: exception); }
        if (state.CompanyId != companyId || state.GoodsAcceptanceId != goodsAcceptanceId)
            throw new PurchasingServiceException(503, PurchaseSupplierReturnCodesV1.LineageUnavailable);
        return Map(state);
    }

    public async Task<PurchaseSupplierReturnStockAmountV1?> ReadStockAmountAsync(Guid companyId, Guid labelId, string quantityKg,
        CancellationToken cancellationToken)
    {
        InventorySupplierReturnStockAmountV1 amount;
        try { amount = await inventory.ReadStockAmountAsync(companyId, labelId, quantityKg, cancellationToken); }
        catch (InventorySupplierReturnExitException exception)
        { throw new PurchasingServiceException(exception.Status, exception.Code, innerException: exception); }
        if (amount.CompanyId != companyId || amount.LabelId != labelId)
            throw new PurchasingServiceException(503, PurchaseSupplierReturnCodesV1.LineageUnavailable);
        if (amount.StockExitAmountTry is null || amount.AverageCostTryPerKg is null) return null;
        return decimal.TryParse(amount.StockExitAmountTry, System.Globalization.NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var exit)
            && decimal.TryParse(amount.AverageCostTryPerKg, System.Globalization.NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var average)
            && exit > 0m && average > 0m
            ? new PurchaseSupplierReturnStockAmountV1(exit, average)
            : throw new PurchasingServiceException(503, PurchaseSupplierReturnCodesV1.LineageUnavailable);
    }

    internal static PurchaseSupplierReturnReceiptStateV1 Map(InventorySupplierReturnReceiptStateV1 state) => new(
        state.GoodsAcceptanceId, state.ReceivedKg, state.OpenKg, state.PartsReadable,
        state.PendingRemainingKg is { } pending ? FloorKg(pending) : null,
        state.Invoiced.Select(item => new PurchaseSupplierReturnInvoicedPartV1(item.SourceEventId, item.InvoiceId, item.InvoiceLineId,
            FloorKg(item.RemainingKg))).ToArray(),
        state.DistributedKg, state.DistributedPieces);

    internal static string FloorKg(InventoryExactCostRatioV1 ratio)
    {
        var exact = ratio.Value();
        var rounded = exact.Round(3);
        if (ExactCostRatio.Of(rounded).CompareTo(exact) > 0) rounded -= 0.001m;
        return rounded.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
