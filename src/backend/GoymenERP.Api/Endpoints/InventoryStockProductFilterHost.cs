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
    IProductStockDisplayReferencesV1 products,
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

        var matches = new HashSet<Guid>();
        var pending = request.Selections
            .GroupBy(selection => selection.ProductId)
            .ToDictionary(group => group.Key,
                group => new Queue<IGrouping<string, InventoryStockProductSelectionV1>>(
                    group.GroupBy(selection => selection.ProductVersion, StringComparer.Ordinal)));

        while (pending.Count > 0)
        {
            var layer = pending.Select(entry => entry.Value.Dequeue()).ToArray();
            foreach (var productId in pending.Where(entry => entry.Value.Count == 0)
                         .Select(entry => entry.Key).ToArray())
            {
                pending.Remove(productId);
            }

            var selections = layer.Select(group => new ProductStockDisplaySelectionV1(
                group.First().ProductId, group.Key)).ToArray();
            ProductStockDisplayBatchV1 answer;
            try
            {
                answer = await products.ReadAsync(new(selections), cancellationToken);
            }
            catch (ProductOperationException exception)
                when (exception.StatusCode == StatusCodes.Status403Forbidden)
            {
                RequireIdentity(request);
                throw Forbidden();
            }
            RequireIdentity(request);
            if (answer.Items.Count != selections.Length
                || answer.Items.Select(item => item.ProductId).Distinct().Count() != answer.Items.Count)
            {
                throw Unavailable();
            }

            var byProduct = answer.Items.ToDictionary(item => item.ProductId);
            foreach (var group in layer)
            {
                var sample = group.First();
                if (!byProduct.TryGetValue(sample.ProductId, out var result)
                    || !string.Equals(result.ExpectedProductVersion, group.Key, StringComparison.Ordinal))
                {
                    throw Unavailable();
                }
                if (result.Technical is not { } technical || !Matches(technical, request.Filter))
                {
                    continue;
                }
                foreach (var selection in group) matches.Add(selection.LabelId);
            }
        }

        RequireIdentity(request);
        return matches;
    }

    private static bool Matches(ProductStockNominalTechnicalV1 technical, InventoryStockProductFilterV1 filter) =>
        (!filter.ProductFamilyId.HasValue || technical.Family.FamilyId == filter.ProductFamilyId)
        && (!filter.ThicknessMm.HasValue || technical.ThicknessMm == filter.ThicknessMm)
        && (!filter.ThicknessMinMm.HasValue || technical.ThicknessMm >= filter.ThicknessMinMm)
        && (!filter.ThicknessMaxMm.HasValue || technical.ThicknessMm <= filter.ThicknessMaxMm)
        && (!filter.WidthMm.HasValue || technical.WidthMm == filter.WidthMm)
        && (!filter.WidthMinMm.HasValue || technical.WidthMm >= filter.WidthMinMm)
        && (!filter.WidthMaxMm.HasValue || technical.WidthMm <= filter.WidthMaxMm);

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
}
