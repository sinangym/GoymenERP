namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>
/// Current purchase eligibility under the server company and fresh operational-selection.read.
/// Active + Purchasable, exact version, the stored purchase unit, accepted family policy and
/// Settings effective profile are all required. This is neither master authorization nor a conversion.
/// Errors are selection refusals: productId invalid_reference/lifecycle_conflict/not_purchasable;
/// expectedProductVersion invalid_version/version_conflict; unitId invalid_reference/unsupported_purchase_unit;
/// references.units.purchase invalid_reference; accepted family policy errors pass through unchanged.
/// No snapshot accompanies errors. Exceptions are not selection answers: ProductOperationException
/// 403 product_access_denied (including revoked/changed scope), 503 family policy/runtime unavailable;
/// SettingsPrerequisiteException retains the owner's code (including unavailable/invalid profiles).
/// Corrupt stored data is InvalidOperationException; cancellation propagates. Consumers must map
/// these channels, never messages, and re-evaluate at command time after replay lookup.
/// </summary>
public interface IProductPurchaseEligibilityV1
{
    Task<ProductPurchaseEligibilityV1> EvaluateAsync(ProductPurchaseSelectionV1 selection,
        CancellationToken cancellationToken);
}

public sealed record ProductPurchaseSelectionV1(Guid ProductId, string ExpectedProductVersion, Guid UnitId,
    DateOnly EffectiveOn);
public sealed record ProductPurchaseEligibilityV1(ProductPurchaseSnapshotV1? Snapshot,
    IReadOnlyList<ProductFieldError> Errors);
public sealed record ProductPurchaseSnapshotV1(Guid ProductId, string ProductVersion, string Code, string Name,
    ProductPurchaseUnitProfileV1 PurchaseUnit, DateOnly EffectiveOn);
/// <summary>Company is profile/authority scope, not ownership of the global Product master.</summary>
public sealed record ProductPurchaseUnitProfileV1(Guid CompanyId, Guid UnitId, string Code, Guid ProfileId,
    string ProfileVersion);
