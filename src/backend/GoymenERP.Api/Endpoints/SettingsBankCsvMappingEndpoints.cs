using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// FNS-D52: the company's per-bank CSV column mappings of the bank statement import (XLSX and MT940 need none) as a
/// persistent, versioned Settings record. Every route is in the server-resolved company (X-Company-Id) under
/// <see cref="SettingsBankCsvMappingPermissionManifestV1.Manage"/> (company-wide, not Critical, granted to no role here),
/// re-authorized by the service. Responses are private, no-store.
/// <list type="bullet">
/// <item><c>GET /api/v1/settings/bank-csv-mappings</c> — every bank's mapping history (empty until the first mapping is
/// recorded; no bank is seeded).</item>
/// <item><c>POST /api/v1/settings/bank-csv-mappings/versions</c> (204) — a new version of one bank's mapping (the latest is in
/// force; stopping is an <c>Inactive</c> version).</item>
/// </list>
/// Commands: one Idempotency-Key (stored terminal answers replay with <c>Idempotency-Replayed</c>), the bank's expected
/// version as decimal text, a mandatory reason (≤ 4000).
/// </summary>
public static class SettingsBankCsvMappingEndpoints
{
    public const string BankCsvMappingsPath = "/api/v1/settings/bank-csv-mappings";
    private const string IdempotencyHeaderName = "Idempotency-Key";
    private const int MaximumIdempotencyKeyLength = 128;

    internal static readonly string[] CommandBadRequestCodes =
    [
        BankCsvMappingProblemCodesV1.RequestInvalid,
        BankCsvMappingProblemCodesV1.CommandMetadataInvalid,
        BankCsvMappingProblemCodesV1.ReasonRequired,
        BankCsvMappingProblemCodesV1.ReasonTooLong,
    ];

    public static IEndpointRouteBuilder MapSettingsBankCsvMappingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BankCsvMappingsPath)
            .RequireAuthorization()
            .WithTags("Settings");

        group.MapGet("", async (
                HttpContext httpContext,
                ISettingsBankCsvMappingAdministrationV1 service,
                CancellationToken cancellationToken) =>
            await ReadAsync(httpContext, () => service.GetAsync(cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsBankCsvMappingPermissionManifestV1.Manage)
            .WithName("GetSettingsBankCsvMappingsV1")
            .Produces<BankCsvMappingsViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(BankCsvMappingProblemCodesV1.RequestInvalid)
            .ProducesErpServiceUnavailable(BankCsvMappingProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/versions", async (
                RecordBankCsvMappingRequestV1 request,
                ISettingsBankCsvMappingAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.RecordAsync(request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsBankCsvMappingPermissionManifestV1.Manage)
            .WithName("RecordSettingsBankCsvMappingV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status409Conflict)
            .Accepts<RecordBankCsvMappingRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(
                BankCsvMappingProblemCodesV1.IdempotencyConflict,
                BankCsvMappingProblemCodesV1.VersionConflict)
            .ProducesErpServiceUnavailable(BankCsvMappingProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        return endpoints;
    }

    private static async Task<IResult> ReadAsync<T>(HttpContext httpContext, Func<Task<T>> read)
    {
        httpContext.Response.Headers.CacheControl = "private, no-store";
        if (httpContext.Request.Query.Count != 0)
        {
            return Problem(StatusCodes.Status400BadRequest, BankCsvMappingProblemCodesV1.RequestInvalid,
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
        Func<SettingsCommandMetadataV1, Task<BankCsvMappingCommandResultV1>> execute)
    {
        httpContext.Response.Headers.CacheControl = "private, no-store";
        if (httpContext.Request.Query.Count != 0)
        {
            return Problem(StatusCodes.Status400BadRequest, BankCsvMappingProblemCodesV1.RequestInvalid,
                "İstek geçersiz.", "Sorgu parametresi kabul edilmez; şirket sunucu tarafından belirlenir.");
        }

        if (!TryReadMetadata(httpContext, currentUser, companyContext, correlationContext, out var metadata))
        {
            return Problem(StatusCodes.Status400BadRequest, BankCsvMappingProblemCodesV1.CommandMetadataInvalid,
                "Komut bilgisi eksik.", "İşlem için tek ve geçerli bir Idempotency-Key ve correlation bilgisi gerekir.");
        }

        BankCsvMappingCommandResultV1 result;
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
            _ => throw new InvalidOperationException("Banka CSV eşleme komut sonucu HTTP durumuyla eşleşmiyor."),
        };
    }

    private static IResult FromPrerequisite(SettingsPrerequisiteException exception) => exception.Code switch
    {
        BankCsvMappingProblemCodesV1.AccessDenied => Results.Problem(statusCode: StatusCodes.Status403Forbidden),
        BankCsvMappingProblemCodesV1.RequestInvalid => Problem(StatusCodes.Status400BadRequest,
            BankCsvMappingProblemCodesV1.RequestInvalid, "İstek geçersiz.", "İstek alanları geçersiz."),
        _ => Problem(StatusCodes.Status503ServiceUnavailable, BankCsvMappingProblemCodesV1.ProviderUnavailable,
            "Ayarlar kullanılamıyor.",
            "Banka CSV sütun eşlemeleri şu anda okunamıyor veya yazılamıyor; daha sonra yeniden deneyin."),
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
