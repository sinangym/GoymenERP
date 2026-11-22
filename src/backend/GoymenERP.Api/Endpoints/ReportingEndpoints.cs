using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Reporting.Domain;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// WP14 report centre (DALGA_PLANI_v2 1.15, YOL-D13, ROL-D13, ROL-D15). Reads take the report's parameters as single query
/// values (every name once; <c>page</c> and <c>pageSize</c> are the paging names); exports and the auditor package are POSTs
/// because each writes an export record and an audit event before the file leaves. Saved-view commands take exactly one
/// Idempotency-Key and a strict JSON body; a replay answers with <c>Idempotency-Replayed</c>. 401/403/404 are code-less.
/// </summary>
public static class ReportingEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public static IEndpointRouteBuilder MapReportingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("").WithTags("Reporting").RequireAuthorization().RequireCompanyContext();

        group.MapGet(ReportingApiV1.ReportsPath, (HttpContext http, [FromServices] IReportingServiceV1 service, ICorrelationContext correlation,
                CancellationToken token) => ReadAsync(http, correlation, () => service.ListReportsAsync(token)))
            .WithName("ListReportingReportsV1").Produces<ReportingCatalogResponseV1>()
            .ProducesErpServiceUnavailable(ReportingProblemCodesV1.PermissionUnregistered).ProducesErpAuthenticatedProblems();

        group.MapGet(ReportingApiV1.ReportPath, (string reportId, HttpContext http, [FromServices] IReportingServiceV1 service,
                ICorrelationContext correlation, CancellationToken token) =>
            {
                if (!TryReadQuery(http.Request.Query, out var parameters, out var page, out var pageSize))
                    return Task.FromResult(Problem(new ReportingException(400, ReportingProblemCodesV1.ParameterUnknown), correlation));
                return ReadAsync(http, correlation, () => service.RunAsync(reportId, parameters, page, pageSize, token));
            })
            .WithName("RunReportingReportV1").Produces<ReportingTableV1>()
            .ProducesErpBadRequest(ReportingProblemCodesV1.ParameterUnknown, ReportingProblemCodesV1.PageInvalid)
            .ProducesErpUnprocessableEntity(ReportingProblemCodesV1.ParameterRequired, ReportingProblemCodesV1.ParameterInvalid)
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable(ReportingProblemCodesV1.SourceUnavailable, ReportingProblemCodesV1.SourceNotConnected,
                ReportingProblemCodesV1.TimeZoneUnavailable, ReportingProblemCodesV1.PermissionUnregistered)
            .ProducesErpAuthenticatedProblems();

        group.MapPost(ReportingApiV1.ExportPath, async (string reportId, HttpContext http, [FromServices] IReportingServiceV1 service,
                ICorrelationContext correlation, CancellationToken token) =>
            {
                NoStore(http.Response);
                if (!http.Request.HasJsonContentType()) return Results.StatusCode(415);
                ReportingExportRequestV1 request;
                try { request = await JsonSerializer.DeserializeAsync<ReportingExportRequestV1>(http.Request.Body, Json, token) ?? throw new JsonException(); }
                catch (JsonException) { return Problem(new ReportingException(400, ReportingProblemCodesV1.ParameterInvalid), correlation); }
                try { return File(http, await service.ExportAsync(reportId, request, token)); }
                catch (ReportingException exception) { return Problem(exception, correlation); }
            })
            .WithName("ExportReportingReportV1").Accepts<ReportingExportRequestV1>("application/json")
            .Produces<Stream>(StatusCodes.Status200OK, "text/csv", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            .ProducesErpBadRequest(ReportingProblemCodesV1.ParameterUnknown, ReportingProblemCodesV1.ParameterInvalid,
                ReportingProblemCodesV1.PageInvalid, ReportingProblemCodesV1.FormatInvalid)
            .ProducesErpUnprocessableEntity(ReportingProblemCodesV1.ParameterRequired, ReportingProblemCodesV1.ParameterInvalid)
            .ProducesErpUnsupportedMediaType().ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable(ReportingProblemCodesV1.SourceUnavailable, ReportingProblemCodesV1.SourceNotConnected,
                ReportingProblemCodesV1.TimeZoneUnavailable, ReportingProblemCodesV1.PermissionUnregistered)
            .ProducesErpAuthenticatedProblems();

        group.MapPost(ReportingApiV1.AuditorPackagePath, async (HttpContext http, [FromServices] IReportingServiceV1 service,
                ICorrelationContext correlation, CancellationToken token) =>
            {
                NoStore(http.Response);
                if (!http.Request.HasJsonContentType()) return Results.StatusCode(415);
                ReportingAuditorPackageRequestV1 request;
                try { request = await JsonSerializer.DeserializeAsync<ReportingAuditorPackageRequestV1>(http.Request.Body, Json, token) ?? throw new JsonException(); }
                catch (JsonException) { return Problem(new ReportingException(400, ReportingProblemCodesV1.ParameterInvalid), correlation); }
                try { return File(http, await service.BuildAuditorPackageAsync(request, token)); }
                catch (ReportingException exception) { return Problem(exception, correlation); }
            })
            .WithName("BuildReportingAuditorPackageV1").Accepts<ReportingAuditorPackageRequestV1>("application/json")
            .Produces<Stream>(StatusCodes.Status200OK, "application/zip")
            .ProducesErpBadRequest(ReportingProblemCodesV1.ParameterInvalid)
            .ProducesErpUnprocessableEntity(ReportingProblemCodesV1.ParameterRequired, ReportingProblemCodesV1.ParameterInvalid)
            .ProducesErpUnsupportedMediaType()
            .ProducesErpServiceUnavailable(ReportingProblemCodesV1.SourceUnavailable, ReportingProblemCodesV1.TimeZoneUnavailable,
                ReportingProblemCodesV1.PermissionUnregistered)
            .ProducesErpAuthenticatedProblems();

        group.MapGet(ReportingApiV1.SavedViewsPath, (string? reportId, HttpContext http, [FromServices] IReportingServiceV1 service,
                ICorrelationContext correlation, CancellationToken token) =>
            ReadAsync(http, correlation, () => service.ListSavedViewsAsync(reportId ?? "", token)))
            .WithName("ListReportingSavedViewsV1").Produces<ReportingSavedViewPageV1>().ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable(ReportingProblemCodesV1.PermissionUnregistered).ProducesErpAuthenticatedProblems();

        Command(group.MapPost(ReportingApiV1.SavedViewsPath, (HttpContext http, [FromServices] IReportingServiceV1 service,
                ICorrelationContext correlation, CancellationToken token) =>
            CommandAsync<ReportingSavedViewRequestV1>(http, correlation, 201,
                (request, metadata) => service.CreateSavedViewAsync(request, metadata, token), token))
            .WithName("CreateReportingSavedViewV1").Accepts<ReportingSavedViewRequestV1>("application/json")
            .WithIdempotencyContract(201, 409).Produces<ReportingSavedViewV1>(201))
            .ProducesErpConflict(ReportingProblemCodesV1.SavedViewNameTaken, ReportingProblemCodesV1.SavedViewLimit,
                ReportingProblemCodesV1.SameKeyPayloadMismatch, ReportingProblemCodesV1.ConcurrentChange);

        Command(group.MapPut(ReportingApiV1.SavedViewPath, (Guid viewId, HttpContext http, [FromServices] IReportingServiceV1 service,
                ICorrelationContext correlation, CancellationToken token) =>
            CommandAsync<ReportingSavedViewRequestV1>(http, correlation, 200,
                (request, metadata) => service.UpdateSavedViewAsync(viewId, request, metadata, token), token))
            .WithName("UpdateReportingSavedViewV1").Accepts<ReportingSavedViewRequestV1>("application/json")
            .WithIdempotencyContract(200, 409).Produces<ReportingSavedViewV1>())
            .ProducesErpConflict(ReportingProblemCodesV1.SavedViewNameTaken, ReportingProblemCodesV1.SavedViewPassive,
                ReportingProblemCodesV1.VersionMismatch, ReportingProblemCodesV1.SameKeyPayloadMismatch, ReportingProblemCodesV1.ConcurrentChange);

        Command(group.MapPost(ReportingApiV1.SavedViewPassivatePath, (Guid viewId, HttpContext http, [FromServices] IReportingServiceV1 service,
                ICorrelationContext correlation, CancellationToken token) =>
            CommandAsync<ReportingSavedViewPassivateRequestV1>(http, correlation, 200,
                (request, metadata) => service.PassivateSavedViewAsync(viewId, request, metadata, token), token))
            .WithName("PassivateReportingSavedViewV1").Accepts<ReportingSavedViewPassivateRequestV1>("application/json")
            .WithIdempotencyContract(200, 409).Produces<ReportingSavedViewV1>())
            .ProducesErpConflict(ReportingProblemCodesV1.SavedViewPassive, ReportingProblemCodesV1.VersionMismatch,
                ReportingProblemCodesV1.SameKeyPayloadMismatch, ReportingProblemCodesV1.ConcurrentChange);
        return endpoints;
    }

    /// <summary>Every query name once; page/pageSize are whole numbers; every other name is a report parameter.</summary>
    internal static bool TryReadQuery(IQueryCollection query, out Dictionary<string, string?> parameters, out int? page, out int? pageSize)
    {
        parameters = new Dictionary<string, string?>(StringComparer.Ordinal);
        page = null;
        pageSize = null;
        foreach (var (name, values) in query)
        {
            if (values.Count != 1 || name.Length is 0 or > 64) return false;
            var value = values[0];
            if (name == "page" || name == "pageSize")
            {
                if (!int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number)) return false;
                if (name == "page") page = number;
                else pageSize = number;
                continue;
            }

            parameters[name] = value;
        }

        return true;
    }

    private static RouteHandlerBuilder Command(RouteHandlerBuilder route) => route
        .ProducesErpBadRequest(ReportingProblemCodesV1.CommandMetadataInvalid, ReportingProblemCodesV1.SavedViewInvalid)
        .AllowsEmptyErpBadRequestBody()
        .ProducesErpUnprocessableEntity(ReportingProblemCodesV1.SavedViewInvalid)
        .ProducesErpUnsupportedMediaType().ProducesErpCodeLessNotFound()
        .ProducesErpServiceUnavailable(ReportingProblemCodesV1.PermissionUnregistered).ProducesErpAuthenticatedProblems();

    private static IResult File(HttpContext http, ReportingFileV1 file)
    {
        // An attachment of the stated type only, never sniffed or rendered as a page of this origin.
        var headers = http.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        headers["X-Reporting-Export-Id"] = file.ExportId.ToString("D");
        headers["X-Reporting-Content-Sha256"] = file.ContentSha256;
        return Results.File(file.Content, file.ContentType, file.FileName);
    }

    private static async Task<IResult> ReadAsync<T>(HttpContext http, ICorrelationContext correlation, Func<Task<T>> read)
    {
        NoStore(http.Response);
        try { return Results.Json(await read()); }
        catch (ReportingException exception) { return Problem(exception, correlation); }
    }

    private static async Task<IResult> CommandAsync<TRequest>(HttpContext http, ICorrelationContext correlation, int status,
        Func<TRequest, ReportingCommandMetadataV1, Task<ReportingSavedViewResponseV1>> command, CancellationToken token)
    {
        NoStore(http.Response);
        if (!correlation.IsResolved || !http.Request.Headers.TryGetValue("Idempotency-Key", out var keys) || keys.Count != 1
            || keys[0] is not { Length: > 0 and <= 128 } key || string.IsNullOrWhiteSpace(key) || key.Trim() != key || key.Contains(','))
            return Problem(new ReportingException(400, ReportingProblemCodesV1.CommandMetadataInvalid), correlation);
        if (!http.Request.HasJsonContentType()) return Results.StatusCode(415);
        TRequest request;
        try { request = await JsonSerializer.DeserializeAsync<TRequest>(http.Request.Body, Json, token) ?? throw new JsonException(); }
        catch (JsonException) { return Problem(new ReportingException(400, ReportingProblemCodesV1.SavedViewInvalid), correlation); }
        try
        {
            var result = await command(request, new(key, correlation.CorrelationId, correlation.CausationId));
            if (result.IsReplay)
            {
                http.Response.Headers["Idempotency-Replayed"] = "true";
                http.Response.Headers["Idempotency-Original-Correlation-Id"] = result.CorrelationId;
            }

            return Results.Json(result.View, statusCode: status);
        }
        catch (ReportingException exception) { return Problem(exception, correlation, http); }
    }

    private static IResult Problem(ReportingException exception, ICorrelationContext correlation, HttpContext? http = null)
    {
        if (exception.IsReplay && http is not null && exception.OriginalCorrelationId is { } original)
        {
            http.Response.Headers["Idempotency-Replayed"] = "true";
            http.Response.Headers["Idempotency-Original-Correlation-Id"] = original;
        }

        if (exception.StatusCode is 401 or 403 or 404 && exception.Code is "unauthenticated" or "forbidden" or "not_found")
            return Results.Problem(statusCode: exception.StatusCode);
        var extensions = new Dictionary<string, object?>
        {
            ["code"] = exception.Code,
            ["correlationId"] = correlation.IsResolved ? correlation.CorrelationId : null,
        };
        if (exception.Field is not null) extensions["field"] = exception.Field;
        return Results.Problem(statusCode: exception.StatusCode, extensions: extensions);
    }

    private static void NoStore(HttpResponse response)
    {
        response.Headers.CacheControl = "private, no-store";
        response.Headers.Pragma = "no-cache";
    }
}
