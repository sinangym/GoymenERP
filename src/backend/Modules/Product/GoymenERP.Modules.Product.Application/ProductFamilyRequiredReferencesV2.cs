using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Modules.Product.Application;

/// <summary>Accepted released family/template/attribute validation shared by operational paths.
/// Operation-specific unit bindings remain with their own policy; this selects no conversion.</summary>
internal static class ProductFamilyRequiredReferencesV2
{
    internal static async Task<List<ProductFieldError>> ValidateAsync(IProductFamilyDefinitionSourceV2 source,
        ProductCardView card, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(card);
        cancellationToken.ThrowIfCancellationRequested();
        var definition = await source.GetAsync(cancellationToken);
        ProductFamilyDefinitionRulesV2.RequireWellFormed(definition);

        var family = card.Product.FamilyId is Guid familyId
            ? definition.Families.SingleOrDefault(candidate => candidate.Id == familyId)
            : null;
        if (family is null)
        {
            // No accepted rule set covers this card's family.
            throw new ProductOperationException(UnacceptedProductFamilyRequirementPolicyV1.UnavailableCode, 503);
        }

        var template = ProductFamilyDefinitionRulesV2.TemplateOf(definition, family.Id)
            ?? throw new ProductOperationException(ProductFamilyDefinitionRulesV2.InvalidDefinitionCode, 503);
        var errors = new List<ProductFieldError>();
        var references = card.References;
        if (references.TemplateId is null)
        {
            errors.Add(new("references.templateId", ProductFamilyPolicyCodesV1.Required));
        }
        else if (references.TemplateId != template.Id
            || !string.Equals(references.TemplateVersion, template.Version, StringComparison.Ordinal))
        {
            errors.Add(new("references.templateId", ProductFamilyPolicyCodesV1.TemplateMismatch));
        }
        else
        {
            errors.AddRange(ProductFamilyDefinitionRulesV2.ValidateAttributes(definition, template,
                references.Attributes, requireComplete: true));
        }

        return errors;
    }
}
