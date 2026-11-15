using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

public static partial class IdentityEndpoints
{
    private const string IdempotencyOriginalCorrelationHeaderName = "Idempotency-Original-Correlation-Id";

    private const string PendingStatusFilter = "pending";

    private const string StatusQueryName = "status";

    private static readonly int[] AccessCommandReplayableStatuses =
    [
        StatusCodes.Status200OK,
        StatusCodes.Status201Created,
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict,
    ];

    /// <summary>
    /// ID-3 writes (spec section ID-3; KIM-D1, KIM-D5, KIM-D15, KIM-D17, KIM-D19; BS-6), all in the server-resolved
    /// company and re-authorized company-wide by <see cref="IIdentityAccessAssignmentService"/>:
    /// <list type="bullet">
    /// <item><c>POST /access/assignments</c> (201): a non-Critical role in one step; a Critical role is
    /// <c>409 identity.assignment.second_approval_required</c> unless the active bootstrap founder assigns it (KIM-D5 a).</item>
    /// <item><c>POST /access/assignments/{id}/revoke</c> (200): one step, a Critical role too (KIM-D15); the company's last
    /// effective Admin is <c>409 identity.last_admin_required</c> (KIM-D17).</item>
    /// <item><c>POST /access/grant-requests</c> (201): a pending request for a Critical role.</item>
    /// <item><c>POST /access/grant-requests/{id}/approve</c> · <c>/reject</c> (200): the second person (KIM-D1);
    /// <c>/withdraw</c> (200): the requester only.</item>
    /// <item><c>GET /access/grant-requests?status=pending</c>: the approval queue.</item>
    /// </list>
    /// Every write takes one <c>Idempotency-Key</c> (the stored terminal answer replays with <c>Idempotency-Replayed</c>
    /// and the original correlation id), the expected version as decimal text where a row exists, and, in the
    /// <c>LocalAccount</c> mode, the antiforgery header of the session cookie (KIM-D31; the pipeline checks it before the
    /// handler, bearer requests are exempt). 401/403/404/503 are code-less. The routes decide nothing about who holds
    /// <see cref="IdentityPermissionManifestV1.RolesManage"/> or <see cref="IdentityPermissionManifestV1.AccessApprove"/>:
    /// grants, roles and Critical assignments stay a human gate.
    /// </summary>
    private static void MapAccessAssignments(RouteGroupBuilder group)
    {
        group.MapPost("/access/assignments", async (
                AssignIdentityRoleRequestV1 request,
                HttpContext httpContext,
                IIdentityAccessAssignmentService service,
                CancellationToken cancellationToken) =>
            await AccessCommandAsync(httpContext,
                metadata => service.AssignAsync(request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(IdentityPermissionManifestV1.RolesManage)
            .WithIdempotencyContract(AccessCommandReplayableStatuses)
            .WithName("AssignIdentityRoleV1")
            .Accepts<AssignIdentityRoleRequestV1>("application/json")
            .Produces<IdentityAccessCommandOutcomeV1>(StatusCodes.Status201Created)
            .ProducesErpBadRequest(
                IdentityAccessProblemCodesV1.IdempotencyKeyInvalid,
                IdentityAccessProblemCodesV1.RequestInvalid)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(
                IdentityAccessProblemCodesV1.IdempotencyConflict,
                IdentityAccessProblemCodesV1.SecondApprovalRequired,
                IdentityAccessProblemCodesV1.DuplicateActiveAssignment,
                IdentityAccessProblemCodesV1.RoleNotAssignable,
                IdentityAccessProblemCodesV1.UserNotAssignable,
                IdentityAccessProblemCodesV1.ValidityElapsed)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/access/assignments/{assignmentId:guid}/revoke", async (
                Guid assignmentId,
                RevokeIdentityRoleAssignmentRequestV1 request,
                HttpContext httpContext,
                IIdentityAccessAssignmentService service,
                CancellationToken cancellationToken) =>
            await AccessCommandAsync(httpContext,
                metadata => service.RevokeAsync(assignmentId, request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(IdentityPermissionManifestV1.RolesManage)
            .RequiresNonEmptyGuidPathParameter("assignmentId")
            .WithIdempotencyContract(AccessCommandReplayableStatuses)
            .WithName("RevokeIdentityRoleAssignmentV1")
            .Accepts<RevokeIdentityRoleAssignmentRequestV1>("application/json")
            .Produces<IdentityAccessCommandOutcomeV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(
                IdentityAccessProblemCodesV1.IdempotencyKeyInvalid,
                IdentityAccessProblemCodesV1.RequestInvalid)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(
                IdentityAccessProblemCodesV1.IdempotencyConflict,
                IdentityAccessProblemCodesV1.VersionConflict,
                IdentityAccessProblemCodesV1.AssignmentNotActive,
                IdentityAccessProblemCodesV1.LastAdminRequired)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/access/grant-requests", async (
                CreateAccessGrantRequestRequestV1 request,
                HttpContext httpContext,
                IIdentityAccessAssignmentService service,
                CancellationToken cancellationToken) =>
            await AccessCommandAsync(httpContext,
                metadata => service.CreateGrantRequestAsync(request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(IdentityPermissionManifestV1.RolesManage)
            .WithIdempotencyContract(AccessCommandReplayableStatuses)
            .WithName("CreateIdentityAccessGrantRequestV1")
            .Accepts<CreateAccessGrantRequestRequestV1>("application/json")
            .Produces<IdentityAccessCommandOutcomeV1>(StatusCodes.Status201Created)
            .ProducesErpBadRequest(
                IdentityAccessProblemCodesV1.IdempotencyKeyInvalid,
                IdentityAccessProblemCodesV1.RequestInvalid)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(
                IdentityAccessProblemCodesV1.IdempotencyConflict,
                IdentityAccessProblemCodesV1.RoleNotCritical,
                IdentityAccessProblemCodesV1.DuplicateActiveAssignment,
                IdentityAccessProblemCodesV1.DuplicatePendingRequest,
                IdentityAccessProblemCodesV1.RoleNotAssignable,
                IdentityAccessProblemCodesV1.UserNotAssignable,
                IdentityAccessProblemCodesV1.ValidityElapsed)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/access/grant-requests/{requestId:guid}/approve", async (
                Guid requestId,
                ApproveAccessGrantRequestV1 request,
                HttpContext httpContext,
                IIdentityAccessAssignmentService service,
                CancellationToken cancellationToken) =>
            await AccessCommandAsync(httpContext,
                metadata => service.ApproveAsync(requestId, request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(IdentityPermissionManifestV1.AccessApprove)
            .RequiresNonEmptyGuidPathParameter("requestId")
            .WithIdempotencyContract(AccessCommandReplayableStatuses)
            .WithName("ApproveIdentityAccessGrantRequestV1")
            .Accepts<ApproveAccessGrantRequestV1>("application/json")
            .Produces<IdentityAccessCommandOutcomeV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(
                IdentityAccessProblemCodesV1.IdempotencyKeyInvalid,
                IdentityAccessProblemCodesV1.RequestInvalid)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(
                IdentityAccessProblemCodesV1.IdempotencyConflict,
                IdentityAccessProblemCodesV1.VersionConflict,
                IdentityAccessProblemCodesV1.RequestNotPending,
                IdentityAccessProblemCodesV1.SelfApprovalRefused,
                IdentityAccessProblemCodesV1.DuplicateActiveAssignment,
                IdentityAccessProblemCodesV1.RoleNotAssignable,
                IdentityAccessProblemCodesV1.UserNotAssignable,
                IdentityAccessProblemCodesV1.ValidityElapsed)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/access/grant-requests/{requestId:guid}/reject", async (
                Guid requestId,
                RejectAccessGrantRequestV1 request,
                HttpContext httpContext,
                IIdentityAccessAssignmentService service,
                CancellationToken cancellationToken) =>
            await AccessCommandAsync(httpContext,
                metadata => service.RejectAsync(requestId, request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(IdentityPermissionManifestV1.AccessApprove)
            .RequiresNonEmptyGuidPathParameter("requestId")
            .WithIdempotencyContract(AccessCommandReplayableStatuses)
            .WithName("RejectIdentityAccessGrantRequestV1")
            .Accepts<RejectAccessGrantRequestV1>("application/json")
            .Produces<IdentityAccessCommandOutcomeV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(
                IdentityAccessProblemCodesV1.IdempotencyKeyInvalid,
                IdentityAccessProblemCodesV1.RequestInvalid)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(
                IdentityAccessProblemCodesV1.IdempotencyConflict,
                IdentityAccessProblemCodesV1.VersionConflict,
                IdentityAccessProblemCodesV1.RequestNotPending,
                IdentityAccessProblemCodesV1.SelfApprovalRefused)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/access/grant-requests/{requestId:guid}/withdraw", async (
                Guid requestId,
                WithdrawAccessGrantRequestV1 request,
                HttpContext httpContext,
                IIdentityAccessAssignmentService service,
                CancellationToken cancellationToken) =>
            await AccessCommandAsync(httpContext,
                metadata => service.WithdrawAsync(requestId, request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(IdentityPermissionManifestV1.RolesManage)
            .RequiresNonEmptyGuidPathParameter("requestId")
            .WithIdempotencyContract(AccessCommandReplayableStatuses)
            .WithName("WithdrawIdentityAccessGrantRequestV1")
            .Accepts<WithdrawAccessGrantRequestV1>("application/json")
            .Produces<IdentityAccessCommandOutcomeV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(
                IdentityAccessProblemCodesV1.IdempotencyKeyInvalid,
                IdentityAccessProblemCodesV1.RequestInvalid)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(
                IdentityAccessProblemCodesV1.IdempotencyConflict,
                IdentityAccessProblemCodesV1.VersionConflict,
                IdentityAccessProblemCodesV1.RequestNotPending,
                IdentityAccessProblemCodesV1.NotRequester)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        // Either AccessApprove or AssignmentsRead (company-wide) opens the queue; one endpoint policy cannot say "or", so
        // the route only requires an authenticated caller and the service answers null (code-less 403) for anyone else.
        group.MapGet("/access/grant-requests", async (
                HttpContext httpContext,
                IIdentityAccessAssignmentService service,
                ICorrelationContext correlationContext,
                CancellationToken cancellationToken) =>
            {
                NoStore(httpContext);
                var query = httpContext.Request.Query;
                if (query.Keys.Any(key => !string.Equals(key, StatusQueryName, StringComparison.Ordinal))
                    || (query.TryGetValue(StatusQueryName, out var status)
                        && (status.Count != 1
                            || !string.Equals(status[0], PendingStatusFilter, StringComparison.Ordinal))))
                {
                    return AccessProblem(
                        StatusCodes.Status400BadRequest,
                        IdentityAccessProblemCodesV1.RequestInvalid,
                        "İstek geçersiz.",
                        "Yalnız status=pending sorgusu kabul edilir; şirket sunucu tarafından belirlenir.",
                        correlationContext);
                }

                var requests = await service.GetPendingGrantRequestsAsync(cancellationToken);
                return requests is null
                    ? Results.Problem(statusCode: StatusCodes.Status403Forbidden)
                    : Results.Ok(requests);
            })
            .RequireCompanyContext()
            .WithName("GetIdentityAccessGrantRequestsV1")
            .Produces<IReadOnlyList<IdentityAccessGrantRequestView>>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(IdentityAccessProblemCodesV1.RequestInvalid)
            .ProducesErpAuthenticatedProblems();
    }

    /// <summary>
    /// The shared write path: the Idempotency-Key (Identity's trimmed reader), the server-resolved envelope, the service
    /// call and the answer's HTTP shape. The company, the actor and the correlation never come from the body.
    /// </summary>
    private static async Task<IResult> AccessCommandAsync(
        HttpContext httpContext,
        Func<IdentityAccessCommandMetadataV1, Task<IdentityAccessCommandResultV1>> execute)
    {
        NoStore(httpContext);
        var services = httpContext.RequestServices;
        var correlationContext = services.GetRequiredService<ICorrelationContext>();
        var currentUser = services.GetRequiredService<ICurrentUser>();
        var companyContext = services.GetRequiredService<ICompanyContext>();
        if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var idempotencyKey))
        {
            return AccessProblem(
                StatusCodes.Status400BadRequest,
                IdentityAccessProblemCodesV1.IdempotencyKeyInvalid,
                "İstek anahtarı geçersiz.",
                "Idempotency-Key başlığı eksik veya geçersiz.",
                correlationContext);
        }

        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
        }

        if (!correlationContext.IsResolved || string.IsNullOrWhiteSpace(correlationContext.SourceChannel))
        {
            // The correlation middleware always resolves it; without it no audit envelope can be written.
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var metadata = new IdentityAccessCommandMetadataV1(
            companyContext.CompanyId,
            currentUser.UserId,
            correlationContext.CorrelationId,
            correlationContext.CausationId,
            correlationContext.SourceChannel,
            correlationContext.ClientIpAddress,
            correlationContext.UserAgent,
            idempotencyKey);

        IdentityAccessCommandResultV1 result;
        try
        {
            result = await execute(metadata);
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
        }

        if (result.IsReplay)
        {
            httpContext.Response.Headers[IdempotencyReplayedHeaderName] = "true";
            httpContext.Response.Headers[IdempotencyOriginalCorrelationHeaderName] = result.OriginalCorrelationId;
        }

        var answer = result.Answer;
        return answer.StatusCode switch
        {
            StatusCodes.Status200OK or StatusCodes.Status201Created when answer.Result is { } outcome =>
                Results.Json(outcome, statusCode: answer.StatusCode),
            StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden or StatusCodes.Status404NotFound
                or StatusCodes.Status503ServiceUnavailable => Results.Problem(
                    title: answer.Title,
                    detail: answer.Detail,
                    statusCode: answer.StatusCode),
            _ when answer.Code is { } code => AccessProblem(answer.StatusCode, code, answer.Title, answer.Detail,
                correlationContext),
            _ => throw new InvalidOperationException("Kimlik yetki komutu sonucu HTTP durumuyla eşleşmiyor."),
        };
    }

    private static IResult AccessProblem(
        int statusCode,
        string code,
        string title,
        string detail,
        ICorrelationContext correlationContext) => Results.Problem(
        title: title,
        detail: detail,
        statusCode: statusCode,
        extensions: new Dictionary<string, object?>
        {
            ["code"] = code,
            ["correlationId"] = correlationContext.IsResolved ? correlationContext.CorrelationId : null,
        });
}
