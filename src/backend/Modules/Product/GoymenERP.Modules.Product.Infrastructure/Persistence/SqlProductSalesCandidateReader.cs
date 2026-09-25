using GoymenERP.Modules.Product.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

public sealed record ProductSalesCandidateWindow(IReadOnlyList<ProductStoredRecord> Items, bool HasMore);

/// <summary>Internal stored candidates, released only through the authorized eligibility service.</summary>
public interface IProductSalesCandidateReader
{
    Task<ProductSalesCandidateWindow> ReadAsync(ProductSalesReadScope scope, ProductSalesCandidateQueryV1 query,
        CancellationToken cancellationToken);
}

public sealed class SqlProductSalesCandidateReader(ProductReadStore records) : IProductSalesCandidateReader
{
    public async Task<ProductSalesCandidateWindow> ReadAsync(ProductSalesReadScope scope,
        ProductSalesCandidateQueryV1 query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var filter = ProductQueryRules.Normalize(new ProductListQuery(Q: query.Q, PageSize: query.Limit,
            Lifecycle: "Active", Roles: "sellable"));
        var rows = await records.BuildQuery(filter, null).Take(query.Limit + 1).ToListAsync(cancellationToken);
        return new(rows.Take(query.Limit).Select(row => row.ToRecord()).ToArray(), rows.Count > query.Limit);
    }
}
