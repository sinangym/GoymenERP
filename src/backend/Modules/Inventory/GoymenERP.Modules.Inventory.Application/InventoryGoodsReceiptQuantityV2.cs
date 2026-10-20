using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// Adapts the existing Product kilogram goods-receipt path to the stock-item quantity V2 contract.
/// Purchase quantity is a commercial fact and is never exposed as an alternate physical quantity.
/// </summary>
public static class InventoryGoodsReceiptQuantityV2
{
    public static InventoryStockItemQuantityV2 Create(
        InventoryGoodsReceiptRequestV1 request,
        Kilograms quantity,
        InventoryKilogramProfile? profile)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (profile is null
            || profile.CompanyId != request.CompanyId
            || profile.UnitId != SettingsUnitIdentitiesV1.Kilogram
            || profile.ProfileId == Guid.Empty
            || profile.ProfileVersion <= 0
            || profile.Scale is < 0 or > Kilograms.MaximumDecimals)
        {
            throw InventoryGoodsReceiptException.Refused(
                503, InventoryGoodsReceiptCodesV1.QuantityProfileUnavailable);
        }

        if (request.CompanyId == Guid.Empty || request.ProductId == Guid.Empty
            || request.AcceptanceId == Guid.Empty)
        {
            throw InventoryGoodsReceiptException.Refused(400, InventoryGoodsReceiptCodesV1.InvalidRequest);
        }

        try
        {
            var baseQuantity = StockQuantityV2.Positive(
                quantity.Value,
                profile.UnitId,
                profile.ProfileId,
                profile.ProfileVersion,
                StockQuantityDimensionV2.Mass,
                profile.Scale);
            return new InventoryStockItemQuantityV2(
                request.CompanyId,
                InventoryStockItemV2.Product(request.ProductId),
                new InventoryStockQuantitySourceV2(
                    InventoryGoodsReceiptContractV1.SourceKind,
                    request.AcceptanceId,
                    request.PurchaseOrderVersionAfter),
                baseQuantity,
                alternateQuantity: null);
        }
        catch (StockQuantityRuleExceptionV2 exception)
            when (exception.Code is StockQuantityRuleCodesV2.QuantityNotPositive
                or StockQuantityRuleCodesV2.ScaleExceeded)
        {
            throw InventoryGoodsReceiptException.Refused(400, InventoryGoodsReceiptCodesV1.InvalidQuantity);
        }
        catch (Exception exception) when (exception is StockQuantityRuleExceptionV2
            or InventoryStockItemQuantityContractExceptionV2)
        {
            throw InventoryGoodsReceiptException.Refused(
                503, InventoryGoodsReceiptCodesV1.QuantityProfileUnavailable);
        }
    }
}
