using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.Endpoints;

public static class SettingsCollateralTypeEndpoints
{
    public const string CollateralTypesPath = "/api/v1/settings/collateral-types";
    private const string IdempotencyHeaderName = "Idempotency-Key";
    private const int MaximumIdempotencyKeyLength = 128;

    internal static readonly string[] CommandBadRequestCodes =
    [
        SettingsCollateralTypeProblemCodesV1.RequestInvalid,
        SettingsCollateralTypeProblemCodesV1.CommandMetadataInvalid,
        SettingsCollateralTypeProblemCodesV1.ReasonRequired,
        SettingsCollateralTypeProblemCodesV1.ReasonTooLong,
    ];

    public static IEndpointRouteBuilder MapSettingsCollateralTypeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(CollateralTypesPath)
            .RequireAuthorization()
            .WithTags("Settings");

        const string permission = SettingsCollateralTypePermissionsV1.Manage;
        {
            group.MapGet("", async (
                    HttpContext httpContext,
                    ISettingsCollateralTypeAdministrationV1 service,
                    CancellationToken cancellationToken) =>
                await ReadAsync(httpContext, () => service.GetAsync(cancellationToken)))
                .RequireCompanyContext()
                .RequireAuthorization(permission)
                .WithName("GetSettingsCollateralTypesV1")
                .Produces<SettingsCollateralTypesViewV1>(StatusCodes.Status200OK)
                .ProducesErpBadRequest(SettingsCollateralTypeProblemCodesV1.RequestInvalid)
                .ProducesErpServiceUnavailable(SettingsCollateralTypeProblemCodesV1.ProviderUnavailable)
                .ProducesErpAuthenticatedProblems();

            group.MapPost("", async (
                    CreateSettingsCollateralTypeRequestV1 request,
                    ISettingsCollateralTypeAdministrationV1 service,
                    ICurrentUser currentUser,
                    ICompanyContext companyContext,
                    ICorrelationContext correlationContext,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                    metadata => service.CreateAsync(request, metadata, cancellationToken)))
                .RequireCompanyContext()
                .RequireAuthorization(permission)
                .WithName("CreateSettingsCollateralTypeV1")
                .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status409Conflict)
                .Accepts<CreateSettingsCollateralTypeRequestV1>("application/json")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesErpBadRequest(CommandBadRequestCodes)
                .AllowsCodeLessErpBadRequestBody()
                .ProducesErpUnsupportedMediaType()
                .ProducesErpConflict(
                    SettingsCollateralTypeProblemCodesV1.IdempotencyConflict,
                    SettingsCollateralTypeProblemCodesV1.CodeConflict)
                .ProducesErpServiceUnavailable(SettingsCollateralTypeProblemCodesV1.ProviderUnavailable)
                .ProducesErpAuthenticatedProblems();

            group.MapPost("/{entryId:guid}/versions", async (
                    Guid entryId,
                    RecordSettingsCollateralTypeRequestV1 request,
                    ISettingsCollateralTypeAdministrationV1 service,
                    ICurrentUser currentUser,
                    ICompanyContext companyContext,
                    ICorrelationContext correlationContext,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                    metadata => service.RecordAsync(entryId, request, metadata, cancellationToken)))
                .RequireCompanyContext()
                .RequireAuthorization(permission)
                .WithName("RecordSettingsCollateralTypeVersionV1")
                .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
                    StatusCodes.Status409Conflict)
                .Accepts<RecordSettingsCollateralTypeRequestV1>("application/json")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesErpBadRequest(CommandBadRequestCodes)
                .AllowsCodeLessErpBadRequestBody()
                .ProducesErpUnsupportedMediaType()
                .ProducesErpNotFound(SettingsCollateralTypeProblemCodesV1.EntryNotFound)
                .ProducesErpConflict(
                    SettingsCollateralTypeProblemCodesV1.IdempotencyConflict,
                    SettingsCollateralTypeProblemCodesV1.VersionConflict)
                .ProducesErpServiceUnavailable(SettingsCollateralTypeProblemCodesV1.ProviderUnavailable)
                .ProducesErpAuthenticatedProblems();
        }

        return endpoints;
    }

    private static async Task<IResult> ReadAsync<T>(HttpContext httpContext, Func<Task<T>> read)
    {
        httpContext.Response.Headers.CacheControl = "private, no-store";
        if (httpContext.Request.Query.Count != 0)
        {
            return Problem(StatusCodes.Status400BadRequest, SettingsCollateralTypeProblemCodesV1.RequestInvalid,
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
        Func<SettingsCommandMetadataV1, Task<SettingsCollateralTypeCommandResultV1>> execute)
    {
        httpContext.Response.Headers.CacheControl = "private, no-store";
        if (httpContext.Request.Query.Count != 0)
        {
            return Problem(StatusCodes.Status400BadRequest, SettingsCollateralTypeProblemCodesV1.RequestInvalid,
                "İstek geçersiz.", "Sorgu parametresi kabul edilmez; şirket sunucu tarafından belirlenir.");
        }

        if (!TryReadMetadata(httpContext, currentUser, companyContext, correlationContext, out var metadata))
        {
            return Problem(StatusCodes.Status400BadRequest, SettingsCollateralTypeProblemCodesV1.CommandMetadataInvalid,
                "Komut bilgisi eksik.", "İşlem için tek ve geçerli bir Idempotency-Key ve correlation bilgisi gerekir.");
        }

        SettingsCollateralTypeCommandResultV1 result;
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
            _ => throw new InvalidOperationException("Teminat türü komut sonucu HTTP durumuyla eşleşmiyor."),
        };
    }

    private static IResult FromPrerequisite(SettingsPrerequisiteException exception) => exception.Code switch
    {
        SettingsCollateralTypeProblemCodesV1.AccessDenied => Results.Problem(statusCode: StatusCodes.Status403Forbidden),
        SettingsCollateralTypeProblemCodesV1.RequestInvalid => Problem(StatusCodes.Status400BadRequest,
            SettingsCollateralTypeProblemCodesV1.RequestInvalid, "İstek geçersiz.", "İstek alanları geçersiz."),
        _ => Problem(StatusCodes.Status503ServiceUnavailable, SettingsCollateralTypeProblemCodesV1.ProviderUnavailable,
            "Ayarlar kullanılamıyor.",
            "Teminat türü şu anda okunamıyor veya yazılamıyor; daha sonra yeniden deneyin."),
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
