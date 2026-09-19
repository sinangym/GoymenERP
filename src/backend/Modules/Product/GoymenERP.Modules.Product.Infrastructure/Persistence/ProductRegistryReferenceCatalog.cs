using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

/// <summary>
/// The reference catalogue a Draft is validated against: the real SQL code groups and, since
/// PRD-FAM-1, the released family registry. A family or template is accepted only when a well-formed
/// release holds it; with no release (or a malformed one) every supplied family and template is
/// rejected, as before — nothing is accepted by shape. Classes, units and tracking policies still have
/// no accepted adapter: absent values are allowed, supplied ones are rejected. This is not U-08/U-09
/// completion. Construct within the command scope; the atomic writer must recheck authority.
/// <para>
/// A Draft may be incomplete: here only the template binding and the attribute keys are checked,
/// with the closed v1 draft codes. Values, requiredness and candidates are the family policy's own
/// answers when a card is judged for operational use.
/// </para>
/// </summary>
public sealed class ProductRegistryReferenceCatalog : IProductReferenceCatalog
{
    private readonly ProductDbContext context;
    private readonly IProductFamilyDefinitionSourceV2 families;

    public ProductRegistryReferenceCatalog(ProductDbContext context, GroupSharedMasterCommandContext authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        families = new SqlProductFamilyDefinitionSource(context);
    }

    public async Task<ProductCodeGroup?> GetCodeGroupAsync(Guid id, CancellationToken cancellationToken)
    {
        var group = await context.CodeGroups.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        return group?.ToContract();
    }

    public async Task<bool> IsClassificationValidAsync(Guid groupId, Guid? classId, Guid? familyId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (classId is not null || await GetCodeGroupAsync(groupId, cancellationToken) is null)
        {
            return false;
        }

        return familyId is not Guid family
            || (await ReleasedAsync(cancellationToken))?.Families.Any(candidate => candidate.Id == family) == true;
    }

    public Task<bool> IsUnitValidAsync(Guid id, bool requiresKilogram, CancellationToken cancellationToken) => Unavailable(cancellationToken);
    public Task<bool> IsTrackingPolicyValidAsync(Guid id, CancellationToken cancellationToken) => Unavailable(cancellationToken);

    public async Task<IReadOnlyList<ProductFieldError>> ValidateTemplateAsync(Guid? familyId, ProductReferences references,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(references);
        cancellationToken.ThrowIfCancellationRequested();
        var definition = await ReleasedAsync(cancellationToken);
        var template = definition?.Templates.SingleOrDefault(candidate => candidate.Id == references.TemplateId
            && string.Equals(candidate.Version, references.TemplateVersion, StringComparison.Ordinal));
        if (template is null || familyId is not Guid family || !template.FamilyIds.Contains(family))
        {
            return [new("references.templateId", "invalid_reference")];
        }

        return [.. (references.Attributes?.Keys ?? [])
            .Where(key => template.Fields.All(field => !string.Equals(field.Key, key, StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal)
            .Select(key => new ProductFieldError(ProductFamilyDefinitionRulesV2.Field(key), "invalid_reference"))];
    }

    /// <summary>The well-formed release, or null when there is none or it is malformed (fail-closed).</summary>
    private async Task<ProductFamilyDefinitionV2?> ReleasedAsync(CancellationToken cancellationToken)
    {
        try
        {
            var definition = await families.GetAsync(cancellationToken);
            ProductFamilyDefinitionRulesV2.RequireWellFormed(definition);
            return definition;
        }
        catch (ProductOperationException)
        {
            return null;
        }
    }

    private static Task<bool> Unavailable(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }
}
