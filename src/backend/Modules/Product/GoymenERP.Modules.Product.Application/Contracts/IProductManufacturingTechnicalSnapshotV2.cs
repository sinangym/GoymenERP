using System.Text.Json.Serialization;

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

/// <summary>Versioned public HTTP projection of the Product-owned V2 snapshot.</summary>
public static class ProductManufacturingTechnicalHttpApiV2
{
    public const string Route = "/{id:guid}/manufacturing-technical-snapshot";
    public const string OperationId = "GetProductManufacturingTechnicalSnapshotV2";

    public static string Path(Guid productId) =>
        $"{ProductApiV1.BasePath}/{productId:D}/manufacturing-technical-snapshot";
}

public sealed record ProductManufacturingTechnicalHttpQueryV2(
    string ExpectedProductVersion,
    DateOnly EffectiveOn);

public sealed record ProductManufacturingTechnicalHttpResultV2(
    ProductManufacturingTechnicalHttpSnapshotV2? Snapshot,
    IReadOnlyList<ProductFieldError> Errors);

public sealed record ProductManufacturingTechnicalHttpSnapshotV2(
    Guid ProductId,
    string ProductVersion,
    ProductManufacturingFamilyRevisionV1 Family,
    ProductManufacturingTemplateRevisionV1 Template,
    ProductManufacturingHttpGeometryV2 Geometry,
    ProductManufacturingKilogramUnitV1 UnitKg,
    DateOnly EffectiveOn);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ProductManufacturingFlatHttpGeometryV2), "flat")]
[JsonDerivedType(typeof(ProductManufacturingRoundPipeHttpGeometryV2), "roundPipe")]
[JsonDerivedType(typeof(ProductManufacturingProfileHttpGeometryV2), "profile")]
public abstract record ProductManufacturingHttpGeometryV2;

public sealed record ProductManufacturingFlatHttpGeometryV2(
    decimal ThicknessMm,
    decimal WidthMm,
    decimal? LengthMm) : ProductManufacturingHttpGeometryV2;

public sealed record ProductManufacturingRoundPipeHttpGeometryV2(
    decimal OutsideDiameterMm,
    decimal WallThicknessMm,
    decimal LengthMm) : ProductManufacturingHttpGeometryV2;

public sealed record ProductManufacturingProfileHttpGeometryV2(
    decimal SectionWidthMm,
    decimal SectionHeightMm,
    decimal WallThicknessMm,
    decimal LengthMm) : ProductManufacturingHttpGeometryV2;

public static class ProductManufacturingTechnicalHttpProjectionV2
{
    public static ProductManufacturingTechnicalHttpResultV2 From(
        ProductManufacturingTechnicalSnapshotResultV2 result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Snapshot is not { } snapshot) return new(null, result.Errors);
        return new(new(
            snapshot.ProductId,
            snapshot.ProductVersion,
            snapshot.Family,
            snapshot.Template,
            Geometry(snapshot.Geometry),
            snapshot.UnitKg,
            snapshot.EffectiveOn), result.Errors);
    }

    private static ProductManufacturingHttpGeometryV2 Geometry(ProductManufacturingGeometryV2 geometry) =>
        geometry switch
        {
            ProductManufacturingFlatGeometryV2 value => new ProductManufacturingFlatHttpGeometryV2(
                value.ThicknessMm, value.WidthMm, value.LengthMm),
            ProductManufacturingRoundPipeGeometryV2 value => new ProductManufacturingRoundPipeHttpGeometryV2(
                value.OutsideDiameterMm, value.WallThicknessMm, value.LengthMm),
            ProductManufacturingProfileGeometryV2 value => new ProductManufacturingProfileHttpGeometryV2(
                value.SectionWidthMm, value.SectionHeightMm, value.WallThicknessMm, value.LengthMm),
            _ => throw new InvalidOperationException("Unsupported Product manufacturing geometry."),
        };
}
