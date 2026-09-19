using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Modules.Product.Application;

/// <summary>
/// The accepted GDC band/coil rule set (Business #265 5722701085, Sinan Ö1/Ö2) over the released
/// family definition (PRD-FAM-1, #66 5739827887). A card passes only when all of these hold:
/// <list type="bullet">
/// <item>its family is one of the released families and the card is bound to that family's template
/// version;</item>
/// <item>every required, applicable field is present: nominal thickness and width &gt; 0 mm with at
/// most three decimals (Ö2; a fourth decimal is refused, never rounded), an approved steel grade and
/// an approved coating type. Kaplamasız is a valid, stated coating; a missing coating is not
/// "uncoated";</item>
/// <item>no supplied value is a candidate in a field that does not take candidates, belongs to a
/// field that does not apply (a colour without the painted coating), names another parent, or is not
/// a template field;</item>
/// <item>the sales unit is the stock unit: the first path is kg→kg with no conversion (whether the
/// stock unit is kilogram is Settings' answer, checked by the eligibility read).</item>
/// </list>
/// Lifecycle, the sellable role, the Settings profiles and the authority fences are the eligibility
/// read's; a positive result needs all of them together.
/// <para>
/// Fail-closed: no released definition, or a card of a family it does not hold, is 503
/// <c>product_family_policy_unavailable</c> — never "no errors". A malformed release is 503
/// <c>product_family_policy_invalid</c>.
/// </para>
/// </summary>
public sealed class ProductFamilyRequirementPolicyV2(IProductFamilyDefinitionSourceV2 source)
    : IProductFamilyRequirementPolicyV1
{
    public async Task<IReadOnlyList<ProductFieldError>> ValidateAsync(ProductCardView card,
        CancellationToken cancellationToken)
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

        // kg→kg needs both bindings: a missing one is not a pass (SİPARİŞ #300 P3-1).
        var units = references.Units;
        var stock = units?.Stock is Guid stockId && stockId != Guid.Empty ? stockId : (Guid?)null;
        var sales = units?.Sales is Guid salesId && salesId != Guid.Empty ? salesId : (Guid?)null;
        if (stock is null)
        {
            errors.Add(new("references.units.stock", ProductFamilyPolicyCodesV1.Required));
        }

        if (sales is null)
        {
            errors.Add(new("references.units.sales", ProductFamilyPolicyCodesV1.Required));
        }
        else if (stock is not null && stock != sales)
        {
            errors.Add(new("references.units.sales", ProductFamilyPolicyCodesV1.UnitPathMismatch));
        }

        return errors;
    }
}
