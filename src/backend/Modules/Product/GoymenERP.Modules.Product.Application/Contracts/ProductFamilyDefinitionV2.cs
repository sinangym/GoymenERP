namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>
/// The authorised Product family definition (PRD-FAM-1): the canonical families, the versioned
/// technical template bound to them and the controlled dictionaries its fields take values from.
/// <para>
/// Every value comes from a stated source and carries it (<c>SourceRef</c>, a #66 decision id). None
/// is chosen by Product. The families and field semantics are Sinan's (#66 5739827887: HR/CR coil and
/// HR/CR slit strip; Cins = form, Tür = process, Tip = sub-form, fixed per family; Kaplama and Renk
/// separate; steel grade, coating type and coating designation kept apart). The rule set is Business
/// #265 5722701085 with Ö1/Ö2.
/// </para>
/// <para>
/// A dictionary entry is <see cref="ProductDefinitionEntryStatusV2.Approved"/> or
/// <see cref="ProductDefinitionEntryStatusV2.Candidate"/>. A candidate (a Göymen commercial code whose
/// meaning is not established, or a web/standard suggestion) is held and shown as a candidate but never
/// satisfies a controlled field, and carries no invented meaning or mass (#66 5739745192, 5739827887).
/// </para>
/// </summary>
public sealed record ProductFamilyDefinitionV2(
    IReadOnlyList<ProductFamilyV2> Families,
    IReadOnlyList<ProductTemplateV2> Templates,
    IReadOnlyList<ProductDictionaryEntryV2> Entries);

/// <summary>One canonical family. Cins/Tür/Tip are fixed by the family, never typed on a card.</summary>
/// <param name="RuleSet">The accepted rule set that validates cards of this family (<see cref="ProductFamilyRuleSetsV2"/>).</param>
public sealed record ProductFamilyV2(
    Guid Id,
    string Code,
    string Name,
    string Cins,
    string Tur,
    string Tip,
    string RuleSet,
    string SourceRef);

/// <summary>A technical template version and the families it serves. A family has exactly one template.</summary>
public sealed record ProductTemplateV2(
    Guid Id,
    string Version,
    string Code,
    string Name,
    IReadOnlySet<Guid> FamilyIds,
    IReadOnlyList<ProductTemplateFieldV2> Fields,
    string SourceRef);

/// <summary>
/// One template field, keyed by <see cref="Key"/> in <c>references.attributes</c>.
/// </summary>
/// <param name="Role">What the rule set knows the field as (<see cref="ProductTemplateFieldRolesV2"/>), or null.</param>
/// <param name="Dictionary">The dictionary a <see cref="ProductTemplateFieldKindV2.DictionaryReference"/> takes its codes from.</param>
/// <param name="AllowsCandidate">
/// Whether a candidate entry may be stored in this field (an informational, optional field). A candidate
/// never makes a required field pass.
/// </param>
/// <param name="AppliesWhenField">With <paramref name="AppliesWhenCode"/>: the field applies only when that field holds that code.</param>
/// <param name="ParentField">A field whose value must be this entry's parent (e.g. a sub-quality under its grade).</param>
public sealed record ProductTemplateFieldV2(
    string Key,
    ProductTemplateFieldKindV2 Kind,
    string? Role,
    bool Required,
    string? Dictionary,
    bool AllowsCandidate,
    string? AppliesWhenField,
    string? AppliesWhenCode,
    string? ParentField);

/// <summary>One controlled value. A code is unique within its dictionary.</summary>
/// <param name="ParentCode">The parent entry's code, in the parent field's dictionary, when the source states one.</param>
/// <param name="MassGm2">Coating mass in g/m², only when the source states it; never derived.</param>
/// <param name="IsExplicitNone">
/// The stated "none" of its dictionary (Kaplamasız): a valid, explicit value, never the same as a missing one.
/// Only an approved entry can be it.
/// </param>
public sealed record ProductDictionaryEntryV2(
    string Dictionary,
    string Code,
    string DisplayName,
    ProductDefinitionEntryStatusV2 Status,
    string SourceRef,
    string? ParentCode,
    decimal? MassGm2,
    bool IsExplicitNone);

public enum ProductDefinitionEntryStatusV2
{
    Approved = 1,
    Candidate = 2,
}

public enum ProductTemplateFieldKindV2
{
    /// <summary>A nominal measure in millimetres: a JSON number &gt; 0 with at most three decimals (Ö2).</summary>
    MeasureMillimetre = 1,

    /// <summary>A code of the field's dictionary, as a JSON string.</summary>
    DictionaryReference = 2,
}

/// <summary>The accepted, family-specific rule sets.</summary>
public static class ProductFamilyRuleSetsV2
{
    public const string GdcBandCoil = "gdc-band-coil";
    public const string GdcCutSheetPlate = "gdc-cut-sheet-plate";
    public const string GdcWeldedRoundPipe = "gdc-welded-round-pipe";
    public const string GdcWeldedSquareProfile = "gdc-welded-square-profile";
    public const string GdcWeldedRectangleProfile = "gdc-welded-rectangle-profile";
}

/// <summary>The field roles a released Product rule set may require, each exactly once and required.</summary>
public static class ProductTemplateFieldRolesV2
{
    public const string Thickness = "thickness";
    public const string Width = "width";
    public const string Length = "length";
    public const string OutsideDiameter = "outside-diameter";
    public const string WallThickness = "wall-thickness";
    public const string SectionWidth = "section-width";
    public const string SectionHeight = "section-height";
    public const string Quality = "quality";
    public const string Coating = "coating";
}

/// <summary>
/// Where the definition comes from: the Product-owned registry. It throws
/// <see cref="ProductOperationException"/> 503 <c>product_family_policy_unavailable</c> while no
/// definition is released; it never returns a guessed or example definition.
/// </summary>
public interface IProductFamilyDefinitionSourceV2
{
    Task<ProductFamilyDefinitionV2> GetAsync(CancellationToken cancellationToken);
}

/// <summary>A source with no released definition: every read is unavailable ("veri eksik").</summary>
public sealed class UnavailableProductFamilyDefinitionSourceV2 : IProductFamilyDefinitionSourceV2
{
    public Task<ProductFamilyDefinitionV2> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException<ProductFamilyDefinitionV2>(
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

    /// <summary>The value is a candidate entry: held, but not approved, so it does not satisfy the field.</summary>
    public const string ValueCandidate = "family_value_candidate";

    /// <summary>The field does not apply here (e.g. a colour without the painted coating).</summary>
    public const string FieldNotApplicable = "family_field_not_applicable";

    /// <summary>The entry's parent is not the value of its parent field (e.g. a sub-quality of another grade).</summary>
    public const string ParentMismatch = "family_value_parent_mismatch";

    /// <summary>The attribute is not a field of the card's template.</summary>
    public const string FieldUnknown = "family_field_unknown";

    /// <summary>The card is not bound to its family's template, or to another version of it.</summary>
    public const string TemplateMismatch = "family_template_mismatch";
}
