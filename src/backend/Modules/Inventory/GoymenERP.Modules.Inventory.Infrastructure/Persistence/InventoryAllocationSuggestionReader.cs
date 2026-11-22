using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Allocations;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// KLN-D34/D39/D40/D70 (STK-S30): the label suggestions for one approved Sales line, over Inventory's own
/// company-filtered free positions. Untracked and side-effect free; the create command re-checks every rule in its own
/// transaction. A label is offered only when it has exactly one free position, that position is company-owned and
/// company-held (the create's own rule), and its free kilograms minus active allocations are positive — so a suggestion
/// can never lead to negative stock. The line's own product comes first, closest to the requested kilograms first when
/// the user stated them (KLN-D99); only when it has no such label are similar products (Settings tolerance) searched,
/// closest first. Nothing is selected for the user.
/// </summary>
internal sealed class InventoryAllocationSuggestionReader(
    InventoryDbContext dbContext,
    ICompanyContext companyContext,
    IInventorySimilarProductToleranceSourceV1 tolerances,
    IInventoryProductSimilarityFactsV1 products,
    IInventoryAllocationAiSuggestionToolV1 aiTool) : IInventoryAllocationSuggestionReaderV1
{
    public async Task<InventoryAllocationSuggestionsV1> ReadAsync(
        InventoryAllocationSuggestionQueryV1 query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            throw new InvalidOperationException("Rezervasyon önerileri yalnız çözümlenmiş şirket bağlamında okunur.");
        }

        if (query.SalesOrderId == Guid.Empty || query.SalesOrderLineId == Guid.Empty || query.ProductId == Guid.Empty
            || string.IsNullOrWhiteSpace(query.ProductVersion))
        {
            throw new InventoryAllocationException(400, InventoryAllocationCodesV1.ValidationFailed);
        }

        var companyId = companyContext.CompanyId;
        var aiState = aiTool.IsEnabled
            ? InventoryAllocationAiSuggestionStatesV1.Enabled
            : InventoryAllocationAiSuggestionStatesV1.Disabled;
        var tolerance = await InventorySimilarityFacts.ToleranceAsync(tolerances, companyId, cancellationToken);
        var mode = tolerance is null
            ? InventorySimilarProductModesV1.SameProductOnly
            : InventorySimilarProductModesV1.DimensionTolerance;

        var same = await EligibleAsync(companyId, query.ProductId, sameProduct: true, cancellationToken);
        if (same.Labels.Count > 0 || tolerance is null)
        {
            // KLN-D99: with a requested quantity the line's own labels are listed closest to it first (ties by label
            // code); the user still picks by hand. Without one the window's label-code order is kept.
            var ordered = query.RequestedKg is { } requestedKg
                ? same.Labels.OrderBy(label => label, Comparer<EligibleLabel>.Create((left, right) =>
                    InventoryLabelQuantityProximityV1.Compare(
                        left.UnreservedKg, left.Code, right.UnreservedKg, right.Code, requestedKg)))
                : same.Labels.AsEnumerable();
            var sameItems = ordered
                .Select(label => Item(label, null, InventoryAllocationSuggestionKindsV1.SameProduct, null))
                .ToList();
            return Answer(query, companyId, mode, aiState, same.Truncated, sameItems);
        }

        var similar = await EligibleAsync(companyId, query.ProductId, sameProduct: false, cancellationToken);
        var truncated = similar.Truncated;
        var requested = new InventoryProductVersionV1(query.ProductId, query.ProductVersion);
        var candidates = similar.Labels
            .Where(label => !string.IsNullOrWhiteSpace(label.ProductVersion))
            .Select(label => new InventoryProductVersionV1(label.ProductId, label.ProductVersion!))
            .Distinct()
            .OrderBy(pair => pair.ProductId)
            .ThenBy(pair => pair.ProductVersion, StringComparer.Ordinal)
            .ToList();
        if (candidates.Count > InventoryAllocationSuggestionLimitsV1.MaximumCandidateProducts)
        {
            candidates = candidates.Take(InventoryAllocationSuggestionLimitsV1.MaximumCandidateProducts).ToList();
            truncated = true;
        }

        if (candidates.Count == 0)
        {
            return Answer(query, companyId, mode, aiState, truncated, []);
        }

        var facts = await InventorySimilarityFacts.ReadAsync(products, [requested, .. candidates], cancellationToken);
        var scored = new List<(EligibleLabel Label, InventoryProductSimilarityFactsV1 Facts, InventorySimilarityScoreV1 Score)>();
        foreach (var label in similar.Labels)
        {
            if (string.IsNullOrWhiteSpace(label.ProductVersion)
                || !facts.TryGetValue(new InventoryProductVersionV1(label.ProductId, label.ProductVersion), out var offered)
                || InventorySimilarProductCalculatorV1.Evaluate(facts[requested], offered, tolerance) is not { } score)
            {
                continue;
            }

            scored.Add((label, offered, score));
        }

        scored.Sort((left, right) =>
            InventorySimilarProductCalculatorV1.Compare(left.Score, left.Label.Code, right.Score, right.Label.Code));
        var items = scored
            .Select(entry => Item(entry.Label, entry.Facts, InventoryAllocationSuggestionKindsV1.SimilarProduct,
                entry.Score.MaximumDeviationPercentText))
            .ToList();
        items = await ApplyAiAsync(companyId, requested, items, cancellationToken);
        return Answer(query, companyId, mode, aiState, truncated, items);
    }

    /// <summary>
    /// KLN-D40: an enabled AI tool may only narrow the rule-eligible list (it names label ids; unknown ids are ignored)
    /// and never reorders or adds. While no provider is configured the tool is disabled and never called.
    /// </summary>
    private async Task<List<InventoryAllocationSuggestionV1>> ApplyAiAsync(
        Guid companyId,
        InventoryProductVersionV1 requested,
        List<InventoryAllocationSuggestionV1> items,
        CancellationToken cancellationToken)
    {
        if (!aiTool.IsEnabled || items.Count == 0)
        {
            return items;
        }

        var chosen = (await aiTool.SuggestLabelsAsync(
                new InventoryAllocationAiSuggestionInputV1(companyId, requested.ProductId, requested.ProductVersion,
                    [.. items.Select(item => item.LabelId)]),
                cancellationToken))
            .ToHashSet();
        return items.Where(item => chosen.Contains(item.LabelId)).ToList();
    }

    private static InventoryAllocationSuggestionsV1 Answer(
        InventoryAllocationSuggestionQueryV1 query,
        Guid companyId,
        string mode,
        string aiState,
        bool truncated,
        List<InventoryAllocationSuggestionV1> items)
    {
        var cut = items.Count > InventoryAllocationSuggestionLimitsV1.MaximumSuggestions;
        return new InventoryAllocationSuggestionsV1(
            companyId,
            query.SalesOrderId,
            query.SalesOrderLineId,
            query.ProductId,
            query.ProductVersion,
            mode,
            aiState,
            truncated || cut,
            [.. items.Take(InventoryAllocationSuggestionLimitsV1.MaximumSuggestions)],
            query.RequestedKg?.ToString());
    }

    private static InventoryAllocationSuggestionV1 Item(
        EligibleLabel label,
        InventoryProductSimilarityFactsV1? facts,
        string kind,
        string? maximumDeviationPercent) =>
        new(
            label.LabelId,
            label.Version.ToString(CultureInfo.InvariantCulture),
            label.Code,
            label.ProductId,
            label.ProductVersion,
            facts?.ProductCode,
            facts?.ProductName,
            kind,
            string.Equals(kind, InventoryAllocationSuggestionKindsV1.SimilarProduct, StringComparison.Ordinal),
            Kilograms.Of(label.UnreservedKg).ToString(),
            maximumDeviationPercent,
            label.UnreservedPieces);

    /// <summary>
    /// The labels with exactly one free position that is company-owned and company-held and still has unreserved
    /// kilograms, for the line's product (<paramref name="sameProduct"/>) or for every other product. The window is
    /// ordered by label code; a label cut by the window edge is dropped and the answer is marked truncated.
    /// </summary>
    private async Task<EligibleWindow> EligibleAsync(
        Guid companyId,
        Guid productId,
        bool sameProduct,
        CancellationToken cancellationToken)
    {
        var free = (int)BlockState.Free;
        var balances = dbContext.Balances.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.Block == free && row.QuantityKg > 0m);
        balances = sameProduct
            ? balances.Where(row => row.ProductId == productId)
            : balances.Where(row => row.ProductId != productId);
        var window = await (
                from balance in balances
                join label in dbContext.Labels.AsNoTracking() on balance.LabelId equals label.Id
                where label.CompanyId == companyId && label.ProductId == balance.ProductId
                orderby label.Code, label.Id, balance.Id
                select new FreePosition(
                    label.Id, label.Code, label.Version, label.ProductId, label.ProductVersion,
                    balance.OwnerKind, balance.OwnerId, balance.CustodianKind, balance.CustodianId,
                    balance.LocationId, balance.QuantityKg, balance.PieceCount))
            .Take(InventoryAllocationSuggestionLimitsV1.MaximumScannedPositions + 1)
            .ToListAsync(cancellationToken);

        var truncated = window.Count > InventoryAllocationSuggestionLimitsV1.MaximumScannedPositions;
        var groups = window.GroupBy(position => position.LabelId).ToList();
        if (truncated && groups.Count > 0)
        {
            // The last label's positions may continue past the window edge: never judge it on a partial view.
            groups.RemoveAt(groups.Count - 1);
        }

        var candidates = groups
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .Where(position => InventorySalesOwnershipGuardV1.IsCompanyOwnedAndHeld(
                (StockPartyKind)position.OwnerKind, position.OwnerId,
                (StockPartyKind)position.CustodianKind, position.CustodianId, companyId))
            .ToList();
        if (candidates.Count == 0)
        {
            return new EligibleWindow([], truncated);
        }

        var labelIds = candidates.Select(position => position.LabelId).ToArray();
        var active = (int)StockAllocationStatus.Active;
        var allocations = await dbContext.Allocations.AsNoTracking()
            .Where(row => row.CompanyId == companyId && labelIds.Contains(row.LabelId)
                && row.Block == free && row.Status == active)
            .Select(row => new ActiveAllocation(row.LabelId, row.OwnerKind, row.OwnerId, row.CustodianKind,
                row.CustodianId, row.LocationId, row.RemainingKg, row.RemainingPieces))
            .ToListAsync(cancellationToken);

        var labels = new List<EligibleLabel>();
        foreach (var position in candidates)
        {
            var held = allocations
                .Where(allocation => allocation.LabelId == position.LabelId
                    && allocation.OwnerKind == position.OwnerKind && allocation.OwnerId == position.OwnerId
                    && allocation.CustodianKind == position.CustodianKind
                    && allocation.CustodianId == position.CustodianId
                    && allocation.LocationId == position.LocationId)
                .ToList();
            var unreserved = position.QuantityKg - held.Sum(allocation => allocation.RemainingKg);
            if (unreserved > 0m)
            {
                // KLN-D119 (AK-4b): a piece-tracking label says how many pieces no active allocation holds (the create's own
                // rule, StockAllocation.UnreservedPieces); a kg-only label says nothing.
                int? unreservedPieces = position.PieceCount is { } pieces
                    ? StockAllocation.UnreservedPieces(PieceCount.Of(pieces),
                        PieceCount.Of(held.Sum(allocation => allocation.RemainingPieces ?? 0))).Value
                    : null;
                labels.Add(new EligibleLabel(position.LabelId, position.Code, position.Version, position.ProductId,
                    position.ProductVersion, unreserved, unreservedPieces));
            }
        }

        return new EligibleWindow(labels, truncated);
    }

    private sealed record FreePosition(
        Guid LabelId,
        string Code,
        long Version,
        Guid ProductId,
        string? ProductVersion,
        int OwnerKind,
        Guid OwnerId,
        int CustodianKind,
        Guid CustodianId,
        Guid LocationId,
        decimal QuantityKg,
        int? PieceCount);

    private sealed record ActiveAllocation(
        Guid LabelId,
        int OwnerKind,
        Guid OwnerId,
        int CustodianKind,
        Guid CustodianId,
        Guid LocationId,
        decimal RemainingKg,
        int? RemainingPieces);

    private sealed record EligibleLabel(
        Guid LabelId,
        string Code,
        long Version,
        Guid ProductId,
        string? ProductVersion,
        decimal UnreservedKg,
        int? UnreservedPieces);

    private sealed record EligibleWindow(IReadOnlyList<EligibleLabel> Labels, bool Truncated);
}
