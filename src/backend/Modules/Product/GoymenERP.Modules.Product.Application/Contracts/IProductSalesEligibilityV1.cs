namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>
/// Product-owned operational read port; implementations must resolve current server
/// authority and revalidate lifecycle, roles and family-required references against
/// the real owners. A previous selection is not authorization for a Sales command.
/// No runtime implementation or permission mapping is provided by this contract.
/// </summary>
public interface IProductSalesEligibilityV1
{
    Task<ProductSalesEligibilityV1> EvaluateAsync(ProductSalesSelectionV1 selection,
        CancellationToken cancellationToken);
}

public sealed record ProductSalesSelectionV1(Guid ProductId, string ExpectedProductVersion, Guid OrderUnitId,
    DateOnly EffectiveOn);

/// <summary>
/// Only the owner can produce a usable snapshot after current reference/profile
/// validation. Quantity and conversions remain the measurement provider's concern.
/// CompanyId is not Product ownership; request authority belongs to the server scope.
/// </summary>
public sealed record ProductSalesEligibilityV1(ProductSalesSnapshotV1? Snapshot,
    IReadOnlyList<ProductFieldError> Errors);

public sealed record ProductSalesSnapshotV1(Guid ProductId, string ProductVersion, string Code,
    string Name, ProductSalesUnitProfileV1 StockUnit, ProductSalesUnitProfileV1 SalesUnit,
    ProductSalesUnitProfileV1 OrderUnit, DateOnly EffectiveOn);

/// <summary>Reference to the Settings-owned profile; not a copied unit catalogue.</summary>
public sealed record ProductSalesUnitProfileV1(Guid CompanyId, Guid UnitId, Guid ProfileId, string Version);
