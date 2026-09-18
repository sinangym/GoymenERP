using System.Globalization;
using System.Text;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

public static class SalesEndpoints
{
    public const string AccountsPath = "/api/v1/sales/references/accounts";
    public const string CurrencyPath = "/api/v1/sales/references/currency";
    public const string ProductPath = "/api/v1/sales/references/product";

    private static readonly HashSet<string> AccountQueryNames =
        new(["q", "pageSize", "cursor"], StringComparer.Ordinal);
    private static readonly HashSet<string> CurrencyQueryNames =
        new(["currencyId", "effectiveOn"], StringComparer.Ordinal);
    private static readonly HashSet<string> ProductQueryNames =
        new(["productId", "expectedProductVersion", "orderUnitId", "effectiveOn"], StringComparer.Ordinal);

    public static IEndpointRouteBuilder MapSalesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/sales/references")
            .WithTags("Sales")
            .RequireAuthorization()
            .RequireCompanyContext();

        group.MapGet("/accounts", async (
            [FromQuery] string? q,
            [FromQuery] int? pageSize,
            [FromQuery] string? cursor,
            HttpContext httpContext,
            ISalesReferenceReadService service,
            IActorSessionBindingResolver sessionBindingResolver,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            var effectivePageSize = pageSize ?? 50;
            if (!HasOnlySingleQuery(httpContext.Request.Query, AccountQueryNames, Array.Empty<string>())
                || effectivePageSize is < 1 or > 100 || cursor?.Length > 8192
                || !TryNormalizeAccountSearch(q, out var normalizedQuery))
            {
                return Problem(StatusCodes.Status400BadRequest, "validation_failed");
            }

            var sessionBinding = await sessionBindingResolver.ResolveAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(sessionBinding))
            {
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return ToHttpResult(await service.GetAccountsAsync(
                new(normalizedQuery, effectivePageSize, cursor), sessionBinding, cancellationToken),
                [400, 401, 403, 503],
                ["validation_failed", "invalid_cursor"]);
        })
            .WithName("GetSalesAccountReferencesV1")
            .Produces<SalesAccountOptionsV1>()
            .ProducesErpBadRequest("validation_failed", "invalid_cursor")
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/currency", async (
            [FromQuery] Guid currencyId,
            [FromQuery] DateOnly effectiveOn,
            HttpContext httpContext,
            ISalesReferenceReadService service,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (!HasOnlySingleQuery(httpContext.Request.Query, CurrencyQueryNames, CurrencyQueryNames)
                || !HasExactDate(httpContext.Request.Query, "effectiveOn")
                || currencyId == Guid.Empty)
            {
                return Problem(StatusCodes.Status400BadRequest, "validation_failed");
            }

            return ToHttpResult(await service.GetCurrencyAsync(
                new(currencyId, effectiveOn), cancellationToken),
                [400, 401, 403, 422, 503],
                ["validation_failed", "unsupported_currency"]);
        })
            .WithName("GetSalesCurrencyReferenceV1")
            .Produces<SalesCurrencyReferenceV1>()
            .ProducesErpBadRequest("validation_failed")
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnprocessableEntity("unsupported_currency")
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/product", async (
            [FromQuery] Guid productId,
            [FromQuery] string expectedProductVersion,
            [FromQuery] Guid orderUnitId,
            [FromQuery] DateOnly effectiveOn,
            HttpContext httpContext,
            ISalesReferenceReadService service,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (!HasOnlySingleQuery(httpContext.Request.Query, ProductQueryNames, ProductQueryNames)
                || !HasExactDate(httpContext.Request.Query, "effectiveOn")
                || productId == Guid.Empty || orderUnitId == Guid.Empty
                || string.IsNullOrWhiteSpace(expectedProductVersion)
                || expectedProductVersion.Length > 128)
            {
                return Problem(StatusCodes.Status400BadRequest, "validation_failed");
            }

            return ToHttpResult(await service.GetProductAsync(
                new(productId, expectedProductVersion, orderUnitId, effectiveOn), cancellationToken),
                [400, 401, 403, 404, 409, 422, 503],
                ["validation_failed", "product_selection_changed", "unsupported_product", "unsupported_unit"]);
        })
            .WithName("GetSalesProductReferenceV1")
            .Produces<SalesProductReferenceV1>()
            .ProducesErpBadRequest("validation_failed")
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict("product_selection_changed")
            .ProducesErpUnprocessableEntity("unsupported_product", "unsupported_unit")
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        return endpoints;
    }

    /// <summary>
    /// The accounts search text, normalized exactly like the Cari list's own search
    /// (<see cref="CounterpartyPageQueryParser"/>): trimmed, Unicode FormC, empty or
    /// whitespace-only means no search, and the 200 limit counts Unicode scalar values
    /// (runes), not UTF-16 code units. Without it, <c>q="ACME "</c> or a decomposed
    /// <c>ş</c> returned an empty page where the Cari screen finds the same record, and a
    /// 200-character search made of astral characters was refused on its UTF-16 length.
    /// </summary>
    internal static bool TryNormalizeAccountSearch(string? raw, out string? normalized)
    {
        normalized = null;
        if (raw is null)
        {
            return true;
        }

        var candidate = raw.Trim().Normalize(NormalizationForm.FormC);
        if (candidate.Length == 0)
        {
            return true;
        }

        if (candidate.EnumerateRunes().Count() > 200)
        {
            return false;
        }

        normalized = candidate;
        return true;
    }

    private static bool HasOnlySingleQuery(IQueryCollection query, IReadOnlySet<string> allowed,
        IReadOnlyCollection<string> required) =>
        query.All(pair => allowed.Contains(pair.Key) && pair.Value.Count == 1)
        && required.All(query.ContainsKey);

    private static bool HasExactDate(IQueryCollection query, string name) =>
        query.TryGetValue(name, out var value) && value.Count == 1
        && DateOnly.TryParseExact(value[0], "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out _);

    private static IResult ToHttpResult<T>(SalesReferenceReadResult<T> result,
        int[] allowedStatuses, string[] allowedCodes) where T : class
    {
        if (result.StatusCode == StatusCodes.Status200OK && result.Value is not null && result.Code is null)
        {
            return Results.Ok(result.Value);
        }

        if (result.StatusCode == StatusCodes.Status200OK || result.Value is not null
            || !allowedStatuses.Contains(result.StatusCode))
        {
            throw new InvalidOperationException("Sales reference read returned an invalid result boundary.");
        }

        var code = result.Code is not null && allowedCodes.Contains(result.Code) ? result.Code : null;
        return code is null ? Results.Problem(statusCode: result.StatusCode)
            : Problem(result.StatusCode, code);
    }

    private static IResult Problem(int statusCode, string code) => Results.Problem(
        statusCode: statusCode,
        extensions: new Dictionary<string, object?> { ["code"] = code });

    private static void SetPrivateNoStore(HttpResponse response)
    {
        response.Headers.CacheControl = "private, no-store";
        response.Headers.Pragma = "no-cache";
    }
}

public sealed class CounterpartySalesAccountSelectionSourceV1(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    ICounterpartyReadService counterparties) : ISalesAccountSelectionSourceV1
{
    public async Task<SalesReferenceReadResult<SalesAccountSelectionPageV1>> GetPageAsync(
        SalesAccountSelectionRequestV1 request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Matches(request)) return new(403, null, null);

        CounterpartyPageExecution execution;
        try
        {
            execution = await counterparties.GetAuthorizedPageAsync(new(
                new(request.Q, null, PartyRole.None, CounterpartyRoleMatch.Any,
                    CounterpartyRecordState.Active, CounterpartyRecordState.Active,
                    CounterpartySort.Code, CounterpartySortDirection.Ascending,
                    request.PageSize, request.Cursor, false),
                request.ActorId, request.SessionBinding), cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return new(403, null, null);
        }

        if (!Matches(request)) return new(403, null, null);
        if (execution.Result == CounterpartyPageResult.Forbidden) return new(403, null, null);
        if (execution.Result == CounterpartyPageResult.InvalidCursor) return new(400, null, "invalid_cursor");
        if (execution.Result == CounterpartyPageResult.ServiceUnavailable) return new(503, null, null);
        if (execution.Result != CounterpartyPageResult.Succeeded || execution.Page is null)
            return new(503, null, null);

        return new(200, new(execution.Page.Items.Select(party => new SalesAccountSelectionPartyV1(
                party.Id, party.Code, (int)party.Kind, party.LegalName, party.TradeName,
                party.IsActive, party.Version.ToString(CultureInfo.InvariantCulture),
                party.CompanyAccounts.Select(account => new SalesAccountSelectionAccountV1(
                    account.Id, account.CompanyId, (int)account.Roles, account.IsActive,
                    account.Version.ToString(CultureInfo.InvariantCulture))).ToArray())).ToArray(),
            execution.Page.NextCursor), null);
    }

    private bool Matches(SalesAccountSelectionRequestV1 request) =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && currentUser.UserId == request.ActorId && !string.IsNullOrWhiteSpace(request.SessionBinding)
        && companyContext.IsResolved && companyContext.CompanyId != Guid.Empty
        && companyContext.CompanyId == request.CompanyId
        && currentUser.CanAccessCompany(request.CompanyId);
}
