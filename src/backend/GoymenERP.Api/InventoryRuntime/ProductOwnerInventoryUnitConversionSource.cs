using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.BuildingBlocks.Multitenancy;

namespace GoymenERP.Api.InventoryRuntime;

/// <summary>Only adapts owner DTOs; all kg arithmetic and coefficient rounding stay in Product's calculator.</summary>
public sealed class ProductOwnerInventoryUnitConversionSource(IProductPurchaseUnitConversionV1 product,
    TimeProvider time, ICompanyContext company) : IInventoryProductUnitConversionSourceV1
{
    public async Task<InventoryProductUnitConversionAnswerV1> ConvertAsync(Guid productId, decimal quantity,
        Guid fromUnitId, Guid? toUnitId, CancellationToken cancellationToken = default)
    {
        if (!company.IsResolved || company.CompanyId == Guid.Empty)
            return new(InventoryProductUnitConversionOutcomeV1.Denied, null);
        var companyId = company.CompanyId;
        var result = await product.ConvertAsync(new(productId, quantity, fromUnitId, toUnitId,
            ProductPriceBusinessDayV2.DateOf(time.GetUtcNow())), cancellationToken);
        if (!company.IsResolved || company.CompanyId != companyId)
            return new(InventoryProductUnitConversionOutcomeV1.Denied, null);
        return result.Outcome switch
        {
            ProductPurchaseUnitConversionOutcomeV1.Available when result.Source is { ProductId: var actual }
                && actual == productId && result.Source.CompanyId == companyId
                && result.Quantity is >= 0m && result.RemainderKg is not null =>
                new(InventoryProductUnitConversionOutcomeV1.Converted, result.Quantity, result.RemainderKg),
            ProductPurchaseUnitConversionOutcomeV1.NoSource or ProductPurchaseUnitConversionOutcomeV1.NotFound
                or ProductPurchaseUnitConversionOutcomeV1.Refused => new(InventoryProductUnitConversionOutcomeV1.NoSource, null),
            ProductPurchaseUnitConversionOutcomeV1.Denied => new(InventoryProductUnitConversionOutcomeV1.Denied, null),
            _ => new(InventoryProductUnitConversionOutcomeV1.Unavailable, null),
        };
    }
}
