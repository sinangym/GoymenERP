using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Work.Domain;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// WP2 (YOL-D17, YOL-D28): <c>GET /api/v1/work/approval-follow-ups?view=escalated|orphaned</c> — the escalated approvals and
/// the "sahipsiz onay" view, for a holder of <c>work.approvals.escalate</c> company-wide (checked by Work, before and after the
/// read). Reads Work's own follow-up rows only; the decision is the owner's command on each item's link. 400 for an unknown
/// view; 401/403/503 are code-less.
/// </summary>
public static class WorkApprovalFollowUpEndpoints
{
    public static IEndpointRouteBuilder MapWorkApprovalFollowUpEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGroup("/api/v1/work").WithTags("Work").RequireAuthorization().RequireCompanyContext()
            .MapGet("/approval-follow-ups", async (string? view, HttpContext http, [FromServices] IWorkApprovalFollowUpReadV1 reader,
                ICorrelationContext correlation, CancellationToken token) =>
            {
                http.Response.Headers.CacheControl = "no-store";
                try
                {
                    return Results.Json(await reader.ListAsync(view, token));
                }
                catch (WorkApprovalFollowUpException exception)
                {
                    return exception.Status == 400
                        ? Results.Problem(statusCode: 400, extensions: new Dictionary<string, object?>
                        {
                            ["code"] = exception.Code,
                            ["correlationId"] = correlation.IsResolved ? correlation.CorrelationId : null,
                        })
                        : Results.Problem(statusCode: exception.Status);
                }
            })
            .WithName("GetWorkApprovalFollowUpsV1").Produces<WorkApprovalFollowUpPageV1>()
            .ProducesErpBadRequest("validation_failed").ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
        return endpoints;
    }
}
