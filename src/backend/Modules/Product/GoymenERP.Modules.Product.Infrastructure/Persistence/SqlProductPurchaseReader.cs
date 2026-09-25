using GoymenERP.Modules.Product.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

public sealed record ProductPurchaseReadScope(Guid CompanyId, string ActorId);
public interface IProductPurchaseRecordReader
{
    Task<ProductStoredRecord?> ReadAsync(ProductPurchaseReadScope scope, Guid productId, CancellationToken cancellationToken);
}
public sealed record ProductPurchaseCandidateWindow(IReadOnlyList<ProductStoredRecord> Items, bool HasMore);
public interface IProductPurchaseCandidateReader
{
    Task<ProductPurchaseCandidateWindow> ReadAsync(ProductPurchaseReadScope scope,
        ProductPurchaseCandidateQueryV1 query, CancellationToken cancellationToken);
}
/// <summary>Global Product source; results are released only through the company-authorized purchase services.</summary>
public sealed class SqlProductPurchaseReader(ProductDbContext context, ProductReadStore records)
    : IProductPurchaseRecordReader, IProductPurchaseCandidateReader
{
    public async Task<ProductStoredRecord?> ReadAsync(ProductPurchaseReadScope scope, Guid productId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var row = await context.Products.AsNoTracking().SingleOrDefaultAsync(row => row.Id == productId, cancellationToken);
        return row?.ToRecord();
    }

    public async Task<ProductPurchaseCandidateWindow> ReadAsync(ProductPurchaseReadScope scope,
        ProductPurchaseCandidateQueryV1 query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var filter = ProductQueryRules.Normalize(new ProductListQuery(Q: query.Q, PageSize: query.Limit,
            Lifecycle: "Active", Roles: "purchasable"));
        var rows = await records.BuildQuery(filter, null).Take(query.Limit + 1).ToListAsync(cancellationToken);
        return new(rows.Take(query.Limit).Select(row => row.ToRecord()).ToArray(), rows.Count > query.Limit);
    }
}
