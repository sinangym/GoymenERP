using GoymenERP.Modules.Product.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

/// <summary>
/// The released family definition, read from the Product-owned registry (PRD-FAM-1). With no released
/// family it is 503 <c>product_family_policy_unavailable</c>, exactly like the unavailable source it
/// replaces: an empty registry is "veri eksik", never an empty rule set. Whether the rows form a usable
/// release is the rules' question (<c>ProductFamilyDefinitionRulesV2.RequireWellFormed</c>); this
/// class neither repairs nor filters them.
/// </summary>
public sealed class SqlProductFamilyDefinitionSource(ProductDbContext context) : IProductFamilyDefinitionSourceV2
{
    public async Task<ProductFamilyDefinitionV2> GetAsync(CancellationToken cancellationToken)
    {
        var families = await context.Families.AsNoTracking().OrderBy(row => row.Code).ToListAsync(cancellationToken);
        if (families.Count == 0)
        {
            throw new ProductOperationException(UnacceptedProductFamilyRequirementPolicyV1.UnavailableCode, 503);
        }

        var templates = await context.Templates.AsNoTracking().OrderBy(row => row.Code).ThenBy(row => row.Version)
            .ToListAsync(cancellationToken);
        var links = await context.TemplateFamilies.AsNoTracking().ToListAsync(cancellationToken);
        var fields = await context.TemplateFields.AsNoTracking().OrderBy(row => row.Position).ToListAsync(cancellationToken);
        var entries = await context.DictionaryEntries.AsNoTracking().OrderBy(row => row.Dictionary).ThenBy(row => row.Code)
            .ToListAsync(cancellationToken);

        return new ProductFamilyDefinitionV2(
            [.. families.Select(row => row.ToContract())],
            [.. templates.Select(template => new ProductTemplateV2(
                template.Id,
                template.VersionText(),
                template.Code,
                template.Name,
                links.Where(link => link.TemplateId == template.Id && link.TemplateVersion == template.Version)
                    .Select(link => link.FamilyId).ToHashSet(),
                [.. fields.Where(field => field.TemplateId == template.Id && field.TemplateVersion == template.Version)
                    .Select(field => field.ToContract())],
                template.SourceRef))],
            [.. entries.Select(row => row.ToContract())]);
    }
}
