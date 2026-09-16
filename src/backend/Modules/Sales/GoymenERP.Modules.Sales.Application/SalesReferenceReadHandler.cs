using System.Globalization;
using System.Text;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Sales.Application;

// Read-only orchestration over actual owner ports. No table access or writes.
public sealed class SalesReferenceReadHandler(
    ICompanyContext companyContext, ICurrentUser currentUser,
    IErpAuthorizationService authorization, ISalesAccountSelectionSourceV1 counterparties,
    ISettingsPrerequisiteProvider settings, IProductSalesEligibilityV1? products,
    SalesReferenceReadBindings bindings) : ISalesReferenceReadService
{
    private const int SearchRuneLimit = 200;

    public async Task<SalesReferenceReadResult<SalesAccountOptionsV1>> GetAccountsAsync(
        SalesAccountQueryV1 query, string sessionBinding, CancellationToken cancellationToken = default)
    {
        var scope = await GetScopeAsync(bindings.AccountsPermission, cancellationToken);
        if (scope.Status != 200) return Failure<SalesAccountOptionsV1>(scope.Status);
        if (query.PageSize is < 1 or > 100 || !WithinSearchBound(query.Q)
            || query.Cursor?.Length > 8192 || string.IsNullOrWhiteSpace(sessionBinding))
            return Failure<SalesAccountOptionsV1>(400, "validation_failed");
        var page = await counterparties.GetPageAsync(new SalesAccountSelectionRequestV1(
            scope.Company, scope.Actor!, sessionBinding, query.Q, query.PageSize, query.Cursor), cancellationToken);
        if (!await StillAuthorizedAsync(scope, bindings.AccountsPermission, cancellationToken))
            return Failure<SalesAccountOptionsV1>(403);
        if (page.StatusCode != 200 || page.Value is null)
            return page.StatusCode switch
            {
                401 => Failure<SalesAccountOptionsV1>(401),
                403 => Failure<SalesAccountOptionsV1>(403),
                400 => Failure<SalesAccountOptionsV1>(400, "invalid_cursor"),
                _ => Failure<SalesAccountOptionsV1>(503, "counterparty_provider_unavailable")
            };
        var options = new List<SalesAccountOptionV1>();
        foreach (var party in page.Value.Items.Where(p => p.IsActive))
        foreach (var account in party.CompanyAccounts.Where(a => a.IsActive && a.CompanyId == scope.Company))
        {
            if (party.Id == Guid.Empty || account.Id == Guid.Empty || !ValidVersion(party.Version)
                || !ValidVersion(account.Version) || string.IsNullOrWhiteSpace(party.Code)
                || string.IsNullOrWhiteSpace(party.LegalName))
                return Failure<SalesAccountOptionsV1>(503, "reference_provider_invalid");
            options.Add(new(party.Id, account.Id, $"{party.Code} - {party.LegalName}",
                party.Version, account.Version));
        }
        return Success(new SalesAccountOptionsV1(scope.Company, options, page.Value.NextCursor));
    }

    public async Task<SalesReferenceReadResult<SalesCurrencyReferenceV1>> GetCurrencyAsync(
        SalesCurrencyQueryV1 query, CancellationToken cancellationToken = default)
    {
        var scope = await GetScopeAsync(bindings.CurrencyPermission, cancellationToken);
        if (scope.Status != 200) return Failure<SalesCurrencyReferenceV1>(scope.Status);
        if (query.CurrencyId == Guid.Empty) return Failure<SalesCurrencyReferenceV1>(400, "validation_failed");
        if (string.IsNullOrWhiteSpace(bindings.MoneyOperation))
            return Failure<SalesCurrencyReferenceV1>(503, "settings_operation_unavailable");
        try
        {
            var profile = await settings.GetMoneyProfileAsync(query.CurrencyId,
                bindings.MoneyOperation, query.EffectiveOn, cancellationToken);
            if (!await StillAuthorizedAsync(scope, bindings.CurrencyPermission, cancellationToken))
                return Failure<SalesCurrencyReferenceV1>(403);
            if (profile.CompanyId != scope.Company) return Failure<SalesCurrencyReferenceV1>(403);
            if (profile.CurrencyId != query.CurrencyId || profile.Operation != bindings.MoneyOperation
                || !ValidProfile(profile.Profile) || profile.Scale < 0 || string.IsNullOrWhiteSpace(profile.CurrencyCode))
                return Failure<SalesCurrencyReferenceV1>(503, "reference_provider_invalid");
            if (!profile.IsActive || !Effective(query.EffectiveOn, profile.EffectiveFrom, profile.EffectiveThrough))
                return Failure<SalesCurrencyReferenceV1>(422, "unsupported_currency");
            return Success(new SalesCurrencyReferenceV1(scope.Company, query.EffectiveOn, profile.CurrencyId,
                profile.CurrencyCode, profile.CurrencyCode, profile.Profile.ProfileId,
                profile.Profile.Version.ToString(CultureInfo.InvariantCulture)));
        }
        catch (SettingsPrerequisiteException exception)
        {
            return await ReleaseFailureAsync(scope, bindings.CurrencyPermission,
                SettingsFailure<SalesCurrencyReferenceV1>(exception), cancellationToken);
        }
    }

    public async Task<SalesReferenceReadResult<SalesProductReferenceV1>> GetProductAsync(
        SalesProductQueryV1 query, CancellationToken cancellationToken = default)
    {
        var scope = await GetScopeAsync(bindings.ProductPermission, cancellationToken);
        if (scope.Status != 200) return Failure<SalesProductReferenceV1>(scope.Status);
        if (query.ProductId == Guid.Empty || query.OrderUnitId == Guid.Empty
            || !IsOwnerProductVersion(query.ExpectedProductVersion))
            return Failure<SalesProductReferenceV1>(400, "validation_failed");
        if (products is null) return Failure<SalesProductReferenceV1>(503, "product_provider_unavailable");
        try
        {
            var result = await products.EvaluateAsync(new ProductSalesSelectionV1(query.ProductId,
                query.ExpectedProductVersion, query.OrderUnitId, query.EffectiveOn), cancellationToken);
            if (!await StillAuthorizedAsync(scope, bindings.ProductPermission, cancellationToken))
                return Failure<SalesProductReferenceV1>(403);
            if (result.Errors.Any(IsStaleSelection))
                return Failure<SalesProductReferenceV1>(409, "product_selection_changed");
            if (result.Snapshot is null)
                return Failure<SalesProductReferenceV1>(result.Errors.Count == 0 ? 503 : 422,
                    result.Errors.Count == 0 ? "product_provider_unavailable" : "unsupported_product");
            if (result.Errors.Count != 0) return Failure<SalesProductReferenceV1>(422, "unsupported_product");
            var snapshot = result.Snapshot;
            if (snapshot.ProductId != query.ProductId || snapshot.ProductVersion != query.ExpectedProductVersion
                || snapshot.OrderUnit.UnitId != query.OrderUnitId || snapshot.EffectiveOn != query.EffectiveOn)
                return Failure<SalesProductReferenceV1>(409, "product_selection_changed");
            if (string.IsNullOrWhiteSpace(snapshot.Code) || string.IsNullOrWhiteSpace(snapshot.Name))
                return Failure<SalesProductReferenceV1>(503, "reference_provider_invalid");
            UnitProfileV1? orderProfile = null;
            foreach (var unit in new[] { snapshot.StockUnit, snapshot.SalesUnit, snapshot.OrderUnit })
            {
                if (unit.CompanyId != scope.Company) return Failure<SalesProductReferenceV1>(403);
                var profile = await settings.GetUnitProfileAsync(unit.UnitId, query.EffectiveOn, cancellationToken);
                if (!await StillAuthorizedAsync(scope, bindings.ProductPermission, cancellationToken))
                    return Failure<SalesProductReferenceV1>(403);
                if (profile.CompanyId != scope.Company) return Failure<SalesProductReferenceV1>(403);
                if (unit.UnitId == Guid.Empty || profile.UnitId != unit.UnitId || !ValidProfile(profile.Profile)
                    || profile.Profile.ProfileId != unit.ProfileId
                    || profile.Profile.Version.ToString(CultureInfo.InvariantCulture) != unit.Version
                    || profile.Scale < 0 || string.IsNullOrWhiteSpace(profile.UnitCode))
                    return Failure<SalesProductReferenceV1>(503, "reference_provider_invalid");
                if (!profile.IsActive || !Effective(query.EffectiveOn, profile.EffectiveFrom, profile.EffectiveThrough))
                    return Failure<SalesProductReferenceV1>(422, "unsupported_unit");
                if (ReferenceEquals(unit, snapshot.OrderUnit)) orderProfile = profile;
            }
            if (orderProfile is null) return Failure<SalesProductReferenceV1>(503, "reference_provider_invalid");
            return Success(new SalesProductReferenceV1(scope.Company, query.EffectiveOn,
                snapshot.ProductId, snapshot.ProductVersion, $"{snapshot.Code} - {snapshot.Name}",
                [new(orderProfile.UnitId, orderProfile.UnitCode, orderProfile.Profile.ProfileId,
                    orderProfile.Profile.Version.ToString(CultureInfo.InvariantCulture))]));
        }
        catch (SettingsPrerequisiteException exception)
        {
            return await ReleaseFailureAsync(scope, bindings.ProductPermission,
                SettingsFailure<SalesProductReferenceV1>(exception), cancellationToken);
        }
        catch (ProductValidationException)
        {
            return await ReleaseFailureAsync(scope, bindings.ProductPermission,
                Failure<SalesProductReferenceV1>(422, "unsupported_product"), cancellationToken);
        }
        catch (ProductOperationException exception)
        {
            return await ReleaseFailureAsync(scope, bindings.ProductPermission, exception.StatusCode switch
            {
                401 => Failure<SalesProductReferenceV1>(401),
                403 => Failure<SalesProductReferenceV1>(403),
                404 => Failure<SalesProductReferenceV1>(404),
                409 => Failure<SalesProductReferenceV1>(409, "product_selection_changed"),
                400 or 422 => Failure<SalesProductReferenceV1>(422, "unsupported_product"),
                _ => Failure<SalesProductReferenceV1>(503, "product_provider_unavailable")
            }, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return Failure<SalesProductReferenceV1>(403);
        }
    }

    // Only the owner's version conflict means the known selection moved on. Its
    // lifecycle_conflict covers every non-Active product, including a Draft that was
    // never selectable, so that is an unsupported product, not a changed one; an
    // activation or deactivation bumps the version, so a real change still arrives
    // here as a version conflict.
    private static bool IsStaleSelection(ProductFieldError error) => error.Code == ProductApiV1.VersionConflict;

    private sealed record Scope(int Status, Guid Company, string? Actor);
    private async Task<Scope> GetScopeAsync(string? permission, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
            return new(401, Guid.Empty, null);
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty
            || !currentUser.CanAccessCompany(companyContext.CompanyId) || string.IsNullOrWhiteSpace(permission))
            return new(403, Guid.Empty, null);
        var scope = new Scope(200, companyContext.CompanyId, currentUser.UserId);
        return await StillAuthorizedAsync(scope, permission, cancellationToken)
            ? scope : new(403, Guid.Empty, null);
    }

    private async Task<bool> StillAuthorizedAsync(Scope scope, string? permission, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(permission) || !Matches(scope)) return false;
        var allowed = await authorization.HasPermissionAsync(permission, scope.Company, cancellationToken);
        return allowed && Matches(scope);
    }
    private bool Matches(Scope scope) => currentUser.IsAuthenticated && currentUser.UserId == scope.Actor
        && companyContext.IsResolved && companyContext.CompanyId == scope.Company
        && currentUser.CanAccessCompany(scope.Company);
    /// <summary>
    /// An owner failure is released only under the authority a success would need: a
    /// 409 or 422 code still tells the caller something about a global Product.
    /// </summary>
    private async Task<SalesReferenceReadResult<T>> ReleaseFailureAsync<T>(Scope scope, string? permission,
        SalesReferenceReadResult<T> failure, CancellationToken cancellationToken) where T : class =>
        await StillAuthorizedAsync(scope, permission, cancellationToken) ? failure : Failure<T>(403);

    // The route's own bound counts runes, so counting UTF-16 units here would refuse a
    // search the route accepted.
    private static bool WithinSearchBound(string? query) =>
        query is null || query.EnumerateRunes().Count() <= SearchRuneLimit;

    // The owner's rule (ProductVersion.Parse): positive, no leading zero, inside Int64.
    // A malformed token is a client error, decided before any owner read.
    private static bool IsOwnerProductVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version) || version.Length > 128) return false;
        try
        {
            ProductVersion.Parse(version);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool Effective(DateOnly on, DateOnly from, DateOnly? through) => on >= from && (through is null || on <= through);
    private static bool ValidProfile(ProfileVersionV1 profile) => profile.ProfileId != Guid.Empty && profile.Version > 0;
    private static bool ValidVersion(string version) => !string.IsNullOrEmpty(version)
        && version.Length <= 128 && version[0] is >= '1' and <= '9'
        && version.All(c => c is >= '0' and <= '9');
    private static SalesReferenceReadResult<T> Success<T>(T value) where T : class => new(200, value, null);
    private static SalesReferenceReadResult<T> Failure<T>(int status, string? code = null) where T : class => new(status, null, code);
    private static SalesReferenceReadResult<T> SettingsFailure<T>(SettingsPrerequisiteException exception) where T : class =>
        exception.Code == "settings_access_denied" ? Failure<T>(403, "settings_access_denied")
            : Failure<T>(503, "settings_provider_unavailable");
}
