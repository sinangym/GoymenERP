using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

public sealed record ProductManufacturingTechnicalReadScope(Guid CompanyId, string ActorId);

/// <summary>One measure as the snapshot reads it: exactly one of the value or a card-value field error.</summary>
public sealed record ProductManufacturingMeasureReadV1(decimal? Millimetres, ProductFieldError? Error);

public interface IProductManufacturingTechnicalRecordReader
{
    Task<ProductStoredRecord?> ReadForManufacturingTechnicalSnapshotAsync(
        ProductManufacturingTechnicalReadScope scope,
        Guid productId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Reads Product-owned Manufacturing facts without importing Manufacturing contracts. Every owner
/// read is followed by the same fresh company/actor/permission fence used by Product operational
/// selectors. Test-only family releases are never promoted to a positive runtime answer.
/// </summary>
public sealed class ProductManufacturingTechnicalSnapshotService(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority,
    IProductManufacturingTechnicalRecordReader records,
    IProductFamilyDefinitionSourceV2 definitions,
    ProductUnitProfileAdapter unitProfiles)
    : IProductManufacturingTechnicalSnapshotV1
{
    public const string UnavailableCode = "product_manufacturing_technical_snapshot_unavailable";

    /// <summary>
    /// 503: the released template does not name exactly one measure field for a role the snapshot publishes
    /// (e.g. a later family without a thickness/width, or a duplicated role). A definition gap, never a card error.
    /// </summary>
    public const string MeasureUndefinedCode = "product_manufacturing_measure_undefined";

    public async Task<ProductManufacturingTechnicalSnapshotResultV1> ReadAsync(
        ProductManufacturingTechnicalSnapshotSelectionV1 selection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireScopeAsync(cancellationToken);
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.ProductId == Guid.Empty) return Rejected("productId", "invalid_reference");

        var record = await FencedAsync(scope,
            () => records.ReadForManufacturingTechnicalSnapshotAsync(scope, selection.ProductId, cancellationToken),
            cancellationToken);
        if (record is null) return Rejected("productId", "invalid_reference");

        var card = Restore(record);
        var local = ProductStockPreconditions.Check(card,
            new ProductStockSelectionV1(selection.ProductId, selection.ExpectedProductVersion, selection.EffectiveOn));
        if (local.Count > 0) return new(null, local);

        var definition = await FencedAsync(scope, () => definitions.GetAsync(cancellationToken), cancellationToken);
        ProductFamilyDefinitionRulesV2.RequireWellFormed(definition);

        var family = card.Product.FamilyId is Guid familyId
            ? definition.Families.SingleOrDefault(candidate => candidate.Id == familyId)
            : null;
        if (family is null)
            throw new ProductOperationException(UnacceptedProductFamilyRequirementPolicyV1.UnavailableCode, 503);

        var template = ProductFamilyDefinitionRulesV2.TemplateOf(definition, family.Id)
            ?? throw new ProductOperationException(ProductFamilyDefinitionRulesV2.InvalidDefinitionCode, 503);
        if (IsSynthetic(family.SourceRef) || IsSynthetic(template.SourceRef))
            throw new ProductOperationException(UnavailableCode, 503);

        var technicalErrors = ValidateTechnicalReferences(card.References, definition, template);
        if (technicalErrors.Count > 0) return new(null, technicalErrors);
        if (UsesSyntheticDictionaryValue(card.References, definition, template))
            throw new ProductOperationException(UnavailableCode, 503);

        var stockUnitId = card.References.Units!.Stock!.Value;
        var unit = await FencedAsync(scope,
            () => unitProfiles.ReadAsync(stockUnitId, selection.EffectiveOn, requiresKilogram: true, cancellationToken),
            cancellationToken);
        if (!unit.IsUsable) return Rejected("references.units.stock", "invalid_reference");

        var thickness = ReadMeasure(card.References, template, ProductTemplateFieldRolesV2.Thickness);
        var width = ReadMeasure(card.References, template, ProductTemplateFieldRolesV2.Width);
        ProductFieldError[] measureErrors = [.. new[] { thickness.Error, width.Error }.OfType<ProductFieldError>()];
        if (measureErrors.Length > 0) return new(null, measureErrors);
        var profile = unit.Profile;
        var snapshot = new ProductManufacturingTechnicalSnapshotV1(
            card.Product.Id,
            card.Product.Version,
            new(family.Id, family.Code, family.SourceRef),
            new(template.Id, template.Version, template.Code, template.SourceRef),
            thickness.Millimetres!.Value,
            width.Millimetres!.Value,
            new(profile.CompanyId, profile.UnitId, profile.UnitCode, profile.Profile.ProfileId,
                profile.Profile.Version.ToString(CultureInfo.InvariantCulture)),
            selection.EffectiveOn);
        return new(snapshot, []);
    }

    private static List<ProductFieldError> ValidateTechnicalReferences(ProductReferences references,
        ProductFamilyDefinitionV2 definition, ProductTemplateV2 template)
    {
        if (references.TemplateId is null)
            return [new("references.templateId", ProductFamilyPolicyCodesV1.Required)];
        if (references.TemplateId != template.Id
            || !string.Equals(references.TemplateVersion, template.Version, StringComparison.Ordinal))
            return [new("references.templateId", ProductFamilyPolicyCodesV1.TemplateMismatch)];
        return [.. ProductFamilyDefinitionRulesV2.ValidateAttributes(definition, template,
            references.Attributes, requireComplete: true)];
    }

    /// <summary>
    /// The card's value for the one template field carrying <paramref name="role"/>, as the snapshot publishes it:
    /// a positive JSON number of millimetres with at most
    /// <see cref="ProductFamilyDefinitionRulesV2.MaximumMillimetreDecimals"/> decimals (Ö2), never rounded.
    /// <list type="bullet">
    /// <item>No field, or more than one field, with the role: 503 <see cref="MeasureUndefinedCode"/> (definition).</item>
    /// <item>Missing or null card value: field error <see cref="ProductFamilyPolicyCodesV1.Required"/>.</item>
    /// <item>Not a number, or not positive: field error <see cref="ProductFamilyPolicyCodesV1.InvalidValue"/>.</item>
    /// <item>More than three decimals: field error <see cref="ProductFamilyPolicyCodesV1.PrecisionExceeded"/>.</item>
    /// </list>
    /// Card-value errors name <c>references.attributes.&lt;key&gt;</c>, the path the family validation already uses.
    /// </summary>
    public static ProductManufacturingMeasureReadV1 ReadMeasure(ProductReferences references, ProductTemplateV2 template,
        string role)
    {
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(template);
        var fields = template.Fields
            .Where(candidate => string.Equals(candidate.Role, role, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (fields.Length != 1 || fields[0].Kind != ProductTemplateFieldKindV2.MeasureMillimetre)
            throw new ProductOperationException(MeasureUndefinedCode, 503);

        var path = ProductFamilyDefinitionRulesV2.Field(fields[0].Key);
        if (references.Attributes is null || !references.Attributes.TryGetValue(fields[0].Key, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return new(null, new(path, ProductFamilyPolicyCodesV1.Required));
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var millimetres) || millimetres <= 0m)
            return new(null, new(path, ProductFamilyPolicyCodesV1.InvalidValue));
        if (decimal.Round(millimetres, ProductFamilyDefinitionRulesV2.MaximumMillimetreDecimals, MidpointRounding.ToZero)
            != millimetres)
            return new(null, new(path, ProductFamilyPolicyCodesV1.PrecisionExceeded));
        return new(millimetres, null);
    }

    private static bool IsSynthetic(string sourceRevision) =>
        sourceRevision.StartsWith("SYNTHETIC", StringComparison.OrdinalIgnoreCase)
        || sourceRevision.StartsWith("SENTETIK", StringComparison.OrdinalIgnoreCase);

    private static bool UsesSyntheticDictionaryValue(ProductReferences references,
        ProductFamilyDefinitionV2 definition, ProductTemplateV2 template) =>
        template.Fields.Where(field => field.Kind == ProductTemplateFieldKindV2.DictionaryReference)
            .Any(field => references.Attributes!.TryGetValue(field.Key, out var value)
                && value.ValueKind == JsonValueKind.String
                && definition.Entries.Single(entry => entry.Dictionary == field.Dictionary
                    && entry.Code == value.GetString()) is { SourceRef: var sourceRevision }
                && IsSynthetic(sourceRevision));

    private static ProductCardView Restore(ProductStoredRecord record)
    {
        try
        {
            return new(ProductDraftView.FromProduct(record.RestoreProduct()), record.RestoreReferences());
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException)
        {
            throw new InvalidOperationException("The stored Product record could not be restored.", exception);
        }
    }

    private async Task<T> FencedAsync<T>(ProductManufacturingTechnicalReadScope scope, Func<Task<T>> read,
        CancellationToken cancellationToken)
    {
        T result;
        try
        {
            result = await read();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            await RequireSameScopeAsync(scope, cancellationToken);
            throw;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await RequireSameScopeAsync(scope, cancellationToken);
        return result;
    }

    private async Task<ProductManufacturingTechnicalReadScope> RequireScopeAsync(CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId)
            || !companyContext.IsResolved || companyContext.CompanyId == Guid.Empty
            || !currentUser.CanAccessCompany(companyContext.CompanyId))
            throw new ProductOperationException(ProductSalesEligibilityService.AccessDeniedCode, 403);

        var companyId = companyContext.CompanyId;
        var permission = await authority.RevalidatePermissionScopeAsync(
            ProductPermissionManifestV1.OperationalSelectionRead, companyId, cancellationToken);
        if (permission is not { IsGranted: true })
            throw new ProductOperationException(ProductSalesEligibilityService.AccessDeniedCode, 403);
        return new(companyId, currentUser.UserId);
    }

    private async Task RequireSameScopeAsync(ProductManufacturingTechnicalReadScope scope,
        CancellationToken cancellationToken)
    {
        var current = await RequireScopeAsync(cancellationToken);
        if (current.CompanyId != scope.CompanyId
            || !string.Equals(current.ActorId, scope.ActorId, StringComparison.Ordinal))
            throw new ProductOperationException(ProductSalesEligibilityService.AccessDeniedCode, 403);
    }

    private static ProductManufacturingTechnicalSnapshotResultV1 Rejected(string field, string code) =>
        new(null, [new(field, code)]);
}
