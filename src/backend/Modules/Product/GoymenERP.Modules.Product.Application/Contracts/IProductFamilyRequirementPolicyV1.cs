namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>
/// Family-required field, reference and tracking validation that a Product must pass
/// before a Sales selection can use it (DEC-PRD-002/010, Business #254 section 4).
/// The first family's rules are accepted (GDC band/coil, kg→kg; Business #265 5722701085)
/// but no implementation ships yet; the policy that encodes them is its own slice. An
/// implementation must never report "no errors" for a
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
