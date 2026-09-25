using System.Globalization;
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

        // The newest recorded version must at least reach the card this caller just read.
        // GREATER is legitimate: an edit can commit between the two reads. SMALLER means the
        // newest events are missing, which no per-record or chain rule can see — the chain is
        // intact, it just stops early. This reader is the only layer holding both the card
        // and the events, so the comparison belongs here.
        if (!TryVersion(card.Product.Version, out var cardVersion)
            || !TryVersion(history.Entries[0].VersionAfter, out var newest)
            || newest < cardVersion)
        {
            throw new InvalidOperationException("The Product history does not reach the card's version.");
        }

        return history;
    }

    /// <summary>A stored version is a positive int64 in canonical decimal form, never a double.</summary>
    private static bool TryVersion(string value, out long parsed) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) && parsed >= 1;
}
