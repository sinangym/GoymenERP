using GoymenERP.Api.Ai;
using GoymenERP.Api.Ai.DocumentDrafts;
using GoymenERP.Api.Errors;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// AI-3 READ-only runtime endpoints. Both require an authenticated user and a server company context. The
/// <c>ai.panel.use</c> capability is measured inside <see cref="AiAskService"/> (fresh) rather than by an endpoint
/// policy so that a missing capability is recorded as an <c>ai.authz.denied</c> security event (Group E).
/// </summary>
public static class AiEndpoints
{
    public const string AiDisabledCode = "ai_disabled";
    public const string AiToolUnavailableCode = "ai_tool_unavailable";
    public const string AiRequestInvalidCode = "ai_request_invalid";
    public const string AiDocumentDraftsDisabledCode = "ai_document_drafts_disabled";
    public const string AiDocumentDraftInvalidCode = "ai_document_draft_invalid";
    public const string AiDocumentUnreadableCode = "ai_document_unreadable";

    /// <summary>WP-A1: the panel feature switch is off in the company's AI gateway policy.</summary>
    public const string AiFeatureDisabledCode = "ai_feature_disabled";

    /// <summary>WP-A1: the month's AI usage ceiling is reached, or no budget is recorded.</summary>
    public const string AiBudgetExhaustedCode = "ai_budget_exhausted";

    public static IEndpointRouteBuilder MapAiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/ai/capabilities", async (
                AiCapabilityService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                NoStore(httpContext);
                return Results.Ok(await service.GetAsync(cancellationToken));
            })
            .RequireAuthorization()
            .RequireCompanyContext()
            .WithTags("Ai")
            .WithName("GetAiCapabilitiesV1")
            .Produces<AiCapabilitiesV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest()
            .AllowsCodeLessErpBadRequestBody()
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpAuthenticatedProblems();

        endpoints.MapPost("/api/v1/ai/ask", async (
                AiAskRequestV1 request,
                AiAskService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                NoStore(httpContext);
                var result = await service.AskAsync(request, cancellationToken);
                return result.Status switch
                {
                    AiAskStatusV1.Answered when result.Response is not null => Results.Ok(result.Response),
                    AiAskStatusV1.Disabled => Results.Problem(
                        title: "AI asistan kapalı.",
                        detail: "Bu ortamda AI asistan açık değil. ERP işlemleri etkilenmez.",
                        statusCode: StatusCodes.Status503ServiceUnavailable,
                        extensions: new Dictionary<string, object?> { ["code"] = AiDisabledCode }),
                    AiAskStatusV1.Invalid => Results.Problem(
                        title: "AI isteği geçersiz.",
                        detail: "Soruyu ve seçilen aracı kontrol edip yeniden deneyin.",
                        statusCode: StatusCodes.Status400BadRequest,
                        extensions: new Dictionary<string, object?>
                        {
                            ["code"] = AiRequestInvalidCode,
                            ["invalidFields"] = result.InvalidFields,
                        }),
                    AiAskStatusV1.FeatureDisabled => Results.Problem(
                        title: "AI asistan bu şirkette kapalı.",
                        detail: "Bu özellik şirketin yapay zekâ ayarlarında kapalı. ERP işlemleri etkilenmez.",
                        statusCode: StatusCodes.Status503ServiceUnavailable,
                        extensions: new Dictionary<string, object?> { ["code"] = AiFeatureDisabledCode }),
                    AiAskStatusV1.BudgetExhausted => Results.Problem(
                        title: "Aylık yapay zekâ kullanım sınırı doldu.",
                        detail: "Bu ayın kullanım sınırına ulaşıldı veya sınır tanımlı değil. ERP işlemleri etkilenmez.",
                        statusCode: StatusCodes.Status503ServiceUnavailable,
                        extensions: new Dictionary<string, object?> { ["code"] = AiBudgetExhaustedCode }),
                    AiAskStatusV1.ToolUnavailable => Results.Problem(
                        title: "Kaynak şu anda okunamıyor.",
                        detail: "Kaynak modül isteği tamamlayamadı. Daha sonra yeniden deneyin.",
                        statusCode: StatusCodes.Status503ServiceUnavailable,
                        extensions: new Dictionary<string, object?> { ["code"] = AiToolUnavailableCode }),
                    AiAskStatusV1.Unavailable => Results.Problem(
                        title: "AI asistan şu anda kullanılamıyor.",
                        detail: "İstek güvenli biçimde işlenemediği için hiçbir kaynak okunmadı.",
                        statusCode: StatusCodes.Status503ServiceUnavailable,
                        extensions: new Dictionary<string, object?> { ["code"] = ErpProblemCodes.ServiceUnavailable }),
                    // Refused (capability, tool, permission, data class or fence); nothing is released.
                    _ => Results.Problem(
                        statusCode: StatusCodes.Status403Forbidden,
                        extensions: new Dictionary<string, object?> { ["code"] = ErpProblemCodes.Forbidden }),
                };
            })
            .RequireAuthorization()
            .RequireCompanyContext()
            .WithTags("Ai")
            .WithName("AskAiV1")
            .Accepts<AiAskRequestV1>("application/json")
            .Produces<AiAskResponseV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(AiRequestInvalidCode)
            .AllowsCodeLessErpBadRequestBody()
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpServiceUnavailable(AiDisabledCode, AiToolUnavailableCode, AiFeatureDisabledCode, AiBudgetExhaustedCode);

        // WP-B3: a document becomes a DRAFT only. No record is created; the response names the owner screen.
        endpoints.MapPost("/api/v1/ai/document-drafts", async (
                AiDocumentDraftRequestV1 request,
                AiDocumentDraftService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                NoStore(httpContext);
                var result = await service.DraftAsync(request, cancellationToken);
                return result.Status switch
                {
                    AiDocumentDraftStatusV1.Drafted when result.Draft is not null => Results.Ok(result.Draft),
                    AiDocumentDraftStatusV1.Disabled => Results.Problem(
                        title: "Belgeden taslak kapalı.",
                        detail: "Bu ortamda belgeden taslak hazırlama açık değil. ERP işlemleri etkilenmez.",
                        statusCode: StatusCodes.Status503ServiceUnavailable,
                        extensions: new Dictionary<string, object?> { ["code"] = AiDocumentDraftsDisabledCode }),
                    AiDocumentDraftStatusV1.Invalid => Results.Problem(
                        title: "Taslak isteği geçersiz.",
                        detail: "Taslak türünü, dosya türünü ve içeriği kontrol edip yeniden deneyin.",
                        statusCode: StatusCodes.Status400BadRequest,
                        extensions: new Dictionary<string, object?>
                        {
                            ["code"] = AiDocumentDraftInvalidCode,
                            ["invalidFields"] = result.InvalidFields,
                        }),
                    AiDocumentDraftStatusV1.Unreadable => Results.Problem(
                        title: "Belge okunamadı.",
                        detail: "Belgeden alan okunamadı. Dosyayı kontrol edin ya da kaydı sahip ekranda elle girin.",
                        statusCode: StatusCodes.Status422UnprocessableEntity,
                        extensions: new Dictionary<string, object?>
                        {
                            ["code"] = AiDocumentUnreadableCode,
                            ["invalidFields"] = result.InvalidFields,
                        }),
                    AiDocumentDraftStatusV1.Unavailable => Results.Problem(
                        title: "Belgeden taslak şu anda kullanılamıyor.",
                        detail: "İstek güvenli biçimde işlenemediği için taslak hazırlanmadı.",
                        statusCode: StatusCodes.Status503ServiceUnavailable,
                        extensions: new Dictionary<string, object?> { ["code"] = ErpProblemCodes.ServiceUnavailable }),
                    _ => Results.Problem(
                        statusCode: StatusCodes.Status403Forbidden,
                        extensions: new Dictionary<string, object?> { ["code"] = ErpProblemCodes.Forbidden }),
                };
            })
            .RequireAuthorization()
            .RequireCompanyContext()
            .WithTags("Ai")
            .WithName("CreateAiDocumentDraftV1")
            .Accepts<AiDocumentDraftRequestV1>("application/json")
            .Produces<AiDocumentDraftResponseV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(AiDocumentDraftInvalidCode)
            .ProducesErpUnprocessableEntity(AiDocumentUnreadableCode)
            .AllowsCodeLessErpBadRequestBody()
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpServiceUnavailable(AiDocumentDraftsDisabledCode);

        return endpoints;
    }

    private static void NoStore(HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = "private, no-store";
        httpContext.Response.Headers.Pragma = "no-cache";
    }
}
