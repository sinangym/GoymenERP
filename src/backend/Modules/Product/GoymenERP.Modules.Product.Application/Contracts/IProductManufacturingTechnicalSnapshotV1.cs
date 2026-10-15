namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>
/// Product-owned operational read for the immutable technical facts a Manufacturing plan may copy.
/// A positive answer is tied to the exact stored Product version, the released family/template
/// revisions and the Settings-owned kilogram profile effective on the requested date. It is not a
/// density, speed, kerf, coating-consumption, scrap, tolerance or process-policy source.
/// <para>
/// <b>Dates.</b> <c>EffectiveOn</c> selects only the Settings kilogram profile. The family/template
/// definition is always the current accepted release, so a future-dated plan must not read the answer
/// as an "as-of" definition; the snapshot echoes <c>EffectiveOn</c> back unchanged.
/// </para>
/// <para>
/// <b>Failure channels</b> (a consumer handles every one; none is a positive answer):
/// <list type="bullet">
/// <item><b>Field errors</b> — <see cref="ProductManufacturingTechnicalSnapshotResultV1.Errors"/> non-empty and
/// <c>Snapshot</c> null. Product field paths and codes:
/// <c>productId</c> <c>invalid_reference</c> / <c>lifecycle_conflict</c>;
/// <c>expectedProductVersion</c> <c>version_conflict</c> / <c>invalid_version</c>;
/// <c>references.units.stock</c> <c>invalid_reference</c>;
/// <c>references.templateId</c> <c>required</c> / <c>family_template_mismatch</c>;
/// <c>references.attributes.&lt;key&gt;</c> with the family codes of
/// <see cref="ProductFamilyPolicyCodesV1"/> (e.g. <c>required</c>, <c>family_invalid_value</c>,
/// <c>family_precision_exceeded</c>, <c>family_not_controlled</c>, <c>family_value_candidate</c>). A card
/// measure the snapshot cannot publish (missing, non-numeric, not positive, more than three decimals) is
/// reported on that attribute path with <c>required</c>, <c>family_invalid_value</c> or
/// <c>family_precision_exceeded</c> — a card-value code, never the definition code.</item>
/// <item><b><c>ProductOperationException</c> 403</b> <c>product_access_denied</c>: no identity, no company
/// access, no fresh <c>product.operational-selection.read</c>, or the company/actor changed during a read.</item>
/// <item><b><c>ProductOperationException</c> 503</b>: <c>product_runtime_unconfigured</c> (runtime or an owner
/// prerequisite absent), <c>product_family_policy_unavailable</c> (the card's family is not in the accepted
/// release), <c>product_family_policy_invalid</c> (malformed release),
/// <c>product_manufacturing_technical_snapshot_unavailable</c> (synthetic/test provenance), and
/// <c>product_manufacturing_measure_undefined</c> (the released template does not name exactly one
/// millimetre field for thickness or width).</item>
/// <item><b><c>SettingsPrerequisiteException</c></b> from the Settings kilogram profile read, passed through with
/// the Settings owner's code.</item>
/// <item><b><c>InvalidOperationException</c></b>: the stored Product record cannot be restored (an internal fault).</item>
/// </list>
/// </para>
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
/// Exact Product facts available from accepted sources. Package identity and profile geometry other
/// than the accepted cut-sheet/plate nominal length (including perimeter) are deliberately absent:
/// consumers must treat unavailable facts as unavailable rather than infer them.
/// <para>
/// <b>Measure scale.</b> <c>ThicknessMm</c>, <c>WidthMm</c> and, when defined by the family, <c>LengthMm</c>
/// are the card's stored nominal millimetres, exact, positive and with at most three decimals (Ö2, the
/// scale the Manufacturing engines accept). Legacy families do not define length and publish null. A
/// stored value outside the accepted scale is refused as a field error, never rounded or cut.
/// </para>
/// </summary>
public sealed record ProductManufacturingTechnicalSnapshotV1(
    Guid ProductId,
    string ProductVersion,
    ProductManufacturingFamilyRevisionV1 Family,
    ProductManufacturingTemplateRevisionV1 Template,
    decimal ThicknessMm,
    decimal WidthMm,
    ProductManufacturingKilogramUnitV1 UnitKg,
    DateOnly EffectiveOn,
    decimal? LengthMm = null);

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
    Guid CompanyId,
    Guid UnitId,
    string UnitCode,
    Guid ProfileId,
    string ProfileVersion);
