using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// F-024 (NS-01): the company's segregation of duties setting — whether the person who prepared a document may also approve
/// it (default: no, a different person is required; a company run by one person may relax it) — as a persistent, versioned
/// Settings record. Every route is in the server-resolved company (X-Company-Id) under
/// <see cref="SettingsSegregationOfDutiesPolicyPermissionManifestV1.Manage"/> (company-wide, not Critical, granted to no role
/// here), re-authorized by the service. Responses are private, no-store.
/// <list type="bullet">
/// <item><c>GET /api/v1/settings/segregation-of-duties</c> — the setting in force and its history (the default until the first
/// version is recorded). KIM-D39: a stored relaxation found while the company has more than one active user is first closed
/// by a system version.</item>
/// <item><c>POST /api/v1/settings/segregation-of-duties/versions</c> (204) — a new version, in force when saved. KIM-D39: the
/// relaxation is accepted only while the company has exactly one active user (409
/// <c>settings_segregation_of_duties_relaxation_requires_single_user</c> otherwise).</item>
/// </list>
/// Commands: one Idempotency-Key (stored terminal answers replay with <c>Idempotency-Replayed</c>), the expected version as
/// decimal text, a mandatory reason (≤ 4000).
/// </summary>
public static class SettingsSegregationOfDutiesPolicyEndpoints
{
    public const string SegregationOfDutiesPolicyPath = "/api/v1/settings/segregation-of-duties";
    private const string IdempotencyHeaderName = "Idempotency-Key";
    private const int MaximumIdempotencyKeyLength = 128;

    internal static readonly string[] CommandBadRequestCodes =
    [
        SegregationOfDutiesPolicyProblemCodesV1.RequestInvalid,
        SegregationOfDutiesPolicyProblemCodesV1.CommandMetadataInvalid,
        SegregationOfDutiesPolicyProblemCodesV1.ReasonRequired,
        SegregationOfDutiesPolicyProblemCodesV1.ReasonTooLong,
    ];

    public static IEndpointRouteBuilder MapSettingsSegregationOfDutiesPolicyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(SegregationOfDutiesPolicyPath)
            .RequireAuthorization()
            .WithTags("Settings");

        group.MapGet("", async (
                HttpContext httpContext,
                ISettingsSegregationOfDutiesPolicyAdministrationV1 service,
                CancellationToken cancellationToken) =>
            await ReadAsync(httpContext, () => service.GetAsync(cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsSegregationOfDutiesPolicyPermissionManifestV1.Manage)
            .WithName("GetSettingsSegregationOfDutiesPolicyV1")
            .Produces<SegregationOfDutiesPolicyViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(SegregationOfDutiesPolicyProblemCodesV1.RequestInvalid)
            .ProducesErpServiceUnavailable(SegregationOfDutiesPolicyProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/versions", async (
                RecordSegregationOfDutiesPolicyRequestV1 request,
                ISettingsSegregationOfDutiesPolicyAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.RecordAsync(request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsSegregationOfDutiesPolicyPermissionManifestV1.Manage)
            .WithName("RecordSettingsSegregationOfDutiesPolicyV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status409Conflict)
            .Accepts<RecordSegregationOfDutiesPolicyRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(
                SegregationOfDutiesPolicyProblemCodesV1.IdempotencyConflict,
                SegregationOfDutiesPolicyProblemCodesV1.VersionConflict,
                SegregationOfDutiesPolicyProblemCodesV1.RelaxationRequiresSingleUser)
            .ProducesErpServiceUnavailable(SegregationOfDutiesPolicyProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        return endpoints;
    }

    private static async Task<IResult> ReadAsync<T>(HttpContext httpContext, Func<Task<T>> read)
    {
        httpContext.Response.Headers.CacheControl = "private, no-store";
        if (httpContext.Request.Query.Count != 0)
        {
            return Problem(StatusCodes.Status400BadRequest, SegregationOfDutiesPolicyProblemCodesV1.RequestInvalid,
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
        Func<SettingsCommandMetadataV1, Task<SegregationOfDutiesPolicyCommandResultV1>> execute)
    {
        httpContext.Response.Headers.CacheControl = "private, no-store";
        if (httpContext.Request.Query.Count != 0)
        {
            return Problem(StatusCodes.Status400BadRequest, SegregationOfDutiesPolicyProblemCodesV1.RequestInvalid,
                "İstek geçersiz.", "Sorgu parametresi kabul edilmez; şirket sunucu tarafından belirlenir.");
        }

        if (!TryReadMetadata(httpContext, currentUser, companyContext, correlationContext, out var metadata))
        {
            return Problem(StatusCodes.Status400BadRequest, SegregationOfDutiesPolicyProblemCodesV1.CommandMetadataInvalid,
                "Komut bilgisi eksik.", "İşlem için tek ve geçerli bir Idempotency-Key ve correlation bilgisi gerekir.");
        }

        SegregationOfDutiesPolicyCommandResultV1 result;
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
            _ => throw new InvalidOperationException("Görev ayrılığı komut sonucu HTTP durumuyla eşleşmiyor."),
        };
    }

    private static IResult FromPrerequisite(SettingsPrerequisiteException exception) => exception.Code switch
    {
        SegregationOfDutiesPolicyProblemCodesV1.AccessDenied => Results.Problem(statusCode: StatusCodes.Status403Forbidden),
        SegregationOfDutiesPolicyProblemCodesV1.RequestInvalid => Problem(StatusCodes.Status400BadRequest,
            SegregationOfDutiesPolicyProblemCodesV1.RequestInvalid, "İstek geçersiz.", "İstek alanları geçersiz."),
        _ => Problem(StatusCodes.Status503ServiceUnavailable, SegregationOfDutiesPolicyProblemCodesV1.ProviderUnavailable,
            "Ayarlar kullanılamıyor.",
            "Görev ayrılığı ayarı şu anda okunamıyor veya yazılamıyor; daha sonra yeniden deneyin."),
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
