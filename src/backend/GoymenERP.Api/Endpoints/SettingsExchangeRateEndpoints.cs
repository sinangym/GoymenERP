using System.Globalization;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// DEC-FIN-010 / KUR_GUNU SAT-D54/D55 / EK24 SAT-D169…D172: the Settings exchange-rate record, in the server-resolved company
/// (X-Company-Id). Responses are private, no-store.
/// <list type="bullet">
/// <item><c>GET /api/v1/settings/exchange-rates?from=yyyy-MM-dd&amp;to=yyyy-MM-dd</c> (read) — every day of the range: TCMB
/// bulletin state, half-day state, the rates in force (official or manual) and the append-only records.</item>
/// <item><c>GET …/resolution?rateDay=yyyy-MM-dd&amp;currencyCode=USD</c> (read) — the one calculator's answer for a rate day,
/// with its provenance (SAT-D172 snapshot shape), or the "kur bulunamadı/belirlenemedi" code.</item>
/// <item><c>POST …/days/{date}/corrections</c> (correct, 204) — SAT-D169 reasoned manual rate.</item>
/// <item><c>POST …/days/{date}/half-day-marks</c> (mark, 204) — SAT-D198 reasoned full / half-day mark of an undetermined day.</item>
/// <item><c>POST …/days/{date}/reverification</c> (import, 204) — reopen a day's final TCMB answer for the next import.</item>
/// <item><c>POST …/imports</c> (import, 200 with a per-day summary) — the official sources; 503 while disabled (the default;
/// enabling them is a human gate). Every 503 is the platform's code-less <c>service_unavailable</c> on the wire; the list read
/// tells the screen whether the sources are enabled.</item>
/// </list>
/// The four names are company-wide, not Critical and granted to no role here. Commands carry one Idempotency-Key (stored
/// terminal answers replay with <c>Idempotency-Replayed</c>).
/// </summary>
public static class SettingsExchangeRateEndpoints
{
    public const string ExchangeRatesPath = "/api/v1/settings/exchange-rates";
    private const string IdempotencyHeaderName = "Idempotency-Key";
    private const int MaximumIdempotencyKeyLength = 128;
    private const string DateFormat = "yyyy-MM-dd";

    private static readonly string[] CommandBadRequestCodes =
    [
        ExchangeRateProblemCodesV1.RequestInvalid,
        ExchangeRateProblemCodesV1.CommandMetadataInvalid,
        ExchangeRateProblemCodesV1.ReasonRequired,
        ExchangeRateProblemCodesV1.ReasonTooLong,
    ];

    // The import carries no reason: only the request and command-metadata refusals.
    private static readonly string[] ImportBadRequestCodes =
    [
        ExchangeRateProblemCodesV1.RequestInvalid,
        ExchangeRateProblemCodesV1.CommandMetadataInvalid,
    ];

    public static IEndpointRouteBuilder MapSettingsExchangeRateEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(ExchangeRatesPath)
            .RequireAuthorization()
            .WithTags("Settings");

        group.MapGet("", async (
                HttpContext httpContext,
                ISettingsExchangeRateAdministrationV1 service,
                CancellationToken cancellationToken) =>
            {
                if (!TryReadQuery(httpContext.Request.Query, ["from", "to"], out var values)
                    || !TryDate(values["from"], out var from) || !TryDate(values["to"], out var to))
                {
                    return Invalid(httpContext, "from ve to (yyyy-MM-dd) zorunludur; başka sorgu parametresi kabul edilmez.");
                }

                return await ReadAsync(httpContext, () => service.ListAsync(from, to, cancellationToken));
            })
            .RequireCompanyContext()
            .RequireAuthorization(SettingsExchangeRatePermissionManifestV1.Read)
            .WithName("ListSettingsExchangeRatesV1")
            .Produces<ExchangeRateDayListV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(ExchangeRateProblemCodesV1.RequestInvalid)
            .ProducesErpServiceUnavailable(ExchangeRateProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/resolution", async (
                HttpContext httpContext,
                ISettingsExchangeRateAdministrationV1 service,
                CancellationToken cancellationToken) =>
            {
                if (!TryReadQuery(httpContext.Request.Query, ["rateDay", "currencyCode"], out var values)
                    || !TryDate(values["rateDay"], out var rateDay))
                {
                    return Invalid(httpContext, "rateDay (yyyy-MM-dd) ve currencyCode zorunludur; başka sorgu parametresi kabul edilmez.");
                }

                return await ReadAsync(httpContext,
                    () => service.ResolveForCurrencyAsync(rateDay, values["currencyCode"], cancellationToken));
            })
            .RequireCompanyContext()
            .RequireAuthorization(SettingsExchangeRatePermissionManifestV1.Read)
            .WithName("ResolveSettingsExchangeRateV1")
            .Produces<ExchangeRateResolutionV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(ExchangeRateProblemCodesV1.RequestInvalid)
            .ProducesErpServiceUnavailable(ExchangeRateProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/days/{date}/corrections", async (
                string date,
                CorrectExchangeRateRequestV1 request,
                ISettingsExchangeRateAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                if (!TryDate(date, out var day))
                {
                    return Invalid(httpContext, "Gün yyyy-MM-dd biçiminde olmalıdır.");
                }

                return await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                    metadata => service.CorrectAsync(day, request, metadata, cancellationToken));
            })
            .RequireCompanyContext()
            .RequireAuthorization(SettingsExchangeRatePermissionManifestV1.Correct)
            .WithName("CorrectSettingsExchangeRateV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .Accepts<CorrectExchangeRateRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(
                ExchangeRateProblemCodesV1.IdempotencyConflict,
                ExchangeRateProblemCodesV1.VersionConflict)
            .ProducesErpUnprocessableEntity(ExchangeRateProblemCodesV1.DayNotPublished)
            .ProducesErpServiceUnavailable(ExchangeRateProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        // EK31 SAT-D198: a reasoned manual full / half-day mark for a day the automatic source left undetermined.
        group.MapPost("/days/{date}/half-day-marks", async (
                string date,
                MarkExchangeRateHalfDayRequestV1 request,
                ISettingsExchangeRateAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                if (!TryDate(date, out var day))
                {
                    return Invalid(httpContext, "Gün yyyy-MM-dd biçiminde olmalıdır.");
                }

                return await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                    metadata => service.MarkHalfDayAsync(day, request, metadata, cancellationToken));
            })
            .RequireCompanyContext()
            .RequireAuthorization(SettingsExchangeRatePermissionManifestV1.MarkHalfDay)
            .WithName("MarkSettingsExchangeRateHalfDayV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status409Conflict)
            .Accepts<MarkExchangeRateHalfDayRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(
                ExchangeRateProblemCodesV1.IdempotencyConflict,
                ExchangeRateProblemCodesV1.VersionConflict,
                ExchangeRateProblemCodesV1.HalfDayDetermined)
            .ProducesErpServiceUnavailable(ExchangeRateProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        // Review B P2: reopen a day's final TCMB answer so the next import asks again (fail-closed meanwhile).
        group.MapPost("/days/{date}/reverification", async (
                string date,
                ReverifyExchangeRateDayRequestV1 request,
                ISettingsExchangeRateAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                if (!TryDate(date, out var day))
                {
                    return Invalid(httpContext, "Gün yyyy-MM-dd biçiminde olmalıdır.");
                }

                return await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                    metadata => service.ReverifyAsync(day, request, metadata, cancellationToken));
            })
            .RequireCompanyContext()
            .RequireAuthorization(SettingsExchangeRatePermissionManifestV1.Import)
            .WithName("ReverifySettingsExchangeRateDayV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .Accepts<ReverifyExchangeRateDayRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(
                ExchangeRateProblemCodesV1.IdempotencyConflict,
                ExchangeRateProblemCodesV1.VersionConflict)
            .ProducesErpUnprocessableEntity(ExchangeRateProblemCodesV1.DayNotFinal)
            .ProducesErpServiceUnavailable(ExchangeRateProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/imports", async (
                ImportExchangeRatesRequestV1 request,
                ISettingsExchangeRateAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.ImportAsync(request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsExchangeRatePermissionManifestV1.Import)
            .WithName("ImportSettingsExchangeRatesV1")
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status409Conflict)
            .Accepts<ImportExchangeRatesRequestV1>("application/json")
            .Produces<ExchangeRateImportSummaryV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(ImportBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(
                ExchangeRateProblemCodesV1.IdempotencyConflict,
                ExchangeRateProblemCodesV1.VersionConflict)
            .ProducesErpServiceUnavailable(ExchangeRateProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        return endpoints;
    }

    private static bool TryReadQuery(IQueryCollection query, string[] names, out Dictionary<string, string> values)
    {
        values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (query.Count != names.Length)
        {
            return false;
        }

        foreach (var name in names)
        {
            if (!query.TryGetValue(name, out var raw) || raw.Count != 1 || raw[0] is not { Length: > 0 } value)
            {
                return false;
            }

            values[name] = value;
        }

        return true;
    }

    private static bool TryDate(string? text, out DateOnly date) =>
        DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static IResult Invalid(HttpContext httpContext, string detail)
    {
        httpContext.Response.Headers.CacheControl = "private, no-store";
        return Problem(StatusCodes.Status400BadRequest, ExchangeRateProblemCodesV1.RequestInvalid, "İstek geçersiz.", detail);
    }

    private static async Task<IResult> ReadAsync<T>(HttpContext httpContext, Func<Task<T>> read)
    {
        httpContext.Response.Headers.CacheControl = "private, no-store";
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
        Func<SettingsCommandMetadataV1, Task<ExchangeRateCommandResultV1>> execute)
    {
        httpContext.Response.Headers.CacheControl = "private, no-store";
        if (httpContext.Request.Query.Count != 0)
        {
            return Problem(StatusCodes.Status400BadRequest, ExchangeRateProblemCodesV1.RequestInvalid,
                "İstek geçersiz.", "Sorgu parametresi kabul edilmez; şirket sunucu tarafından belirlenir.");
        }

        if (!TryReadMetadata(httpContext, currentUser, companyContext, correlationContext, out var metadata))
        {
            return Problem(StatusCodes.Status400BadRequest, ExchangeRateProblemCodesV1.CommandMetadataInvalid,
                "Komut bilgisi eksik.", "İşlem için tek ve geçerli bir Idempotency-Key ve correlation bilgisi gerekir.");
        }

        ExchangeRateCommandResultV1 result;
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
            StatusCodes.Status200OK when answer.Import is { } import => Results.Ok(import),
            _ when answer.Code is { } code => Problem(answer.StatusCode, code, answer.Title, answer.Detail),
            _ => throw new InvalidOperationException("Kur komut sonucu HTTP durumuyla eşleşmiyor."),
        };
    }

    private static IResult FromPrerequisite(SettingsPrerequisiteException exception) => exception.Code switch
    {
        ExchangeRateProblemCodesV1.AccessDenied => Results.Problem(statusCode: StatusCodes.Status403Forbidden),
        ExchangeRateProblemCodesV1.RequestInvalid => Problem(StatusCodes.Status400BadRequest,
            ExchangeRateProblemCodesV1.RequestInvalid, "İstek geçersiz.",
            $"Gün aralığı en çok {ExchangeRateInputLimitsV1.MaximumListDays} gün; para birimi TRY dışında üç harfli ISO 4217 kodu olmalıdır."),
        _ => Problem(StatusCodes.Status503ServiceUnavailable, ExchangeRateProblemCodesV1.ProviderUnavailable,
            "Ayarlar kullanılamıyor.", "Kur kaydı şu anda okunamıyor veya yazılamıyor; daha sonra yeniden deneyin."),
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
