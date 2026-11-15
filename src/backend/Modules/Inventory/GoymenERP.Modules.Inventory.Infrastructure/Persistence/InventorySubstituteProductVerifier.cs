using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// KLN-D39/D40/D70: the server-side check behind a confirmed substitute ("muadil") allocation. It reads the Settings
/// tolerance first: with none configured nothing is similar and the Product owner is not asked. Otherwise it reads both
/// exact product versions' similarity facts and applies the one deterministic calculator the suggestion read uses.
/// </summary>
internal sealed class InventorySubstituteProductVerifier(
    IInventorySimilarProductToleranceSourceV1 tolerances,
    IInventoryProductSimilarityFactsV1 products) : IInventorySubstituteProductVerifierV1
{
    public async Task<bool> IsSimilarAsync(
        Guid companyId,
        InventoryProductVersionV1 demandProduct,
        InventoryProductVersionV1 labelProduct,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(demandProduct);
        ArgumentNullException.ThrowIfNull(labelProduct);
        cancellationToken.ThrowIfCancellationRequested();
        if (companyId == Guid.Empty || demandProduct.ProductId == labelProduct.ProductId)
        {
            return false;
        }

        var tolerance = await InventorySimilarityFacts.ToleranceAsync(tolerances, companyId, cancellationToken);
        if (tolerance is null)
        {
            return false;
        }

        var facts = await InventorySimilarityFacts.ReadAsync(products, [demandProduct, labelProduct], cancellationToken);
        return InventorySimilarProductCalculatorV1.Evaluate(facts[demandProduct], facts[labelProduct], tolerance)
            is not null;
    }
}

/// <summary>The fail-closed reads of the two owner answers both the verifier and the suggestion read use.</summary>
internal static class InventorySimilarityFacts
{
    /// <summary>The company tolerance, or null when none is configured; a malformed answer is 503, never "no tolerance".</summary>
    public static async Task<InventorySimilarProductToleranceV1?> ToleranceAsync(
        IInventorySimilarProductToleranceSourceV1 tolerances,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var tolerance = await tolerances.GetAsync(companyId, cancellationToken);
        if (tolerance is not null && !tolerance.IsWellFormed)
        {
            throw new InventoryAllocationException(503, InventoryAllocationSuggestionCodesV1.ToleranceUnavailable);
        }

        return tolerance;
    }

    /// <summary>
    /// Reads the facts of distinct exact product versions; the answer must hold exactly one item per requested pair,
    /// otherwise the owner's answer is not reliable (503).
    /// </summary>
    public static async Task<IReadOnlyDictionary<InventoryProductVersionV1, InventoryProductSimilarityFactsV1>> ReadAsync(
        IInventoryProductSimilarityFactsV1 products,
        IReadOnlyList<InventoryProductVersionV1> selections,
        CancellationToken cancellationToken)
    {
        if (selections.Count == 0 || selections.Distinct().Count() != selections.Count
            || selections.Any(selection => selection.ProductId == Guid.Empty
                || string.IsNullOrWhiteSpace(selection.ProductVersion)))
        {
            throw new InventoryAllocationException(503, InventoryAllocationSuggestionCodesV1.ProductFactsUnavailable);
        }

        var answer = await products.ReadAsync(selections, cancellationToken);
        if (answer is null || answer.Count != selections.Count)
        {
            throw new InventoryAllocationException(503, InventoryAllocationSuggestionCodesV1.ProductFactsUnavailable);
        }

        var requested = selections.ToHashSet();
        var facts = new Dictionary<InventoryProductVersionV1, InventoryProductSimilarityFactsV1>();
        foreach (var item in answer)
        {
            var key = item is null ? null : new InventoryProductVersionV1(item.ProductId, item.ProductVersion);
            if (key is null || !requested.Contains(key) || !facts.TryAdd(key, item!))
            {
                throw new InventoryAllocationException(503, InventoryAllocationSuggestionCodesV1.ProductFactsUnavailable);
            }
        }

        return facts;
    }
}
