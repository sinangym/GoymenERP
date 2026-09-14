using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

/// <summary>Server-derived caller scope for one operational selection read; never request data.</summary>
public sealed record ProductSalesReadScope(Guid CompanyId, string ActorId);

/// <summary>
/// Reads the stored Product for an operational selection. It is not the global master
/// read: it releases nothing by itself and the eligibility service fences every result.
/// </summary>
public interface IProductSalesRecordReader
{
    Task<ProductStoredRecord?> ReadForSalesSelectionAsync(ProductSalesReadScope scope, Guid productId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Product-owned runtime for <see cref="IProductSalesEligibilityV1"/>.
///
/// Authority: an authenticated actor with access to the server-resolved company. The
/// global Product master grant is deliberately NOT borrowed; the consuming module's own
/// selector permission (for Sales, its default-deny binding) gates the use case.
///
/// Order: scope → stored Product → local preconditions (lifecycle, sellable role,
/// version, stock/sales unit bindings) → order unit → family-required policy → Settings
/// unit profiles (stock must be kilogram) → snapshot. Every awaited result, success or
/// failure, is released only after the same company/actor is re-verified.
///
/// Fail-closed limits at this base: only the Product's own sales unit may be ordered,
/// because no accepted offered-unit/conversion policy exists (U-08); and no family rule
/// set is accepted, so the shipped policy makes every otherwise-valid selection 503.
/// No snapshot is ever produced from completeness labels, examples or defaults.
/// </summary>
public sealed class ProductSalesEligibilityService(ICompanyContext companyContext, ICurrentUser currentUser,
    IProductSalesRecordReader records, IProductFamilyRequirementPolicyV1 familyPolicy,
    ProductUnitProfileAdapter unitProfiles) : IProductSalesEligibilityV1
{
    public const string AccessDeniedCode = "product_access_denied";
    public const string UnsupportedOrderUnitCode = "unsupported_order_unit";
    public const string FamilyPolicyInvalidCode = "product_family_policy_invalid";

    public async Task<ProductSalesEligibilityV1> EvaluateAsync(ProductSalesSelectionV1 selection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        if (selection.ProductId == Guid.Empty) return Rejected("productId", "invalid_reference");

        var record = await FencedAsync(scope,
            () => records.ReadForSalesSelectionAsync(scope, selection.ProductId, cancellationToken), cancellationToken);
        if (record is null) return Rejected("productId", "invalid_reference");

        var card = new ProductCardView(ProductDraftView.FromProduct(record.RestoreProduct()), record.RestoreReferences());
        var local = ProductSalesPreconditions.Check(card, selection);
        if (local.Count > 0) return new(null, local);

        // Preconditions guarantee both bindings are present and non-empty.
        var stockUnitId = card.References.Units!.Stock!.Value;
        var salesUnitId = card.References.Units.Sales!.Value;
        if (selection.OrderUnitId != salesUnitId) return Rejected("orderUnitId", UnsupportedOrderUnitCode);

        var family = await FencedAsync(scope, () => familyPolicy.ValidateAsync(card, cancellationToken), cancellationToken);
        if (family is null) throw new ProductOperationException(FamilyPolicyInvalidCode, 503);
        if (family.Count > 0) return new(null, family);

        var stock = await FencedAsync(scope,
            () => unitProfiles.ReadAsync(stockUnitId, selection.EffectiveOn, requiresKilogram: true, cancellationToken),
            cancellationToken);
        var sales = await FencedAsync(scope,
            () => unitProfiles.ReadAsync(salesUnitId, selection.EffectiveOn, requiresKilogram: false, cancellationToken),
            cancellationToken);

        var salesReference = Reference(sales);
        return new(new ProductSalesSnapshotV1(card.Product.Id, card.Product.Version, card.Product.Code,
            card.Product.Name, Reference(stock), salesReference, salesReference, selection.EffectiveOn), []);
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
            // A failure observed under a stale caller can disclose as much as a success.
            RequireSameScope(scope);
            throw;
        }

        cancellationToken.ThrowIfCancellationRequested();
        RequireSameScope(scope);
        return result;
    }

    private ProductSalesReadScope RequireScope()
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId)
            || !companyContext.IsResolved || companyContext.CompanyId == Guid.Empty
            || !currentUser.CanAccessCompany(companyContext.CompanyId))
            throw new ProductOperationException(AccessDeniedCode, 403);
        return new(companyContext.CompanyId, currentUser.UserId);
    }

    private void RequireSameScope(ProductSalesReadScope scope)
    {
        var current = RequireScope();
        if (current.CompanyId != scope.CompanyId || !string.Equals(current.ActorId, scope.ActorId, StringComparison.Ordinal))
            throw new ProductOperationException(AccessDeniedCode, 403);
    }

    private static ProductSalesUnitProfileV1 Reference(UnitProfileV1 profile) => new(profile.CompanyId, profile.UnitId,
        profile.Profile.ProfileId, profile.Profile.Version.ToString(CultureInfo.InvariantCulture));

    private static ProductSalesEligibilityV1 Rejected(string field, string code) => new(null, [new(field, code)]);
}
