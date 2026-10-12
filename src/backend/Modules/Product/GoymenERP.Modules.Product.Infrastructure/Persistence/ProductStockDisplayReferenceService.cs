using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.Products;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

public sealed record ProductStockDisplayReadScope(Guid CompanyId, string ActorId);

public interface IProductStockDisplayRecordReader
{
    Task<IReadOnlyList<ProductStoredRecord>> ReadForStockDisplayAsync(
        ProductStockDisplayReadScope scope,
        IReadOnlySet<Guid> productIds,
        CancellationToken cancellationToken);
}

public sealed class ProductStockDisplayReferenceService(
    ICompanyContext company,
    ICurrentUser user,
    IErpAuthorityRevalidationService authority,
    IProductStockDisplayRecordReader records,
    IProductFamilyDefinitionSourceV2 definitions)
    : IProductStockDisplayReferencesV1, IProductStockTechnicalMatcherV1
{
    public async Task<ProductStockDisplayBatchV1> ReadAsync(
        ProductStockDisplayBatchQueryV1 query,
        CancellationToken cancellationToken = default)
    {
        var scope = await RequireScopeAsync(null, cancellationToken);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(query.Selections);
        if (query.Selections.Count is < 1 or > ProductStockDisplayCodesV1.MaximumBatchSize
            || query.Selections.Any(selection => selection is null || selection.ProductId == Guid.Empty)
            || query.Selections.Select(selection => selection.ProductId).Distinct().Count() != query.Selections.Count)
        {
            throw new ProductOperationException(ProductStockDisplayCodesV1.InvalidRequest, 400);
        }

        var ids = query.Selections.Select(selection => selection.ProductId).ToHashSet();
        var stored = await FencedAsync(scope,
            () => records.ReadForStockDisplayAsync(scope, ids, cancellationToken), cancellationToken);
        if (stored.Count > ids.Count || stored.Select(record => record.Id).Distinct().Count() != stored.Count
            || stored.Any(record => !ids.Contains(record.Id)))
        {
            throw new InvalidOperationException("Product stock-display reader returned an inconsistent batch.");
        }

        var byId = stored.ToDictionary(record => record.Id);
        var exact = new Dictionary<Guid, (ProductCardView Card, ProductStockDisplayIdentityV1 Identity)>();
        foreach (var selection in query.Selections)
        {
            if (!byId.TryGetValue(selection.ProductId, out var record)) continue;
            try
            {
                var expected = ProductVersion.Parse(selection.ExpectedProductVersion);
                if (expected != record.Version) continue;
            }
            catch (ArgumentException)
            {
                continue;
            }

            var card = ProductManufacturingTechnicalSnapshotService.Restore(record);
            exact.Add(selection.ProductId, (card, new(
                card.Product.Id,
                card.Product.Version,
                card.Product.Code,
                card.Product.Name)));
        }

        ProductFamilyDefinitionV2? definition = null;
        if (exact.Count > 0)
        {
            definition = await FencedAsync(scope, () => definitions.GetAsync(cancellationToken), cancellationToken);
            ProductFamilyDefinitionRulesV2.RequireWellFormed(definition);
        }

        var items = new List<ProductStockDisplayResultV1>(query.Selections.Count);
        foreach (var selection in query.Selections)
        {
            if (!byId.TryGetValue(selection.ProductId, out var record))
            {
                items.Add(Rejected(selection, "productId", "invalid_reference"));
                continue;
            }

            long expected;
            try
            {
                expected = ProductVersion.Parse(selection.ExpectedProductVersion);
            }
            catch (ArgumentException)
            {
                items.Add(Rejected(selection, "expectedProductVersion", "invalid_version"));
                continue;
            }
            if (expected != record.Version)
            {
                items.Add(Rejected(selection, "expectedProductVersion", ProductApiV1.VersionConflict));
                continue;
            }

            var (card, identity) = exact[selection.ProductId];
            var family = card.Product.FamilyId is Guid familyId
                ? definition!.Families.SingleOrDefault(candidate => candidate.Id == familyId)
                : null;
            if (family is null)
            {
                throw new ProductOperationException(UnacceptedProductFamilyRequirementPolicyV1.UnavailableCode, 503);
            }
            var template = ProductFamilyDefinitionRulesV2.TemplateOf(definition!, family.Id)
                ?? throw new ProductOperationException(ProductFamilyDefinitionRulesV2.InvalidDefinitionCode, 503);
            if (ProductManufacturingTechnicalSnapshotService.IsSynthetic(family.SourceRef)
                || ProductManufacturingTechnicalSnapshotService.IsSynthetic(template.SourceRef))
            {
                throw new ProductOperationException(ProductManufacturingTechnicalSnapshotService.UnavailableCode, 503);
            }

            var errors = ProductManufacturingTechnicalSnapshotService.ValidateTechnicalReferences(
                card.References, definition!, template);
            if (errors.Count > 0)
            {
                items.Add(new(selection.ProductId, selection.ExpectedProductVersion, identity, null, errors));
                continue;
            }
            if (ProductManufacturingTechnicalSnapshotService.UsesSyntheticDictionaryValue(
                    card.References, definition!, template))
            {
                throw new ProductOperationException(ProductManufacturingTechnicalSnapshotService.UnavailableCode, 503);
            }
            var thickness = ProductManufacturingTechnicalSnapshotService.ReadMeasure(
                card.References, template, ProductTemplateFieldRolesV2.Thickness);
            var width = ProductManufacturingTechnicalSnapshotService.ReadMeasure(
                card.References, template, ProductTemplateFieldRolesV2.Width);
            errors.AddRange(new[] { thickness.Error, width.Error }.OfType<ProductFieldError>());
            var technical = errors.Count == 0
                ? new ProductStockNominalTechnicalV1(
                    new(family.Id, family.Code, family.SourceRef),
                    new(template.Id, template.Version, template.Code, template.SourceRef),
                    thickness.Millimetres!.Value,
                    width.Millimetres!.Value)
                : null;
            items.Add(new(selection.ProductId, selection.ExpectedProductVersion, identity, technical, errors));
        }

        await RequireScopeAsync(scope, cancellationToken);
        return new(items);
    }

    public async Task<ProductStockTechnicalMatchBatchV1> MatchAsync(
        ProductStockTechnicalMatchQueryV1 query,
        CancellationToken cancellationToken = default)
    {
        var scope = await RequireScopeAsync(null, cancellationToken);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(query.Selections);
        if (query.Selections.Count is < 1 or > ProductStockDisplayCodesV1.MaximumBatchSize
            || query.Selections.Any(selection => selection is null || selection.ProductId == Guid.Empty
                || string.IsNullOrWhiteSpace(selection.ExpectedProductVersion))
            || query.Selections.DistinctBy(selection =>
                    (selection.ProductId, selection.ExpectedProductVersion), ProductVersionKeyComparer.Instance)
                .Count() != query.Selections.Count)
        {
            throw new ProductOperationException(ProductStockDisplayCodesV1.InvalidRequest, 400);
        }

        var ids = query.Selections.Select(selection => selection.ProductId).ToHashSet();
        var stored = await FencedAsync(scope,
            () => records.ReadForStockDisplayAsync(scope, ids, cancellationToken), cancellationToken);
        if (stored.Count > ids.Count || stored.Select(record => record.Id).Distinct().Count() != stored.Count
            || stored.Any(record => !ids.Contains(record.Id)))
        {
            throw new InvalidOperationException("Product stock-display reader returned an inconsistent batch.");
        }

        var byId = stored.ToDictionary(record => record.Id);
        var exact = new List<(ProductStockTechnicalMatchSelectionV1 Selection, ProductCardView Card)>();
        foreach (var selection in query.Selections)
        {
            if (!byId.TryGetValue(selection.ProductId, out var record)) continue;
            try
            {
                if (ProductVersion.Parse(selection.ExpectedProductVersion) != record.Version) continue;
            }
            catch (ArgumentException)
            {
                continue;
            }

            exact.Add((selection, ProductManufacturingTechnicalSnapshotService.Restore(record)));
        }

        if (exact.Count == 0)
        {
            await RequireScopeAsync(scope, cancellationToken);
            return new([]);
        }

        var definition = await FencedAsync(scope, () => definitions.GetAsync(cancellationToken), cancellationToken);
        ProductFamilyDefinitionRulesV2.RequireWellFormed(definition);
        var matches = new List<ProductStockTechnicalMatchSelectionV1>();
        foreach (var (selection, card) in exact)
        {
            var family = card.Product.FamilyId is Guid familyId
                ? definition.Families.SingleOrDefault(candidate => candidate.Id == familyId)
                : null;
            if (family is null)
            {
                throw new ProductOperationException(UnacceptedProductFamilyRequirementPolicyV1.UnavailableCode, 503);
            }
            var template = ProductFamilyDefinitionRulesV2.TemplateOf(definition, family.Id)
                ?? throw new ProductOperationException(ProductFamilyDefinitionRulesV2.InvalidDefinitionCode, 503);
            if (ProductManufacturingTechnicalSnapshotService.IsSynthetic(family.SourceRef)
                || ProductManufacturingTechnicalSnapshotService.IsSynthetic(template.SourceRef))
            {
                throw new ProductOperationException(ProductManufacturingTechnicalSnapshotService.UnavailableCode, 503);
            }

            var errors = ProductManufacturingTechnicalSnapshotService.ValidateTechnicalReferences(
                card.References, definition, template);
            if (errors.Count > 0) continue;
            if (ProductManufacturingTechnicalSnapshotService.UsesSyntheticDictionaryValue(
                    card.References, definition, template))
            {
                throw new ProductOperationException(ProductManufacturingTechnicalSnapshotService.UnavailableCode, 503);
            }
            var thickness = ProductManufacturingTechnicalSnapshotService.ReadMeasure(
                card.References, template, ProductTemplateFieldRolesV2.Thickness);
            var width = ProductManufacturingTechnicalSnapshotService.ReadMeasure(
                card.References, template, ProductTemplateFieldRolesV2.Width);
            if (thickness.Error is not null || width.Error is not null) continue;
            if (Matches(new(family.Id, family.Code, family.SourceRef),
                    thickness.Millimetres!.Value, width.Millimetres!.Value, query))
            {
                matches.Add(selection);
            }
        }

        await RequireScopeAsync(scope, cancellationToken);
        return new(matches);
    }

    private static bool Matches(
        ProductManufacturingFamilyRevisionV1 family,
        decimal thickness,
        decimal width,
        ProductStockTechnicalMatchQueryV1 query) =>
        (!query.ProductFamilyId.HasValue || family.FamilyId == query.ProductFamilyId)
        && (!query.ThicknessMm.HasValue || thickness == query.ThicknessMm)
        && (!query.ThicknessMinMm.HasValue || thickness >= query.ThicknessMinMm)
        && (!query.ThicknessMaxMm.HasValue || thickness <= query.ThicknessMaxMm)
        && (!query.WidthMm.HasValue || width == query.WidthMm)
        && (!query.WidthMinMm.HasValue || width >= query.WidthMinMm)
        && (!query.WidthMaxMm.HasValue || width <= query.WidthMaxMm);

    private async Task<T> FencedAsync<T>(ProductStockDisplayReadScope scope, Func<Task<T>> read,
        CancellationToken cancellationToken)
    {
        T value;
        try
        {
            value = await read();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            await RequireScopeAsync(scope, cancellationToken);
            throw;
        }

        await RequireScopeAsync(scope, cancellationToken);
        return value;
    }

    private async Task<ProductStockDisplayReadScope> RequireScopeAsync(
        ProductStockDisplayReadScope? expected,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId)
            || !company.IsResolved || company.CompanyId == Guid.Empty
            || !user.CanAccessCompany(company.CompanyId))
        {
            throw Denied();
        }

        var scope = new ProductStockDisplayReadScope(company.CompanyId, user.UserId);
        if (expected is not null && scope != expected) throw Denied();
        var grant = await authority.RevalidatePermissionScopeAsync(
            ProductPermissionManifestV1.OperationalSelectionRead, scope.CompanyId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!grant.IsGranted || !user.IsAuthenticated || !company.IsResolved
            || company.CompanyId != scope.CompanyId || user.UserId != scope.ActorId
            || !user.CanAccessCompany(scope.CompanyId))
        {
            throw Denied();
        }
        return scope;
    }

    private static ProductStockDisplayResultV1 Rejected(
        ProductStockDisplaySelectionV1 selection,
        string field,
        string code) => new(selection.ProductId, selection.ExpectedProductVersion, null, null,
        [new ProductFieldError(field, code)]);

    private static ProductOperationException Denied() =>
        new(ProductSalesEligibilityService.AccessDeniedCode, 403);

    private sealed class ProductVersionKeyComparer : IEqualityComparer<(Guid ProductId, string ExpectedVersion)>
    {
        public static ProductVersionKeyComparer Instance { get; } = new();

        public bool Equals((Guid ProductId, string ExpectedVersion) x,
            (Guid ProductId, string ExpectedVersion) y) =>
            x.ProductId == y.ProductId
            && string.Equals(x.ExpectedVersion, y.ExpectedVersion, StringComparison.Ordinal);

        public int GetHashCode((Guid ProductId, string ExpectedVersion) value) =>
            HashCode.Combine(value.ProductId, StringComparer.Ordinal.GetHashCode(value.ExpectedVersion));
    }
}
