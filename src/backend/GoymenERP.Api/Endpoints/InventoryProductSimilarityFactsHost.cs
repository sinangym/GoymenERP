using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// KLN-D39/D70 (STK-S30): central adapter from the Product-owned similarity facts to Inventory's similar-product
/// check. Product keeps its own authority, records and definition; Inventory receives only the exact-version facts it
/// asked for. Any Product failure other than a refused authority is 503, and an answer that does not hold exactly one
/// item per requested pair is never trusted.
/// </summary>
public sealed class InventoryProductSimilarityFactsHost(
    IProductStockSimilarityFactsV1 products,
    ICompanyContext company,
    ICurrentUser user) : IInventoryProductSimilarityFactsV1
{
    public async Task<IReadOnlyList<InventoryProductSimilarityFactsV1>> ReadAsync(
        IReadOnlyList<InventoryProductVersionV1> selections,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selections);
        RequireIdentity();
        if (selections.Count is < 1 or > ProductStockDisplayCodesV1.MaximumBatchSize
            || selections.Any(selection => selection is null || selection.ProductId == Guid.Empty
                || string.IsNullOrWhiteSpace(selection.ProductVersion))
            || selections.Distinct().Count() != selections.Count)
        {
            throw Unavailable();
        }

        ProductStockSimilarityBatchV1 answer;
        try
        {
            answer = await products.ReadSimilarityAsync(new(
                [.. selections.Select(selection =>
                    new ProductStockTechnicalMatchSelectionV1(selection.ProductId, selection.ProductVersion))]),
                cancellationToken);
        }
        catch (ProductOperationException exception)
            when (exception.StatusCode == StatusCodes.Status403Forbidden)
        {
            RequireIdentity();
            throw Forbidden();
        }
        catch (ProductOperationException exception)
        {
            RequireIdentity();
            throw Unavailable(exception);
        }

        RequireIdentity();
        if (answer?.Items is null || answer.Items.Count != selections.Count)
        {
            throw Unavailable();
        }

        var requested = selections.ToHashSet();
        var facts = new List<InventoryProductSimilarityFactsV1>(answer.Items.Count);
        var seen = new HashSet<InventoryProductVersionV1>();
        foreach (var item in answer.Items)
        {
            if (item is null)
            {
                throw Unavailable();
            }

            var key = new InventoryProductVersionV1(item.ProductId, item.ExpectedProductVersion);
            if (!requested.Contains(key) || !seen.Add(key)
                || item.Identity is { } identity && (identity.ProductId != item.ProductId
                    || !string.Equals(identity.ProductVersion, item.ExpectedProductVersion, StringComparison.Ordinal)))
            {
                throw Unavailable();
            }

            facts.Add(new InventoryProductSimilarityFactsV1(
                item.ProductId,
                item.ExpectedProductVersion,
                item.Identity?.Code,
                item.Identity?.Name,
                item.SimilarityKey,
                item.ThicknessMm,
                item.WidthMm,
                item.LengthMm,
                item.CoatingCode));
        }

        return facts;
    }

    private void RequireIdentity()
    {
        if (!company.IsResolved || company.CompanyId == Guid.Empty
            || !user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId)
            || !user.CanAccessCompany(company.CompanyId))
        {
            throw Forbidden();
        }
    }

    private static InventoryAllocationException Forbidden() =>
        new(StatusCodes.Status403Forbidden, InventoryAllocationSuggestionCodesV1.ProductFactsForbidden);

    private static InventoryAllocationException Unavailable(Exception? innerException = null) =>
        new(StatusCodes.Status503ServiceUnavailable, InventoryAllocationSuggestionCodesV1.ProductFactsUnavailable,
            innerException: innerException);
}
