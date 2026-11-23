using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.Errors;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Logistics.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

/// <summary>Actual internal transfer document workflow, using its existing owner command bindings. No GİB transport.</summary>
public static class LogisticsTransferOutgoingDocumentEndpoints
{
    public const string Path = "/api/v1/logistics/transfer-outgoing-documents";
    public static IEndpointRouteBuilder MapLogisticsTransferOutgoingDocumentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(Path).WithTags("Logistics").RequireAuthorization().RequireCompanyContext()
            .WithMetadata(new PrivateNoStoreOnAuthorizationFailureMetadata());
        group.MapGet("/by-transfer/{transferId:guid}", ReadAsync)
            .RequireAuthorization(LogisticsTransferOutgoingDocumentPermissionsV1.Read).WithName("GetLogisticsTransferOutgoingDocumentByTransferV1")
            .Produces<TransferOutgoingDocumentForTransferV1>().ProducesErpBadRequest().ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
        group.MapPost("", (CreateTransferOutgoingDocumentV1 request, HttpContext http, ICorrelationContext correlation,
            ILogisticsTransferOutgoingDocumentScopeV1 scopes, [FromServices] ILogisticsTransferOutgoingDocumentServiceV1? service, CancellationToken token) =>
            ExecuteAsync(http, correlation, scopes, LogisticsPermissionManifestV1.DispatchNotesCreate,
                service is null ? null : metadata => service.CreateDraftAsync(request, metadata, token), true, token))
            .RequireAuthorization(LogisticsPermissionManifestV1.DispatchNotesCreate).WithName("CreateLogisticsTransferOutgoingDocumentV1")
            .Accepts<CreateTransferOutgoingDocumentV1>("application/json").Produces<TransferOutgoingDocumentResponseV1>(201)
            .WithIdempotencyContract(201, 409).ProducesErpBadRequest().AllowsCodeLessErpBadRequestBody().ProducesErpUnsupportedMediaType()
            .ProducesErpUnprocessableEntity("validation_failed").ProducesErpConflict(LogisticsStoreCodes.SameKeyPayloadMismatch,
                LogisticsTransferOutgoingDocumentCodesV1.TransferAlreadyBound).ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
        group.MapPost("/{documentId:guid}/finalize", (Guid documentId, FinalizeTransferOutgoingDocumentV1 request,
            HttpContext http, ICorrelationContext correlation, ILogisticsTransferOutgoingDocumentScopeV1 scopes,
            [FromServices] ILogisticsTransferOutgoingDocumentServiceV1? service,
            CancellationToken token) => ExecuteAsync(http, correlation, scopes, LogisticsPermissionManifestV1.DispatchNotesApprove,
                service is null ? null : metadata => service.FinalizeAsync(documentId, request, metadata, token), false, token))
            .RequireAuthorization(LogisticsPermissionManifestV1.DispatchNotesApprove).WithName("FinalizeLogisticsTransferOutgoingDocumentV1")
            .Accepts<FinalizeTransferOutgoingDocumentV1>("application/json").Produces<TransferOutgoingDocumentResponseV1>()
            .WithIdempotencyContract(200, 409).ProducesErpBadRequest().AllowsCodeLessErpBadRequestBody().ProducesErpUnsupportedMediaType()
            .ProducesErpUnprocessableEntity("validation_failed").ProducesErpNotFound(LogisticsTransferOutgoingDocumentCodesV1.NotFound)
            .ProducesErpConflict(LogisticsStoreCodes.SameKeyPayloadMismatch, LogisticsTransferOutgoingDocumentCodesV1.VersionConflict,
                LogisticsTransferOutgoingDocumentCodesV1.State).ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
        group.MapPost("/{documentId:guid}/cancel", (Guid documentId, CancelTransferOutgoingDocumentV1 request,
            HttpContext http, ICorrelationContext correlation, ILogisticsTransferOutgoingDocumentScopeV1 scopes,
            [FromServices] ILogisticsTransferOutgoingDocumentServiceV1? service,
            CancellationToken token) => ExecuteAsync(http, correlation, scopes, LogisticsPermissionManifestV1.DispatchNotesCancel,
                service is null ? null : metadata => service.CancelAsync(documentId, request, metadata, token), false, token))
            .RequireAuthorization(LogisticsPermissionManifestV1.DispatchNotesCancel).WithName("CancelLogisticsTransferOutgoingDocumentV1")
            .Accepts<CancelTransferOutgoingDocumentV1>("application/json").Produces<TransferOutgoingDocumentResponseV1>()
            .WithIdempotencyContract(200, 409).ProducesErpBadRequest().AllowsCodeLessErpBadRequestBody().ProducesErpUnsupportedMediaType()
            .ProducesErpUnprocessableEntity("validation_failed").ProducesErpNotFound(LogisticsTransferOutgoingDocumentCodesV1.NotFound)
            .ProducesErpConflict(LogisticsStoreCodes.SameKeyPayloadMismatch, LogisticsTransferOutgoingDocumentCodesV1.VersionConflict,
                LogisticsTransferOutgoingDocumentCodesV1.State).ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
        return endpoints;
    }

    private static async Task<IResult> ReadAsync(Guid transferId, HttpContext http,
        ILogisticsTransferOutgoingDocumentScopeV1 scopes, [FromServices] ILogisticsTransferOutgoingDocumentReaderV1? reader,
        CancellationToken token)
    {
        http.Response.Headers.CacheControl = "private, no-store";
        if (transferId == Guid.Empty || http.Request.Query.Count != 0) return Results.Problem(statusCode: 400);
        if (reader is null) return Results.Problem(statusCode: 503);
        LogisticsTransferOutgoingDocumentScopeV1? scope = null;
        try
        {
            scope = await scopes.RequireAsync(LogisticsTransferOutgoingDocumentPermissionsV1.Read, token);
            var value = await reader.ReadByTransferAsync(transferId, token);
            await scopes.RequireStillAsync(scope, LogisticsTransferOutgoingDocumentPermissionsV1.Read, token);
            if (value is null) return Results.Problem(statusCode: 404);
            if (value.Document.CompanyId != scope.CompanyId || value.Document.TransferId != transferId) return Results.Problem(statusCode: 503);
            return Results.Ok(value);
        }
        catch (LogisticsTransferOutgoingDocumentServiceException exception) { return Problem(exception); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch
        {
            if (scope is not null)
            {
                try { await scopes.RequireStillAsync(scope, LogisticsTransferOutgoingDocumentPermissionsV1.Read, token); }
                catch (LogisticsTransferOutgoingDocumentServiceException exception) { return Problem(exception); }
            }
            return Results.Problem(statusCode: 503);
        }
    }

    private static async Task<IResult> ExecuteAsync(HttpContext http, ICorrelationContext correlation,
        ILogisticsTransferOutgoingDocumentScopeV1 scopes, string permission,
        Func<LogisticsCommandMetadata, Task<TransferOutgoingDocumentResponseV1>>? execute, bool created, CancellationToken token)
    {
        http.Response.Headers.CacheControl = "private, no-store";
        if (http.Request.Query.Count != 0 || !correlation.IsResolved
            || !http.Request.Headers.TryGetValue("Idempotency-Key", out var values) || values.Count != 1
            || values[0]?.Trim() is not { Length: > 0 and <= 128 } key || key.Contains(',', StringComparison.Ordinal))
            return Results.Problem(statusCode: 400);
        if (execute is null) return Results.Problem(statusCode: 503);
        LogisticsTransferOutgoingDocumentScopeV1? original = null;
        try
        {
            original = await scopes.RequireAsync(permission, token);
            var result = await execute(new(key, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow));
            await scopes.RequireStillAsync(original, permission, token);
            if (result.IsReplay)
            {
                http.Response.Headers["Idempotency-Replayed"] = "true";
                http.Response.Headers["Idempotency-Original-Correlation-Id"] = result.OriginalCorrelationId;
            }
            return created ? Results.Created(Path + "/by-transfer/" + result.Document.TransferId.ToString("D"), result) : Results.Ok(result);
        }
        catch (LogisticsTransferOutgoingDocumentServiceException exception)
        {
            var failedFence = await FinalFenceAsync(original, scopes, permission, token);
            return failedFence ?? Problem(exception);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { return await FinalFenceAsync(original, scopes, permission, token) ?? Results.Problem(statusCode: 503); }
    }
    private static async Task<IResult?> FinalFenceAsync(LogisticsTransferOutgoingDocumentScopeV1? original,
        ILogisticsTransferOutgoingDocumentScopeV1 scopes, string permission, CancellationToken token)
    {
        if (original is null) return null;
        try { await scopes.RequireStillAsync(original, permission, token); return null; }
        catch (LogisticsTransferOutgoingDocumentServiceException exception) { return Problem(exception); }
    }
    private static IResult Problem(LogisticsTransferOutgoingDocumentServiceException exception) => Results.Problem(statusCode: exception.Status,
        extensions: new Dictionary<string, object?> { ["code"] = exception.Code });
}
