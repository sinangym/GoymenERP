using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>Inventory-owned company-wide purchase cost in the requested unit. No authority is conferred.</summary>
public interface ISalesMarginCostReadPortV1
{
    Task<SalesMarginCostReadAnswerV1> ReadAsync(Guid companyId, Guid productId, Guid unitId,
        DateOnly effectiveOn, CancellationToken cancellationToken = default);
}

public sealed record SalesMarginCostReadAnswerV1(
    int ContractVersion, Guid CompanyId, Guid ProductId, Guid UnitId,
    Guid? CurrencyId, decimal? CostPerUnit, SalesLineMarginUnavailableReason? UnavailableReason, string SourceProof);
