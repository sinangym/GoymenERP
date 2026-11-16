using GoymenERP.Modules.Inventory.Domain.Quantities;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// KLN-D34/D39/D40/D70 label-based reservation suggestions for one approved Sales order line. A suggestion is only a
/// suggestion: nothing is reserved until the user confirms a label through the physical allocation create command,
/// which re-checks every rule on the server. Suggestions are not stock movements and never exceed the label's
/// unreserved free kilograms (free physical minus active allocations), so no negative stock can follow from them.
/// </summary>
public static class InventoryAllocationSuggestionKindsV1
{
    /// <summary>A label of the line's own product.</summary>
    public const string SameProduct = "same-product";

    /// <summary>KLN-D39: a label of another, similar product ("muadil"); confirming it flags the allocation.</summary>
    public const string SimilarProduct = "similar-product";
}

/// <summary>How similar products were searched for this answer (KLN-D40).</summary>
public static class InventorySimilarProductModesV1
{
    /// <summary>No tolerance is configured in Settings: only the line's own product is suggested.</summary>
    public const string SameProductOnly = "same-product-only";

    /// <summary>The Settings dimension tolerance is configured and applied when the line's own product is missing.</summary>
    public const string DimensionTolerance = "dimension-tolerance";
}

/// <summary>The state of the AI suggestion interface (KLN-D40, AI-D7): disabled while no provider is configured.</summary>
public static class InventoryAllocationAiSuggestionStatesV1
{
    public const string Disabled = "disabled";
    public const string Enabled = "enabled";
}

/// <summary>The suggestion read's and the substitute check's own 503 codes (fail-closed; never a guessed answer).</summary>
public static class InventoryAllocationSuggestionCodesV1
{
    /// <summary>503: the Product owner could not answer the similarity facts reliably.</summary>
    public const string ProductFactsUnavailable = "inventory_allocation_product_facts_unavailable";

    /// <summary>403: the Product owner refused the similarity facts read for the current actor.</summary>
    public const string ProductFactsForbidden = "inventory_allocation_product_facts_forbidden";

    /// <summary>503: the Settings tolerance answer is not a well-formed percentage per dimension.</summary>
    public const string ToleranceUnavailable = "inventory_allocation_similar_tolerance_unavailable";

    /// <summary>503: the stored free positions or allocations could not be read consistently.</summary>
    public const string Unavailable = "inventory_allocation_suggestions_unavailable";
}

public static class InventoryAllocationSuggestionLimitsV1
{
    /// <summary>The most labels one answer lists; a longer list is cut and <c>Truncated</c> is true.</summary>
    public const int MaximumSuggestions = 50;

    /// <summary>
    /// The most distinct product/version pairs (beside the line's own) whose similarity facts one answer reads. It equals
    /// the Product display batch size minus the line's own pair; more candidates set <c>Truncated</c>.
    /// </summary>
    public const int MaximumCandidateProducts = 99;

    /// <summary>
    /// The most free label positions one search reads (per same-product or similar-product pass); a fuller window sets
    /// <c>Truncated</c>, so a cut list is never presented as complete.
    /// </summary>
    public const int MaximumScannedPositions = 500;
}

/// <summary>
/// The company Settings tolerance for "benzer ürün" (KLN-D70): a symmetric ± percentage per dimension, each a separate
/// Settings key. Every value is in [0, 100) with at most <see cref="PercentDecimals"/> decimals. The initial value the
/// decision names lives in Settings, never in code.
/// </summary>
public sealed record InventorySimilarProductToleranceV1(
    decimal ThicknessPercent,
    decimal WidthPercent,
    decimal LengthPercent)
{
    public const int PercentDecimals = 4;

    public const decimal ExclusiveMaximumPercent = 100m;

    public bool IsWellFormed =>
        IsPercent(ThicknessPercent) && IsPercent(WidthPercent) && IsPercent(LengthPercent);

    private static bool IsPercent(decimal value) =>
        value >= 0m && value < ExclusiveMaximumPercent
        && decimal.Round(value, PercentDecimals, MidpointRounding.ToZero) == value;
}

/// <summary>
/// Where the similar-product tolerance comes from: the company Settings ("Ayarlar") owner implements this port. It answers
/// null while no tolerance is configured; the consumer then suggests and accepts only the line's own product (KLN-D40).
/// </summary>
public interface IInventorySimilarProductToleranceSourceV1
{
    Task<InventorySimilarProductToleranceV1?> GetAsync(Guid companyId, CancellationToken cancellationToken = default);
}

/// <summary>The default source until Settings implements the port: no tolerance, so the same product only.</summary>
public sealed class UnconfiguredInventorySimilarProductToleranceSourceV1 : IInventorySimilarProductToleranceSourceV1
{
    public Task<InventorySimilarProductToleranceV1?> GetAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<InventorySimilarProductToleranceV1?>(null);
    }
}

public sealed record InventoryProductVersionV1(Guid ProductId, string ProductVersion);

/// <summary>
/// The Product-owned facts a similarity check needs for one exact product version. <see cref="SimilarityKey"/> is an
/// opaque key that is equal exactly when two products share the same material, quality and product type (family,
/// template and every controlled attribute value); it is null when the product has no accepted technical facts, and such
/// a product is never similar to anything. Measures are the card's nominal millimetres; a null measure is not defined.
/// <see cref="ProductCode"/>/<see cref="ProductName"/> are null only when the exact version is not readable.
/// <see cref="CoatingCode"/> is the product's stored coating code (KLN-D95); a null coating is never similar to anything.
/// </summary>
public sealed record InventoryProductSimilarityFactsV1(
    Guid ProductId,
    string ProductVersion,
    string? ProductCode,
    string? ProductName,
    string? SimilarityKey,
    decimal? ThicknessMm,
    decimal? WidthMm,
    decimal? LengthMm,
    string? CoatingCode);

/// <summary>
/// Reads <see cref="InventoryProductSimilarityFactsV1"/> for a batch of exact product versions in the server-resolved
/// company (the host adapts the Product owner's port). It answers one item per requested pair; a pair the owner cannot
/// read comes back with null facts. Throws <see cref="InventoryAllocationException"/> 403/503 for authority or owner
/// failures; it never guesses a missing fact.
/// </summary>
public interface IInventoryProductSimilarityFactsV1
{
    Task<IReadOnlyList<InventoryProductSimilarityFactsV1>> ReadAsync(
        IReadOnlyList<InventoryProductVersionV1> selections,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The default facts source until the host adapts the Product owner: it refuses with 503, so a substitute is never
/// accepted and a similar-product search never runs on guessed facts.
/// </summary>
public sealed class UnavailableInventoryProductSimilarityFactsV1 : IInventoryProductSimilarityFactsV1
{
    public Task<IReadOnlyList<InventoryProductSimilarityFactsV1>> ReadAsync(
        IReadOnlyList<InventoryProductVersionV1> selections,
        CancellationToken cancellationToken = default) =>
        Task.FromException<IReadOnlyList<InventoryProductSimilarityFactsV1>>(new InventoryAllocationException(
            503, InventoryAllocationSuggestionCodesV1.ProductFactsUnavailable));
}

/// <summary>
/// KLN-D40 / AI-D7: the AI suggestion interface. An AI provider may only answer through this schemaed backend tool, may
/// only name candidate label ids that the server then filters with the same rules, and never reserves anything. While no
/// provider is configured (a human gate) the default implementation is disabled and suggestions use only the dimension
/// tolerance.
/// </summary>
public interface IInventoryAllocationAiSuggestionToolV1
{
    bool IsEnabled { get; }

    Task<IReadOnlyList<Guid>> SuggestLabelsAsync(
        InventoryAllocationAiSuggestionInputV1 input,
        CancellationToken cancellationToken = default);
}

/// <summary>The tool's typed input: the line's product and the candidate labels the server already found eligible.</summary>
public sealed record InventoryAllocationAiSuggestionInputV1(
    Guid CompanyId,
    Guid ProductId,
    string ProductVersion,
    IReadOnlyList<Guid> EligibleLabelIds);

/// <summary>The default AI tool: disabled, so it is never called.</summary>
public sealed class DisabledInventoryAllocationAiSuggestionToolV1 : IInventoryAllocationAiSuggestionToolV1
{
    public bool IsEnabled => false;

    public Task<IReadOnlyList<Guid>> SuggestLabelsAsync(
        InventoryAllocationAiSuggestionInputV1 input,
        CancellationToken cancellationToken = default) =>
        Task.FromException<IReadOnlyList<Guid>>(new InvalidOperationException("AI öneri aracı kapalı."));
}

/// <summary>
/// The suggestion query. Order, line, product and version are derived by the host from the approved Sales line (never
/// from the caller). <see cref="RequestedKg"/> is the kilograms the user is about to reserve, as typed on the screen:
/// when stated, the line's own product labels are listed closest to it first (KLN-D99); it never selects a label.
/// </summary>
public sealed record InventoryAllocationSuggestionQueryV1(
    Guid SalesOrderId,
    Guid SalesOrderLineId,
    Guid ProductId,
    string ProductVersion,
    Kilograms? RequestedKg = null);

/// <summary>
/// One suggested label. <see cref="UnreservedKg"/> is the label's free kilograms minus its active allocations;
/// <see cref="LabelVersion"/> is the version a confirming create must name. <see cref="MaximumDeviationPercent"/> is the
/// largest relative dimension deviation of a similar product, as percent text (null for the same product).
/// </summary>
public sealed record InventoryAllocationSuggestionV1(
    Guid LabelId,
    string LabelVersion,
    string LabelCode,
    Guid ProductId,
    string? ProductVersion,
    string? ProductCode,
    string? ProductName,
    string Kind,
    bool IsSubstitute,
    string UnreservedKg,
    string? MaximumDeviationPercent);

/// <summary>
/// The suggestion answer. <see cref="RequestedKg"/> echoes the query's requested kilograms as invariant text (null when
/// none was stated); when it is set, same-product items are ordered by KLN-D99 proximity, otherwise by label code.
/// </summary>
public sealed record InventoryAllocationSuggestionsV1(
    Guid CompanyId,
    Guid SalesOrderId,
    Guid SalesOrderLineId,
    Guid ProductId,
    string ProductVersion,
    string SimilarProductMode,
    string AiSuggestions,
    bool Truncated,
    IReadOnlyList<InventoryAllocationSuggestionV1> Items,
    string? RequestedKg);

/// <summary>
/// The suggestion read (KLN-D34/D39): labels of the line's own product with unreserved free kilograms; when there is none,
/// labels of similar products within the Settings tolerance (KLN-D70), closest first (ASSUMPTION-STK-SIMILAR-ORDER-1).
/// Only company-owned and company-held free positions are suggested, as the create command requires.
/// </summary>
public interface IInventoryAllocationSuggestionReaderV1
{
    Task<InventoryAllocationSuggestionsV1> ReadAsync(
        InventoryAllocationSuggestionQueryV1 query,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The server-side check of a substitute ("muadil") allocation: true only when a tolerance is configured and the label's
/// product version is similar to the line's product version within it. Used by the create command.
/// </summary>
public interface IInventorySubstituteProductVerifierV1
{
    Task<bool> IsSimilarAsync(
        Guid companyId,
        InventoryProductVersionV1 demandProduct,
        InventoryProductVersionV1 labelProduct,
        CancellationToken cancellationToken = default);
}
