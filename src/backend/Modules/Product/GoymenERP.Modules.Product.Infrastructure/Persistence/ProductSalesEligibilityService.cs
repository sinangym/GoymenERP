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
/// The company, the actor AND the permission are measured afresh at every fenced point. Company
/// and actor are re-read from the live request context; the permission is asked through the
/// central fresh-authority port <see cref="IErpAuthorityRevalidationService"/> (#276), which
/// re-reads the stored authority and moves the request's authorization view forward with it.
/// So a grant revoked while this request is in flight is seen at the NEXT fence and turns the
/// result in hand into 403 — the per-request limit measured in #265 5719822744 is closed here,
/// at the price of one authority read per fence (five for a full evaluation). The answer is
/// used exactly as the cached path uses it (<see cref="PermissionScope.IsGranted"/>); this
/// service decides no scope semantics of its own. A grant added mid-request is seen the same
/// way, which opens nothing the stored authority does not already say.
///
/// The refusal is the same <c>product_access_denied</c> 403 as a missing scope and happens
/// before any record, policy or profile read, so no id, code, version or field error can
/// leak — including for a null selection, which is rejected only after the gate.
///
/// The check goes straight to the central port, as Organization, Counterparty and Audit use
/// the cached one, rather than through a Product-owned authorizer: the central service fails
/// closed for a name it does not know and for an empty company or unidentified actor. The
/// narrow name is registered centrally (#277) but granted to nobody; until a human grants it,
/// every caller is denied — which is the decision's default-closed rule.
///
/// Order: scope → stored Product → local preconditions (lifecycle, sellable role,
/// version, stock/sales unit bindings) → order unit → family-required policy → Settings
/// unit profiles (stock must be kilogram) → snapshot. Every AWAITED result, success or
/// failure, is released only after the same company and actor are re-verified and the
/// permission is re-measured at that point.
/// What is then derived locally from an already re-verified read — the preconditions, the
/// order-unit comparison, <see cref="Restore"/>'s integrity fault and the final snapshot —
/// is returned without a further check: the guarantee is per read, not per returned value,
/// and the window after the last fence cannot be closed from inside this service.
///
/// Cancellation: the caller's token reaches the authority read too. Because the
/// revalidation in the catch path is awaited, a token cancelled while a read is failing can
/// surface <see cref="OperationCanceledException"/> in place of that read's own failure.
/// Cancellation discloses nothing, and the success path still checks the token first.
///
/// Which outcome travels on which channel is part of the port contract; see
/// <see cref="IProductSalesEligibilityV1"/>.
///
/// Fail-closed limits at this base: only the Product's own sales unit may be ordered,
/// because no accepted offered-unit/conversion policy exists (U-08); and the first family's
/// accepted rules (GDC band/coil, Business 5722701085) are not implemented yet — that is the
/// next slice — so the shipped policy still makes every otherwise-valid selection 503.
/// No snapshot is ever produced from completeness labels, examples or defaults.
/// </summary>
public sealed class ProductSalesEligibilityService(ICompanyContext companyContext, ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority, IProductSalesRecordReader records,
    IProductFamilyRequirementPolicyV1 familyPolicy, ProductUnitProfileAdapter unitProfiles)
    : IProductSalesEligibilityV1
{
    public const string AccessDeniedCode = "product_access_denied";
    public const string UnsupportedOrderUnitCode = "unsupported_order_unit";
    public const string FamilyPolicyInvalidCode = "product_family_policy_invalid";

    public async Task<ProductSalesEligibilityV1> EvaluateAsync(ProductSalesSelectionV1 selection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // The gate first: a caller without the narrow permission must not be able to tell a
        // bad argument from a refusal, even on an in-process call.
        var scope = await RequireScopeAsync(cancellationToken);
        ArgumentNullException.ThrowIfNull(selection);
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
        // Measured now, not taken from the request's cached view (#276): a revocation made by
        // another request since the last fence is seen here.
        var permission = await authority.RevalidatePermissionScopeAsync(
            ProductPermissionManifestV1.OperationalSelectionRead, companyId, cancellationToken);
        if (permission is not { IsGranted: true })
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
