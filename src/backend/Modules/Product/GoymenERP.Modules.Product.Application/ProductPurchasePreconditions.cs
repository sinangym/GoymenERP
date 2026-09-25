using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Modules.Product.Application;

/// <summary>
/// Necessary local conditions from DEC-PRD-006/007/010 and P-04 purchase selection.
/// Empty errors mean only that owner reference validation may proceed, never usable.
/// This does not activate a Product, authorize a caller, validate a unit registry,
/// select a family policy, or substitute completeness labels for reference checks.
/// </summary>
public static class ProductPurchasePreconditions
{
    public static IReadOnlyList<ProductFieldError> Check(ProductCardView card,
        ProductPurchaseSelectionV1 selection)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(selection);
        var errors = new List<ProductFieldError>();
        if (selection.ProductId == Guid.Empty || selection.ProductId != card.Product.Id)
            errors.Add(new("productId", "invalid_reference"));

        try
        {
            var expected = ProductVersion.Parse(selection.ExpectedProductVersion);
            var actual = ProductVersion.Parse(card.Product.Version);
            if (expected != actual) errors.Add(new("expectedProductVersion", ProductApiV1.VersionConflict));
        }
        catch (ArgumentException)
        {
            errors.Add(new("expectedProductVersion", "invalid_version"));
        }

        if (card.Product.Lifecycle != "Active")
            errors.Add(new("productId", ProductApiV1.LifecycleConflict));
        if (card.Product.Roles.Purchasable is not { Value: true, Provenance: "manual" or "derived" })
            errors.Add(new("productId", "not_purchasable"));

        if (card.References.Units?.Purchase is not Guid purchase || purchase == Guid.Empty)
            errors.Add(new("references.units.purchase", "invalid_reference"));
        if (selection.UnitId == Guid.Empty)
            errors.Add(new("unitId", "invalid_reference"));

        // Equality is not profile validation; the Settings owner must still answer.
        return errors;
    }
}
