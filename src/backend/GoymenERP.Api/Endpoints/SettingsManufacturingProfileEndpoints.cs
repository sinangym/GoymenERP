using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// Üretim U1-a — API-01 / API-02 (URT-D6, D7, D9, D10; DEC-SET-001): the Settings owner's Manufacturing parameter profiles
/// and the write path of the Manufacturing tolerance registry, in the server-resolved company (X-Company-Id). Each action has
/// its own permission name (company-wide, not Critical, granted to no role here), re-authorized by the service.
/// <list type="bullet">
/// <item><c>GET /api/v1/settings/manufacturing-parameter-profiles</c> — the reviewed version-0 catalog and every Settings
/// version (all statuses) with the declared keys; <c>GET …/{profileId}</c> one profile (another company's id is the same
/// code-less 404 as an unknown one).</item>
/// <item><c>POST …</c> prepare a draft (200 with the receipt) · <c>POST …/{profileId}/approval</c> ·
/// <c>POST …/{profileId}/publication</c>; the approver and the publisher are not the preparer.</item>
/// <item>The same five routes under <c>/api/v1/settings/manufacturing-tolerance-profiles</c>; publication writes the immutable
/// registry row the close path reads.</item>
/// </list>
/// Commands: one Idempotency-Key (stored terminal answers replay with <c>Idempotency-Replayed</c>), the expected version as
/// decimal text, a mandatory reason (≤ 500). Responses are private, no-store.
/// </summary>
public static class SettingsManufacturingProfileEndpoints
{
    public const string ParameterProfilesPath = "/api/v1/settings/manufacturing-parameter-profiles";
    public const string ToleranceProfilesPath = "/api/v1/settings/manufacturing-tolerance-profiles";
    private const string IdempotencyHeaderName = "Idempotency-Key";
    private const int MaximumIdempotencyKeyLength = 128;

    private static readonly string[] CommandBadRequestCodes =
    [
        ManufacturingProfileProblemCodesV1.RequestInvalid,
        ManufacturingProfileProblemCodesV1.CommandMetadataInvalid,
        ManufacturingProfileProblemCodesV1.ReasonRequired,
        ManufacturingProfileProblemCodesV1.ReasonTooLong,
    ];

    public static IEndpointRouteBuilder MapSettingsManufacturingProfileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var parameters = endpoints.MapGroup(ParameterProfilesPath).RequireAuthorization().WithTags("Settings");

        parameters.MapGet("", async (
                HttpContext httpContext,
                ISettingsManufacturingParameterProfileAdministrationV1 service,
                CancellationToken cancellationToken) =>
            await ReadAsync(httpContext, () => service.ListAsync(cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ManufacturingParameterProfileActionsV1.Read)
            .WithName("ListSettingsManufacturingParameterProfilesV1")
            .Produces<ManufacturingParameterProfileListViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(ManufacturingProfileProblemCodesV1.RequestInvalid)
            .ProducesErpServiceUnavailable(ManufacturingProfileProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        parameters.MapGet("/{profileId:guid}", async (
                Guid profileId,
                HttpContext httpContext,
                ISettingsManufacturingParameterProfileAdministrationV1 service,
                CancellationToken cancellationToken) =>
            await ReadOneAsync(httpContext, () => service.GetAsync(profileId, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ManufacturingParameterProfileActionsV1.Read)
            .RequiresNonEmptyGuidPathParameter("profileId")
            .WithName("GetSettingsManufacturingParameterProfileV1")
            .Produces<ManufacturingParameterProfileViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(ManufacturingProfileProblemCodesV1.RequestInvalid)
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable(ManufacturingProfileProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        Prepare(parameters.MapPost("", async (
                PrepareManufacturingParameterProfileRequestV1 request,
                ISettingsManufacturingParameterProfileAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.PrepareAsync(request, metadata, cancellationToken))),
            ManufacturingParameterProfileActionsV1.Prepare, "PrepareSettingsManufacturingParameterProfileV1")
            .Accepts<PrepareManufacturingParameterProfileRequestV1>("application/json");

        Transition(parameters.MapPost("/{profileId:guid}/approval", async (
                Guid profileId,
                ManufacturingProfileLifecycleRequestV1 request,
                ISettingsManufacturingParameterProfileAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.ApproveAsync(profileId, request, metadata, cancellationToken))),
            ManufacturingParameterProfileActionsV1.Approve, "ApproveSettingsManufacturingParameterProfileV1", publish: false);

        Transition(parameters.MapPost("/{profileId:guid}/publication", async (
                Guid profileId,
                ManufacturingProfileLifecycleRequestV1 request,
                ISettingsManufacturingParameterProfileAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.PublishAsync(profileId, request, metadata, cancellationToken))),
            ManufacturingParameterProfileActionsV1.Publish, "PublishSettingsManufacturingParameterProfileV1", publish: true);

        var tolerances = endpoints.MapGroup(ToleranceProfilesPath).RequireAuthorization().WithTags("Settings");

        tolerances.MapGet("", async (
                HttpContext httpContext,
                ISettingsManufacturingToleranceProfileAdministrationV1 service,
                CancellationToken cancellationToken) =>
            await ReadAsync(httpContext, () => service.ListAsync(cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ManufacturingToleranceProfileReadActionV1.Read)
            .WithName("ListSettingsManufacturingToleranceProfilesV1")
            .Produces<ManufacturingToleranceProfileListViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(ManufacturingProfileProblemCodesV1.RequestInvalid)
            .ProducesErpServiceUnavailable(ManufacturingProfileProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        tolerances.MapGet("/{profileId:guid}", async (
                Guid profileId,
                HttpContext httpContext,
                ISettingsManufacturingToleranceProfileAdministrationV1 service,
                CancellationToken cancellationToken) =>
            await ReadOneAsync(httpContext, () => service.GetAsync(profileId, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ManufacturingToleranceProfileReadActionV1.Read)
            .RequiresNonEmptyGuidPathParameter("profileId")
            .WithName("GetSettingsManufacturingToleranceProfileV1")
            .Produces<ManufacturingToleranceProfileViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(ManufacturingProfileProblemCodesV1.RequestInvalid)
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable(ManufacturingProfileProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        Prepare(tolerances.MapPost("", async (
                PrepareManufacturingToleranceProfileRequestV1 request,
                ISettingsManufacturingToleranceProfileAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.PrepareAsync(request, metadata, cancellationToken))),
            ManufacturingToleranceProfileActionsV1.Prepare, "PrepareSettingsManufacturingToleranceProfileV1")
            .Accepts<PrepareManufacturingToleranceProfileRequestV1>("application/json");

        Transition(tolerances.MapPost("/{profileId:guid}/approval", async (
                Guid profileId,
                ManufacturingProfileLifecycleRequestV1 request,
                ISettingsManufacturingToleranceProfileAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.ApproveAsync(profileId, request, metadata, cancellationToken))),
            ManufacturingToleranceProfileActionsV1.Approve, "ApproveSettingsManufacturingToleranceProfileV1", publish: false);

        Transition(tolerances.MapPost("/{profileId:guid}/publication", async (
                Guid profileId,
                ManufacturingProfileLifecycleRequestV1 request,
                ISettingsManufacturingToleranceProfileAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.PublishAsync(profileId, request, metadata, cancellationToken))),
            ManufacturingToleranceProfileActionsV1.Publish, "PublishSettingsManufacturingToleranceProfileV1", publish: true,
            windowOverlap: true);

        return endpoints;
    }

    private static RouteHandlerBuilder Prepare(RouteHandlerBuilder builder, string permission, string name) =>
        builder
            .RequireCompanyContext()
            .RequireAuthorization(permission)
            .WithName(name)
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .Produces<ManufacturingProfileCommandReceiptV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(
                ManufacturingProfileProblemCodesV1.IdempotencyConflict,
                ManufacturingProfileProblemCodesV1.VersionConflict)
            .ProducesErpUnprocessableEntity(ManufacturingProfileProblemCodesV1.EffectiveFromPast)
            .ProducesErpServiceUnavailable(ManufacturingProfileProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

    private static RouteHandlerBuilder Transition(RouteHandlerBuilder builder, string permission, string name, bool publish,
        bool windowOverlap = false)
    {
        string[] conflicts = windowOverlap
            ?
            [
                ManufacturingProfileProblemCodesV1.IdempotencyConflict,
                ManufacturingProfileProblemCodesV1.VersionConflict,
                ManufacturingProfileProblemCodesV1.TransitionInvalid,
                ManufacturingProfileProblemCodesV1.WindowOverlap,
            ]
            :
            [
                ManufacturingProfileProblemCodesV1.IdempotencyConflict,
                ManufacturingProfileProblemCodesV1.VersionConflict,
                ManufacturingProfileProblemCodesV1.TransitionInvalid,
            ];
        string[] unprocessable = publish
            ? [ManufacturingProfileProblemCodesV1.MakerSeparationRequired, ManufacturingProfileProblemCodesV1.EffectiveFromPast]
            : [ManufacturingProfileProblemCodesV1.MakerSeparationRequired];
        return builder
            .RequireCompanyContext()
            .RequireAuthorization(permission)
            .RequiresNonEmptyGuidPathParameter("profileId")
            .WithName(name)
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .Accepts<ManufacturingProfileLifecycleRequestV1>("application/json")
            .Produces<ManufacturingProfileCommandReceiptV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(conflicts)
            .ProducesErpUnprocessableEntity(unprocessable)
            .ProducesErpServiceUnavailable(ManufacturingProfileProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();
    }

    private static async Task<IResult> ReadAsync<T>(HttpContext httpContext, Func<Task<T>> read)
    {
        httpContext.Response.Headers.CacheControl = "private, no-store";
        if (httpContext.Request.Query.Count != 0)
        {
            return QueryRefusal();
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

    private static async Task<IResult> ReadOneAsync<T>(HttpContext httpContext, Func<Task<T?>> read)
        where T : class
    {
        httpContext.Response.Headers.CacheControl = "private, no-store";
        if (httpContext.Request.Query.Count != 0)
        {
            return QueryRefusal();
        }

        try
        {
            return await read() is { } view
                ? Results.Ok(view)
                : Results.Problem(title: "Kayıt bulunamadı.", detail: "İstenen üretim profili bu şirkette bulunamadı.",
                    statusCode: StatusCodes.Status404NotFound);
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

    private static IResult QueryRefusal() => Problem(StatusCodes.Status400BadRequest,
        ManufacturingProfileProblemCodesV1.RequestInvalid, "İstek geçersiz.",
        "Şirket yalnız X-Company-Id başlığından belirlenir; sorgu parametresi kabul edilmez.");

    private static async Task<IResult> CommandAsync(
        HttpContext httpContext,
        ICurrentUser currentUser,
        ICompanyContext companyContext,
        ICorrelationContext correlationContext,
        Func<SettingsCommandMetadataV1, Task<ManufacturingProfileCommandResultV1>> execute)
    {
        httpContext.Response.Headers.CacheControl = "private, no-store";
        if (httpContext.Request.Query.Count != 0)
        {
            return QueryRefusal();
        }

        if (!TryReadMetadata(httpContext, currentUser, companyContext, correlationContext, out var metadata))
        {
            return Problem(StatusCodes.Status400BadRequest, ManufacturingProfileProblemCodesV1.CommandMetadataInvalid,
                "Komut bilgisi eksik.", "İşlem için tek ve geçerli bir Idempotency-Key ve correlation bilgisi gerekir.");
        }

        ManufacturingProfileCommandResultV1 result;
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
            StatusCodes.Status200OK when answer.Receipt is { } receipt => Results.Ok(receipt),
            StatusCodes.Status404NotFound when answer.Code is null => Results.Problem(title: answer.Title,
                detail: answer.Detail, statusCode: StatusCodes.Status404NotFound),
            _ when answer.Code is { } code => Problem(answer.StatusCode, code, answer.Title, answer.Detail),
            _ => throw new InvalidOperationException("Üretim profili komut sonucu HTTP durumuyla eşleşmiyor."),
        };
    }

    private static IResult FromPrerequisite(SettingsPrerequisiteException exception) => exception.Code switch
    {
        ManufacturingProfileProblemCodesV1.AccessDenied => Results.Problem(statusCode: StatusCodes.Status403Forbidden),
        _ => Problem(StatusCodes.Status503ServiceUnavailable, ManufacturingProfileProblemCodesV1.ProviderUnavailable,
            "Ayarlar kullanılamıyor.", "Üretim profilleri şu anda okunamıyor veya yazılamıyor; daha sonra yeniden deneyin."),
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
