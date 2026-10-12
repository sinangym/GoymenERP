using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// Central adapter for an Inventory query over Product-owned, exact-version typed facts. Product
/// retains its own authority and source; Inventory receives only the label ids that match.
/// </summary>
public sealed class InventoryStockProductFilterHost(
    IProductStockTechnicalMatcherV1 products,
    ICompanyContext company,
    ICurrentUser user) : IInventoryStockProductFilterV1
{
    public async Task<IReadOnlySet<Guid>> MatchAsync(
        InventoryStockProductFilterRequestV1 request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Selections);
        ArgumentNullException.ThrowIfNull(request.Filter);
        RequireIdentity(request);
        if (request.Selections.Count is < 1 or > ProductStockDisplayCodesV1.MaximumBatchSize
            || request.Selections.Any(selection => selection.LabelId == Guid.Empty
                || selection.ProductId == Guid.Empty
                || string.IsNullOrWhiteSpace(selection.ProductVersion))
            || request.Selections.Select(selection => selection.LabelId).Distinct().Count()
                != request.Selections.Count)
        {
            throw Unavailable();
        }

        var groups = request.Selections.GroupBy(selection =>
            (selection.ProductId, selection.ProductVersion), ProductVersionKeyComparer.Instance).ToArray();
        var selections = groups.Select(group => new ProductStockTechnicalMatchSelectionV1(
            group.Key.ProductId, group.Key.ProductVersion)).ToArray();
        ProductStockTechnicalMatchBatchV1 answer;
        try
        {
            answer = await products.MatchAsync(new(
                selections,
                request.Filter.ProductFamilyId,
                request.Filter.ThicknessMm,
                request.Filter.ThicknessMinMm,
                request.Filter.ThicknessMaxMm,
                request.Filter.WidthMm,
                request.Filter.WidthMinMm,
                request.Filter.WidthMaxMm), cancellationToken);
        }
        catch (ProductOperationException exception)
            when (exception.StatusCode == StatusCodes.Status403Forbidden)
        {
            RequireIdentity(request);
            throw Forbidden();
        }
        RequireIdentity(request);
        if (answer.Matches.Count > selections.Length
            || answer.Matches.Distinct(ProductVersionKeyComparer.Instance).Count() != answer.Matches.Count)
        {
            throw Unavailable();
        }

        var requested = selections.ToHashSet(ProductVersionKeyComparer.Instance);
        if (answer.Matches.Any(match => !requested.Contains(match))) throw Unavailable();
        var matches = answer.Matches.ToHashSet(ProductVersionKeyComparer.Instance);

        RequireIdentity(request);
        return groups.Where(group => matches.Contains(new(group.Key.ProductId, group.Key.ProductVersion)))
            .SelectMany(group => group.Select(selection => selection.LabelId)).ToHashSet();
    }

    private void RequireIdentity(InventoryStockProductFilterRequestV1 request)
    {
        if (request.CompanyId == Guid.Empty || string.IsNullOrWhiteSpace(request.ActorId)
            || !company.IsResolved || company.CompanyId != request.CompanyId
            || !user.IsAuthenticated || !string.Equals(user.UserId, request.ActorId, StringComparison.Ordinal)
            || !user.CanAccessCompany(request.CompanyId))
        {
            throw Forbidden();
        }
    }

    private static InventoryStockPositionException Forbidden() =>
        new(StatusCodes.Status403Forbidden, InventoryStockPositionCodesV1.Forbidden);

    private static InventoryStockPositionException Unavailable() =>
        new(StatusCodes.Status503ServiceUnavailable, InventoryStockPositionCodesV1.Unavailable);

    private sealed class ProductVersionKeyComparer :
        IEqualityComparer<(Guid ProductId, string ProductVersion)>,
        IEqualityComparer<ProductStockTechnicalMatchSelectionV1>
    {
        public static ProductVersionKeyComparer Instance { get; } = new();

        public bool Equals((Guid ProductId, string ProductVersion) x,
            (Guid ProductId, string ProductVersion) y) =>
            x.ProductId == y.ProductId
            && string.Equals(x.ProductVersion, y.ProductVersion, StringComparison.Ordinal);

        public int GetHashCode((Guid ProductId, string ProductVersion) value) =>
            HashCode.Combine(value.ProductId, StringComparer.Ordinal.GetHashCode(value.ProductVersion));

        public bool Equals(ProductStockTechnicalMatchSelectionV1? x, ProductStockTechnicalMatchSelectionV1? y) =>
            ReferenceEquals(x, y) || x is not null && y is not null
            && x.ProductId == y.ProductId
            && string.Equals(x.ExpectedProductVersion, y.ExpectedProductVersion, StringComparison.Ordinal);

        public int GetHashCode(ProductStockTechnicalMatchSelectionV1 value) =>
            HashCode.Combine(value.ProductId, StringComparer.Ordinal.GetHashCode(value.ExpectedProductVersion));
    }
}
