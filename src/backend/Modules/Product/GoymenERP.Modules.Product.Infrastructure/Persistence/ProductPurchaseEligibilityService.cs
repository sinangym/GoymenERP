using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

public sealed class ProductPurchaseEligibilityService(ICompanyContext company, ICurrentUser user,
    IErpAuthorityRevalidationService authority, IProductPurchaseRecordReader records,
    IProductPurchaseFamilyRequirementPolicyV1 familyPolicy, ProductUnitProfileAdapter units) : IProductPurchaseEligibilityV1
{
    private readonly ProductPurchaseReadFence fence = new(company, user, authority);

    public async Task<ProductPurchaseEligibilityV1> EvaluateAsync(ProductPurchaseSelectionV1 selection,
        CancellationToken cancellationToken)
    {
        var scope = await fence.RequireScopeAsync(null, cancellationToken);
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.ProductId == Guid.Empty) return new(null, [new("productId", "invalid_reference")]);
        var record = await fence.FencedAsync(() => records.ReadAsync(scope, selection.ProductId, cancellationToken),
            scope, cancellationToken);
        if (record is null) return new(null, [new("productId", "invalid_reference")]);
        ProductCardView card;
        try { card = new(ProductDraftView.FromProduct(record.RestoreProduct()), record.RestoreReferences()); }
        catch (Exception error) when (error is ArgumentException or System.Text.Json.JsonException)
        { throw new InvalidOperationException("The stored Product record could not be restored.", error); }
        var errors = ProductPurchasePreconditions.Check(card, selection);
        if (errors.Count > 0) return new(null, errors);
        var purchaseUnit = card.References.Units!.Purchase!.Value;
        if (selection.UnitId != purchaseUnit) return new(null, [new("unitId", "unsupported_purchase_unit")]);
        var family = await fence.FencedAsync(() => familyPolicy.ValidateAsync(card, cancellationToken), scope, cancellationToken);
        if (family is null) throw new ProductOperationException("product_family_policy_invalid", 503);
        if (family.Count > 0) return new(null, family);
        var unit = await fence.FencedAsync(() => units.ReadAsync(purchaseUnit, selection.EffectiveOn,
            requiresKilogram: false, cancellationToken), scope, cancellationToken);
        if (!unit.IsUsable) return new(null, [new("references.units.purchase", "invalid_reference")]);
        var profile = unit.Profile;
        return new(new(card.Product.Id, card.Product.Version, card.Product.Code, card.Product.Name,
            new(profile.CompanyId, profile.UnitId, profile.UnitCode, profile.Profile.ProfileId,
                profile.Profile.Version.ToString(CultureInfo.InvariantCulture)), selection.EffectiveOn), []);
    }
}
