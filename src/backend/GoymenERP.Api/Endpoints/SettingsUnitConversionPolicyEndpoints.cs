using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// F-004 (BS-9/BS-13/BS-16/BS-17; URN-D7…D9): the company's unit conversion policy — density, coefficient and document total
/// decimals and rounding, whole-unit rounding, weighbridge tolerance with its boundary and base — as a persistent, versioned
/// Settings record. Every route is in the server-resolved company (X-Company-Id) under
/// <see cref="SettingsUnitConversionPolicyPermissionManifestV1.Manage"/> (company-wide, not Critical, granted to no role
/// here), re-authorized by the service. Responses are private, no-store.
/// <list type="bullet">
/// <item><c>GET /api/v1/settings/unit-conversion-policy</c> — the timeline (empty until the first version is recorded).</item>
/// <item><c>POST /api/v1/settings/unit-conversion-policy/versions</c> (204) — a new version from its business date (today or
/// later).</item>
/// </list>
/// Commands: one Idempotency-Key (stored terminal answers replay with <c>Idempotency-Replayed</c>), the expected version as
/// decimal text, a mandatory reason (≤ 4000).
/// </summary>
public static class SettingsUnitConversionPolicyEndpoints
{
    public const string UnitConversionPolicyPath = "/api/v1/settings/unit-conversion-policy";
    private const string IdempotencyHeaderName = "Idempotency-Key";
    private const int MaximumIdempotencyKeyLength = 128;

    internal static readonly string[] CommandBadRequestCodes =
    [
        UnitConversionPolicyProblemCodesV1.RequestInvalid,
        UnitConversionPolicyProblemCodesV1.CommandMetadataInvalid,
        UnitConversionPolicyProblemCodesV1.ReasonRequired,
        UnitConversionPolicyProblemCodesV1.ReasonTooLong,
    ];

    public static IEndpointRouteBuilder MapSettingsUnitConversionPolicyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(UnitConversionPolicyPath)
            .RequireAuthorization()
            .WithTags("Settings");

        group.MapGet("", async (
                HttpContext httpContext,
                ISettingsUnitConversionPolicyAdministrationV1 service,
                CancellationToken cancellationToken) =>
            await ReadAsync(httpContext, () => service.GetAsync(cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsUnitConversionPolicyPermissionManifestV1.Manage)
            .WithName("GetSettingsUnitConversionPolicyV1")
            .Produces<UnitConversionPolicyViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(UnitConversionPolicyProblemCodesV1.RequestInvalid)
            .ProducesErpServiceUnavailable(UnitConversionPolicyProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/versions", async (
                RecordUnitConversionPolicyRequestV1 request,
                ISettingsUnitConversionPolicyAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.RecordAsync(request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsUnitConversionPolicyPermissionManifestV1.Manage)
            .WithName("RecordSettingsUnitConversionPolicyV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .Accepts<RecordUnitConversionPolicyRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(
                UnitConversionPolicyProblemCodesV1.IdempotencyConflict,
                UnitConversionPolicyProblemCodesV1.VersionConflict)
            .ProducesErpUnprocessableEntity(UnitConversionPolicyProblemCodesV1.EffectiveFromPast)
            .ProducesErpServiceUnavailable(UnitConversionPolicyProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        return endpoints;
    }

    private static async Task<IResult> ReadAsync<T>(HttpContext httpContext, Func<Task<T>> read)
    {
        httpContext.Response.Headers.CacheControl = "private, no-store";
        if (httpContext.Request.Query.Count != 0)
        {
            return Problem(StatusCodes.Status400BadRequest, UnitConversionPolicyProblemCodesV1.RequestInvalid,
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
        Func<SettingsCommandMetadataV1, Task<UnitConversionPolicyCommandResultV1>> execute)
    {
        httpContext.Response.Headers.CacheControl = "private, no-store";
        if (httpContext.Request.Query.Count != 0)
        {
            return Problem(StatusCodes.Status400BadRequest, UnitConversionPolicyProblemCodesV1.RequestInvalid,
                "İstek geçersiz.", "Sorgu parametresi kabul edilmez; şirket sunucu tarafından belirlenir.");
        }

        if (!TryReadMetadata(httpContext, currentUser, companyContext, correlationContext, out var metadata))
        {
            return Problem(StatusCodes.Status400BadRequest, UnitConversionPolicyProblemCodesV1.CommandMetadataInvalid,
                "Komut bilgisi eksik.", "İşlem için tek ve geçerli bir Idempotency-Key ve correlation bilgisi gerekir.");
        }

        UnitConversionPolicyCommandResultV1 result;
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
            _ => throw new InvalidOperationException("Birim çevrim politikası komut sonucu HTTP durumuyla eşleşmiyor."),
        };
    }

    private static IResult FromPrerequisite(SettingsPrerequisiteException exception) => exception.Code switch
    {
        UnitConversionPolicyProblemCodesV1.AccessDenied => Results.Problem(statusCode: StatusCodes.Status403Forbidden),
        UnitConversionPolicyProblemCodesV1.RequestInvalid => Problem(StatusCodes.Status400BadRequest,
            UnitConversionPolicyProblemCodesV1.RequestInvalid, "İstek geçersiz.", "İstek alanları geçersiz."),
        _ => Problem(StatusCodes.Status503ServiceUnavailable, UnitConversionPolicyProblemCodesV1.ProviderUnavailable,
            "Ayarlar kullanılamıyor.",
            "Birim çevrim politikası şu anda okunamıyor veya yazılamıyor; daha sonra yeniden deneyin."),
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
