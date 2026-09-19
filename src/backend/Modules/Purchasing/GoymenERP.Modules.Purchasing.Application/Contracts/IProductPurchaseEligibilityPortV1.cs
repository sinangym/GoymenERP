namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>
/// The Product owner's answer about a line's product at command time (PUR-4a): may this product be bought
/// in this unit on the business date (Purchasable role, Active lifecycle, the unit being the product's
/// purchase unit) and which stored version is it? A consumer boundary the Product owner implements when
/// it publishes purchase eligibility (ÜRÜN #288 P3-3); until then the host registers NO implementation
/// and the service answers 503 <see cref="PurchasingProblemCodesV1.ProductEligibilityUnavailable"/>
/// fail-closed — exactly as Sales did before <c>IProductSalesEligibilityV1</c>. Purchasing keeps no
/// product data beyond the identity and the answered version.
/// </summary>
public interface IProductPurchaseEligibilityPortV1
{
    Task<ProductPurchaseEligibilityAnswerV1> EvaluateAsync(ProductPurchaseSelectionV1 selection, CancellationToken cancellationToken = default);
}

public sealed record ProductPurchaseSelectionV1(Guid ProductId, string ExpectedProductVersion, Guid UnitId, DateOnly EffectiveOn);

public enum ProductPurchaseEligibilityOutcomeV1
{
    /// <summary>The owner cannot answer (its provider is down, its policy data missing): 503, nothing stored.</summary>
    Unavailable = 0,

    /// <summary>The server scope lacks the owner's narrow product-selection permission, or the actor/company moved: 403.</summary>
    Denied = 1,

    /// <summary>The owner answered about THIS selection: <see cref="ProductPurchaseEligibilityAnswerV1.Errors"/> says why it is not purchasable.</summary>
    Refused = 2,

    /// <summary>Purchasable: <see cref="ProductPurchaseEligibilityAnswerV1.ProductVersion"/> is the stored version the answer is about.</summary>
    Eligible = 3,
}

/// <summary>
/// Field errors use the Sales/Product base: <c>productId</c> — <c>invalid_reference</c>, <c>lifecycle_conflict</c>,
/// <c>not_purchasable</c>; <c>expectedProductVersion</c> — <c>version_conflict</c>, <c>invalid_version</c>;
/// <c>unitId</c> — <c>invalid_reference</c>, <c>unsupported_purchase_unit</c>. A <c>version_conflict</c> is the
/// terminal refusal of ADR-011 §7.
/// </summary>
public sealed record ProductPurchaseEligibilityAnswerV1(ProductPurchaseEligibilityOutcomeV1 Outcome, string? ProductVersion,
    IReadOnlyList<PurchasingFieldError> Errors);
