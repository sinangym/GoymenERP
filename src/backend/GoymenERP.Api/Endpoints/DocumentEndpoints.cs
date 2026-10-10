using System.Text.Json;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Documents.Domain;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// The document vault's routes (ERP-000G core, AT-065; the Logistics routes' shape). The route reads nothing but the
/// server context, the headers and the wire body; <see cref="IDocumentService"/> decides, with a fresh central
/// measurement of the vault's own name at every fence and the owner record's read on top.
/// <list type="bullet">
/// <item><c>POST /api/v1/documents</c> — multipart/form-data with exactly <c>ownerKind</c>, <c>ownerId</c> and one
/// <c>file</c>; one canonical <c>Idempotency-Key</c>. 201 with the stored document and the AT-057 duplicate warning; the
/// same key answers the same 201 with the replay headers.</item>
/// <item><c>GET /api/v1/documents?ownerKind&amp;ownerId</c> — the owner record's documents, newest first, and whether the
/// actor may upload now.</item>
/// <item><c>GET /api/v1/documents/{id}/content</c> — the verified bytes of a clean version as an attachment, never
/// rendered inline (nosniff, sandbox CSP, no-store). A NotScanned version is 409 <c>document_not_scanned</c>.</item>
/// </list>
/// No delete, rename or overwrite route exists: retention/deletion is undecided (OPEN-018/021) and a document is never
/// overwritten (DEC-DOC-003). Problems carry <c>code</c>, <c>correlationId</c> and field <c>errors</c>; platform
/// 401/403/404 and every 503 stay code-less.
/// </summary>
public static class DocumentEndpoints
{
    public const string DocumentsPath = DocumentsApiV1.DocumentsPath;
    private const int MaximumIdempotencyKeyLength = 128;
    private const int MaximumOwnerKindLength = 64;

    /// <summary>The multipart framing allowed on top of the file bound (boundaries, headers and the two small fields).</summary>
    private const long MultipartOverheadBytes = 64 * 1024;

    private static readonly HashSet<string> ListQueryNames = new(["ownerKind", "ownerId"], StringComparer.Ordinal);
    private static readonly HashSet<string> FormFieldNames = new([DocumentsApiV1.OwnerKindField, DocumentsApiV1.OwnerIdField],
        StringComparer.Ordinal);
    private static readonly HashSet<string> VersionFormFieldNames = new([DocumentsApiV1.ExpectedCurrentVersionField], StringComparer.Ordinal);
    private static readonly HashSet<string> VersionListQueryNames = new(["pageSize", "cursor"], StringComparer.Ordinal);
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(DocumentsPath)
            .WithTags("Documents")
            .RequireAuthorization()
            .RequireCompanyContext();

        group.MapPost("/", UploadAsync)
            .WithName("UploadDocumentV1")
            .WithIdempotencyContract(DocumentsApiV1.CreatedStatus)
            .Accepts<DocumentUploadFormV1>("multipart/form-data")
            .Produces<DocumentUploadResponse>(DocumentsApiV1.CreatedStatus)
            .ProducesErpBadRequest(DocumentProblemCodesV1.ValidationFailed, DocumentProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .Produces(StatusCodes.Status413PayloadTooLarge)
            .ProducesErpConflict(DocumentProblemCodesV1.SameKeyPayloadMismatch, DocumentProblemCodesV1.ConcurrentChange)
            .ProducesErpUnprocessableEntity(DocumentRuleCodes.Empty, DocumentRuleCodes.TooLarge,
                DocumentRuleCodes.TypeNotAllowed, DocumentRuleCodes.ContentMismatch, DocumentProblemCodesV1.ContentRejected)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/", async ([FromQuery] string? ownerKind, [FromQuery] Guid? ownerId, HttpContext httpContext,
            [FromServices] IDocumentService service, ICorrelationContext correlation, CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (!httpContext.Request.Query.All(pair => ListQueryNames.Contains(pair.Key) && pair.Value.Count == 1)
                || string.IsNullOrWhiteSpace(ownerKind) || ownerKind.Length > MaximumOwnerKindLength || ownerId is null)
            {
                return Problem(StatusCodes.Status400BadRequest, DocumentProblemCodesV1.ValidationFailed, correlation);
            }

            try
            {
                return Results.Json(await service.ListAsync(ownerKind, ownerId.Value, cancellationToken), Json);
            }
            catch (DocumentServiceException exception)
            {
                return Problem(exception, correlation);
            }
        })
            .WithName("GetDocumentsV1")
            .Produces<DocumentListResponse>()
            .ProducesErpBadRequest(DocumentProblemCodesV1.ValidationFailed)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(DocumentProblemCodesV1.ConcurrentChange)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/{id:guid}/content", async (Guid id, HttpContext httpContext, [FromServices] IDocumentService service,
            ICorrelationContext correlation, CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (httpContext.Request.Query.Count > 0)
            {
                return Problem(StatusCodes.Status400BadRequest, DocumentProblemCodesV1.ValidationFailed, correlation);
            }

            DocumentContentV1 content;
            try
            {
                content = await service.GetContentAsync(id, cancellationToken);
            }
            catch (DocumentServiceException exception)
            {
                return Problem(exception, correlation);
            }

            // Never rendered by the browser as a page of this origin: an attachment, the stored type only, no sniffing,
            // a sandboxed empty policy; the digest lets the client verify the bytes it saved.
            var headers = httpContext.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
            headers["Repr-Digest"] = $"sha-256=:{Convert.ToBase64String(Convert.FromHexString(content.Sha256))}:";
            return Results.File(content.Content, content.ContentType, content.FileName);
        })
            .WithName("GetDocumentContentV1")
            .Produces<Stream>(StatusCodes.Status200OK, "application/pdf", "image/png", "image/jpeg")
            .ProducesErpBadRequest(DocumentProblemCodesV1.ValidationFailed)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(DocumentProblemCodesV1.ConcurrentChange, DocumentProblemCodesV1.NotScanned)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/{id:guid}/versions", AppendVersionAsync)
            .WithName("AppendDocumentVersionV1")
            .WithIdempotencyContract(DocumentsApiV1.CreatedStatus)
            .Accepts<DocumentVersionUploadFormV1>("multipart/form-data")
            .Produces<DocumentVersionWriteResponse>(DocumentsApiV1.CreatedStatus)
            .ProducesErpBadRequest(DocumentProblemCodesV1.ValidationFailed, DocumentProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .Produces(StatusCodes.Status413PayloadTooLarge)
            .ProducesErpConflict(DocumentProblemCodesV1.SameKeyPayloadMismatch, DocumentProblemCodesV1.ConcurrentChange)
            .ProducesErpUnprocessableEntity(DocumentRuleCodes.Empty, DocumentRuleCodes.TooLarge,
                DocumentRuleCodes.TypeNotAllowed, DocumentRuleCodes.ContentMismatch, DocumentProblemCodesV1.ContentRejected)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/{id:guid}/versions", async (Guid id, [FromQuery] int? pageSize, [FromQuery] string? cursor,
            HttpContext httpContext, [FromServices] IDocumentService service, ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (!httpContext.Request.Query.All(pair => VersionListQueryNames.Contains(pair.Key) && pair.Value.Count == 1))
            {
                return Problem(StatusCodes.Status400BadRequest, DocumentProblemCodesV1.ValidationFailed, correlation);
            }

            try
            {
                return Results.Json(await service.ListVersionsAsync(id, pageSize, cursor, cancellationToken), Json);
            }
            catch (DocumentServiceException exception)
            {
                return Problem(exception, correlation);
            }
        })
            .WithName("GetDocumentVersionsV1")
            .Produces<DocumentVersionHistoryResponse>()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(DocumentProblemCodesV1.ConcurrentChange)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/{id:guid}/versions/{versionNo:int}/content", async (Guid id, int versionNo, HttpContext httpContext,
            [FromServices] IDocumentService service, ICorrelationContext correlation, CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (httpContext.Request.Query.Count > 0 || versionNo < 1)
            {
                return Problem(StatusCodes.Status400BadRequest, DocumentProblemCodesV1.ValidationFailed, correlation);
            }

            DocumentContentV1 content;
            try
            {
                content = await service.GetVersionContentAsync(id, versionNo, cancellationToken);
            }
            catch (DocumentServiceException exception)
            {
                return Problem(exception, correlation);
            }

            var headers = httpContext.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
            headers["Repr-Digest"] = $"sha-256=:{Convert.ToBase64String(Convert.FromHexString(content.Sha256))}:";
            return Results.File(content.Content, content.ContentType, content.FileName);
        })
            .WithName("GetDocumentVersionContentV1")
            .Produces<Stream>(StatusCodes.Status200OK, "application/pdf", "image/png", "image/jpeg")
            .ProducesErpBadRequest(DocumentProblemCodesV1.ValidationFailed)
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(DocumentProblemCodesV1.NotScanned)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        return endpoints;
    }

    private static async Task<IResult> AppendVersionAsync(Guid id, HttpContext httpContext, [FromServices] IDocumentService service,
        [FromServices] DocumentsServiceBindings bindings, ICorrelationContext correlation, CancellationToken cancellationToken)
    {
        SetPrivateNoStore(httpContext.Response);
        if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var idempotencyKey) || !correlation.IsResolved)
        {
            return Problem(StatusCodes.Status400BadRequest, DocumentProblemCodesV1.CommandMetadataInvalid, correlation);
        }

        if (!httpContext.Request.HasFormContentType
            || !(httpContext.Request.ContentType ?? string.Empty).StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        var bodyLimit = bindings.MaximumUploadBytes + MultipartOverheadBytes;
        var sizeFeature = httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false })
        {
            sizeFeature.MaxRequestBodySize = bodyLimit;
        }

        if (httpContext.Request.ContentLength is { } declaredLength && declaredLength > bodyLimit)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        IFormCollection form;
        try
        {
            httpContext.Features.Set<IFormFeature>(new FormFeature(httpContext.Request, new FormOptions
            {
                MultipartBodyLengthLimit = bodyLimit,
                ValueCountLimit = 2,
                ValueLengthLimit = 32,
                MultipartHeadersCountLimit = 8,
            }));
            form = await httpContext.Request.ReadFormAsync(cancellationToken);
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }
        catch (Exception exception) when (exception is InvalidDataException or BadHttpRequestException or IOException)
        {
            return Problem(StatusCodes.Status400BadRequest, DocumentProblemCodesV1.ValidationFailed, correlation);
        }

        if (form.Keys.Count != 1 || !form.Keys.All(VersionFormFieldNames.Contains) || form.Keys.Any(key => form[key].Count != 1)
            || form.Files.Count != 1 || !string.Equals(form.Files[0].Name, DocumentsApiV1.FileField, StringComparison.Ordinal)
            || !int.TryParse(form[DocumentsApiV1.ExpectedCurrentVersionField].ToString(), out var expectedCurrentVersion)
            || expectedCurrentVersion < 1 || expectedCurrentVersion == int.MaxValue)
        {
            return Problem(StatusCodes.Status400BadRequest, DocumentProblemCodesV1.ValidationFailed, correlation);
        }

        var file = form.Files[0];
        if (file.Length > bindings.MaximumUploadBytes)
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, DocumentRuleCodes.TooLarge, correlation);
        }

        byte[] content;
        await using (var stream = file.OpenReadStream())
        using (var buffer = new MemoryStream(checked((int)file.Length)))
        {
            await stream.CopyToAsync(buffer, cancellationToken);
            content = buffer.ToArray();
        }

        try
        {
            var response = await service.AppendVersionAsync(id,
                new AppendDocumentVersionRequest(expectedCurrentVersion, file.FileName, file.ContentType, content),
                new DocumentCommandMetadata(idempotencyKey, correlation.CorrelationId, correlation.CausationId), cancellationToken);
            if (response.IsReplay)
            {
                ApplyReplayHeaders(httpContext, response.OriginalCorrelationId);
            }

            return Results.Json(response, Json, statusCode: DocumentsApiV1.CreatedStatus);
        }
        catch (DocumentServiceException exception)
        {
            return Problem(exception, correlation);
        }
    }

    private static async Task<IResult> UploadAsync(HttpContext httpContext, [FromServices] IDocumentService service,
        [FromServices] DocumentsServiceBindings bindings, ICorrelationContext correlation, CancellationToken cancellationToken)
    {
        SetPrivateNoStore(httpContext.Response);
        if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var idempotencyKey) || !correlation.IsResolved)
        {
            return Problem(StatusCodes.Status400BadRequest, DocumentProblemCodesV1.CommandMetadataInvalid, correlation);
        }

        if (!httpContext.Request.HasFormContentType
            || !(httpContext.Request.ContentType ?? string.Empty).StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        // The body is bounded before it is read: the file bound plus the multipart framing, never the server default.
        var bodyLimit = bindings.MaximumUploadBytes + MultipartOverheadBytes;
        var sizeFeature = httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false })
        {
            sizeFeature.MaxRequestBodySize = bodyLimit;
        }

        if (httpContext.Request.ContentLength is { } declaredLength && declaredLength > bodyLimit)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        IFormCollection form;
        try
        {
            httpContext.Features.Set<IFormFeature>(new FormFeature(httpContext.Request, new FormOptions
            {
                MultipartBodyLengthLimit = bodyLimit,
                ValueCountLimit = FormFieldNames.Count + 1,
                ValueLengthLimit = MaximumOwnerKindLength * 2,
                MultipartHeadersCountLimit = 8,
            }));
            form = await httpContext.Request.ReadFormAsync(cancellationToken);
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }
        catch (Exception exception) when (exception is InvalidDataException or BadHttpRequestException or IOException)
        {
            return Problem(StatusCodes.Status400BadRequest, DocumentProblemCodesV1.ValidationFailed, correlation);
        }

        // Exactly the two fields and exactly one file under its field; anything else is not this contract.
        if (form.Keys.Count != FormFieldNames.Count || !form.Keys.All(FormFieldNames.Contains) || form.Keys.Any(key => form[key].Count != 1)
            || form.Files.Count != 1 || !string.Equals(form.Files[0].Name, DocumentsApiV1.FileField, StringComparison.Ordinal)
            || !Guid.TryParseExact(form[DocumentsApiV1.OwnerIdField].ToString(), "D", out var ownerId))
        {
            return Problem(StatusCodes.Status400BadRequest, DocumentProblemCodesV1.ValidationFailed, correlation);
        }

        var file = form.Files[0];
        if (file.Length > bindings.MaximumUploadBytes)
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, DocumentRuleCodes.TooLarge, correlation);
        }

        byte[] content;
        await using (var stream = file.OpenReadStream())
        using (var buffer = new MemoryStream(checked((int)file.Length)))
        {
            await stream.CopyToAsync(buffer, cancellationToken);
            content = buffer.ToArray();
        }

        try
        {
            var response = await service.UploadAsync(
                new UploadDocumentRequest(form[DocumentsApiV1.OwnerKindField].ToString(), ownerId, file.FileName, file.ContentType, content),
                new DocumentCommandMetadata(idempotencyKey, correlation.CorrelationId, correlation.CausationId),
                cancellationToken);
            if (response.IsReplay)
            {
                ApplyReplayHeaders(httpContext, response.OriginalCorrelationId);
            }

            return Results.Json(response, Json, statusCode: DocumentsApiV1.CreatedStatus);
        }
        catch (DocumentServiceException exception)
        {
            return Problem(exception, correlation);
        }
    }

    /// <summary>Exactly one Idempotency-Key: non-blank, canonical, at most 128 characters, no comma.</summary>
    private static bool TryReadIdempotencyKey(IHeaderDictionary headers, out string idempotencyKey)
    {
        idempotencyKey = string.Empty;
        if (!headers.TryGetValue(DocumentsApiV1.IdempotencyHeader, out var values) || values.Count != 1 || values[0] is not { } value
            || string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Length > MaximumIdempotencyKeyLength || value.Contains(',', StringComparison.Ordinal))
        {
            return false;
        }

        idempotencyKey = value;
        return true;
    }

    private static void ApplyReplayHeaders(HttpContext httpContext, string originalCorrelationId)
    {
        httpContext.Response.Headers["Idempotency-Replayed"] = "true";
        httpContext.Response.Headers["Idempotency-Original-Correlation-Id"] = originalCorrelationId;
    }

    /// <summary>Platform 401/403/404 and every 503 stay code-less; every other status carries its code and errors.</summary>
    private static IResult Problem(DocumentServiceException exception, ICorrelationContext correlation)
    {
        if (exception.Status is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden or StatusCodes.Status404NotFound
            or StatusCodes.Status503ServiceUnavailable)
        {
            return Results.Problem(statusCode: exception.Status);
        }

        var extensions = new Dictionary<string, object?>
        {
            ["code"] = exception.Code,
            ["correlationId"] = correlation.IsResolved ? correlation.CorrelationId : null,
        };
        if (exception.Errors.Count > 0)
        {
            extensions["errors"] = exception.Errors;
        }

        return Results.Problem(statusCode: exception.Status, extensions: extensions);
    }

    private static IResult Problem(int statusCode, string code, ICorrelationContext correlation) => Results.Problem(statusCode: statusCode,
        extensions: new Dictionary<string, object?> { ["code"] = code, ["correlationId"] = correlation.IsResolved ? correlation.CorrelationId : null });

    private static void SetPrivateNoStore(HttpResponse response)
    {
        response.Headers.CacheControl = "private, no-store";
        response.Headers.Pragma = "no-cache";
    }
}

/// <summary>The multipart upload form, for the OpenAPI description only (the route reads the form itself, strictly).</summary>
public sealed class DocumentUploadFormV1
{
    /// <summary>An owner-declared link kind, e.g. <c>counterparty.party</c>.</summary>
    public required string OwnerKind { get; init; }

    /// <summary>The owner record's id in the context company.</summary>
    public required Guid OwnerId { get; init; }

    /// <summary>One PDF, PNG or JPEG file; the bytes must carry the kind's signature.</summary>
    public required IFormFile File { get; init; }
}

/// <summary>The immutable-version multipart form, used for OpenAPI only.</summary>
public sealed class DocumentVersionUploadFormV1
{
    /// <summary>The latest version observed by the caller; the server refuses a stale value.</summary>
    public required int ExpectedCurrentVersion { get; init; }

    /// <summary>One PDF, PNG or JPEG file; the bytes must carry the kind's signature.</summary>
    public required IFormFile File { get; init; }
}
