using GoymenERP.Api.LogisticsRuntime;
using GoymenERP.Api.Errors;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Organization.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

public static class LogisticsTransferDepartureEndpoints
{
    public const string Path = "/api/v1/logistics/transfer-departures";
    public static IEndpointRouteBuilder MapLogisticsTransferDepartureEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(Path).WithTags("Logistics").RequireAuthorization()
            .RequireCompanyContext().RequireAuthorization(LogisticsTransferDepartureV1.RecordPermission)
            .WithMetadata(new PrivateNoStoreOnAuthorizationFailureMetadata());
        group.MapGet("/candidates", CandidatesAsync).WithName("ListLogisticsTransferDepartureCandidatesV1")
            .Produces<LogisticsTransferDepartureCandidatesV1>().ProducesErpBadRequest().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
        group.MapGet("/{transferId:guid}", ReadAsync).WithName("GetLogisticsTransferDepartureV1")
            .Produces<LogisticsTransferDepartureFactsV1>().ProducesErpBadRequest().ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable("logistics_transfer_departure_unavailable").ProducesErpAuthenticatedProblems();
        group.MapPost("/{transferId:guid}", RecordAsync).WithName("RecordLogisticsTransferDepartureV1")
            .Accepts<RecordTransferDepartureV1>("application/json").Produces<LogisticsTransferDepartureRecordedV1>()
            .WithIdempotencyContract(200, 409).ProducesErpBadRequest().AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType().ProducesErpUnprocessableEntity("logistics_transfer_departure_request_invalid")
            .ProducesErpConflict("logistics_transfer_departure_version_conflict", "logistics_transfer_departure_closed",
                "logistics_transfer_departure_already_recorded", "logistics_transfer_departure_idempotency_conflict")
            .ProducesErpServiceUnavailable("logistics_transfer_departure_unavailable").ProducesErpAuthenticatedProblems();
        return endpoints;
    }

    private static async Task<IResult> CandidatesAsync(HttpContext http, IInventoryTransferReaderV1 transfers,
        IOrganizationLocationDisplayReaderV1 locations, ICompanyContext company, ICurrentUser user,
        IErpAuthorityRevalidationService authority, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "private, no-store";
        if (http.Request.Query.Count != 0) return Results.Problem(statusCode: 400);
        var companyId = company.IsResolved ? company.CompanyId : Guid.Empty;
        string actor;
        try { actor = await FenceAsync(companyId, null, company, user, authority, cancellationToken); }
        catch (UnauthorizedAccessException) { return Results.Problem(statusCode: 403); }
        try
        {
            // Owner-only discovery under the consuming command's own permission; no extra Inventory or Organization grant.
            var window = await transfers.ListAsync(50, cancellationToken);
            await FenceAsync(companyId, actor, company, user, authority, cancellationToken);
            if (window.CompanyId != companyId || window.Items.Count > 50 || window.Items.Any(x => x.CompanyId != companyId
                || x.TransferId == Guid.Empty || x.SourceLocationId == Guid.Empty || x.DestinationLocationId == Guid.Empty)
                || window.Items.Select(x => x.TransferId).Distinct().Count() != window.Items.Count) return Unavailable();
            var items = window.Items.Where(x => x.DispatchedAtUtc is not null).ToArray();
            var ids = items.SelectMany(x => new[] { x.SourceLocationId, x.DestinationLocationId }).Distinct().ToArray();
            var names = ids.Length == 0 ? [] : await locations.ReadAsync(companyId, ids, cancellationToken);
            await FenceAsync(companyId, actor, company, user, authority, cancellationToken);
            if (names.Count != ids.Length || names.Any(x => !ids.Contains(x.LocationId) || string.IsNullOrWhiteSpace(x.LocationCode)
                || string.IsNullOrWhiteSpace(x.LocationName)) || names.Select(x => x.LocationId).Distinct().Count() != ids.Length) return Unavailable();
            var byId = names.ToDictionary(x => x.LocationId);
            return Results.Ok(new LogisticsTransferDepartureCandidatesV1(companyId, items.Select(x => new LogisticsTransferDepartureCandidateV1(
                x.TransferId, x.Version, x.Status, x.SourceLocationId, byId[x.SourceLocationId].LocationCode, byId[x.SourceLocationId].LocationName,
                x.DestinationLocationId, byId[x.DestinationLocationId].LocationCode, byId[x.DestinationLocationId].LocationName,
                x.DispatchedAtUtc!.Value)).ToArray(), window.WindowFull));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (UnauthorizedAccessException) { return Results.Problem(statusCode: 403); }
        catch { return await FailureAsync(companyId, actor, company, user, authority, cancellationToken); }
    }

    private static async Task<IResult> ReadAsync(Guid transferId, HttpContext http, ICompanyContext company, ICurrentUser user,
        IErpAuthorityRevalidationService authority, [FromServices] ILogisticsTransferReferenceSourceV1? transfers,
        [FromServices] ILogisticsTransferDepartureOwnerV1? owner, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "private, no-store";
        if (transferId == Guid.Empty || http.Request.Query.Count != 0) return Results.Problem(statusCode: 400);
        if (owner is null || transfers is null) return Unavailable();
        var companyId = company.IsResolved ? company.CompanyId : Guid.Empty;
        string? actor = null;
        try
        {
            actor = await FenceAsync(companyId, null, company, user, authority, cancellationToken);
            var reference = await transfers.ReadAsync(companyId, transferId, cancellationToken);
            await FenceAsync(companyId, actor, company, user, authority, cancellationToken);
            if (reference is null) return Results.Problem(statusCode: 404);
            if (reference.DispatchedAtUtc is not { } at || reference.CompanyId != companyId || reference.TransferId != transferId) return Unavailable();
            var result = await owner.ReadAsync(new(companyId, transferId, reference.SourceLocationId, at), cancellationToken);
            await FenceAsync(companyId, actor, company, user, authority, cancellationToken);
            return Results.Ok(result);
        }
        catch (UnauthorizedAccessException) { return Results.Problem(statusCode: 403); }
        catch (LogisticsTransferDepartureException exception) { return Problem(exception); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return await FailureAsync(companyId, actor, company, user, authority, cancellationToken); }
    }

    private static async Task<IResult> RecordAsync(Guid transferId, RecordTransferDepartureV1 request, HttpContext http,
        ICompanyContext company, ICurrentUser user, IErpAuthorityRevalidationService authority, ICorrelationContext correlation,
        [FromServices] ILogisticsTransferDepartureOwnerV1? owner, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "private, no-store";
        if (http.Request.Query.Count != 0 || transferId == Guid.Empty || !correlation.IsResolved
            || !http.Request.Headers.TryGetValue("Idempotency-Key", out var values) || values.Count != 1
            || values[0]?.Trim() is not { Length: > 0 and <= 128 } key || key.Contains(',', StringComparison.Ordinal))
            return Results.Problem(statusCode: 400);
        if (owner is null) return Unavailable();
        var companyId = company.IsResolved ? company.CompanyId : Guid.Empty;
        string? actor = null;
        try
        {
            actor = await FenceAsync(companyId, null, company, user, authority, cancellationToken);
            var permission = await authority.RevalidatePermissionScopeAsync(LogisticsTransferDepartureV1.RecordPermission, companyId, cancellationToken);
            await FenceAsync(companyId, actor, company, user, authority, cancellationToken);
            var envelope = new LogisticsWriteEnvelope(actor, LogisticsTransferDepartureV1.RecordPermission, permission.RoleCodes,
                correlation.CorrelationId, correlation.CausationId, key, correlation.SourceChannel, correlation.ClientIpAddress, correlation.UserAgent);
            var result = await owner.RecordAsync(transferId, request, envelope,
                async token => { await FenceAsync(companyId, actor, company, user, authority, token); }, cancellationToken);
            await FenceAsync(companyId, actor, company, user, authority, cancellationToken);
            if (result.IsReplay)
            {
                http.Response.Headers["Idempotency-Replayed"] = "true";
                http.Response.Headers["Idempotency-Original-Correlation-Id"] = result.OriginalCorrelationId;
            }
            return Results.Ok(result);
        }
        catch (UnauthorizedAccessException) { return Results.Problem(statusCode: 403); }
        catch (LogisticsTransferDepartureException exception) { return Problem(exception); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return await FailureAsync(companyId, actor, company, user, authority, cancellationToken); }
    }

    private static async Task<string> FenceAsync(Guid companyId, string? expectedActor, ICompanyContext company, ICurrentUser user,
        IErpAuthorityRevalidationService authority, CancellationToken token)
    {
        var actor = TransferHostFence.Member(company, user, companyId, token);
        if (expectedActor is not null && expectedActor != actor) throw new UnauthorizedAccessException();
        var scope = await authority.RevalidatePermissionScopeAsync(LogisticsTransferDepartureV1.RecordPermission, companyId, token);
        TransferHostFence.Still(company, user, companyId, actor, token);
        if (!scope.IsGranted || !scope.IsCompanyWide) throw new UnauthorizedAccessException();
        return actor;
    }
    private static IResult Problem(LogisticsTransferDepartureException exception) => Results.Problem(statusCode: exception.Status,
        extensions: new Dictionary<string, object?> { ["code"] = exception.Code });
    private static async Task<IResult> FailureAsync(Guid companyId, string? actor, ICompanyContext company, ICurrentUser user,
        IErpAuthorityRevalidationService authority, CancellationToken token)
    {
        try { await FenceAsync(companyId, actor, company, user, authority, token); return Unavailable(); }
        catch (UnauthorizedAccessException) { return Results.Problem(statusCode: 403); }
    }
    private static IResult Unavailable() => Problem(new(503, "logistics_transfer_departure_unavailable"));
}

public sealed record LogisticsTransferDepartureCandidateV1(Guid TransferId, string Version, string Status, Guid SourceLocationId,
    string SourceLocationCode, string SourceLocationName, Guid DestinationLocationId, string DestinationLocationCode,
    string DestinationLocationName, DateTimeOffset DispatchedAtUtc);
public sealed record LogisticsTransferDepartureCandidatesV1(Guid CompanyId, IReadOnlyList<LogisticsTransferDepartureCandidateV1> Items,
    bool WindowFull);
