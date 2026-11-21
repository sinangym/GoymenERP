using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

/// <summary>
/// URN-D25 (Sinan 2026-11-21): the technical identity a duplicate WARNING compares — the card's family and its template's
/// thickness, width, quality and coating values. Exact equality only (no similarity, no tolerance). Null when the card has no
/// family, its family has no template in the released definition, the template does not define exactly one field for each of
/// the four roles, or the card lacks a value: such a card is never matched on technical grounds (it still gets the name rule).
/// </summary>
internal static class ProductTechnicalDuplicateKeyV1
{
    public static string? Of(Guid? familyId, ProductReferences references, ProductFamilyDefinitionV2? definition)
    {
        ArgumentNullException.ThrowIfNull(references);
        if (familyId is not Guid family || definition is null) return null;
        var template = ProductFamilyDefinitionRulesV2.TemplateOf(definition, family);
        if (template is null || references.Attributes is null) return null;
        var thickness = Measure(template, references, ProductTemplateFieldRolesV2.Thickness);
        var width = Measure(template, references, ProductTemplateFieldRolesV2.Width);
        var quality = Code(template, references, ProductTemplateFieldRolesV2.Quality);
        var coating = Code(template, references, ProductTemplateFieldRolesV2.Coating);
        if (thickness is null || width is null || quality is null || coating is null) return null;
        // Canonical text: 1.50 and 1.5 are the same millimetre value; codes compare ordinally (the stored code).
        return string.Join('|', family.ToString("N", CultureInfo.InvariantCulture),
            thickness.Value.ToString("0.############################", CultureInfo.InvariantCulture),
            width.Value.ToString("0.############################", CultureInfo.InvariantCulture), quality, coating);
    }

    private static ProductTemplateFieldV2? Single(ProductTemplateV2 template, string role, ProductTemplateFieldKindV2 kind)
    {
        var fields = template.Fields.Where(field => string.Equals(field.Role, role, StringComparison.Ordinal)).Take(2).ToArray();
        return fields.Length == 1 && fields[0].Kind == kind ? fields[0] : null;
    }

    private static decimal? Measure(ProductTemplateV2 template, ProductReferences references, string role)
    {
        if (Single(template, role, ProductTemplateFieldKindV2.MeasureMillimetre) is not { } field
            || !references.Attributes!.TryGetValue(field.Key, out var value)
            || value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var millimetres) || millimetres <= 0m)
            return null;
        return millimetres;
    }

    private static string? Code(ProductTemplateV2 template, ProductReferences references, string role)
    {
        if (Single(template, role, ProductTemplateFieldKindV2.DictionaryReference) is not { } field
            || !references.Attributes!.TryGetValue(field.Key, out var value) || value.ValueKind != JsonValueKind.String)
            return null;
        var code = value.GetString();
        return string.IsNullOrWhiteSpace(code) ? null : code;
    }
}
