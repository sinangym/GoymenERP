namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>
/// Family-required field, reference and tracking validation that a Product must pass
/// before a Sales selection can use it (DEC-PRD-002/010, Business #254 section 4).
/// No first family rule set is accepted yet: the decision card is #254 comment
/// 5654539215 (NEEDS-SINAN). An implementation must never report "no errors" for a
/// family it holds no accepted rule set for, and must not infer requiredness, scale or
/// tracking from completeness labels, examples or the U-09 candidate table.
/// </summary>
public interface IProductFamilyRequirementPolicyV1
{
    /// <returns>Empty only when an accepted rule set covers the card's family and passes.</returns>
    /// <exception cref="ProductOperationException">503 when no accepted rule set applies.</exception>
    Task<IReadOnlyList<ProductFieldError>> ValidateAsync(ProductCardView card, CancellationToken cancellationToken);
}

/// <summary>The only shipped policy: every evaluation is unavailable, never a positive result.</summary>
public sealed class UnacceptedProductFamilyRequirementPolicyV1 : IProductFamilyRequirementPolicyV1
{
    public const string UnavailableCode = "product_family_policy_unavailable";

    public Task<IReadOnlyList<ProductFieldError>> ValidateAsync(ProductCardView card, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(card);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException<IReadOnlyList<ProductFieldError>>(new ProductOperationException(UnavailableCode, 503));
    }
}
