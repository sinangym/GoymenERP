using System.Globalization;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.Products;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.Endpoints;

public static class SalesProductCandidateEndpoints
{
    public const string Path = "/api/v1/sales/references/products";
    private static readonly HashSet<string> QueryNames = new(["q", "effectiveOn", "limit", "itemType"], StringComparer.Ordinal);

    public static IEndpointRouteBuilder MapSalesProductCandidateEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Path, async (
            [FromQuery] string? q,
            [FromQuery] DateOnly effectiveOn,
            [FromQuery] int? limit,
            [FromQuery] string? itemType,
            HttpContext context,
            ICompanyContext company,
            ICurrentUser user,
            IErpAuthorityRevalidationService authority,
            IProductSalesCandidatesV1 candidates,
            [FromServices] SalesReferenceReadBindings bindings,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(context.Response);
            // SAT-D27 (2): the product binding of the reference reads, or the request / quote write names beside it.
            var names = bindings.AnswerableBy(bindings.ProductPermission);
            var scope = await CaptureScopeAsync(company, user, authority, names, cancellationToken);
            if (scope.Status != StatusCodes.Status200OK) return Problem(scope.Status);
            if (!HasOnlySingleQuery(context.Request.Query) || !HasExactDate(context.Request.Query, "effectiveOn"))
                return Problem(StatusCodes.Status400BadRequest, "validation_failed");
            // SAT-D176 (EK25): a subcontract service order's picker asks for one item type (the Product owner's wire value).
            if (itemType is not null && !ProductItemTypesV1.IsKnown(itemType))
                return Problem(StatusCodes.Status400BadRequest, "validation_failed");

            try
            {
                var result = await candidates.SearchAsync(new(q, effectiveOn, limit ?? 25), cancellationToken);
                if (!await StillAuthorizedAsync(scope, company, user, authority, names, cancellationToken))
                    return Problem(StatusCodes.Status403Forbidden);
                if (itemType is null) return Results.Ok(result);

                // SAT-D176: only the candidates whose shared product card (SAT-D149) names the asked item type. The window stays
                // the owner's (SearchWindowFull unchanged); an unreadable item type is 503, never a candidate assumed to match.
                if (context.RequestServices.GetService<IProductItemTypeReaderV1>() is not { } itemTypes)
                    return Problem(StatusCodes.Status503ServiceUnavailable, "service_unavailable");
                var matching = new List<ProductSalesCandidateV1>(result.Items.Count);
                foreach (var candidate in result.Items)
                {
                    var answer = await itemTypes.GetAsync(candidate.ProductId, cancellationToken);
                    if (answer.Outcome == ProductItemTypeOutcomeV1.Denied)
                        return Problem(StatusCodes.Status403Forbidden);
                    if (answer.Outcome == ProductItemTypeOutcomeV1.Unavailable)
                        return Problem(StatusCodes.Status503ServiceUnavailable, "service_unavailable");
                    if (answer.Outcome == ProductItemTypeOutcomeV1.Found && string.Equals(answer.ItemType, itemType, StringComparison.Ordinal))
                        matching.Add(candidate);
                }

                if (!await StillAuthorizedAsync(scope, company, user, authority, names, cancellationToken))
                    return Problem(StatusCodes.Status403Forbidden);
                return Results.Ok(result with { Items = matching });
            }
            catch (ProductValidationException)
            {
                if (!await StillAuthorizedAsync(scope, company, user, authority, names, cancellationToken))
                    return Problem(StatusCodes.Status403Forbidden);
                return Problem(StatusCodes.Status400BadRequest, "validation_failed");
            }
            catch (ProductOperationException exception)
            {
                if (exception.StatusCode == StatusCodes.Status403Forbidden
                    || !await StillAuthorizedAsync(scope, company, user, authority, names, cancellationToken))
                    return Problem(StatusCodes.Status403Forbidden);
                return Problem(StatusCodes.Status503ServiceUnavailable, "service_unavailable");
            }
            catch (UnauthorizedAccessException)
            {
                return Problem(StatusCodes.Status403Forbidden);
            }
            catch (InvalidOperationException)
            {
                if (!await StillAuthorizedAsync(scope, company, user, authority, names, cancellationToken))
                    return Problem(StatusCodes.Status403Forbidden);
                return Problem(StatusCodes.Status503ServiceUnavailable, "service_unavailable");
            }
        })
            .WithName("GetSalesProductCandidatesV1")
            .WithTags("Sales")
            .RequireAuthorization()
            .RequireCompanyContext()
            .Produces<ProductSalesCandidatesV1>()
            .ProducesErpBadRequest("validation_failed")
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
        return endpoints;
    }

    private static async Task<ConsumerScope> CaptureScopeAsync(ICompanyContext company, ICurrentUser user,
        IErpAuthorityRevalidationService authority, IReadOnlyList<string> names, CancellationToken cancellationToken)
    {
        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId))
            return new(StatusCodes.Status401Unauthorized, Guid.Empty, null);
        if (!company.IsResolved || company.CompanyId == Guid.Empty || !user.CanAccessCompany(company.CompanyId))
            return new(StatusCodes.Status403Forbidden, Guid.Empty, null);
        var scope = new ConsumerScope(StatusCodes.Status200OK, company.CompanyId, user.UserId);
        return await StillAuthorizedAsync(scope, company, user, authority, names, cancellationToken)
            ? scope : new(StatusCodes.Status403Forbidden, Guid.Empty, null);
    }

    /// <summary>
    /// Freshly measured: one of <paramref name="names"/> (the product binding first, then SAT-D27's request / quote write
    /// names) is held in the scope's company now. No name — a missing product binding — refuses.
    /// </summary>
    private static async Task<bool> StillAuthorizedAsync(ConsumerScope scope, ICompanyContext company,
        ICurrentUser user, IErpAuthorityRevalidationService authority, IReadOnlyList<string> names,
        CancellationToken cancellationToken)
    {
        if (names.Count == 0 || !Matches(scope, company, user)) return false;
        foreach (var name in names)
        {
            var permissionScope = await authority.RevalidatePermissionScopeAsync(name, scope.CompanyId, cancellationToken);
            if (permissionScope.IsGranted) return Matches(scope, company, user);
            if (!Matches(scope, company, user)) return false;
        }

        return false;
    }

    private static bool Matches(ConsumerScope scope, ICompanyContext company, ICurrentUser user) =>
        user.IsAuthenticated && user.UserId == scope.ActorId && company.IsResolved
        && company.CompanyId == scope.CompanyId && user.CanAccessCompany(scope.CompanyId);

    private static bool HasOnlySingleQuery(IQueryCollection query) => query.Keys.All(QueryNames.Contains)
        && query.All(pair => pair.Value.Count == 1) && query.ContainsKey("effectiveOn");

    private static bool HasExactDate(IQueryCollection query, string name) =>
        query.TryGetValue(name, out var values) && values.Count == 1
        && DateOnly.TryParseExact(values[0], "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out _);

    private static void SetPrivateNoStore(HttpResponse response)
    {
        response.Headers.CacheControl = "private, no-store";
        response.Headers.Pragma = "no-cache";
    }

    private static IResult Problem(int status, string? code = null) => code is null
        ? Results.Problem(statusCode: status)
        : Results.Problem(statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code });

    private sealed record ConsumerScope(int Status, Guid CompanyId, string? ActorId);
}
