using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Identity.Application.Contracts;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

public static class WorkDelegationEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public static IEndpointRouteBuilder MapWorkDelegationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/work/delegations").WithTags("Work").RequireAuthorization().RequireCompanyContext();
        group.MapGet("/people", async (string kind, string? q, HttpContext http, ICompanyContext company, ICurrentUser user,
            IErpAuthorityRevalidationService authority, IUserDirectoryReadPortV1 directory,
            IEnumerable<IWorkInboxSourceV1> sources, IOptions<WorkDelegationOptionsV1> options, CancellationToken token) =>
        {
            NoStore(http);
            if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId)) return Results.Problem(statusCode: 401);
            if (!company.IsResolved || !user.CanAccessCompany(company.CompanyId)) return Results.Problem(statusCode: 403);
            if (!options.Value.Enabled) return Results.Problem(statusCode: 503);
            var matches = sources.Where(source => source.Kind == kind).ToArray();
            if (matches.Length != 1 || q?.Length > UserDirectoryLimitsV1.MaximumQueryLength) return Results.Problem(statusCode: 400);
            var source = matches[0];
            if (!ErpPermissions.IsKnown(source.ApproverPermission)) return Results.Problem(statusCode: 503);
            var companyId = company.CompanyId; var actor = user.UserId;
            async Task<bool> Granted()
            {
                var approve = await authority.RevalidatePermissionScopeAsync(source.ApproverPermission, companyId, token);
                var allowed = approve.IsGranted && (approve.IsCompanyWide || source.AcceptsScopedApprovers);
                if (!allowed) { var manage = await authority.RevalidatePermissionScopeAsync(ErpPermissions.WorkDelegationsManage, companyId, token); allowed = manage.IsGranted && manage.IsCompanyWide; }
                return allowed && options.Value.Enabled && company.IsResolved && company.CompanyId == companyId
                    && user.IsAuthenticated && user.UserId == actor && user.CanAccessCompany(companyId);
            }
            if (!await Granted()) return Results.Problem(statusCode: 403);
            var people = await directory.SearchActiveAsync(new(companyId, actor, q?.Trim(), UserDirectoryLimitsV1.MaximumSearchLimit,
                source.ApproverPermission), token);
            if (!await Granted() || people.Outcome == UserDirectoryOutcomeV1.Denied) return Results.Problem(statusCode: 403);
            if (people.Outcome != UserDirectoryOutcomeV1.Found) return Results.Problem(statusCode: 503);
            // Picker eligibility is a hint. Creation remeasures exact scopes and ROL-D4 manager eligibility.
            return Results.Json(new WorkDelegationPeopleV1(companyId, kind,
                people.Items.Where(person => person.IsActiveInCompany).Select(person => new WorkDelegationPersonV1(person.UserId, person.DisplayName)).ToArray(), people.HasMore));
        }).WithName("GetWorkDelegationPeopleV1").Produces<WorkDelegationPeopleV1>().ProducesErpCodeLessBadRequest()
            .AllowsCodeLessErpBadRequestBody().ProducesErpAuthenticatedProblems().ProducesErpServiceUnavailable();
        group.MapGet("", async (bool? all, int? page, HttpContext http, ICorrelationContext correlation,
                [FromServices] IWorkDelegationServiceV1 service, CancellationToken token) =>
            {
                NoStore(http);
                try { return Results.Json(await service.ListAsync(all == true, page ?? 0, token)); }
                catch (WorkDelegationException error) { return Problem(error, http, correlation); }
            }).WithName("GetWorkDelegationsV1").Produces<WorkDelegationTableV1>()
            .ProducesErpBadRequest(WorkDelegationCodesV1.Invalid).AllowsEmptyErpBadRequestBody()
            .ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

        group.MapPost("", (HttpContext http, ICorrelationContext correlation,
                [FromServices] IWorkDelegationServiceV1 service, CancellationToken token) =>
            CommandAsync<CreateWorkDelegationV1>(http, correlation,
                (request, metadata) => service.CreateAsync(request, metadata, token), 201, token))
            .WithName("CreateWorkDelegationV1").Accepts<CreateWorkDelegationV1>("application/json")
            .WithIdempotencyContract(201, 409).Produces<WorkDelegationViewV1>(201)
            .ProducesErpBadRequest(WorkDelegationCodesV1.Invalid, WorkDelegationCodesV1.Metadata).AllowsEmptyErpBadRequestBody()
            .ProducesErpUnprocessableEntity([WorkDelegationCodesV1.Invalid, WorkDelegationCodesV1.MissingPermission, WorkDelegationCodesV1.ScopeMismatch])
            .ProducesErpConflict(WorkDelegationCodesV1.Chain, WorkDelegationCodesV1.Concurrent, WorkDelegationCodesV1.KeyMismatch)
            .ProducesErpUnsupportedMediaType().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

        group.MapPost("/{id:guid}/revocation", (Guid id, HttpContext http, ICorrelationContext correlation,
                [FromServices] IWorkDelegationServiceV1 service, CancellationToken token) =>
            CommandAsync<RevokeWorkDelegationV1>(http, correlation,
                (request, metadata) => service.RevokeAsync(id, request, metadata, token), 200, token))
            .WithName("RevokeWorkDelegationV1").Accepts<RevokeWorkDelegationV1>("application/json")
            .WithIdempotencyContract(200, 409).Produces<WorkDelegationViewV1>()
            .ProducesErpBadRequest(WorkDelegationCodesV1.Invalid, WorkDelegationCodesV1.Metadata).AllowsEmptyErpBadRequestBody()
            .ProducesErpUnprocessableEntity([WorkDelegationCodesV1.Invalid])
            .ProducesErpConflict(WorkDelegationCodesV1.Version, WorkDelegationCodesV1.Concurrent, WorkDelegationCodesV1.KeyMismatch)
            .ProducesErpCodeLessNotFound().ProducesErpUnsupportedMediaType().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
        return endpoints;
    }

    private static async Task<IResult> CommandAsync<T>(HttpContext http, ICorrelationContext correlation,
        Func<T, WorkCommandMetadataV1, Task<WorkDelegationCommandResultV1>> execute, int status, CancellationToken token)
    {
        NoStore(http);
        if (!correlation.IsResolved || !http.Request.Headers.TryGetValue("Idempotency-Key", out var keys) || keys.Count != 1
            || keys[0] is not { Length: > 0 and <= 128 } key || string.IsNullOrWhiteSpace(key) || key.Trim() != key || key.Contains(','))
            return Problem(new(400, WorkDelegationCodesV1.Metadata), http, correlation);
        if (!http.Request.HasJsonContentType()) return Results.StatusCode(415);
        T request;
        try { request = await JsonSerializer.DeserializeAsync<T>(http.Request.Body, Json, token) ?? throw new JsonException(); }
        catch (JsonException) { return Problem(new(400, WorkDelegationCodesV1.Invalid), http, correlation); }
        try
        {
            var answer = await execute(request, new(key, correlation.CorrelationId, correlation.CausationId));
            if (answer.Replayed)
            {
                http.Response.Headers["Idempotency-Replayed"] = "true";
                http.Response.Headers["Idempotency-Original-Correlation-Id"] = answer.OriginalCorrelationId;
            }
            return Results.Json(answer.Delegation, statusCode: status);
        }
        catch (WorkDelegationException error) { return Problem(error, http, correlation); }
    }

    private static IResult Problem(WorkDelegationException error, HttpContext http, ICorrelationContext correlation)
    {
        if (error.IsReplay && error.OriginalCorrelationId is { } original)
        {
            http.Response.Headers["Idempotency-Replayed"] = "true";
            http.Response.Headers["Idempotency-Original-Correlation-Id"] = original;
        }
        return error.Status is 401 or 403 or 404 or 503 ? Results.Problem(statusCode: error.Status)
            : Results.Problem(statusCode: error.Status, extensions: new Dictionary<string, object?>
                { ["code"] = error.Code, ["correlationId"] = correlation.IsResolved ? correlation.CorrelationId : null });
    }

    private static void NoStore(HttpContext http)
    {
        http.Response.Headers.CacheControl = "private, no-store";
        http.Response.Headers.Pragma = "no-cache";
    }
}

public sealed record WorkDelegationPersonV1(Guid UserId, string DisplayName);
public sealed record WorkDelegationPeopleV1(Guid CompanyId, string Kind, IReadOnlyList<WorkDelegationPersonV1> Items, bool HasMore);
