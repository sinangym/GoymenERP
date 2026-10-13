using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>FI04 reads Finance-owned admitted open items; no write, allocation or money command.</summary>
public static class FinanceOpenItemReadEndpoints
{
    public const string Path = "/api/v1/finance/open-items";
    private const int MaximumCursorLength = 8192;

    public static IEndpointRouteBuilder MapFinanceOpenItemReadEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(Path)
            .WithTags("Finance")
            .RequireAuthorization(ErpPermissions.FinanceOpenItemsRead)
            .RequireCompanyContext();

        group.MapGet("", async (string? cursor, HttpContext http, IFinanceOpenItemReadServiceV1 service,
            ICompanyContext company, ICurrentUser actor, ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            PrivateNoStore(http);
            if (http.Request.Query.Any(pair => pair.Key != "cursor" || pair.Value.Count != 1)
                || (http.Request.Query.TryGetValue("cursor", out var raw)
                    && (string.IsNullOrWhiteSpace(raw[0]) || raw[0]!.Length > MaximumCursorLength)))
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
            var result = await service.ListAuthorizedAsync(Context(company, actor, correlation),
                new SourceListRequest(cursor), cancellationToken);
            return Read(result, list: true);
        })
            .WithName("ListFinanceOpenItemsV1")
            .Produces<FinanceOpenItemPageV1>()
            .ProducesErpBadRequest()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/{openItemId:guid}", async (Guid openItemId, HttpContext http,
            IFinanceOpenItemReadServiceV1 service, ICompanyContext company, ICurrentUser actor,
            ICorrelationContext correlation, CancellationToken cancellationToken) =>
        {
            PrivateNoStore(http);
            if (http.Request.Query.Count != 0)
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
            return Read(await service.DetailAuthorizedAsync(Context(company, actor, correlation),
                openItemId, cancellationToken), list: false);
        })
            .WithName("GetFinanceOpenItemV1")
            .Produces<FinanceOpenItemViewV1>()
            .ProducesErpBadRequest()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
        return endpoints;
    }

    private static FinanceContext Context(ICompanyContext company, ICurrentUser actor, ICorrelationContext correlation) =>
        new(company.CompanyId, actor.UserId ?? string.Empty, correlation.CorrelationId, correlation.CausationId);

    private static IResult Read<T>(ReadResult<T> result, bool list) where T : class => result.Outcome switch
    {
        ReadOutcome.Found when result.Resource is not null => Results.Json(result.Resource, FinanceJson.CreateOptions()),
        ReadOutcome.NotFound => Results.Problem(statusCode: list ? StatusCodes.Status400BadRequest : StatusCodes.Status404NotFound),
        ReadOutcome.Denied => Results.Problem(statusCode: StatusCodes.Status403Forbidden),
        ReadOutcome.Unavailable => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable),
        _ => throw new InvalidOperationException("FI04 returned an invalid read outcome/resource combination."),
    };

    private static void PrivateNoStore(HttpContext http)
    {
        http.Response.Headers.CacheControl = "private, no-store";
        http.Response.Headers.Pragma = "no-cache";
    }
}
