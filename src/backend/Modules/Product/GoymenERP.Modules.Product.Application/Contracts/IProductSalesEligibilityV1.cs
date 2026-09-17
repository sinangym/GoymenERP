namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>
/// Product-owned operational read port; implementations must resolve current server
/// authority and revalidate lifecycle, roles and family-required references against
/// the real owners. A previous selection is not authorization for a Sales command.
/// No runtime implementation or permission mapping is provided by this contract.
///
/// <para><b>Error channel.</b> Which outcome arrives how is part of the contract; a
/// consumer maps channels and codes, never exception messages.</para>
///
/// <para><b>Result with <c>Errors</c></b> (and a null snapshot): the owner's answer about
/// THIS selection. Codes at this base:</para>
/// <list type="bullet">
/// <item><c>productId</c>: <c>invalid_reference</c> (empty id, missing or mismatched
/// Product), <c>lifecycle_conflict</c> (any non-Active Product, including a Draft that was
/// never selectable), <c>not_sellable</c>.</item>
/// <item><c>expectedProductVersion</c>: <c>version_conflict</c> (the stored version moved
/// on), <c>invalid_version</c>.</item>
/// <item><c>references.units.stock</c> / <c>references.units.sales</c>:
/// <c>invalid_reference</c> — the binding is missing, or the owner's Settings profile is
/// inactive, not effective on <c>EffectiveOn</c>, or (stock only) not kilogram.</item>
/// <item><c>orderUnitId</c>: <c>invalid_reference</c> (empty), <c>unsupported_order_unit</c>.</item>
/// <item>Field errors returned by the accepted family-required policy (none is accepted at
/// this base).</item>
/// </list>
/// <para>Several errors may arrive together; their order carries no precedence. Conflicts
/// are field errors: <c>version_conflict</c> and <c>lifecycle_conflict</c> are never thrown.</para>
///
/// <para><b>Exceptions</b>, which are not answers about the selection:</para>
/// <list type="bullet">
/// <item><c>ProductOperationException</c> 403 <c>product_access_denied</c>: no server scope,
/// the caller lacks the narrow operational product-selection permission for the
/// server-resolved company, or the company/actor/permission changed during any read; it
/// replaces whatever that read produced. One code for all of them: which of the three it
/// was is not disclosed.</item>
/// <item><c>ProductOperationException</c> 503 <c>product_family_policy_unavailable</c> /
/// <c>product_family_policy_invalid</c>: no accepted family policy, or it answered nothing.</item>
/// <item><c>SettingsPrerequisiteException</c>: the Settings provider's own codes pass through
/// (<c>settings_access_denied</c>, <c>settings_provider_unavailable</c>, …);
/// <c>settings_profile_invalid</c> only for malformed, foreign or self-contradictory profile
/// data.</item>
/// <item><c>InvalidOperationException</c>: a stored Product record that cannot be restored.</item>
/// <item><c>OperationCanceledException</c> on cancellation.</item>
/// </list>
/// <para><c>ProductVersionConflictException</c> (Product.Domain) is never thrown on this read
/// path: no Domain command runs, and a stale version is the <c>version_conflict</c> error.</para>
/// </summary>
public interface IProductSalesEligibilityV1
{
    Task<ProductSalesEligibilityV1> EvaluateAsync(ProductSalesSelectionV1 selection,
        CancellationToken cancellationToken);
}

public sealed record ProductSalesSelectionV1(Guid ProductId, string ExpectedProductVersion, Guid OrderUnitId,
    DateOnly EffectiveOn);

/// <summary>
/// Only the owner can produce a usable snapshot after current reference/profile
/// validation. Quantity and conversions remain the measurement provider's concern.
/// CompanyId is not Product ownership; request authority belongs to the server scope.
/// </summary>
public sealed record ProductSalesEligibilityV1(ProductSalesSnapshotV1? Snapshot,
    IReadOnlyList<ProductFieldError> Errors);

public sealed record ProductSalesSnapshotV1(Guid ProductId, string ProductVersion, string Code,
    string Name, ProductSalesUnitProfileV1 StockUnit, ProductSalesUnitProfileV1 SalesUnit,
    ProductSalesUnitProfileV1 OrderUnit, DateOnly EffectiveOn);

/// <summary>Reference to the Settings-owned profile; not a copied unit catalogue.</summary>
public sealed record ProductSalesUnitProfileV1(Guid CompanyId, Guid UnitId, Guid ProfileId, string Version);
