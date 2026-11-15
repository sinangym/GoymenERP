using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.PriceLists;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

/// <summary>
/// B-09: the V2 technical snapshot composed from two Product-owned reads — the unchanged V1 snapshot and the shared card's
/// "Ebat/kalınlık" row (<see cref="IProductDimensionReaderV1"/>). The scope (company, actor and the narrow
/// <see cref="ProductPermissionManifestV1.OperationalSelectionRead"/> permission) is measured before the first read and again
/// after each read, as V1 does after its own reads: a change in flight hides both the answer and the failure.
/// The dimension reader measures access only, so the permission of this port is this fence.
/// </summary>
public sealed class ProductManufacturingTechnicalSnapshotServiceV2(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority,
    IProductManufacturingTechnicalSnapshotV1 technical,
    IProductDimensionReaderV1 dimensions)
    : IProductManufacturingTechnicalSnapshotV2
{
    /// <summary>The dimension version of a product without a row (the "0" of the dimension contract).</summary>
    public const string NoDimensionVersion = "0";

    public async Task<ProductManufacturingTechnicalSnapshotResultV2> ReadAsync(
        ProductManufacturingTechnicalSnapshotSelectionV2 selection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireScopeAsync(cancellationToken);
        ArgumentNullException.ThrowIfNull(selection);

        long? expectedDimensionVersion = null;
        List<ProductFieldError> bindingErrors = [];
        if (selection.ExpectedDimensionVersion is not null)
        {
            if (TryParseVersion(selection.ExpectedDimensionVersion, out var parsed)) expectedDimensionVersion = parsed;
            else
                bindingErrors.Add(new(ProductManufacturingTechnicalSnapshotCodesV2.ExpectedDimensionVersionField,
                    ProductManufacturingTechnicalSnapshotCodesV2.InvalidVersion));
        }

        var v1 = await FencedAsync(scope,
            () => technical.ReadAsync(
                new ProductManufacturingTechnicalSnapshotSelectionV1(
                    selection.ProductId, selection.ExpectedProductVersion, selection.EffectiveOn),
                cancellationToken),
            cancellationToken);
        if (v1.Errors.Count > 0 || v1.Snapshot is null)
        {
            if (v1.Errors.Count == 0)
                throw new InvalidOperationException("The V1 technical snapshot answered neither a snapshot nor an error.");
            return new(null, [.. v1.Errors, .. bindingErrors]);
        }

        if (bindingErrors.Count > 0) return new(null, bindingErrors);

        var snapshot = v1.Snapshot;
        var row = await FencedAsync(scope, () => dimensions.GetAsync(snapshot.ProductId, cancellationToken),
            cancellationToken);
        ArgumentNullException.ThrowIfNull(row);
        var (dimension, unitWeight, error) = Interpret(row);
        if (error is not null) return new(null, [error]);

        if (expectedDimensionVersion is { } expected
            && !string.Equals(dimension!.Version, expected.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            return new(null, [new(ProductManufacturingTechnicalSnapshotCodesV2.ExpectedDimensionVersionField,
                ProductApiV1.VersionConflict)]);

        return new(new(
            snapshot,
            new(snapshot.ProductId, snapshot.ProductVersion, snapshot.Template.TemplateId,
                snapshot.Template.TemplateVersion, dimension!.Version),
            dimension,
            unitWeight!), []);
    }

    /// <summary>
    /// The dimension read as V2 publishes it. Denied is 403 like V1's fence, Unavailable is 503, a card the dimension store no
    /// longer sees is the V1 owner rejection, and a stored row that breaks the section-kind rule (the same
    /// <see cref="ProductDimensionShape"/> the save enforces) or an unknown outcome is an integrity fault — never a guess.
    /// </summary>
    internal static (ProductManufacturingDimensionV2? Dimension, ProductManufacturingUnitWeightV2? UnitWeight,
        ProductFieldError? Error) Interpret(ProductDimensionReadV1 row)
    {
        ArgumentNullException.ThrowIfNull(row);
        switch (row.Outcome)
        {
            case ProductDimensionOutcomeV1.Denied:
                throw new ProductOperationException(ProductSalesEligibilityService.AccessDeniedCode, 403);
            case ProductDimensionOutcomeV1.Unavailable:
                throw new ProductOperationException(ProductDimensionProblemCodesV1.ProviderUnavailable, 503);
            case ProductDimensionOutcomeV1.NotFound:
                return (null, null, new("productId", "invalid_reference"));
            case ProductDimensionOutcomeV1.NoSource:
                return (new(ProductManufacturingDimensionSourceV2.NoSource, NoDimensionVersion,
                        null, null, null, null, null, null, null),
                    new(ProductManufacturingUnitWeightSourceV2.None, null), null);
            case ProductDimensionOutcomeV1.Found:
                break;
            default:
                throw new InvalidOperationException("The Product dimension read answered an unknown outcome.");
        }

        if (row.Version is not { } version || version <= 0
            || ProductDimensionShape.Refusal(new SetProductDimensionRequestV1(null, row.SectionKind, row.ThicknessMm,
                row.WidthMm, row.LengthMm, row.HeightMm, row.OuterDiameterMm, row.WallThicknessMm, null), out _) is not null)
            throw new InvalidOperationException("The stored Product dimension row breaks its section-kind rule.");

        var dimension = new ProductManufacturingDimensionV2(ProductManufacturingDimensionSourceV2.Stored,
            version.ToString(CultureInfo.InvariantCulture), row.SectionKind, row.ThicknessMm, row.WidthMm,
            row.LengthMm, row.HeightMm, row.OuterDiameterMm, row.WallThicknessMm);
        var unitWeight = row.KgPerMetre is { } kgPerMetre
            ? new ProductManufacturingUnitWeightV2(ProductManufacturingUnitWeightSourceV2.ProductMaster, kgPerMetre)
            : new ProductManufacturingUnitWeightV2(ProductManufacturingUnitWeightSourceV2.None, null);
        return (dimension, unitWeight, null);
    }

    /// <summary>"0" or a positive decimal version without sign, white space or leading zero (the dimension contract's text).</summary>
    internal static bool TryParseVersion(string? text, out long version)
    {
        version = 0;
        return !string.IsNullOrEmpty(text) && text.All(char.IsAsciiDigit) && (text.Length == 1 || text[0] != '0')
            && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out version);
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
}
