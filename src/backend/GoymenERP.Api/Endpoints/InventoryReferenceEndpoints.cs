using System.Globalization;
using GoymenERP.Api.Errors;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.Products;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.Endpoints;

public static class InventoryReferenceEndpoints
{
    public const string ProductPath = "/api/v1/inventory/references/products";
    public const string LocationPath = "/api/v1/inventory/references/locations";
    public const string ProductFamilyPath = "/api/v1/inventory/references/product-families";

    private static readonly HashSet<string> ProductQueryNames =
        new(["q", "effectiveOn", "limit"], StringComparer.Ordinal);
    private static readonly HashSet<string> LocationQueryNames =
        new(["q", "limit"], StringComparer.Ordinal);

    public static IEndpointRouteBuilder MapInventoryReferenceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(ProductPath, GetProductsAsync)
            .WithName("GetInventoryProductCandidatesV1")
            .WithTags("Inventory")
            .WithMetadata(new PrivateNoStoreOnAuthorizationFailureMetadata())
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.InventoryOpeningStockRecord)
            .Produces<ProductStockCandidatesV1>()
            .ProducesErpBadRequest(InventoryReferenceCodesV1.ProductInvalidQuery)
            .ProducesErpConflict(InventoryReferenceCodesV1.ProductReadConflict)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        endpoints.MapGet(LocationPath, GetLocationsAsync)
            .WithName("GetInventoryLocationCandidatesV1")
            .WithTags("Inventory")
            .WithMetadata(new PrivateNoStoreOnAuthorizationFailureMetadata())
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.InventoryOpeningStockRecord)
            .Produces<OrganizationLocationCandidatesV1>()
            .ProducesErpBadRequest(InventoryReferenceCodesV1.LocationInvalidQuery)
            .ProducesErpConflict(InventoryReferenceCodesV1.LocationReadConflict)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        endpoints.MapGet(ProductFamilyPath, GetProductFamiliesAsync)
            .WithName("GetInventoryProductFamilyOptionsV1")
            .WithTags("Inventory")
            .WithMetadata(new PrivateNoStoreOnAuthorizationFailureMetadata())
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead)
            .Produces<ProductStockFamilyOptionsV1>()
            .ProducesErpBadRequest(InventoryReferenceCodesV1.ProductFamilyInvalidQuery)
            .ProducesErpConflict(InventoryReferenceCodesV1.ProductFamilyReadConflict)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        return endpoints;
    }

    private static async Task<IResult> GetProductFamiliesAsync(
        HttpContext context,
        ICompanyContext company,
        ICurrentUser user,
        IErpAuthorityRevalidationService authority,
        IProductStockFamilyOptionsV1 options,
        CancellationToken cancellationToken)
    {
        SetPrivateNoStore(context.Response);
        var scope = await CaptureStockReadScopeAsync(company, user, authority, cancellationToken);
        if (scope is null) return Problem(StatusCodes.Status403Forbidden);
        if (context.Request.Query.Count != 0)
            return Problem(StatusCodes.Status400BadRequest, InventoryReferenceCodesV1.ProductFamilyInvalidQuery);

        try
        {
            var result = await options.ReadFamilyOptionsAsync(cancellationToken);
            if (!await StillStockReadAuthorizedAsync(scope, company, user, authority, cancellationToken))
                return Problem(StatusCodes.Status403Forbidden);
            return IsValid(result)
                ? Results.Ok(result)
                : Problem(StatusCodes.Status409Conflict, InventoryReferenceCodesV1.ProductFamilyReadConflict);
        }
        catch (ProductOperationException exception)
        {
            return exception.StatusCode == StatusCodes.Status403Forbidden
                ? Problem(StatusCodes.Status403Forbidden)
                : await AuthorizedStockReadProblemAsync(scope, company, user, authority, cancellationToken,
                    StatusCodes.Status503ServiceUnavailable);
        }
        catch (InvalidOperationException)
        {
            return await AuthorizedStockReadProblemAsync(scope, company, user, authority, cancellationToken,
                StatusCodes.Status409Conflict, InventoryReferenceCodesV1.ProductFamilyReadConflict);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return await AuthorizedStockReadProblemAsync(scope, company, user, authority, cancellationToken,
                StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> GetProductsAsync(
        HttpContext context,
        ICompanyContext company,
        ICurrentUser user,
        IErpAuthorityRevalidationService authority,
        IProductStockCandidatesV1 candidates,
        CancellationToken cancellationToken)
    {
        SetPrivateNoStore(context.Response);
        var scope = await CaptureScopeAsync(company, user, authority, cancellationToken);
        if (scope is null) return Problem(StatusCodes.Status403Forbidden);
        if (!TryReadProductQuery(context.Request.Query, out var query))
            return Problem(StatusCodes.Status400BadRequest, InventoryReferenceCodesV1.ProductInvalidQuery);

        try
        {
            var result = await candidates.SearchAsync(query, cancellationToken);
            if (!await StillAuthorizedAsync(scope, company, user, authority, cancellationToken))
                return Problem(StatusCodes.Status403Forbidden);
            return IsValid(result)
                ? Results.Ok(result)
                : Problem(StatusCodes.Status409Conflict, InventoryReferenceCodesV1.ProductReadConflict);
        }
        catch (ProductValidationException)
        {
            return await AuthorizedProblemAsync(scope, company, user, authority, cancellationToken,
                StatusCodes.Status400BadRequest, InventoryReferenceCodesV1.ProductInvalidQuery);
        }
        catch (ProductOperationException exception)
        {
            return exception.StatusCode == StatusCodes.Status403Forbidden
                ? Problem(StatusCodes.Status403Forbidden)
                : await AuthorizedProblemAsync(scope, company, user, authority, cancellationToken,
                    StatusCodes.Status503ServiceUnavailable);
        }
        catch (SettingsPrerequisiteException exception)
        {
            return string.Equals(exception.Code, "settings_access_denied", StringComparison.Ordinal)
                ? Problem(StatusCodes.Status403Forbidden)
                : await AuthorizedProblemAsync(scope, company, user, authority, cancellationToken,
                    StatusCodes.Status503ServiceUnavailable);
        }
        catch (UnauthorizedAccessException)
        {
            return Problem(StatusCodes.Status403Forbidden);
        }
        catch (InvalidOperationException)
        {
            return await AuthorizedProblemAsync(scope, company, user, authority, cancellationToken,
                StatusCodes.Status409Conflict, InventoryReferenceCodesV1.ProductReadConflict);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return await AuthorizedProblemAsync(scope, company, user, authority, cancellationToken,
                StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> GetLocationsAsync(
        HttpContext context,
        ICompanyContext company,
        ICurrentUser user,
        IErpAuthorityRevalidationService authority,
        IOrganizationLocationCandidatesV1 candidates,
        CancellationToken cancellationToken)
    {
        SetPrivateNoStore(context.Response);
        var scope = await CaptureScopeAsync(company, user, authority, cancellationToken);
        if (scope is null) return Problem(StatusCodes.Status403Forbidden);
        if (!TryReadLocationQuery(context.Request.Query, out var query))
            return Problem(StatusCodes.Status400BadRequest, InventoryReferenceCodesV1.LocationInvalidQuery);

        try
        {
            var result = await candidates.SearchAsync(scope.CompanyId, query, cancellationToken);
            if (!await StillAuthorizedAsync(scope, company, user, authority, cancellationToken))
                return Problem(StatusCodes.Status403Forbidden);
            return IsValid(result)
                ? Results.Ok(result)
                : Problem(StatusCodes.Status409Conflict, InventoryReferenceCodesV1.LocationReadConflict);
        }
        catch (OrganizationLocationCandidateQueryException)
        {
            return await AuthorizedProblemAsync(scope, company, user, authority, cancellationToken,
                StatusCodes.Status400BadRequest, InventoryReferenceCodesV1.LocationInvalidQuery);
        }
        catch (OrganizationCompanyContextMismatchException)
        {
            return Problem(StatusCodes.Status403Forbidden);
        }
        catch (InvalidOperationException)
        {
            return await AuthorizedProblemAsync(scope, company, user, authority, cancellationToken,
                StatusCodes.Status409Conflict, InventoryReferenceCodesV1.LocationReadConflict);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return await AuthorizedProblemAsync(scope, company, user, authority, cancellationToken,
                StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static bool TryReadProductQuery(IQueryCollection values, out ProductStockCandidateQueryV1 query)
    {
        query = new(null, default);
        if (!HasOnlySingleKnownValues(values, ProductQueryNames)
            || !values.TryGetValue("effectiveOn", out var dateValues)
            || !DateOnly.TryParseExact(dateValues[0], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var effectiveOn)
            || !TryReadLimit(values, out var limit))
            return false;
        query = new(values.TryGetValue("q", out var q) ? q[0] : null, effectiveOn, limit);
        return true;
    }

    private static bool TryReadLocationQuery(IQueryCollection values, out OrganizationLocationCandidateQueryV1 query)
    {
        query = new();
        if (!HasOnlySingleKnownValues(values, LocationQueryNames) || !TryReadLimit(values, out var limit))
            return false;
        query = new(values.TryGetValue("q", out var q) ? q[0] : null, limit);
        return true;
    }

    private static bool HasOnlySingleKnownValues(IQueryCollection query, IReadOnlySet<string> names) =>
        query.Keys.All(names.Contains) && query.All(item => item.Value.Count == 1);

    private static bool TryReadLimit(IQueryCollection query, out int limit)
    {
        limit = 25;
        return !query.TryGetValue("limit", out var values)
               || int.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out limit)
               && limit is >= 1 and <= 100;
    }

    private static bool IsValid(ProductStockCandidatesV1 result) =>
        result.Items.All(item => item.ProductId != Guid.Empty && item.StockUnitId != Guid.Empty
            && item.ProfileId != Guid.Empty && Required(item.ProductVersion) && Required(item.Code)
            && Required(item.Name) && Required(item.StockUnitCode) && Required(item.ProfileVersion))
        && result.Items.Select(item => item.ProductId).Distinct().Count() == result.Items.Count;

    private static bool IsValid(OrganizationLocationCandidatesV1 result) =>
        result.Items.All(item => item.LocationId != Guid.Empty && item.SiteId != Guid.Empty
            && Required(item.LocationCode) && Required(item.LocationName)
            && Required(item.SiteCode) && Required(item.SiteName))
        && result.Items.Select(item => item.LocationId).Distinct().Count() == result.Items.Count;

    private static bool IsValid(ProductStockFamilyOptionsV1 result) =>
        result.Items.Count > 0
        && result.Items.All(item => item.FamilyId != Guid.Empty && Required(item.Code) && Required(item.DisplayName))
        && result.Items.Select(item => item.FamilyId).Distinct().Count() == result.Items.Count
        && result.Items.Select(item => item.Code).Distinct(StringComparer.Ordinal).Count() == result.Items.Count;

    private static bool Required(string? value) => !string.IsNullOrWhiteSpace(value);

    private static async Task<ConsumerScope?> CaptureScopeAsync(
        ICompanyContext company,
        ICurrentUser user,
        IErpAuthorityRevalidationService authority,
        CancellationToken cancellationToken)
    {
        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId)
            || !company.IsResolved || company.CompanyId == Guid.Empty
            || !user.CanAccessCompany(company.CompanyId))
            return null;
        var scope = new ConsumerScope(company.CompanyId, user.UserId);
        return await StillAuthorizedAsync(scope, company, user, authority, cancellationToken) ? scope : null;
    }

    private static async Task<bool> StillAuthorizedAsync(
        ConsumerScope scope,
        ICompanyContext company,
        ICurrentUser user,
        IErpAuthorityRevalidationService authority,
        CancellationToken cancellationToken)
    {
        if (!Matches(scope, company, user)) return false;
        var permission = await authority.RevalidatePermissionScopeAsync(
            ErpPermissions.InventoryOpeningStockRecord, scope.CompanyId, cancellationToken);
        return permission.IsGranted && Matches(scope, company, user);
    }

    private static bool Matches(ConsumerScope scope, ICompanyContext company, ICurrentUser user) =>
        user.IsAuthenticated && user.UserId == scope.ActorId && company.IsResolved
        && company.CompanyId == scope.CompanyId && user.CanAccessCompany(scope.CompanyId);

    private static async Task<ConsumerScope?> CaptureStockReadScopeAsync(
        ICompanyContext company,
        ICurrentUser user,
        IErpAuthorityRevalidationService authority,
        CancellationToken cancellationToken)
    {
        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId)
            || !company.IsResolved || company.CompanyId == Guid.Empty
            || !user.CanAccessCompany(company.CompanyId))
            return null;
        var scope = new ConsumerScope(company.CompanyId, user.UserId);
        return await StillStockReadAuthorizedAsync(scope, company, user, authority, cancellationToken) ? scope : null;
    }

    private static async Task<bool> StillStockReadAuthorizedAsync(
        ConsumerScope scope,
        ICompanyContext company,
        ICurrentUser user,
        IErpAuthorityRevalidationService authority,
        CancellationToken cancellationToken)
    {
        foreach (var permission in new[]
                 {
                     ErpPermissions.InventoryLabelsRead,
                     ErpPermissions.InventoryBalancesRead,
                 })
        {
            if (!Matches(scope, company, user)) return false;
            var fresh = await authority.RevalidatePermissionScopeAsync(
                permission, scope.CompanyId, cancellationToken);
            if (!fresh.IsGranted || !Matches(scope, company, user)) return false;
        }
        return true;
    }

    private static async Task<IResult> AuthorizedProblemAsync(
        ConsumerScope scope,
        ICompanyContext company,
        ICurrentUser user,
        IErpAuthorityRevalidationService authority,
        CancellationToken cancellationToken,
        int status,
        string? code = null) =>
        await StillAuthorizedAsync(scope, company, user, authority, cancellationToken)
            ? Problem(status, code)
            : Problem(StatusCodes.Status403Forbidden);

    private static async Task<IResult> AuthorizedStockReadProblemAsync(
        ConsumerScope scope,
        ICompanyContext company,
        ICurrentUser user,
        IErpAuthorityRevalidationService authority,
        CancellationToken cancellationToken,
        int status,
        string? code = null) =>
        await StillStockReadAuthorizedAsync(scope, company, user, authority, cancellationToken)
            ? Problem(status, code)
            : Problem(StatusCodes.Status403Forbidden);

    private static void SetPrivateNoStore(HttpResponse response)
    {
        response.Headers.CacheControl = "private, no-store";
        response.Headers.Pragma = "no-cache";
    }

    private static IResult Problem(int status, string? code = null) => code is null
        ? Results.Problem(statusCode: status)
        : Results.Problem(statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    private sealed record ConsumerScope(Guid CompanyId, string ActorId);
}

public static class InventoryReferenceCodesV1
{
    public const string ProductInvalidQuery = "inventory_product_candidate_invalid_query";
    public const string ProductReadConflict = "inventory_product_candidate_read_conflict";
    public const string LocationInvalidQuery = "organization_location_candidate_invalid_query";
    public const string LocationReadConflict = "inventory_location_candidate_read_conflict";
    public const string ProductFamilyInvalidQuery = "inventory_product_family_option_invalid_query";
    public const string ProductFamilyReadConflict = "inventory_product_family_option_read_conflict";
}
