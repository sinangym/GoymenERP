using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

/// <summary>
/// Product-owned runtime for <see cref="IProductStockEligibilityV1"/> (T07: an Inventory receipt).
///
/// Authority is the sales port's, unchanged (K-1(a)): an authenticated actor with access to the
/// server-resolved company AND the narrow <see cref="ProductPermissionManifestV1.OperationalSelectionRead"/>
/// permission for that company, measured afresh through the central port
/// <see cref="IErpAuthorityRevalidationService"/> (#276) before anything is read and again after
/// every awaited read, so a revocation in flight is seen at the next fence. It is an operational
/// selection read, not the Product master grant; the consuming module's own permission (Inventory's)
/// is still required on its side. The refusal is the one <c>product_access_denied</c> 403 and
/// happens before any record, policy or profile read.
///
/// Order: scope → stored Product → local preconditions (lifecycle, version, stock unit binding) →
/// family-required policy → the stock unit's Settings profile (must be kilogram) → snapshot. Every
/// awaited result, success or failure, is released only after the same company and actor are
/// re-verified and the permission re-measured. The fence code is the sales port's, repeated here
/// on purpose so that the reviewed sales runtime is not touched by this slice.
///
/// Fail-closed limits at this base: the first family's accepted rules are implemented but their
/// authorised data is not there yet, so the shipped definition source makes every otherwise-valid
/// selection 503, exactly as for sales. No snapshot is ever produced from completeness labels,
/// examples or defaults. The host does not register this port yet (a central step, with C3).
/// </summary>
public sealed class ProductStockEligibilityService(ICompanyContext companyContext, ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority, IProductSalesRecordReader records,
    IProductFamilyRequirementPolicyV1 familyPolicy, ProductUnitProfileAdapter unitProfiles)
    : IProductStockEligibilityV1
{
    public async Task<ProductStockEligibilityV1> EvaluateAsync(ProductStockSelectionV1 selection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // The gate first: a caller without the narrow permission must not be able to tell a bad
        // argument from a refusal, even on an in-process call.
        var scope = await RequireScopeAsync(cancellationToken);
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.ProductId == Guid.Empty) return Rejected("productId", "invalid_reference");

        // The stored-record read is the operational selection's own (not the master read).
        var record = await FencedAsync(scope,
            () => records.ReadForSalesSelectionAsync(scope, selection.ProductId, cancellationToken), cancellationToken);
        if (record is null) return Rejected("productId", "invalid_reference");

        var card = Restore(record);
        var local = ProductStockPreconditions.Check(card, selection);
        if (local.Count > 0) return new(null, local);

        // Preconditions guarantee the stock binding is present and non-empty.
        var stockUnitId = card.References.Units!.Stock!.Value;

        var family = await FencedAsync(scope, () => familyPolicy.ValidateAsync(card, cancellationToken), cancellationToken);
        if (family is null) throw new ProductOperationException(ProductSalesEligibilityService.FamilyPolicyInvalidCode, 503);
        if (family.Count > 0) return new(null, family);

        var stock = await FencedAsync(scope,
            () => unitProfiles.ReadAsync(stockUnitId, selection.EffectiveOn, requiresKilogram: true, cancellationToken),
            cancellationToken);

        // A well-formed profile the owner says cannot be used on this date (inactive, out of window,
        // not kilogram) rejects the reference; it is not an unavailable provider.
        if (!stock.IsUsable) return Rejected("references.units.stock", "invalid_reference");

        return new(new ProductStockSnapshotV1(card.Product.Id, card.Product.Version, card.Product.Code,
            card.Product.Name, Reference(stock.Profile), selection.EffectiveOn), []);
    }

    /// <summary>
    /// A stored record that cannot be restored is an integrity fault, reported on one channel, as for
    /// sales: the Domain's own argument and the JSON reader's failures become <see cref="InvalidOperationException"/>.
    /// </summary>
    private static ProductCardView Restore(ProductStoredRecord record)
    {
        try
        {
            return new ProductCardView(ProductDraftView.FromProduct(record.RestoreProduct()), record.RestoreReferences());
        }
        catch (Exception exception) when (exception is ArgumentException or System.Text.Json.JsonException)
        {
            throw new InvalidOperationException("The stored Product record could not be restored.", exception);
        }
    }

    private async Task<T> FencedAsync<T>(ProductSalesReadScope scope, Func<Task<T>> read,
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
            // A failure observed under a stale or no-longer-permitted caller can disclose as much as a success.
            await RequireSameScopeAsync(scope, cancellationToken);
            throw;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await RequireSameScopeAsync(scope, cancellationToken);
        return result;
    }

    private async Task<ProductSalesReadScope> RequireScopeAsync(CancellationToken cancellationToken)
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

    private async Task RequireSameScopeAsync(ProductSalesReadScope scope, CancellationToken cancellationToken)
    {
        var current = await RequireScopeAsync(cancellationToken);
        if (current.CompanyId != scope.CompanyId || !string.Equals(current.ActorId, scope.ActorId, StringComparison.Ordinal))
            throw new ProductOperationException(ProductSalesEligibilityService.AccessDeniedCode, 403);
    }

    private static ProductSalesUnitProfileV1 Reference(UnitProfileV1 profile) => new(profile.CompanyId, profile.UnitId,
        profile.Profile.ProfileId, profile.Profile.Version.ToString(CultureInfo.InvariantCulture));

    private static ProductStockEligibilityV1 Rejected(string field, string code) => new(null, [new(field, code)]);
}
