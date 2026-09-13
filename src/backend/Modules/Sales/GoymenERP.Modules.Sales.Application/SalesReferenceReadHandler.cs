using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Sales.Application;

// Read-only orchestration over actual owner ports. No table access or writes.
public sealed class SalesReferenceReadHandler(
    ICompanyContext companyContext, ICurrentUser currentUser,
    IErpAuthorizationService authorization, ICounterpartyReadService counterparties,
    ISettingsPrerequisiteProvider settings, IProductSalesEligibilityV1? products,
    SalesReferenceReadBindings bindings) : ISalesReferenceReadService
{
    public async Task<SalesReferenceReadResult<SalesAccountOptionsV1>> GetAccountsAsync(
        SalesAccountQueryV1 query, string sessionBinding, CancellationToken cancellationToken = default)
    {
        var scope = await GetScopeAsync(bindings.AccountsPermission, cancellationToken);
        if (scope.Status != 200) return Failure<SalesAccountOptionsV1>(scope.Status);
        if (query.PageSize is < 1 or > 100 || query.Q?.Length > 200
            || query.Cursor?.Length > 8192 || string.IsNullOrWhiteSpace(sessionBinding))
            return Failure<SalesAccountOptionsV1>(400, "validation_failed");
        var page = await counterparties.GetAuthorizedPageAsync(new CounterpartyReadRequest(
            new CounterpartyPageQuery(query.Q, null, PartyRole.None, CounterpartyRoleMatch.Any,
                CounterpartyRecordState.Active, CounterpartyRecordState.Active,
                CounterpartySort.Code, CounterpartySortDirection.Ascending, query.PageSize,
                query.Cursor, false), scope.Actor!, sessionBinding), cancellationToken);
        if (!await StillAuthorizedAsync(scope, bindings.AccountsPermission, cancellationToken))
            return Failure<SalesAccountOptionsV1>(403);
        if (page.Result != CounterpartyPageResult.Succeeded || page.Page is null)
            return page.Result switch
            {
                CounterpartyPageResult.Forbidden => Failure<SalesAccountOptionsV1>(403),
                CounterpartyPageResult.InvalidCursor => Failure<SalesAccountOptionsV1>(400, "invalid_cursor"),
                _ => Failure<SalesAccountOptionsV1>(503, "counterparty_provider_unavailable")
            };
        var options = new List<SalesAccountOptionV1>();
        foreach (var party in page.Page.Items.Where(p => p.IsActive))
        foreach (var account in party.CompanyAccounts.Where(a => a.IsActive && a.CompanyId == scope.Company))
        {
            if (party.Id == Guid.Empty || account.Id == Guid.Empty || party.Version <= 0
                || account.Version <= 0 || string.IsNullOrWhiteSpace(party.Code)
                || string.IsNullOrWhiteSpace(party.LegalName))
                return Failure<SalesAccountOptionsV1>(503, "reference_provider_invalid");
            options.Add(new(party.Id, account.Id, $"{party.Code} - {party.LegalName}",
                party.Version.ToString(CultureInfo.InvariantCulture),
                account.Version.ToString(CultureInfo.InvariantCulture)));
        }
        return Success(new SalesAccountOptionsV1(scope.Company, options, page.Page.NextCursor));
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
            return SettingsFailure<SalesCurrencyReferenceV1>(exception);
        }
    }

    public async Task<SalesReferenceReadResult<SalesProductReferenceV1>> GetProductAsync(
        SalesProductQueryV1 query, CancellationToken cancellationToken = default)
    {
        var scope = await GetScopeAsync(bindings.ProductPermission, cancellationToken);
        if (scope.Status != 200) return Failure<SalesProductReferenceV1>(scope.Status);
        if (query.ProductId == Guid.Empty || query.OrderUnitId == Guid.Empty
            || string.IsNullOrWhiteSpace(query.ExpectedProductVersion) || query.ExpectedProductVersion.Length > 128)
            return Failure<SalesProductReferenceV1>(400, "validation_failed");
        if (products is null) return Failure<SalesProductReferenceV1>(503, "product_provider_unavailable");
        try
        {
            var result = await products.EvaluateAsync(new ProductSalesSelectionV1(query.ProductId,
                query.ExpectedProductVersion, query.OrderUnitId, query.EffectiveOn), cancellationToken);
            if (!await StillAuthorizedAsync(scope, bindings.ProductPermission, cancellationToken))
                return Failure<SalesProductReferenceV1>(403);
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
            return SettingsFailure<SalesProductReferenceV1>(exception);
        }
        catch (ProductOperationException exception)
        {
            return exception.StatusCode switch
            {
                401 => Failure<SalesProductReferenceV1>(401),
                403 => Failure<SalesProductReferenceV1>(403),
                404 => Failure<SalesProductReferenceV1>(404),
                _ => Failure<SalesProductReferenceV1>(503, "product_provider_unavailable")
            };
        }
    }

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
    private static bool Effective(DateOnly on, DateOnly from, DateOnly? through) => on >= from && (through is null || on <= through);
    private static bool ValidProfile(ProfileVersionV1 profile) => profile.ProfileId != Guid.Empty && profile.Version > 0;
    private static SalesReferenceReadResult<T> Success<T>(T value) where T : class => new(200, value, null);
    private static SalesReferenceReadResult<T> Failure<T>(int status, string? code = null) where T : class => new(status, null, code);
    private static SalesReferenceReadResult<T> SettingsFailure<T>(SettingsPrerequisiteException exception) where T : class =>
        exception.Code == "settings_access_denied" ? Failure<T>(403, "settings_access_denied")
            : Failure<T>(503, "settings_provider_unavailable");
}
