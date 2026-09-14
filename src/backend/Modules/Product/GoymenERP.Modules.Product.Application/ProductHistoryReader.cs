using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Modules.Product.Application;

/// <summary>
/// U-01 Product 360 history read. The card read proves the Product exists under the
/// caller's current global read authority (404 otherwise, the same non-disclosure as the
/// card); the Audit owner's source then authorizes and fences its own read.
/// </summary>
public sealed class ProductHistoryReader(IProductService products, IProductAuditHistorySourceV1 audit)
{
    public async Task<ProductHistoryView> GetAsync(Guid productId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (productId == Guid.Empty) throw new ProductOperationException(ProductApiV1.NotFound, 404);
        var card = await products.GetAsync(productId, cancellationToken)
            ?? throw new ProductOperationException(ProductApiV1.NotFound, 404);
        if (card.Product.Id != productId) throw new InvalidOperationException("The Product card does not match the request.");

        var records = await audit.ReadNewestAsync(productId, ProductHistoryApiV1.PageSize + 1, cancellationToken)
            ?? throw new InvalidOperationException("The Product audit source returned no result.");
        cancellationToken.ThrowIfCancellationRequested();
        var history = ProductHistoryProjector.Project(productId, records);
        // A Product always has its creation event. An empty history for an existing card is an
        // audit integrity gap, not "nothing happened".
        if (history.Entries.Count == 0) throw new InvalidOperationException("The Product has no recorded history.");
        return history;
    }
}
