namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>
/// Product-owned operational read for exact, versioned Manufacturing geometry. V2 preserves the
/// shape-specific Product roles instead of mapping pipe/profile dimensions into the flat-product
/// thickness and width fields published by V1.
/// </summary>
public interface IProductManufacturingTechnicalSnapshotV2
{
    Task<ProductManufacturingTechnicalSnapshotResultV2> ReadAsync(
        ProductManufacturingTechnicalSnapshotSelectionV2 selection,
        CancellationToken cancellationToken);
}

public sealed record ProductManufacturingTechnicalSnapshotSelectionV2(
    Guid ProductId,
    string ExpectedProductVersion,
    DateOnly EffectiveOn);

public sealed record ProductManufacturingTechnicalSnapshotResultV2(
    ProductManufacturingTechnicalSnapshotV2? Snapshot,
    IReadOnlyList<ProductFieldError> Errors);

public sealed record ProductManufacturingTechnicalSnapshotV2(
    Guid ProductId,
    string ProductVersion,
    ProductManufacturingFamilyRevisionV1 Family,
    ProductManufacturingTemplateRevisionV1 Template,
    ProductManufacturingGeometryV2 Geometry,
    ProductManufacturingKilogramUnitV1 UnitKg,
    DateOnly EffectiveOn);

/// <summary>
/// One accepted Product geometry. Consumers must handle the concrete shape and must not substitute
/// one role for another. All values are stored nominal millimetres: positive, exact and at most three
/// decimals. They are not measured lot values or formula inputs inferred by Product.
/// </summary>
public abstract record ProductManufacturingGeometryV2;

public sealed record ProductManufacturingFlatGeometryV2(
    decimal ThicknessMm,
    decimal WidthMm,
    decimal? LengthMm) : ProductManufacturingGeometryV2;

public sealed record ProductManufacturingRoundPipeGeometryV2(
    decimal OutsideDiameterMm,
    decimal WallThicknessMm,
    decimal LengthMm) : ProductManufacturingGeometryV2;

public sealed record ProductManufacturingProfileGeometryV2(
    decimal SectionWidthMm,
    decimal SectionHeightMm,
    decimal WallThicknessMm,
    decimal LengthMm) : ProductManufacturingGeometryV2;
