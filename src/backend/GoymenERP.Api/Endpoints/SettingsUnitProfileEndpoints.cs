using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// F-029 S1/S2 (URUN_F029 spec §5.4(4); URN-D2…D5): the company's unit profiles — which unit it may use, from when and at
/// what quantity scale — as a persistent, versioned Settings record behind the unchanged prerequisite port. Every route is in
/// the server-resolved company (X-Company-Id) under <see cref="SettingsUnitProfilePermissionManifestV1.Manage"/>
/// (company-wide, not Critical, granted to no role here), re-authorized by the service. Responses are private, no-store.
/// <list type="bullet">
/// <item><c>GET /api/v1/settings/unit-profiles</c> — the company's profiles per unit (the approved catalog's entries until the
/// baseline transfer, the stored timeline afterwards) and the unit identities a profile may be recorded for.</item>
/// <item><c>POST /api/v1/settings/unit-profiles/baseline-transfer</c> (204) — the one-time transfer of the approved catalog's
/// entries, values unchanged (URN-D5).</item>
/// <item><c>POST /api/v1/settings/unit-profiles/versions</c> (204) — a new version of one unit from its business date (today
/// or later, URN-D4).</item>
/// </list>
/// Commands: one Idempotency-Key (stored terminal answers replay with <c>Idempotency-Replayed</c>), the unit's expected
/// version as decimal text, a mandatory reason (≤ 4000).
/// </summary>
public static class SettingsUnitProfileEndpoints
{
    public const string UnitProfilesPath = "/api/v1/settings/unit-profiles";
    private const string IdempotencyHeaderName = "Idempotency-Key";
    private const int MaximumIdempotencyKeyLength = 128;

    internal static readonly string[] CommandBadRequestCodes =
    [
        UnitProfileProblemCodesV1.RequestInvalid,
        UnitProfileProblemCodesV1.CommandMetadataInvalid,
        UnitProfileProblemCodesV1.ReasonRequired,
        UnitProfileProblemCodesV1.ReasonTooLong,
    ];

    internal static readonly string[] RecordBadRequestCodes =
    [
        UnitProfileProblemCodesV1.RequestInvalid,
        UnitProfileProblemCodesV1.CommandMetadataInvalid,
        UnitProfileProblemCodesV1.ReasonRequired,
        UnitProfileProblemCodesV1.ReasonTooLong,
        UnitProfileProblemCodesV1.UnitUnknown,
    ];

    public static IEndpointRouteBuilder MapSettingsUnitProfileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(UnitProfilesPath)
            .RequireAuthorization()
            .WithTags("Settings");

        group.MapGet("", async (
                HttpContext httpContext,
                ISettingsUnitProfileAdministrationV1 service,
                CancellationToken cancellationToken) =>
            await ReadAsync(httpContext, () => service.GetAsync(cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsUnitProfilePermissionManifestV1.Manage)
            .WithName("GetSettingsUnitProfilesV1")
            .Produces<UnitProfileViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(UnitProfileProblemCodesV1.RequestInvalid)
            .ProducesErpServiceUnavailable(UnitProfileProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/baseline-transfer", async (
                TransferUnitProfileBaselineRequestV1 request,
                ISettingsUnitProfileAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.TransferBaselineAsync(request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsUnitProfilePermissionManifestV1.Manage)
            .WithName("TransferSettingsUnitProfileBaselineV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status409Conflict)
            .Accepts<TransferUnitProfileBaselineRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(
                UnitProfileProblemCodesV1.IdempotencyConflict,
                UnitProfileProblemCodesV1.AlreadyTransferred)
            .ProducesErpServiceUnavailable(UnitProfileProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/versions", async (
                RecordUnitProfileRequestV1 request,
                ISettingsUnitProfileAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.RecordAsync(request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsUnitProfilePermissionManifestV1.Manage)
            .WithName("RecordSettingsUnitProfileV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .Accepts<RecordUnitProfileRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(RecordBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(
                UnitProfileProblemCodesV1.IdempotencyConflict,
                UnitProfileProblemCodesV1.VersionConflict,
                UnitProfileProblemCodesV1.BaselineRequired)
            .ProducesErpUnprocessableEntity(UnitProfileProblemCodesV1.EffectiveFromPast)
            .ProducesErpServiceUnavailable(UnitProfileProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        return endpoints;
    }

    private static async Task<IResult> ReadAsync<T>(HttpContext httpContext, Func<Task<T>> read)
    {
        httpContext.Response.Headers.CacheControl = "private, no-store";
        if (httpContext.Request.Query.Count != 0)
        {
            return Problem(StatusCodes.Status400BadRequest, UnitProfileProblemCodesV1.RequestInvalid,
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
        Func<SettingsCommandMetadataV1, Task<UnitProfileCommandResultV1>> execute)
    {
        httpContext.Response.Headers.CacheControl = "private, no-store";
        if (httpContext.Request.Query.Count != 0)
        {
            return Problem(StatusCodes.Status400BadRequest, UnitProfileProblemCodesV1.RequestInvalid,
                "İstek geçersiz.", "Sorgu parametresi kabul edilmez; şirket sunucu tarafından belirlenir.");
        }

        if (!TryReadMetadata(httpContext, currentUser, companyContext, correlationContext, out var metadata))
        {
            return Problem(StatusCodes.Status400BadRequest, UnitProfileProblemCodesV1.CommandMetadataInvalid,
                "Komut bilgisi eksik.", "İşlem için tek ve geçerli bir Idempotency-Key ve correlation bilgisi gerekir.");
        }

        UnitProfileCommandResultV1 result;
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
            _ => throw new InvalidOperationException("Birim profili komut sonucu HTTP durumuyla eşleşmiyor."),
        };
    }

    private static IResult FromPrerequisite(SettingsPrerequisiteException exception) => exception.Code switch
    {
        UnitProfileProblemCodesV1.AccessDenied => Results.Problem(statusCode: StatusCodes.Status403Forbidden),
        _ => Problem(StatusCodes.Status503ServiceUnavailable, UnitProfileProblemCodesV1.ProviderUnavailable,
            "Ayarlar kullanılamıyor.", "Birim profilleri şu anda okunamıyor veya yazılamıyor; daha sonra yeniden deneyin."),
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
