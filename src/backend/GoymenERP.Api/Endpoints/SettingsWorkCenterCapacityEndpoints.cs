using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// URT-D89 (<c>docs/decisions/SINAN_KARARLARI_2026-11-15_DALGA2_EK12.md</c>; URT-D87 the unit, URT-D91/URT-D92 the buffer):
/// the input capacity (kg/hour) and the optional due-date safety buffer (hours or days) of every work center of the company,
/// as a persistent, versioned Settings record that the Manufacturing due-date suggestion reads
/// (<see cref="ISettingsWorkCenterCapacityProviderV1"/>). Every route runs in the server-resolved company (X-Company-Id)
/// under <see cref="SettingsWorkCenterCapacityPermissionManifestV1.Manage"/> (company-wide, not Critical, granted to no role
/// here), re-authorized by the service. Responses are private, no-store.
/// <list type="bullet">
/// <item><c>GET /api/v1/settings/work-center-capacities</c> — every work center's timeline (a work center with no version has
/// no capacity).</item>
/// <item><c>POST /api/v1/settings/work-center-capacities/{workCenterId}/versions</c> (204) — a new version of one work center,
/// in force when saved; an unknown or foreign work center is 404.</item>
/// </list>
/// Commands: one Idempotency-Key (stored terminal answers replay with <c>Idempotency-Replayed</c>), the work center's expected
/// version as decimal text, a mandatory reason (≤ 4000).
/// </summary>
public static class SettingsWorkCenterCapacityEndpoints
{
    public const string WorkCenterCapacitiesPath = "/api/v1/settings/work-center-capacities";
    private const string IdempotencyHeaderName = "Idempotency-Key";
    private const int MaximumIdempotencyKeyLength = 128;

    internal static readonly string[] CommandBadRequestCodes =
    [
        WorkCenterCapacityProblemCodesV1.RequestInvalid,
        WorkCenterCapacityProblemCodesV1.CommandMetadataInvalid,
        WorkCenterCapacityProblemCodesV1.ReasonRequired,
        WorkCenterCapacityProblemCodesV1.ReasonTooLong,
    ];

    public static IEndpointRouteBuilder MapSettingsWorkCenterCapacityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(WorkCenterCapacitiesPath)
            .RequireAuthorization()
            .WithTags("Settings");

        group.MapGet("", async (
                HttpContext httpContext,
                ISettingsWorkCenterCapacityAdministrationV1 service,
                CancellationToken cancellationToken) =>
            await ReadAsync(httpContext, () => service.GetAsync(cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsWorkCenterCapacityPermissionManifestV1.Manage)
            .WithName("GetSettingsWorkCenterCapacitiesV1")
            .Produces<WorkCenterCapacityViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(WorkCenterCapacityProblemCodesV1.RequestInvalid)
            .ProducesErpServiceUnavailable(WorkCenterCapacityProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/{workCenterId:guid}/versions", async (
                Guid workCenterId,
                RecordWorkCenterCapacityRequestV1 request,
                ISettingsWorkCenterCapacityAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.RecordAsync(workCenterId, request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsWorkCenterCapacityPermissionManifestV1.Manage)
            .WithName("RecordSettingsWorkCenterCapacityV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict)
            .Accepts<RecordWorkCenterCapacityRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpNotFound(WorkCenterCapacityProblemCodesV1.WorkCenterNotFound)
            .ProducesErpConflict(
                WorkCenterCapacityProblemCodesV1.IdempotencyConflict,
                WorkCenterCapacityProblemCodesV1.VersionConflict)
            .ProducesErpServiceUnavailable(WorkCenterCapacityProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        return endpoints;
    }

    private static async Task<IResult> ReadAsync<T>(HttpContext httpContext, Func<Task<T>> read)
    {
        httpContext.Response.Headers.CacheControl = "private, no-store";
        if (httpContext.Request.Query.Count != 0)
        {
            return Problem(StatusCodes.Status400BadRequest, WorkCenterCapacityProblemCodesV1.RequestInvalid,
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
        Func<SettingsCommandMetadataV1, Task<WorkCenterCapacityCommandResultV1>> execute)
    {
        httpContext.Response.Headers.CacheControl = "private, no-store";
        if (httpContext.Request.Query.Count != 0)
        {
            return Problem(StatusCodes.Status400BadRequest, WorkCenterCapacityProblemCodesV1.RequestInvalid,
                "İstek geçersiz.", "Sorgu parametresi kabul edilmez; şirket sunucu tarafından belirlenir.");
        }

        if (!TryReadMetadata(httpContext, currentUser, companyContext, correlationContext, out var metadata))
        {
            return Problem(StatusCodes.Status400BadRequest, WorkCenterCapacityProblemCodesV1.CommandMetadataInvalid,
                "Komut bilgisi eksik.", "İşlem için tek ve geçerli bir Idempotency-Key ve correlation bilgisi gerekir.");
        }

        WorkCenterCapacityCommandResultV1 result;
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
            _ => throw new InvalidOperationException("İş merkezi kapasitesi komut sonucu HTTP durumuyla eşleşmiyor."),
        };
    }

    private static IResult FromPrerequisite(SettingsPrerequisiteException exception) => exception.Code switch
    {
        WorkCenterCapacityProblemCodesV1.AccessDenied => Results.Problem(statusCode: StatusCodes.Status403Forbidden),
        WorkCenterCapacityProblemCodesV1.RequestInvalid => Problem(StatusCodes.Status400BadRequest,
            WorkCenterCapacityProblemCodesV1.RequestInvalid, "İstek geçersiz.", "İstek alanları geçersiz."),
        _ => Problem(StatusCodes.Status503ServiceUnavailable, WorkCenterCapacityProblemCodesV1.ProviderUnavailable,
            "Ayarlar kullanılamıyor.",
            "İş merkezi kapasitesi şu anda okunamıyor veya yazılamıyor; daha sonra yeniden deneyin."),
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
