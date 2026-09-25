namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>
/// Product-owned operational read for the immutable technical facts a Manufacturing plan may copy.
/// A positive answer is tied to the exact stored Product version, the released family/template
/// revisions and the Settings-owned kilogram profile effective on the requested date. It is not a
/// density, speed, kerf, coating-consumption, scrap, tolerance or process-policy source.
/// </summary>
public interface IProductManufacturingTechnicalSnapshotV1
{
    Task<ProductManufacturingTechnicalSnapshotResultV1> ReadAsync(
        ProductManufacturingTechnicalSnapshotSelectionV1 selection,
        CancellationToken cancellationToken);
}

public sealed record ProductManufacturingTechnicalSnapshotSelectionV1(
    Guid ProductId,
    string ExpectedProductVersion,
    DateOnly EffectiveOn);

public sealed record ProductManufacturingTechnicalSnapshotResultV1(
    ProductManufacturingTechnicalSnapshotV1? Snapshot,
    IReadOnlyList<ProductFieldError> Errors);

/// <summary>
/// Exact Product facts available from accepted sources. Package identity and profile geometry
/// (including length/perimeter) are deliberately absent: no accepted real owner source currently
/// validates them, so consumers must treat those facts as unavailable rather than infer them.
/// </summary>
public sealed record ProductManufacturingTechnicalSnapshotV1(
    Guid ProductId,
    string ProductVersion,
    ProductManufacturingFamilyRevisionV1 Family,
    ProductManufacturingTemplateRevisionV1 Template,
    decimal ThicknessMm,
    decimal WidthMm,
    ProductManufacturingKilogramUnitV1 UnitKg,
    DateOnly EffectiveOn);

public sealed record ProductManufacturingFamilyRevisionV1(
    Guid FamilyId,
    string FamilyCode,
    string SourceRevision);

public sealed record ProductManufacturingTemplateRevisionV1(
    Guid TemplateId,
    string TemplateVersion,
    string TemplateCode,
    string SourceRevision);

public sealed record ProductManufacturingKilogramUnitV1(
    Guid UnitId,
    string UnitCode,
    Guid ProfileId,
    string ProfileVersion);
