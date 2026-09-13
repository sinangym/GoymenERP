using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Modules.Product.Application;

/// <summary>
/// Necessary local conditions from DEC-PRD-006/007/010 and Sales JIT section 4.
/// Empty errors mean only that owner reference validation may proceed, never usable.
/// This does not activate a Product, authorize a caller, validate a unit registry,
/// select a family policy, or substitute completeness labels for reference checks.
/// </summary>
public static class ProductSalesPreconditions
{
    public static IReadOnlyList<ProductFieldError> Check(ProductCardView card,
        ProductSalesSelectionV1 selection)
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
        if (card.Product.Roles.Sellable is not { Value: true, Provenance: "manual" or "derived" })
            errors.Add(new("productId", "not_sellable"));

        if (card.References.Units?.Stock is not Guid stock || stock == Guid.Empty)
            errors.Add(new("references.units.stock", "invalid_reference"));
        if (card.References.Units?.Sales is not Guid sales || sales == Guid.Empty)
            errors.Add(new("references.units.sales", "invalid_reference"));
        if (selection.OrderUnitId == Guid.Empty)
            errors.Add(new("orderUnitId", "invalid_reference"));

        // Even equal stock/sales/order UUIDs must be resolved by the real provider.
        // Unequal IDs are not rejected here: conversion policy is not ours to invent.
        return errors;
    }
}
