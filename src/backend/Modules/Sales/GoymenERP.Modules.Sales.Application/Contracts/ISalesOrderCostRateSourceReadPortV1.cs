namespace GoymenERP.Modules.Sales.Application.Contracts;

public enum SalesOrderCostRateSourceOutcomeV1 { Available = 1, NotFound = 2, Unavailable = 3 }

/// <summary>FNS-D73/D83/D107: the Sales owner's exact approved revision, including historical revisions.
/// No rate field exists on a Sales order; D83 supplies the order date and TCMB selling type when those fields are absent.
/// This read supplies facts only. The host resolves the decided default through Settings and never substitutes a receipt.</summary>
public interface ISalesOrderCostRateSourceReadPortV1
{
    Task<SalesOrderCostRateSourceV1> GetAsync(Guid companyId, Guid orderId, Guid lineId, int revision, CancellationToken cancellationToken = default);
}

public sealed record SalesOrderCostRateSourceV1(SalesOrderCostRateSourceOutcomeV1 Outcome,
    Guid CompanyId, Guid OrderId, Guid LineId, int Revision, long? OrderVersion, Guid? OrderCurrencyId,
    DateOnly? RateDay, string? RateType, string SourceVersion);
