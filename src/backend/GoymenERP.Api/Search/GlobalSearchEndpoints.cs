using System.Text;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Search;
using GoymenERP.Modules.Sales.Infrastructure.Search;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.Search;

/// <summary>
/// AT-002 / ERP-000I: the shell's global search route and its composition. Owning module: the PLATFORM (this host) owns
/// only the composer and the route; every record comes from its owner's search source (<see cref="IGlobalSearchSourceV1"/>),
/// which measures the owner's own read permission. No new permission name: a kind is searched exactly when the actor holds
/// the read its owner's list already measures, so the search can never show more than the owner's own screens would.
/// </summary>
public static class GlobalSearchEndpoints
{
    /// <summary>
    /// Registers the composer and the first-release sources: Cari parties and product cards through their owners' own list
    /// reads (host adapters), and Sales orders through the Sales-owned source. Call after the modules are added.
    /// </summary>
    public static IServiceCollection AddGlobalSearch(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IGlobalSearchSourceV1, CounterpartyGlobalSearchSource>();
        services.AddScoped<IGlobalSearchSourceV1, ProductGlobalSearchSource>();
        services.AddSalesGlobalSearchSource();
        services.AddScoped<GlobalSearchComposer>();
        return services;
    }

    /// <summary>
    /// GET /api/v1/search?q= — authenticated, in the committed company context, GET only, creating nothing. The only query
    /// member is <c>q</c>, once: trimmed, NFC, 2–100 characters, no control character; anything else is 400
    /// <c>validation_failed</c>. 200 is the groups the actor may read (possibly none — indistinguishable from no match);
    /// 503 when any owner could not answer. <c>private, no-store</c>: a search answer is never cached.
    /// </summary>
    public static IEndpointRouteBuilder MapGlobalSearchEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // `q` is bound only so the contract documents it; the rule is applied to the raw query (one value, no other member).
        endpoints.MapGet(GlobalSearchApiV1.Path, async ([FromQuery(Name = GlobalSearchApiV1.QueryParameter)] string? q, HttpContext httpContext,
                [FromServices] GlobalSearchComposer composer, ICorrelationContext correlation, CancellationToken cancellationToken) =>
            {
                httpContext.Response.Headers.CacheControl = "private, no-store";
                httpContext.Response.Headers.Pragma = "no-cache";
                if (!TryReadTerm(httpContext.Request.Query, out var term))
                {
                    return Problem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
                }

                try
                {
                    return Results.Ok(await composer.SearchAsync(term, cancellationToken));
                }
                catch (GlobalSearchUnavailableException)
                {
                    return Problem(StatusCodes.Status503ServiceUnavailable, "service_unavailable", correlation);
                }
            })
            .WithTags("Search")
            .RequireAuthorization()
            .RequireCompanyContext()
            .WithName("SearchGlobalV1")
            .Produces<GlobalSearchResponseV1>()
            .ProducesErpBadRequest("validation_failed")
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
        return endpoints;
    }

    /// <summary>The term rule: exactly one <c>q</c>, and no other member.</summary>
    public static bool TryReadTerm(IQueryCollection query, out string term)
    {
        term = string.Empty;
        if (query.Count != 1 || !query.TryGetValue(GlobalSearchApiV1.QueryParameter, out var values) || values.Count != 1 || values[0] is null)
        {
            return false;
        }

        var normalized = values[0]!.Trim().Normalize(NormalizationForm.FormC);
        var length = normalized.EnumerateRunes().Count();
        if (length < GlobalSearchApiV1.MinimumTermLength || length > GlobalSearchApiV1.MaximumTermLength
            || normalized.EnumerateRunes().Any(Rune.IsControl))
        {
            return false;
        }

        term = normalized;
        return true;
    }

    private static IResult Problem(int statusCode, string code, ICorrelationContext correlation) => Results.Problem(statusCode: statusCode,
        extensions: new Dictionary<string, object?> { ["code"] = code, ["correlationId"] = correlation.IsResolved ? correlation.CorrelationId : null });
}
