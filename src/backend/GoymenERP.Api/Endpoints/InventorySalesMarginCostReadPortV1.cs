using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.Endpoints;

/// <summary>Owner boundary only: Inventory calculates cost and conversion; Sales authorizes and pins the exact answer.</summary>
public sealed class InventorySalesMarginCostReadPortV1(
    ICompanyContext companyContext, IInventoryAverageCostUnitReadPortV1 costs) : ISalesMarginCostReadPortV1
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<SalesMarginCostReadAnswerV1> ReadAsync(Guid companyId, Guid productId, Guid unitId,
        DateOnly effectiveOn, CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty || companyContext.CompanyId != companyId || productId == Guid.Empty || unitId == Guid.Empty)
            throw new InvalidOperationException("Marj maliyeti şirket bağlamıyla uyuşmuyor.");
        var answer = await costs.ReadCompanyWideInUnitAsync(productId, unitId, effectiveOn, cancellationToken);
        if (companyContext.CompanyId != companyId || answer is null || answer.ContractVersion != 1
            || answer.CompanyId != companyId || answer.ProductId != productId || answer.UnitId != unitId)
            throw new InvalidOperationException("Stok maliyet kaynağı istekle uyuşmuyor.");
        SalesLineMarginUnavailableReason? reason = answer.Status switch
        {
            // SAT-D438 / AK-6: the margin of a line in another unit than the kg (a converted cost, named by its policy) stays
            // closed until its golden is on main; AK-1's Product kg/unit source must not open it. Only the kg cost passes.
            InventoryAverageCostUnitReadStatusV1.Value when answer.PolicyProfileId is not null
                => SalesLineMarginUnavailableReason.UnitConversionUndecided,
            InventoryAverageCostUnitReadStatusV1.Value when answer.CostCurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira => null,
            InventoryAverageCostUnitReadStatusV1.Value => SalesLineMarginUnavailableReason.CurrencyConversionUndecided,
            InventoryAverageCostUnitReadStatusV1.NoSource => SalesLineMarginUnavailableReason.CostNoSource,
            InventoryAverageCostUnitReadStatusV1.Unreadable => SalesLineMarginUnavailableReason.CostUnreadable,
            InventoryAverageCostUnitReadStatusV1.ConversionRefused => SalesLineMarginUnavailableReason.UnitConversionUndecided,
            _ => throw new InvalidOperationException("Stok maliyet durumu geçersiz."),
        };
        return new(1, companyId, productId, unitId, reason is null ? answer.CostCurrencyId : null,
            reason is null ? answer.CostPerUnit : null, reason,
            JsonSerializer.Serialize(new { effectiveOn, source = "inventory.company-wide-average-cost.unit.v1", answer }, Json));
    }
}
