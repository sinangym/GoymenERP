namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>
/// The data the accepted GDC band/coil rule set (Business #265 5722701085) needs, as its
/// authorised owner publishes it: which family the rules cover, which template fields hold the
/// thickness, width, quality and coating status, and the controlled quality and coating lists.
/// <para>
/// None of these values is chosen by Product. They come from the authorised product family,
/// template and dictionary source through <see cref="IGdcBandCoilFamilyDefinitionSourceV1"/>. Until
/// that source exists the data is missing — the rule set is accepted, the data is not — and every
/// evaluation stays unavailable; the policy is not reopened.
/// </para>
/// </summary>
/// <param name="FamilyId">The Product family the rules cover (the authorised GDC band/coil family).</param>
/// <param name="ThicknessFieldId">Template field holding the nominal thickness in mm.</param>
/// <param name="WidthFieldId">Template field holding the nominal width in mm.</param>
/// <param name="QualityFieldId">Template field holding the controlled quality reference.</param>
/// <param name="CoatingFieldId">Template field holding the controlled coating-status reference.</param>
/// <param name="QualityReferences">The controlled quality references (the only accepted values).</param>
/// <param name="CoatingStatusReferences">The controlled coating statuses (the only accepted values).</param>
/// <param name="UncoatedStatusReference">
/// The explicit "kaplamasız" status. It is a valid, stated status — never the same as an unknown
/// or empty value — and must be one of <paramref name="CoatingStatusReferences"/>.
/// </param>
public sealed record GdcBandCoilFamilyDefinitionV1(
    Guid FamilyId,
    string ThicknessFieldId,
    string WidthFieldId,
    string QualityFieldId,
    string CoatingFieldId,
    IReadOnlySet<string> QualityReferences,
    IReadOnlySet<string> CoatingStatusReferences,
    string UncoatedStatusReference);

/// <summary>
/// Where the GDC band/coil definition comes from: the authorised owner of product families,
/// templates and dictionaries. It throws <see cref="ProductOperationException"/> 503 when that
/// source has no approved data; it never returns a guessed or example definition.
/// </summary>
public interface IGdcBandCoilFamilyDefinitionSourceV1
{
    Task<GdcBandCoilFamilyDefinitionV1> GetAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The only shipped source: no authorised GDC family, template or dictionary data exists yet, so
/// every read is unavailable ("veri eksik"). Tests use their own isolated synthetic source.
/// </summary>
public sealed class UnavailableGdcBandCoilFamilyDefinitionSourceV1 : IGdcBandCoilFamilyDefinitionSourceV1
{
    public Task<GdcBandCoilFamilyDefinitionV1> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException<GdcBandCoilFamilyDefinitionV1>(
            new ProductOperationException(UnacceptedProductFamilyRequirementPolicyV1.UnavailableCode, 503));
    }
}

/// <summary>
/// The field-error codes a family policy returns beside the shared <c>required</c>. They are the
/// family policy's own answers about a selection, not part of the draft edit code set
/// (<see cref="ProductFieldCodesV1"/>).
/// </summary>
public static class ProductFamilyPolicyCodesV1
{
    /// <summary>The field is missing or null. An unknown value is never an accepted status.</summary>
    public const string Required = "required";

    /// <summary>A measure is not a positive number of millimetres.</summary>
    public const string InvalidValue = "family_invalid_value";

    /// <summary>A measure has more than three decimals (Ö2); it is refused, never rounded.</summary>
    public const string PrecisionExceeded = "family_precision_exceeded";

    /// <summary>The value is not one of the owner's controlled references.</summary>
    public const string NotControlled = "family_not_controlled";

    /// <summary>The family's first path is kg→kg: the sales unit must be the stock unit (no conversion).</summary>
    public const string UnitPathMismatch = "family_unit_path_mismatch";
}
