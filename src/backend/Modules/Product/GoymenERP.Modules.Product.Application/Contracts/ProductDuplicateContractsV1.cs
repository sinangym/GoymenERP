namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>
/// The read-only duplicate-candidate preview of spec PRODUCT-U01-U02-JIT-001 §15 ("duplicate candidate read-only
/// preview"), under the rules of §10.2:
/// <list type="bullet">
/// <item>a code/alias exact collision is a hard block — that is the server-minted code and the DB uniqueness of
/// code and alias (§7), not this read;</item>
/// <item>a name or technical similarity only WARNS and lets the person compare; it never merges or blocks a save or
/// an approval (README: "Same name is not a duplicate hard block").</item>
/// </list>
/// This read answers the decided rules and nothing else. <see cref="ProductDuplicateRules.ExactName"/>: the cards
/// whose name equals the given one after the write path's own normalisation (<c>Product.NormalizeName</c>: trim),
/// compared by the name column's own collation (<c>Turkish_100_CI_AS</c>: Turkish case-insensitive, accent
/// sensitive) — the same equality the store already applies to every name. <see cref="ProductDuplicateRules.SameTechnical"/>
/// (URN-D25, Sinan 2026-11-21): for a saved card (<c>ExcludeProductId</c>), the ACTIVE cards of the same family whose
/// template's thickness, width, quality and coating values are exactly equal to the card's. No similarity score,
/// threshold or fuzzy rule is computed, and neither list blocks a save or an approval.
/// </summary>
public sealed record ProductDuplicateQuery(string? Name = null, Guid? ExcludeProductId = null);

/// <param name="Rule">The rule these candidates answer; today only <see cref="ProductDuplicateRules.ExactName"/>.</param>
/// <param name="Name">The normalised name the server compared.</param>
/// <param name="ExactName">Every lifecycle (Draft, Active, Inactive), in code order; at most
/// <see cref="ProductDuplicateRules.MaximumCandidates"/>. The excluded card (the one being edited) is never listed.</param>
/// <param name="HasMore">More cards share the name than are listed; the list search shows them all.</param>
/// <param name="SameTechnical">URN-D25: the other ACTIVE cards of the saved card's family with exactly its thickness, width,
/// quality and coating, in code order; at most <see cref="ProductDuplicateRules.MaximumCandidates"/>. Empty for an unsaved card
/// or a card without all five values.</param>
/// <param name="SameTechnicalHasMore">More active cards share the technical values than are listed.</param>
public sealed record ProductDuplicateCandidates(string Rule, string Name, IReadOnlyList<ProductDraftView> ExactName,
    bool HasMore, IReadOnlyList<ProductDraftView> SameTechnical, bool SameTechnicalHasMore);

public static class ProductDuplicateRules
{
    public const string Path = "/api/v1/products/duplicate-candidates";
    public const string ExactName = "exactName";

    /// <summary>URN-D25: family + thickness + width + quality + coating exactly equal, active cards only.</summary>
    public const string SameTechnical = "sameTechnical";

    /// <summary>A display bound for one warning panel, not a matching threshold.</summary>
    public const int MaximumCandidates = 20;

    /// <summary>
    /// The name goes through the create/edit normaliser, so a name the write path would refuse is refused here
    /// the same way (<c>name</c> / <c>invalid_name</c>), and an empty exclusion is <c>query</c> /
    /// <c>invalid_metadata</c> as for the list's empty filters.
    /// </summary>
    public static ProductDuplicateQuery Normalize(ProductDuplicateQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var errors = new List<ProductFieldError>();
        string? name = null;
        try { name = Domain.Products.Product.NormalizeName(query.Name!); }
        catch (ArgumentException) { errors.Add(new("name", "invalid_name")); }
        if (query.ExcludeProductId == Guid.Empty) errors.Add(new("query", "invalid_metadata"));
        if (errors.Count > 0) throw new ProductValidationException(errors);
        return query with { Name = name };
    }
}
