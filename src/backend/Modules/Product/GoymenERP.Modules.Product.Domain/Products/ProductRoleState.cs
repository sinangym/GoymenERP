namespace GoymenERP.Modules.Product.Domain.Products;

public enum ProductRoleProvenance { NotEvaluated, Manual, Derived }

public sealed record ProductRoleState
{
    private ProductRoleState(bool? value, ProductRoleProvenance provenance)
    {
        Value = value;
        Provenance = provenance;
    }

    public bool? Value { get; }
    public ProductRoleProvenance Provenance { get; }
    public static ProductRoleState NotEvaluated { get; } = new(null, ProductRoleProvenance.NotEvaluated);
    public static ProductRoleState Manual(bool value) => new(value, ProductRoleProvenance.Manual);
    public static ProductRoleState Derived(bool value) => new(value, ProductRoleProvenance.Derived);

    public ProductRoleState WithManualSelection(bool? value)
    {
        if (Provenance == ProductRoleProvenance.Derived)
        {
            throw new InvalidOperationException("Türetilmiş ürün rolü elle değiştirilemez.");
        }

        return value.HasValue ? Manual(value.Value) : NotEvaluated;
    }
}

public sealed record ProductRoles(
    ProductRoleState Purchasable,
    ProductRoleState Sellable,
    ProductRoleState ProductionInput,
    ProductRoleState ProductionOutput)
{
    public static ProductRoles NotEvaluated { get; } = new(
        ProductRoleState.NotEvaluated, ProductRoleState.NotEvaluated,
        ProductRoleState.NotEvaluated, ProductRoleState.NotEvaluated);

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Purchasable);
        ArgumentNullException.ThrowIfNull(Sellable);
        ArgumentNullException.ThrowIfNull(ProductionInput);
        ArgumentNullException.ThrowIfNull(ProductionOutput);
    }
}
