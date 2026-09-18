namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>
/// Product-owned operational read port for a STOCK operation (T07: an Inventory receipt of a
/// labelled piece): may this Product, at this version, be stocked on this date, and under which
/// stock-unit profile? The same pattern and channels as <see cref="IProductSalesEligibilityV1"/>;
/// only the rules that belong to stock are asked. An implementation resolves current server
/// authority and revalidates lifecycle, the stock unit binding and the family-required references
/// against the real owners. A previous answer is not authorization for an Inventory command.
///
/// <para><b>What decides it</b> (sources, nothing else):</para>
/// <list type="bullet">
/// <item>Lifecycle Active: DEC-PRD-010, "a draft card cannot be used in a definitive transaction".</item>
/// <item>The stock unit binding, and its Settings profile usable on <c>EffectiveOn</c> and kilogram:
/// DEC-PRD-007, "the main stock quantity of every product is kg".</item>
/// <item>The accepted family-required policy of the card's family, as for sales (the card either is
/// a valid card of its family or it is not).</item>
/// <item><b>No role</b> is required: no source ties stocking to Purchasable, Sellable,
/// ProductionInput or ProductionOutput, so none is invented. Sellable is sales' question.</item>
/// </list>
///
/// <para><b>Result with <c>Errors</c></b> (and a null snapshot): <c>productId</c>:
/// <c>invalid_reference</c>, <c>lifecycle_conflict</c>; <c>expectedProductVersion</c>:
/// <c>version_conflict</c>, <c>invalid_version</c>; <c>references.units.stock</c>:
/// <c>invalid_reference</c> (missing binding; profile inactive, out of window or not kilogram); and
/// the family policy's own field errors. Several may arrive together; order carries no precedence.</para>
///
/// <para><b>Exceptions</b> exactly as <see cref="IProductSalesEligibilityV1"/>: 403
/// <c>product_access_denied</c> (the same narrow operational-selection permission, measured afresh
/// at every fence), 503 <c>product_family_policy_unavailable</c> / <c>product_family_policy_invalid</c>,
/// Settings' own codes, <c>InvalidOperationException</c> for a stored record that cannot be
/// restored, and cancellation.</para>
/// </summary>
public interface IProductStockEligibilityV1
{
    Task<ProductStockEligibilityV1> EvaluateAsync(ProductStockSelectionV1 selection,
        CancellationToken cancellationToken);
}

public sealed record ProductStockSelectionV1(Guid ProductId, string ExpectedProductVersion, DateOnly EffectiveOn);

public sealed record ProductStockEligibilityV1(ProductStockSnapshotV1? Snapshot,
    IReadOnlyList<ProductFieldError> Errors);

/// <summary>
/// The owner's answer a stock operation keeps with what it records. The stock unit is a reference to
/// the Settings-owned profile (the same shape the sales port uses), not a copied unit catalogue.
/// </summary>
public sealed record ProductStockSnapshotV1(Guid ProductId, string ProductVersion, string Code, string Name,
    ProductSalesUnitProfileV1 StockUnit, DateOnly EffectiveOn);
