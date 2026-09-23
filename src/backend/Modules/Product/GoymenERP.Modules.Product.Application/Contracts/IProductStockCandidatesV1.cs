namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>
/// Product-owned bounded source for stock-operation selectors. Every returned row is a current
/// <see cref="IProductStockEligibilityV1"/> positive for the requested company and date; it is not
/// stock availability, allocation, reservation or a Product-master read.
/// </summary>
public interface IProductStockCandidatesV1
{
    Task<ProductStockCandidatesV1> SearchAsync(ProductStockCandidateQueryV1 query,
        CancellationToken cancellationToken);
}

public sealed record ProductStockCandidateQueryV1(string? Q, DateOnly EffectiveOn, int Limit = 25);

public sealed record ProductStockCandidatesV1(
    IReadOnlyList<ProductStockCandidateV1> Items,
    bool SearchWindowFull);

public sealed record ProductStockCandidateV1(
    Guid ProductId,
    string ProductVersion,
    string Code,
    string Name,
    Guid StockUnitId,
    string StockUnitCode,
    Guid ProfileId,
    string ProfileVersion);
