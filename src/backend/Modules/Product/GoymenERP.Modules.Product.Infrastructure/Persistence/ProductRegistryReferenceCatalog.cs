using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

/// <summary>
/// The reference catalogue a Draft is validated against: the real SQL code groups and, since
/// PRD-FAM-1, the released family registry. A family or template is accepted only when a well-formed
/// release holds it; with no release (or a malformed one) every supplied family and template is
/// rejected, as before — nothing is accepted by shape. A unit reference is a Settings unit IDENTITY
/// (company-independent: kg is kg in every company, <c>SettingsUnitIdentitiesV1</c>); whether a company
/// may use it is its profile, checked at eligibility (PRD-UNIT-1). Without the Settings identity
/// catalogue every unit is rejected, as before. Classes and tracking policies still have no accepted
/// adapter: absent values are allowed, supplied ones are rejected. This is not U-08/U-09 completion.
/// Construct within the command scope; the atomic writer must recheck authority.
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
    private readonly ISettingsUnitIdentityCatalogV1? units;

    public ProductRegistryReferenceCatalog(ProductDbContext context, GroupSharedMasterCommandContext authority,
        ISettingsUnitIdentityCatalogV1? units = null)
    {
        ArgumentNullException.ThrowIfNull(authority);
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        families = new SqlProductFamilyDefinitionSource(context);
        this.units = units;
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

    /// <summary>
    /// A known Settings unit identity; where kilogram is required (the stock unit, Ö1 kg→kg) it must be the
    /// kilogram identity. Not a profile check: the company's usable profile is the eligibility read's.
    /// </summary>
    public async Task<bool> IsUnitValidAsync(Guid id, bool requiresKilogram, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (units is null || id == Guid.Empty)
        {
            return false;
        }

        var identity = await units.ReadAsync(id, cancellationToken);
        return identity is not null && identity.UnitId == id && (!requiresKilogram || identity.IsKilogram);
    }
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

    /// <summary>
    /// The well-formed release, or null when there is none or it is malformed (fail-closed). The staging
    /// operation has no logger; a malformed release is logged by the options read the Draft form makes first.
    /// </summary>
    private Task<ProductFamilyDefinitionV2?> ReleasedAsync(CancellationToken cancellationToken) =>
        ProductFamilyReleaseRead.WellFormedAsync(families, logger: null, nameof(ProductRegistryReferenceCatalog), cancellationToken);

    private static Task<bool> Unavailable(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }
}
