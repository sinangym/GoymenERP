using System.Text.Json;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Modules.Product.Application;

/// <summary>
/// PRD-CARD-1: what the normal screens need of a RELEASED family definition — the template's own fields, the
/// dictionaries they take values from, the facts a family fixes, and the names a reopened card shows. Pure: no
/// I/O, no authority, nothing invented. With no well-formed release every list is empty and every name null, so
/// the screen says "unavailable" instead of guessing (Business Q6 stays open).
/// </summary>
public static class ProductCardPresentation
{
    /// <summary>Resolve assigned unit roles through Settings identities, without a fallback or conversion.</summary>
    public static ProductReferenceLabels WithUnitLabels(ProductReferenceLabels labels, ProductUnitReferences? units,
        IReadOnlyDictionary<Guid, string> codes)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        (string Role, Guid? Id)[] assignments = [("stock", units?.Stock), ("sales", units?.Sales),
            ("purchase", units?.Purchase), ("price", units?.Price), ("production", units?.Production), ("package", units?.Package)];
        foreach (var (role, id) in assignments)
        {
            if (id is { } assigned && codes.TryGetValue(assigned, out var code)) names[role] = code;
        }
        return labels with { PrimaryStockUnit = names.GetValueOrDefault("stock"), Units = names.Count == 0 ? null : names };
    }

    /// <summary>The released templates with their own fields, in the release's order.</summary>
    public static IEnumerable<ProductTemplateOption> Templates(ProductFamilyDefinitionV2? definition) =>
        (definition?.Templates ?? []).Select(template => new ProductTemplateOption(template.Id, template.Version,
            template.Name, [.. template.Fields.Select(Field)]));

    /// <summary>The released families with the facts each one fixes.</summary>
    public static IEnumerable<ProductFamilyFactsOption> FamilyFacts(ProductFamilyDefinitionV2? definition) =>
        (definition?.Families ?? []).Select(family => Facts(family, definition!));

    /// <summary>One template field, exactly as the release states it. The kind travels as a stable wire name.</summary>
    public static ProductTemplateFieldOption Field(ProductTemplateFieldV2 field) => new(field.Key,
        field.Kind == ProductTemplateFieldKindV2.MeasureMillimetre ? "measureMillimetre" : "dictionaryReference",
        field.Role, field.Required, field.Dictionary, field.AllowsCandidate, field.AppliesWhenField,
        field.AppliesWhenCode, field.ParentField);

    /// <summary>A family with the facts it fixes and the template serving it (a family has exactly one).</summary>
    public static ProductFamilyFactsOption Facts(ProductFamilyV2 family, ProductFamilyDefinitionV2 definition)
    {
        var template = ProductFamilyDefinitionRulesV2.TemplateOf(definition, family.Id);
        return new(family.Id, family.Code, family.Name, family.Cins, family.Tur, family.Tip, template?.Id, template?.Version);
    }

    /// <summary>The release's dictionaries, each with its entries in code order; a candidate stays a candidate.</summary>
    public static IEnumerable<ProductDictionaryOption> Dictionaries(ProductFamilyDefinitionV2? definition) =>
        (definition?.Entries ?? [])
            .GroupBy(entry => entry.Dictionary, StringComparer.Ordinal)
            .OrderBy(dictionary => dictionary.Key, StringComparer.Ordinal)
            .Select(dictionary => new ProductDictionaryOption(dictionary.Key,
                [.. dictionary.OrderBy(entry => entry.Code, StringComparer.Ordinal).Select(entry => new ProductDictionaryEntryOption(
                    entry.Code, entry.DisplayName,
                    entry.Status == ProductDefinitionEntryStatusV2.Approved ? "approved" : "candidate",
                    entry.ParentCode, entry.IsExplicitNone, entry.MassGm2))]));

    /// <summary>
    /// The reopened card's names: the group's, and — when a well-formed release holds the card's family — the
    /// family's name and fixed facts. Template and attribute names require the card's exact stored template
    /// identity and version; another release cannot supply labels for an older card.
    /// </summary>
    public static ProductReferenceLabels Labels(string? group, ProductFamilyDefinitionV2? definition, Guid? familyId,
        ProductReferences references)
    {
        var family = definition is null || familyId is not { } id
            ? null
            : definition.Families.SingleOrDefault(candidate => candidate.Id == id);
        if (family is null || definition is null)
        {
            return new(group, null, null, null);
        }

        var template = ProductFamilyDefinitionRulesV2.TemplateOf(definition, family.Id);
        if (template?.Id != references.TemplateId
            || !string.Equals(template?.Version, references.TemplateVersion, StringComparison.Ordinal))
        {
            template = null;
        }
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in template?.Fields ?? [])
        {
            if (field.Dictionary is not { } dictionary
                || references.Attributes is not { } attributes
                || !attributes.TryGetValue(field.Key, out var value)
                || value.ValueKind != JsonValueKind.String
                || value.GetString() is not { } code)
            {
                continue;
            }

            var entry = definition.Entries.SingleOrDefault(candidate =>
                string.Equals(candidate.Dictionary, dictionary, StringComparison.Ordinal)
                && string.Equals(candidate.Code, code, StringComparison.Ordinal));
            if (entry is not null)
            {
                names[field.Key] = entry.DisplayName;
            }
        }

        return new(group, null, family.Name, null, template?.Name,
            new ProductFamilyFactsLabels(family.Cins, family.Tur, family.Tip),
            names.Count == 0 ? null : names);
    }
}
