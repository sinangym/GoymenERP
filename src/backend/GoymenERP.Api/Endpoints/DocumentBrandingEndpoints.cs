using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.Api.Errors;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Documents.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

public static class DocumentBrandingEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 12 };
    private static readonly string[] ConflictCodes = ["same_key_payload_mismatch", "document_branding_version_conflict",
        "document_branding_asset_invalid", "document_branding_asset_not_clean", "document_template_maker_cannot_publish",
        "document_template_already_published", "document_template_assets_required"];

    public static IEndpointRouteBuilder MapDocumentBrandingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(DocumentBrandingContractV1.BasePath).WithTags("Documents")
            .RequireAuthorization().RequireCompanyContext();
        Common(group.MapGet("/current", async (HttpContext context, ICompanyDocumentBrandingServiceV1 service, CancellationToken ct) =>
            await Read(context, () => service.GetBrandingAsync(ct))))
            .RequireAuthorization(DocumentBrandingPermissionsV1.Manage).WithName("GetCompanyDocumentBrandingV1")
            .Produces<CompanyDocumentBrandingCurrentV1>();
        Write(group.MapPut("/current", async (HttpContext context, ICompanyDocumentBrandingServiceV1 service, ICorrelationContext correlation, CancellationToken ct) =>
            await Command(context, correlation, async metadata => await service.SetBrandingAsync(await Body<SetCompanyDocumentBrandingV1>(context, ct), metadata, ct))))
            .RequireAuthorization(DocumentBrandingPermissionsV1.Manage).WithName("SetCompanyDocumentBrandingV1")
            .Accepts<SetCompanyDocumentBrandingV1>("application/json").Produces<CompanyDocumentBrandingMutationV1>();

        foreach (var module in Enum.GetValues<DocumentTemplateModuleV1>())
        {
            var slug = module.ToString().ToLowerInvariant();
            Common(group.MapGet("/templates/" + slug, async (HttpContext context, ICompanyDocumentBrandingServiceV1 service, CancellationToken ct) =>
                await Read(context, () => service.GetTemplateAsync(module, ct))))
                .WithName("Get" + module + "DocumentTemplateV1").Produces<DocumentTemplateCurrentV1>();
            Write(group.MapPost("/templates/" + slug + "/drafts", async (HttpContext context, ICompanyDocumentBrandingServiceV1 service, ICorrelationContext correlation, CancellationToken ct) =>
                await Command(context, correlation, async metadata =>
                {
                    var body = await Body<PrepareDocumentTemplateV1>(context, ct);
                    if (body.Module != module) throw new DocumentBrandingServiceException(400, "validation_failed");
                    return await service.PrepareAsync(body, metadata, ct);
                }))).RequireAuthorization(DocumentBrandingPermissionsV1.Prepare(module)!)
                .WithName("Prepare" + module + "DocumentTemplateV1").Accepts<PrepareDocumentTemplateV1>("application/json")
                .Produces<DocumentTemplateDraftMutationV1>();
            Write(group.MapPost("/templates/" + slug + "/publications", async (HttpContext context, ICompanyDocumentBrandingServiceV1 service, ICorrelationContext correlation, CancellationToken ct) =>
                await Command(context, correlation, async metadata =>
                {
                    var body = await Body<PublishDocumentTemplateV1>(context, ct);
                    if (body.Module != module) throw new DocumentBrandingServiceException(400, "validation_failed");
                    return await service.PublishAsync(body, metadata, ct);
                }))).RequireAuthorization(DocumentBrandingPermissionsV1.Publish(module)!)
                .WithName("Publish" + module + "DocumentTemplateV1").Accepts<PublishDocumentTemplateV1>("application/json")
                .Produces<DocumentTemplatePublicationMutationV1>();
        }
        return endpoints;
    }

    private static RouteHandlerBuilder Common(RouteHandlerBuilder route) => route
        .WithMetadata(new PrivateNoStoreOnAuthorizationFailureMetadata()).ProducesErpAuthenticatedProblems()
        .ProducesErpServiceUnavailable().ProducesErpBadRequest("validation_failed");
    private static RouteHandlerBuilder Write(RouteHandlerBuilder route) => Common(route)
        .WithIdempotencyContract(200, 409).ProducesErpConflict(ConflictCodes).ProducesErpUnsupportedMediaType()
        .ProducesErpUnprocessableEntity("document_branding_asset_render_invalid");

    private static async Task<IResult> Read<T>(HttpContext context, Func<Task<T>> action)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (context.Request.Query.Count != 0) return Problem(400, "validation_failed");
        try { return Results.Json(await action(), Json); }
        catch (OperationCanceledException) { throw; }
        catch (DocumentBrandingServiceException ex) { return Problem(ex.Status, ex.Code); }
        catch { return Problem(503, "service_unavailable"); }
    }

    private static async Task<IResult> Command<T>(HttpContext context, ICorrelationContext correlation,
        Func<DocumentCommandMetadata, Task<T>> action)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (context.Request.Query.Count != 0 || !context.Request.Headers.TryGetValue(DocumentsApiV1.IdempotencyHeader, out var header)
            || header.Count != 1 || header[0] is not { } key || string.IsNullOrWhiteSpace(key) || key.Length > 128
            || key.Trim() != key || key.Any(c => char.IsControl(c) || c == ',') || !correlation.IsResolved)
            return Problem(400, "validation_failed");
        try
        {
            var value = await action(new(key, correlation.CorrelationId, correlation.CausationId));
            var replay = value switch
            {
                CompanyDocumentBrandingMutationV1 branding => (branding.IsReplay, branding.OriginalCorrelationId),
                DocumentTemplateDraftMutationV1 draft => (draft.IsReplay, draft.OriginalCorrelationId),
                DocumentTemplatePublicationMutationV1 publication => (publication.IsReplay, publication.OriginalCorrelationId),
                _ => throw new InvalidOperationException("Unknown Documents branding result."),
            };
            ReplayHeaders(context, replay.IsReplay, replay.OriginalCorrelationId);
            return Results.Json(value, Json);
        }
        catch (OperationCanceledException) { throw; }
        catch (DocumentBrandingServiceException ex)
        { ReplayHeaders(context, ex.IsReplay, ex.OriginalCorrelationId); return Problem(ex.Status, ex.Code); }
        catch (JsonException) { return Problem(400, "validation_failed"); }
        catch { return Problem(503, "service_unavailable"); }
    }

    internal static async Task<T> Body<T>(HttpContext context, CancellationToken ct)
    {
        if (!context.Request.HasJsonContentType()) throw new DocumentBrandingServiceException(415, "unsupported_media_type");
        using var buffer = new MemoryStream();
        var chunk = new byte[2048];
        while (true)
        {
            var read = await context.Request.Body.ReadAsync(chunk.AsMemory(), ct);
            if (read == 0) break;
            if (buffer.Length + read > 8192) throw new DocumentBrandingServiceException(400, "validation_failed");
            buffer.Write(chunk, 0, read);
        }
        using var document = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 12 });
        RejectDuplicates(document.RootElement);
        return document.RootElement.Deserialize<T>(Json) ?? throw new JsonException("Missing request.");
    }
    private static void RejectDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            { if (!names.Add(property.Name)) throw new JsonException("Duplicate member."); RejectDuplicates(property.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) RejectDuplicates(item);
    }
    private static void ReplayHeaders(HttpContext context, bool replay, string? correlation)
    {
        if (!replay || correlation is null) return;
        context.Response.Headers["Idempotency-Replayed"] = "true";
        context.Response.Headers["Idempotency-Original-Correlation-Id"] = correlation;
    }
    private static IResult Problem(int status, string code) => status is 400 or 409 or 422
        ? Results.Problem(statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code })
        : Results.Problem(statusCode: status);
}
