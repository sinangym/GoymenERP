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
/// Authority (K-1(a), Sinan's decision relayed in Business #66 5719251344): an
/// authenticated actor with access to the server-resolved company AND the narrow
/// <see cref="ProductPermissionManifestV1.OperationalSelectionRead"/> permission for that
/// same server-resolved company. The global Product master grant is deliberately NOT
/// borrowed, and the narrow one is not a master, cost, audit or listing read. The
/// consuming module's own selector permission is still required on its side; that gate is
/// not this port's business.
///
/// The permission is re-asked on EVERY read and on every fenced revalidation, from
/// <see cref="ICompanyContext"/> — never from request or selection data, and never from a
/// scope the caller supplies. Losing it mid-flight turns the result in hand into 403,
/// exactly as a company or actor change does. The refusal is the same
/// <c>product_access_denied</c> 403 as a missing scope and happens before any record,
/// policy or profile read, so no id, code, version or field error can leak.
///
/// The check goes straight to <see cref="IErpAuthorizationService"/>, as Organization,
/// Counterparty and Audit do, rather than through a Product-owned authorizer port: a new
/// port would need a central DI registration this slice must not make, and the platform
/// service already fails closed for a name it does not know
/// (<c>GetPermissionScopeAsync</c> answers <c>PermissionScope.Denied</c> for any name
/// outside <c>ErpPermissions</c>). Until the central owner registers the name and a human
/// grants it, every caller is denied — which is the decision's default-closed rule.
///
/// Order: scope → stored Product → local preconditions (lifecycle, sellable role,
/// version, stock/sales unit bindings) → order unit → family-required policy → Settings
/// unit profiles (stock must be kilogram) → snapshot. Every awaited result, success or
/// failure, is released only after the same company/actor is re-verified.
///
/// Which outcome travels on which channel is part of the port contract; see
/// <see cref="IProductSalesEligibilityV1"/>.
///
/// Fail-closed limits at this base: only the Product's own sales unit may be ordered,
/// because no accepted offered-unit/conversion policy exists (U-08); and no family rule
/// set is accepted, so the shipped policy makes every otherwise-valid selection 503.
/// No snapshot is ever produced from completeness labels, examples or defaults.
/// </summary>
public sealed class ProductSalesEligibilityService(ICompanyContext companyContext, ICurrentUser currentUser,
    IErpAuthorizationService authorization, IProductSalesRecordReader records,
    IProductFamilyRequirementPolicyV1 familyPolicy, ProductUnitProfileAdapter unitProfiles)
    : IProductSalesEligibilityV1
{
    public const string AccessDeniedCode = "product_access_denied";
    public const string UnsupportedOrderUnitCode = "unsupported_order_unit";
    public const string FamilyPolicyInvalidCode = "product_family_policy_invalid";

    public async Task<ProductSalesEligibilityV1> EvaluateAsync(ProductSalesSelectionV1 selection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireScopeAsync(cancellationToken);
        if (selection.ProductId == Guid.Empty) return Rejected("productId", "invalid_reference");

        var record = await FencedAsync(scope,
            () => records.ReadForSalesSelectionAsync(scope, selection.ProductId, cancellationToken), cancellationToken);
        if (record is null) return Rejected("productId", "invalid_reference");

        var card = Restore(record);
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

        // A well-formed profile the owner says cannot be used on this date (inactive, out of
        // window, stock not kilogram) rejects the reference; it is not an unavailable provider.
        var unusable = new List<ProductFieldError>();
        if (!stock.IsUsable) unusable.Add(new("references.units.stock", "invalid_reference"));
        if (!sales.IsUsable) unusable.Add(new("references.units.sales", "invalid_reference"));
        if (unusable.Count > 0) return new(null, unusable);

        var salesReference = Reference(sales.Profile);
        return new(new ProductSalesSnapshotV1(card.Product.Id, card.Product.Version, card.Product.Code,
            card.Product.Name, Reference(stock.Profile), salesReference, salesReference, selection.EffectiveOn), []);
    }

    /// <summary>
    /// A stored record that cannot be restored is an integrity fault, reported on one
    /// channel: the Domain's own argument and the JSON reader's failures are folded into
    /// <see cref="InvalidOperationException"/> so no corrupt value reads as a rejection.
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
            // A failure observed under a stale or no-longer-permitted caller can disclose as
            // much as a success.
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
            throw new ProductOperationException(AccessDeniedCode, 403);

        // The company is the server's, read again here: a scope captured earlier could be
        // one the actor has since lost, and request data never decides authority.
        var companyId = companyContext.CompanyId;
        if (!await authorization.HasPermissionAsync(
                ProductPermissionManifestV1.OperationalSelectionRead, companyId, cancellationToken))
            throw new ProductOperationException(AccessDeniedCode, 403);

        return new(companyId, currentUser.UserId);
    }

    private async Task RequireSameScopeAsync(ProductSalesReadScope scope, CancellationToken cancellationToken)
    {
        var current = await RequireScopeAsync(cancellationToken);
        if (current.CompanyId != scope.CompanyId || !string.Equals(current.ActorId, scope.ActorId, StringComparison.Ordinal))
            throw new ProductOperationException(AccessDeniedCode, 403);
    }

    private static ProductSalesUnitProfileV1 Reference(UnitProfileV1 profile) => new(profile.CompanyId, profile.UnitId,
        profile.Profile.ProfileId, profile.Profile.Version.ToString(CultureInfo.InvariantCulture));

    private static ProductSalesEligibilityV1 Rejected(string field, string code) => new(null, [new(field, code)]);
}
