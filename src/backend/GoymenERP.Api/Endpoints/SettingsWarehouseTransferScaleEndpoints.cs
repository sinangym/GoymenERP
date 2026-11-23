using System.Globalization;
using GoymenERP.Api.Errors;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.Endpoints;

/// <summary>LOJ-D11: real Organization candidates and append-only warehouse policy. Registration grants nothing.</summary>
public static class SettingsWarehouseTransferScaleEndpoints
{
    public const string BasePath = "/api/v1/settings/warehouse-transfer-scale";
    public const string InvalidRequest = "settings_warehouse_transfer_scale_request_invalid";
    public const string VersionConflict = "settings_warehouse_transfer_scale_version_conflict";
    public const string KeyConflict = "settings_warehouse_transfer_scale_idempotency_conflict";

    public static IEndpointRouteBuilder MapSettingsWarehouseTransferScaleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath).WithTags("Settings").RequireAuthorization();
        Common(group.MapGet("/warehouses", CandidatesAsync).WithName("GetSettingsWarehouseTransferScaleCandidatesV1"))
            .Produces<WarehouseTransferScaleCandidatesResponseV1>();
        Common(group.MapGet("/{locationId:guid}", ReadAsync).WithName("GetSettingsWarehouseTransferScaleV1"))
            .Produces<WarehouseTransferScaleViewV1>().ProducesErpCodeLessNotFound();
        Common(group.MapPost("/{locationId:guid}/versions", RecordAsync).WithName("RecordSettingsWarehouseTransferScaleV1"))
            .Accepts<RecordWarehouseTransferScaleV1>("application/json").Produces(204)
            .ProducesErpBadRequest(CompanyPolicyProblemCodesV1.CommandMetadataInvalid).AllowsCodeLessErpBadRequestBody()
            .ProducesErpConflict(VersionConflict, KeyConflict).ProducesErpCodeLessNotFound().ProducesErpUnsupportedMediaType()
            .WithIdempotencyContract(204, 409);
        return endpoints;
    }
    private static RouteHandlerBuilder Common(RouteHandlerBuilder route) => route
        .RequireCompanyContext().RequireAuthorization(SettingsWarehouseTransferScaleV1.Manage)
        .WithMetadata(new PrivateNoStoreOnAuthorizationFailureMetadata())
        .ProducesErpBadRequest(InvalidRequest).ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

    internal static async Task<IResult> CandidatesAsync(HttpContext context, ICompanyContext company, ICurrentUser user,
        IErpAuthorityRevalidationService authority, IOrganizationLocationCandidatesV1 source, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        var companyId = company.IsResolved ? company.CompanyId : Guid.Empty;
        var actor = user.UserId;
        try
        {
            await Fence();
            if (!TryQuery(context.Request.Query, out var query)) return Problem(400, InvalidRequest);
            var result = await source.SearchAsync(companyId, query, ct);
            await Fence();
            if (result.Items is null || result.Items.Count > query.Limit || result.SearchWindowFull && result.Items.Count != query.Limit
                || result.Items.Select(x => x.LocationId).Distinct().Count() != result.Items.Count
                || result.Items.Any(x => x.LocationId == Guid.Empty || x.SiteId == Guid.Empty || !Text(x.LocationCode, 200)
                    || !Text(x.LocationName, 2000) || !Text(x.SiteCode, 200) || !Text(x.SiteName, 2000))) return Unavailable();
            return Results.Ok(new WarehouseTransferScaleCandidatesResponseV1(companyId, query.Q, query.Limit, result.Items, result.SearchWindowFull));
        }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException) { return Results.Problem(statusCode: 403); }
        catch (OrganizationLocationCandidateQueryException) { return Problem(400, InvalidRequest); }
        catch
        {
            try { await Fence(); return Unavailable(); }
            catch (OperationCanceledException) { throw; }
            catch (UnauthorizedAccessException) { return Results.Problem(statusCode: 403); }
            catch { return Unavailable(); }
        }

        async Task Fence()
        {
            ct.ThrowIfCancellationRequested();
            if (!Same(company, user, companyId, actor)) throw new UnauthorizedAccessException();
            var grant = await authority.RevalidatePermissionScopeAsync(SettingsWarehouseTransferScaleV1.Manage, companyId, ct);
            if (!Same(company, user, companyId, actor) || !grant.IsGranted || !grant.IsCompanyWide) throw new UnauthorizedAccessException();
        }
    }
    internal static bool TryQuery(IQueryCollection values, out OrganizationLocationCandidateQueryV1 query)
    {
        query = new();
        if (values.Any(x => x.Key is not ("q" or "limit") || x.Value.Count != 1)) return false;
        var limit = 25;
        if (values.TryGetValue("limit", out var raw) && (!int.TryParse(raw[0], NumberStyles.None, CultureInfo.InvariantCulture, out limit)
            || raw[0] != limit.ToString(CultureInfo.InvariantCulture))) return false;
        var q = values.TryGetValue("q", out var value) ? value[0]?.Trim() : null;
        if (limit is < 1 or > 100 || q?.Length > 200 || q?.Any(char.IsControl) == true) return false;
        query = new(string.IsNullOrWhiteSpace(q) ? null : q, limit);
        return true;
    }
    internal static async Task<IResult> ReadAsync(Guid locationId, HttpContext context, ICompanyContext company, ICurrentUser user,
        ISettingsWarehouseTransferScaleAdministrationV1 service, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (locationId == Guid.Empty || context.Request.Query.Count != 0) return Problem(400, InvalidRequest);
        var companyId = company.IsResolved ? company.CompanyId : Guid.Empty;
        var actor = user.UserId;
        if (!Same(company, user, companyId, actor)) return Results.Problem(statusCode: 403);
        try
        {
            var result = await service.ReadAsync(locationId, ct);
            if (!Same(company, user, companyId, actor)) return Results.Problem(statusCode: 403);
            return result.CompanyId == companyId && result.LocationId == locationId && result.Policy.CompanyId == companyId
                && result.Policy.LocationId == locationId ? Results.Ok(result) : Unavailable();
        }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException) { return Results.Problem(statusCode: 403); }
        catch (SettingsPrerequisiteException exception) { return Prerequisite(exception); }
        catch { return Unavailable(); }
    }
    internal static async Task<IResult> RecordAsync(Guid locationId, RecordWarehouseTransferScaleV1 request, HttpContext context,
        ICompanyContext company, ICurrentUser user, ICorrelationContext correlation,
        ISettingsWarehouseTransferScaleAdministrationV1 service, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (locationId == Guid.Empty || context.Request.Query.Count != 0) return Problem(400, InvalidRequest);
        if (!TryMetadata(context, company, user, correlation, out var metadata)) return Problem(400, CompanyPolicyProblemCodesV1.CommandMetadataInvalid);
        try
        {
            var result = await service.RecordAsync(locationId, request, metadata, ct);
            if (!Same(company, user, metadata.CompanyId, metadata.ActorId)) return Results.Problem(statusCode: 403);
            var answer = result.Answer;
            if (!(answer.StatusCode == 204 && answer.Code is null || answer.StatusCode == 409 && answer.Code is VersionConflict or KeyConflict
                || answer.StatusCode == 400 && answer.Code == InvalidRequest)
                || !Text(result.OriginalCorrelationId, 128)) return Unavailable();
            if (result.IsReplay)
            {
                context.Response.Headers[ErpOpenApiContract.IdempotencyReplayedHeaderName] = "true";
                context.Response.Headers[ErpOpenApiContract.OriginalCorrelationHeaderName] = result.OriginalCorrelationId;
            }
            return answer.StatusCode == 204 ? Results.NoContent() : Problem(answer.StatusCode, answer.Code!);
        }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException) { return Results.Problem(statusCode: 403); }
        catch (SettingsPrerequisiteException exception) { return Prerequisite(exception); }
        catch { return Unavailable(); }
    }
    internal static bool TryMetadata(HttpContext context, ICompanyContext company, ICurrentUser user,
        ICorrelationContext correlation, out SettingsCommandMetadataV1 metadata)
    {
        metadata = null!;
        if (!Same(company, user, company.IsResolved ? company.CompanyId : Guid.Empty, user.UserId) || !correlation.IsResolved
            || !context.Request.Headers.TryGetValue("Idempotency-Key", out var raw) || raw.Count != 1
            || raw[0]?.Trim() is not { Length: > 0 and <= 128 } key || key.Contains(',') || key.Any(char.IsControl)) return false;
        metadata = new(company.CompanyId, user.UserId!, correlation.CorrelationId, correlation.CausationId,
            correlation.SourceChannel, correlation.ClientIpAddress, correlation.UserAgent, key);
        return true;
    }
    private static bool Same(ICompanyContext company, ICurrentUser user, Guid id, string? actor) => id != Guid.Empty
        && company.IsResolved && company.CompanyId == id && user.IsAuthenticated && !string.IsNullOrWhiteSpace(actor)
        && string.Equals(actor, user.UserId, StringComparison.Ordinal) && user.CanAccessCompany(id);
    private static bool Text(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max && !value.Any(char.IsControl);
    private static IResult Prerequisite(SettingsPrerequisiteException exception) => exception.Code == CompanyPolicyProblemCodesV1.AccessDenied
        ? Results.Problem(statusCode: 403) : exception.Code == CompanyPolicyProblemCodesV1.RequestInvalid ? Problem(400, InvalidRequest) : Unavailable();
    private static IResult Problem(int status, string code) => Results.Problem(statusCode: status, title: "Depo transfer tartısı ayarı tamamlanamadı.",
        extensions: new Dictionary<string, object?> { ["code"] = code });
    private static IResult Unavailable() => Results.Problem(statusCode: 503, title: "Depo transfer tartısı ayarı şu anda kullanılamıyor.");
}

public sealed record WarehouseTransferScaleCandidatesResponseV1(Guid CompanyId, string? Q, int Limit,
    IReadOnlyList<OrganizationLocationCandidateV1> Items, bool SearchWindowFull);
