using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// API-98/B-14 (ALS-D79, BS-10; KIM-D49, KIM-D51): the company's reference lists as persistent Settings records that the
/// purchasing, stock/logistics, production and finance modules read (<see cref="ISettingsReferenceListReaderV1"/>). Every
/// route runs in the server-resolved company (X-Company-Id) under the list's own name (company-wide, not Critical, granted to
/// no role here), re-authorized by the service. Responses are private, no-store. Only the enabled lists have routes:
/// <list type="bullet">
/// <item><c>expense-types</c>, <c>service-types</c> — <see cref="SettingsReferenceListPermissionManifestV1.PurchasingListsManage"/>;</item>
/// <item><c>paint-colors</c>, <c>scrap-kinds</c> — <see cref="SettingsReferenceListPermissionManifestV1.ProductionListsManage"/>.</item>
/// </list>
/// The operation type list is not opened (no route). Per list:
/// <list type="bullet">
/// <item><c>GET /api/v1/settings/reference-lists/{list}</c> — every entry (active and inactive) by code.</item>
/// <item><c>POST /api/v1/settings/reference-lists/{list}/entries</c> (204) — a new active entry; a code already in the list
/// is 409.</item>
/// <item><c>POST /api/v1/settings/reference-lists/{list}/entries/{entryId}/versions</c> (204) — rename, deactivate or
/// reactivate an entry; an unknown entry is 404.</item>
/// </list>
/// Commands: one Idempotency-Key (stored terminal answers replay with <c>Idempotency-Replayed</c>), the entry's expected
/// version for a new version, a mandatory reason (≤ 4000).
/// </summary>
public static class SettingsReferenceListEndpoints
{
    public const string ReferenceListsPath = "/api/v1/settings/reference-lists";
    private const string IdempotencyHeaderName = "Idempotency-Key";
    private const int MaximumIdempotencyKeyLength = 128;

    internal static readonly string[] CommandBadRequestCodes =
    [
        SettingsReferenceListProblemCodesV1.RequestInvalid,
        SettingsReferenceListProblemCodesV1.CommandMetadataInvalid,
        SettingsReferenceListProblemCodesV1.ReasonRequired,
        SettingsReferenceListProblemCodesV1.ReasonTooLong,
    ];

    /// <summary>The routed lists: the kind, its slug, its permission and the stem of its endpoint names.</summary>
    private static readonly (SettingsReferenceListKindV1 Kind, string Slug, string Permission, string NameStem)[] Lists =
    [
        (SettingsReferenceListKindV1.ExpenseType, SettingsReferenceListKindsV1.ExpenseTypeSlug,
            SettingsReferenceListPermissionManifestV1.PurchasingListsManage, "ExpenseType"),
        (SettingsReferenceListKindV1.ServiceType, SettingsReferenceListKindsV1.ServiceTypeSlug,
            SettingsReferenceListPermissionManifestV1.PurchasingListsManage, "ServiceType"),
        (SettingsReferenceListKindV1.PaintColor, SettingsReferenceListKindsV1.PaintColorSlug,
            SettingsReferenceListPermissionManifestV1.ProductionListsManage, "PaintColor"),
        (SettingsReferenceListKindV1.ScrapKind, SettingsReferenceListKindsV1.ScrapKindSlug,
            SettingsReferenceListPermissionManifestV1.ProductionListsManage, "ScrapKind"),
    ];

    public static IEndpointRouteBuilder MapSettingsReferenceListEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(ReferenceListsPath)
            .RequireAuthorization()
            .WithTags("Settings");

        foreach (var (kind, slug, permission, nameStem) in Lists)
        {
            group.MapGet("/" + slug, async (
                    HttpContext httpContext,
                    ISettingsReferenceListAdministrationV1 service,
                    CancellationToken cancellationToken) =>
                await ReadAsync(httpContext, () => service.GetAsync(kind, cancellationToken)))
                .RequireCompanyContext()
                .RequireAuthorization(permission)
                .WithName($"GetSettings{nameStem}ListV1")
                .Produces<SettingsReferenceListViewV1>(StatusCodes.Status200OK)
                .ProducesErpBadRequest(SettingsReferenceListProblemCodesV1.RequestInvalid)
                .ProducesErpServiceUnavailable(SettingsReferenceListProblemCodesV1.ProviderUnavailable)
                .ProducesErpAuthenticatedProblems();

            group.MapPost("/" + slug + "/entries", async (
                    CreateSettingsReferenceListEntryRequestV1 request,
                    ISettingsReferenceListAdministrationV1 service,
                    ICurrentUser currentUser,
                    ICompanyContext companyContext,
                    ICorrelationContext correlationContext,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                    metadata => service.CreateAsync(kind, request, metadata, cancellationToken)))
                .RequireCompanyContext()
                .RequireAuthorization(permission)
                .WithName($"CreateSettings{nameStem}EntryV1")
                .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status409Conflict)
                .Accepts<CreateSettingsReferenceListEntryRequestV1>("application/json")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesErpBadRequest(CommandBadRequestCodes)
                .AllowsCodeLessErpBadRequestBody()
                .ProducesErpUnsupportedMediaType()
                .ProducesErpConflict(
                    SettingsReferenceListProblemCodesV1.IdempotencyConflict,
                    SettingsReferenceListProblemCodesV1.CodeConflict)
                .ProducesErpServiceUnavailable(SettingsReferenceListProblemCodesV1.ProviderUnavailable)
                .ProducesErpAuthenticatedProblems();

            group.MapPost("/" + slug + "/entries/{entryId:guid}/versions", async (
                    Guid entryId,
                    RecordSettingsReferenceListEntryRequestV1 request,
                    ISettingsReferenceListAdministrationV1 service,
                    ICurrentUser currentUser,
                    ICompanyContext companyContext,
                    ICorrelationContext correlationContext,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                    metadata => service.RecordAsync(kind, entryId, request, metadata, cancellationToken)))
                .RequireCompanyContext()
                .RequireAuthorization(permission)
                .WithName($"RecordSettings{nameStem}EntryVersionV1")
                .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
                    StatusCodes.Status409Conflict)
                .Accepts<RecordSettingsReferenceListEntryRequestV1>("application/json")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesErpBadRequest(CommandBadRequestCodes)
                .AllowsCodeLessErpBadRequestBody()
                .ProducesErpUnsupportedMediaType()
                .ProducesErpNotFound(SettingsReferenceListProblemCodesV1.EntryNotFound)
                .ProducesErpConflict(
                    SettingsReferenceListProblemCodesV1.IdempotencyConflict,
                    SettingsReferenceListProblemCodesV1.VersionConflict)
                .ProducesErpServiceUnavailable(SettingsReferenceListProblemCodesV1.ProviderUnavailable)
                .ProducesErpAuthenticatedProblems();
        }

        return endpoints;
    }

    private static async Task<IResult> ReadAsync<T>(HttpContext httpContext, Func<Task<T>> read)
    {
        httpContext.Response.Headers.CacheControl = "private, no-store";
        if (httpContext.Request.Query.Count != 0)
        {
            return Problem(StatusCodes.Status400BadRequest, SettingsReferenceListProblemCodesV1.RequestInvalid,
                "İstek geçersiz.", "Şirket yalnız X-Company-Id başlığından belirlenir; sorgu parametresi kabul edilmez.");
        }

        try
        {
            return Results.Ok(await read());
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
        }
        catch (SettingsPrerequisiteException exception)
        {
            return FromPrerequisite(exception);
        }
    }

    private static async Task<IResult> CommandAsync(
        HttpContext httpContext,
        ICurrentUser currentUser,
        ICompanyContext companyContext,
        ICorrelationContext correlationContext,
        Func<SettingsCommandMetadataV1, Task<SettingsReferenceListCommandResultV1>> execute)
    {
        httpContext.Response.Headers.CacheControl = "private, no-store";
        if (httpContext.Request.Query.Count != 0)
        {
            return Problem(StatusCodes.Status400BadRequest, SettingsReferenceListProblemCodesV1.RequestInvalid,
                "İstek geçersiz.", "Sorgu parametresi kabul edilmez; şirket sunucu tarafından belirlenir.");
        }

        if (!TryReadMetadata(httpContext, currentUser, companyContext, correlationContext, out var metadata))
        {
            return Problem(StatusCodes.Status400BadRequest, SettingsReferenceListProblemCodesV1.CommandMetadataInvalid,
                "Komut bilgisi eksik.", "İşlem için tek ve geçerli bir Idempotency-Key ve correlation bilgisi gerekir.");
        }

        SettingsReferenceListCommandResultV1 result;
        try
        {
            result = await execute(metadata);
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
        }
        catch (SettingsPrerequisiteException exception)
        {
            return FromPrerequisite(exception);
        }

        if (result.IsReplay)
        {
            httpContext.Response.Headers["Idempotency-Replayed"] = "true";
            httpContext.Response.Headers["Idempotency-Original-Correlation-Id"] = result.OriginalCorrelationId;
        }

        var answer = result.Answer;
        return answer.StatusCode switch
        {
            StatusCodes.Status204NoContent => Results.NoContent(),
            _ when answer.Code is { } code => Problem(answer.StatusCode, code, answer.Title, answer.Detail),
            _ => throw new InvalidOperationException("Referans listesi komut sonucu HTTP durumuyla eşleşmiyor."),
        };
    }

    private static IResult FromPrerequisite(SettingsPrerequisiteException exception) => exception.Code switch
    {
        SettingsReferenceListProblemCodesV1.AccessDenied => Results.Problem(statusCode: StatusCodes.Status403Forbidden),
        SettingsReferenceListProblemCodesV1.RequestInvalid => Problem(StatusCodes.Status400BadRequest,
            SettingsReferenceListProblemCodesV1.RequestInvalid, "İstek geçersiz.", "İstek alanları geçersiz."),
        _ => Problem(StatusCodes.Status503ServiceUnavailable, SettingsReferenceListProblemCodesV1.ProviderUnavailable,
            "Ayarlar kullanılamıyor.",
            "Referans listesi şu anda okunamıyor veya yazılamıyor; daha sonra yeniden deneyin."),
    };

    private static IResult Problem(int status, string code, string title, string detail) =>
        Results.Problem(title: title, detail: detail, statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    private static bool TryReadMetadata(
        HttpContext httpContext,
        ICurrentUser currentUser,
        ICompanyContext companyContext,
        ICorrelationContext correlationContext,
        out SettingsCommandMetadataV1 metadata)
    {
        metadata = null!;
        if (!httpContext.Request.Headers.TryGetValue(IdempotencyHeaderName, out var values)
            || values.Count != 1
            || values[0] is not { } raw
            || raw.Trim() is not { Length: > 0 and <= MaximumIdempotencyKeyLength } key
            || key.Contains(',', StringComparison.Ordinal)
            || !correlationContext.IsResolved
            || string.IsNullOrWhiteSpace(currentUser.UserId)
            || !companyContext.IsResolved)
        {
            return false;
        }

        metadata = new SettingsCommandMetadataV1(
            companyContext.CompanyId,
            currentUser.UserId,
            correlationContext.CorrelationId,
            correlationContext.CausationId,
            correlationContext.SourceChannel,
            correlationContext.ClientIpAddress,
            correlationContext.UserAgent,
            key);
        return true;
    }
}
