using GoymenERP.Modules.Product.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

public sealed record ProductStockReadScope(Guid CompanyId, string ActorId);

public sealed record ProductStockCandidateWindow(
    IReadOnlyList<ProductStoredRecord> Items,
    bool HasMore);

/// <summary>Internal stored rows; only the fenced stock candidate service may release them.</summary>
public interface IProductStockCandidateReader
{
    Task<ProductStockCandidateWindow> ReadAsync(
        ProductStockReadScope scope,
        ProductStockCandidateQueryV1 query,
        CancellationToken cancellationToken);
}

public sealed class SqlProductStockCandidateReader(ProductReadStore records) : IProductStockCandidateReader
{
    public async Task<ProductStockCandidateWindow> ReadAsync(
        ProductStockReadScope scope,
        ProductStockCandidateQueryV1 query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var filter = ProductQueryRules.Normalize(new ProductListQuery(
            Q: query.Q,
            PageSize: query.Limit,
            Lifecycle: "Active"));
        var rows = await records.BuildQuery(filter, null)
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);
        return new(
            rows.Take(query.Limit).Select(row => row.ToRecord()).ToArray(),
            rows.Count > query.Limit);
    }
}
