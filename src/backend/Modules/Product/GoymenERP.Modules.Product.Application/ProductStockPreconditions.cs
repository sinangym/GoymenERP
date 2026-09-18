using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Modules.Product.Application;

/// <summary>
/// Necessary local conditions for a stock operation: DEC-PRD-010 (only an Active card in a
/// definitive transaction) and DEC-PRD-007 (a stock unit exists; that it is kilogram is the
/// Settings profile's answer, read later). Empty errors mean only that owner reference validation
/// may proceed, never usable. No role is checked: no source ties stocking to one.
/// </summary>
public static class ProductStockPreconditions
{
    public static IReadOnlyList<ProductFieldError> Check(ProductCardView card, ProductStockSelectionV1 selection)
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
        if (card.References.Units?.Stock is not Guid stock || stock == Guid.Empty)
            errors.Add(new("references.units.stock", "invalid_reference"));

        return errors;
    }
}
