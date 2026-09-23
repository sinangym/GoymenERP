using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Modules.Product.Application;

public sealed class ProductPurchaseFamilyRequirementPolicyV1(IProductFamilyDefinitionSourceV2 source)
    : IProductPurchaseFamilyRequirementPolicyV1
{
    public async Task<IReadOnlyList<ProductFieldError>> ValidateAsync(ProductCardView card, CancellationToken cancellationToken) =>
        await ProductFamilyRequiredReferencesV2.ValidateAsync(source, card, cancellationToken);
}
