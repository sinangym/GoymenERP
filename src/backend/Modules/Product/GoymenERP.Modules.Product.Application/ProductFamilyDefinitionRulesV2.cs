using System.Text.Json;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Modules.Product.Application;

/// <summary>
/// The rules of a released <see cref="ProductFamilyDefinitionV2"/>: whether the release itself is
/// well formed, which template a family uses, and whether a card's attributes fit that template.
/// Shared by the family policy (a complete card, at eligibility) and the reference catalog (a draft,
/// at save). Nothing here guesses a family, field, value or requiredness.
/// </summary>
public static class ProductFamilyDefinitionRulesV2
{
    public const string InvalidDefinitionCode = "product_family_policy_invalid";
    public const int MaximumMillimetreDecimals = 3;

    private static readonly string[] RequiredMeasureRoles = [ProductTemplateFieldRolesV2.Thickness, ProductTemplateFieldRolesV2.Width];
    private static readonly string[] RequiredReferenceRoles = [ProductTemplateFieldRolesV2.Quality, ProductTemplateFieldRolesV2.Coating];

    /// <summary>
    /// Refuses a malformed release as a whole with 503 <see cref="InvalidDefinitionCode"/>. Nothing is
    /// repaired or skipped: a release the rule set cannot read is not partly used.
    /// </summary>
    public static void RequireWellFormed(ProductFamilyDefinitionV2? definition)
    {
        if (definition is null || !IsWellFormed(definition))
        {
            throw new ProductOperationException(InvalidDefinitionCode, 503);
        }
    }

    /// <summary>The one template serving <paramref name="familyId"/> in a well-formed release, or null.</summary>
    public static ProductTemplateV2? TemplateOf(ProductFamilyDefinitionV2 definition, Guid familyId)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.Templates.SingleOrDefault(template => template.FamilyIds.Contains(familyId));
    }

    /// <summary>
    /// The field errors of <paramref name="attributes"/> against <paramref name="template"/>.
    /// <paramref name="requireComplete"/>: a required, applicable field that is missing is an error (a card
    /// being judged for operational use); otherwise only supplied values are checked.
    /// </summary>
    public static IReadOnlyList<ProductFieldError> ValidateAttributes(ProductFamilyDefinitionV2 definition,
        ProductTemplateV2 template, IReadOnlyDictionary<string, JsonElement>? attributes, bool requireComplete)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(template);
        var errors = new List<ProductFieldError>();
        var fields = template.Fields.ToDictionary(field => field.Key, StringComparer.Ordinal);
        foreach (var key in (attributes?.Keys ?? []).Order(StringComparer.Ordinal))
        {
            if (!fields.ContainsKey(key))
            {
                errors.Add(new(Field(key), ProductFamilyPolicyCodesV1.FieldUnknown));
            }
        }

        foreach (var field in template.Fields)
        {
            var present = TryGet(attributes, field.Key, out var value);
            if (!Applies(field, attributes))
            {
                if (present)
                {
                    errors.Add(new(Field(field.Key), ProductFamilyPolicyCodesV1.FieldNotApplicable));
                }

                continue;
            }

            if (!present)
            {
                if (requireComplete && field.Required)
                {
                    errors.Add(new(Field(field.Key), ProductFamilyPolicyCodesV1.Required));
                }

                continue;
            }

            var code = field.Kind == ProductTemplateFieldKindV2.MeasureMillimetre
                ? MeasureError(value)
                : ReferenceError(definition, field, value, attributes);
            if (code is not null)
            {
                errors.Add(new(Field(field.Key), code));
            }
        }

        return errors;
    }

    /// <summary>The attribute path a field error names.</summary>
    public static string Field(string key) => $"references.attributes.{key}";

    private static string? MeasureError(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var millimetres) || millimetres <= 0m)
        {
            return ProductFamilyPolicyCodesV1.InvalidValue;
        }

        return decimal.Round(millimetres, MaximumMillimetreDecimals, MidpointRounding.ToZero) != millimetres
            ? ProductFamilyPolicyCodesV1.PrecisionExceeded
            : null;
    }

    private static string? ReferenceError(ProductFamilyDefinitionV2 definition, ProductTemplateFieldV2 field,
        JsonElement value, IReadOnlyDictionary<string, JsonElement>? attributes)
    {
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } code
            || Entry(definition, field.Dictionary!, code) is not { } entry)
        {
            return ProductFamilyPolicyCodesV1.NotControlled;
        }

        if (entry.Status != ProductDefinitionEntryStatusV2.Approved && !field.AllowsCandidate)
        {
            return ProductFamilyPolicyCodesV1.ValueCandidate;
        }

        if (field.ParentField is { } parentField && entry.ParentCode is { } parentCode
            && !(TryGet(attributes, parentField, out var parent) && parent.ValueKind == JsonValueKind.String
                && string.Equals(parent.GetString(), parentCode, StringComparison.Ordinal)))
        {
            return ProductFamilyPolicyCodesV1.ParentMismatch;
        }

        return null;
    }

    /// <summary>A field with a condition applies only while its condition field holds the condition code.</summary>
    private static bool Applies(ProductTemplateFieldV2 field, IReadOnlyDictionary<string, JsonElement>? attributes) =>
        field.AppliesWhenField is null
        || (TryGet(attributes, field.AppliesWhenField, out var condition) && condition.ValueKind == JsonValueKind.String
            && string.Equals(condition.GetString(), field.AppliesWhenCode, StringComparison.Ordinal));

    private static ProductDictionaryEntryV2? Entry(ProductFamilyDefinitionV2 definition, string dictionary, string code) =>
        definition.Entries.SingleOrDefault(entry => string.Equals(entry.Dictionary, dictionary, StringComparison.Ordinal)
            && string.Equals(entry.Code, code, StringComparison.Ordinal));

    /// <summary>Missing, JSON null and undefined are all "no value": an unknown is never an answer.</summary>
    private static bool TryGet(IReadOnlyDictionary<string, JsonElement>? attributes, string key, out JsonElement value)
    {
        value = default;
        return attributes is not null && attributes.TryGetValue(key, out value)
            && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
    }

    private static bool IsWellFormed(ProductFamilyDefinitionV2 definition)
    {
        if (definition.Families is not { Count: > 0 } families || definition.Templates is not { Count: > 0 } templates
            || definition.Entries is null)
        {
            return false;
        }

        if (families.Any(family => family is null || family.Id == Guid.Empty
                || Blank(family.Code, family.Name, family.Cins, family.Tur, family.Tip, family.SourceRef)
                || family.RuleSet != ProductFamilyRuleSetsV2.GdcBandCoil)
            || families.Select(family => family.Id).Distinct().Count() != families.Count
            || families.Select(family => family.Code).Distinct(StringComparer.Ordinal).Count() != families.Count)
        {
            return false;
        }

        var entries = definition.Entries;
        if (entries.Any(entry => entry is null || Blank(entry.Dictionary, entry.Code, entry.DisplayName, entry.SourceRef)
                || !Enum.IsDefined(entry.Status) || entry.MassGm2 is <= 0m
                || (entry.IsExplicitNone && entry.Status != ProductDefinitionEntryStatusV2.Approved)
                || (entry.ParentCode is not null && string.IsNullOrWhiteSpace(entry.ParentCode)))
            || entries.Select(entry => (entry.Dictionary, entry.Code)).Distinct().Count() != entries.Count)
        {
            return false;
        }

        // Every family is served by exactly one template.
        if (templates.Any(template => template is null)
            || families.Any(family => templates.Count(template => template.FamilyIds?.Contains(family.Id) == true) != 1))
        {
            return false;
        }

        return templates.All(template => IsWellFormed(template, definition));
    }

    private static bool IsWellFormed(ProductTemplateV2 template, ProductFamilyDefinitionV2 definition)
    {
        if (template.Id == Guid.Empty || Blank(template.Code, template.Name, template.SourceRef)
            || template.FamilyIds is not { Count: > 0 } familyIds
            || familyIds.Any(id => definition.Families.All(family => family.Id != id))
            || template.Fields is not { Count: > 0 } fields
            || fields.Any(field => field is null || string.IsNullOrWhiteSpace(field.Key))
            || fields.Select(field => field.Key).Distinct(StringComparer.Ordinal).Count() != fields.Count)
        {
            return false;
        }

        try
        {
            _ = ProductVersion.Parse(template.Version);
        }
        catch (ArgumentException)
        {
            return false;
        }

        var byKey = fields.ToDictionary(field => field.Key, StringComparer.Ordinal);
        foreach (var field in fields)
        {
            var reference = field.Kind == ProductTemplateFieldKindV2.DictionaryReference;
            // A candidate never satisfies a required field: no required field takes candidates (ORG #334 P3-1).
            // A role field's dictionary must hold an approved entry (checked with the rule set below); a
            // non-role field's dictionary may be empty while its values are undecided (Business 5740811346:
            // colour applies with the painted coating but no colour value is activated). Such a field
            // refuses every value it is given, and when required it keeps its cards closed.
            if (!Enum.IsDefined(field.Kind)
                || (field.Required && field.AllowsCandidate)
                || (reference ? string.IsNullOrWhiteSpace(field.Dictionary)
                    || (field.Role is not null && definition.Entries.All(entry => entry.Dictionary != field.Dictionary))
                    : field.Dictionary is not null || field.AllowsCandidate || field.ParentField is not null)
                || (field.AppliesWhenField is null) != (field.AppliesWhenCode is null)
                || (field.Role is not null && !RequiredMeasureRoles.Concat(RequiredReferenceRoles).Contains(field.Role)))
            {
                return false;
            }

            // A condition names another reference field and an approved code of its dictionary.
            if (field.AppliesWhenField is { } conditionKey
                && !(conditionKey != field.Key && byKey.TryGetValue(conditionKey, out var condition)
                    && condition.Kind == ProductTemplateFieldKindV2.DictionaryReference
                    && Entry(definition, condition.Dictionary!, field.AppliesWhenCode!) is { Status: ProductDefinitionEntryStatusV2.Approved }))
            {
                return false;
            }

            // A parent is another reference field, and every parented entry names an entry of the parent's dictionary.
            if (field.ParentField is { } parentKey
                && !(parentKey != field.Key && byKey.TryGetValue(parentKey, out var parent)
                    && parent.Kind == ProductTemplateFieldKindV2.DictionaryReference
                    && definition.Entries.Where(entry => entry.Dictionary == field.Dictionary && entry.ParentCode is not null)
                        .All(entry => Entry(definition, parent.Dictionary!, entry.ParentCode!) is not null)))
            {
                return false;
            }
        }

        // The GDC band/coil rule set: each of its four roles exactly once, required, unconditional,
        // and of the right kind; the controlled fields take approved values only.
        foreach (var role in RequiredMeasureRoles.Concat(RequiredReferenceRoles))
        {
            var holders = fields.Where(field => field.Role == role).ToArray();
            var expected = RequiredMeasureRoles.Contains(role)
                ? ProductTemplateFieldKindV2.MeasureMillimetre
                : ProductTemplateFieldKindV2.DictionaryReference;
            if (holders.Length != 1 || holders[0].Kind != expected || !holders[0].Required
                || holders[0].AppliesWhenField is not null || holders[0].AllowsCandidate
                || (expected == ProductTemplateFieldKindV2.DictionaryReference && !definition.Entries.Any(entry =>
                    entry.Dictionary == holders[0].Dictionary && entry.Status == ProductDefinitionEntryStatusV2.Approved)))
            {
                return false;
            }
        }

        // The coating field's dictionary states exactly one explicit "none" (Kaplamasız).
        var coating = fields.Single(field => field.Role == ProductTemplateFieldRolesV2.Coating);
        return definition.Entries.Count(entry => entry.Dictionary == coating.Dictionary && entry.IsExplicitNone) == 1;
    }

    private static bool Blank(params string?[] values) => values.Any(string.IsNullOrWhiteSpace);
}
