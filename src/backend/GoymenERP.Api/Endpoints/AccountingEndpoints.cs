using System.Globalization;
using GoymenERP.Api.Errors;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain;
using Microsoft.Net.Http.Headers;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// ACC-V0 (card approved 2026-09-19): the authorized Accounting surface for the server-resolved company —
/// periods (propose; approve by ANOTHER person, Ö4; close, Ö5), the journal read (M-04, read-only) and the
/// posting queues (M-01, read-only). Every endpoint needs the company context (<c>X-Company-Id</c>) and its
/// own narrow company-wide permission; a write also needs an <c>Idempotency-Key</c> (ADR-011: one attempt
/// per command and key, replayed with <c>Idempotency-Replayed</c> and
/// <c>Idempotency-Original-Correlation-Id</c>; another request or actor under the key is 409) and is measured
/// again against fresh authority before it is stored. Another company's period or journal is the same
/// code-less 404 as an unknown id. No voucher is created, approved or reversed here (ACC-V1).
/// </summary>
public static class AccountingEndpoints
{
    private const string IdempotencyHeaderName = "Idempotency-Key";
    private const int MaximumIdempotencyKeyLength = 128;
    private const int MaximumCursorLength = 512;

    private static readonly HashSet<string> TrialBalanceQueryNames =
        new(StringComparer.Ordinal) { "fromAccountingDate", "throughAccountingDate", "currencyId" };

    private static readonly string[] BadRequestCodes = [AccountingApiCodes.RequestInvalid];

    private static readonly string[] PeriodConflictCodes =
    [
        AccountingApiCodes.IdempotencyConflict,
        AccountingApiCodes.VersionConflict,
        AccountingStoreCodes.PeriodConflict,
        AccountingStoreCodes.PeriodPreparerConflict,
        AccountingStoreCodes.PeriodOverlap,
        AccountingStoreCodes.ConcurrentChange,
    ];

    private static readonly string[] NormalJournalConflictCodes =
    [
        .. PeriodConflictCodes,
        NormalJournalDraftStoreCodesV1.ContentConflict,
        NormalJournalDraftStoreCodesV1.NotPending,
        NormalJournalDraftStoreCodesV1.PostingConflict,
    ];

    private static readonly string[] PeriodUnprocessableCodes =
    [
        AccountingErrorCodes.PeriodNotProposed,
        AccountingErrorCodes.PeriodNotOpen,
        AccountingErrorCodes.PeriodAlreadyClosed,
        AccountingErrorCodes.PeriodInvalid,
        AccountingErrorCodes.InvalidReference,
        AccountingStoreCodes.ValueDoesNotFit,
    ];

    private static readonly string[] NormalJournalUnprocessableCodes =
    [
        .. PeriodUnprocessableCodes,
        AccountingErrorCodes.CompanyMismatch,
        AccountingErrorCodes.JournalTooFewLines,
        AccountingErrorCodes.JournalLineInvalid,
        AccountingErrorCodes.JournalUnbalanced,
        AccountingErrorCodes.PeriodClosed,
        AccountingErrorCodes.PeriodDoesNotCover,
    ];

    public static IEndpointRouteBuilder MapAccountingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/accounting")
            .RequireAuthorization()
            .WithTags("Accounting");

        group.MapGet("/periods", async (
                int? pageSize,
                string? cursor,
                AccountingApiService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response.Headers);
                return cursor is { Length: 0 or > MaximumCursorLength }
                    ? Problem(StatusCodes.Status400BadRequest, AccountingApiCodes.RequestInvalid, "cursor")
                    : ToHttpResult(httpContext, await service.ListPeriodsAsync(pageSize, cursor, cancellationToken), null);
            })
            .RequireCompanyContext()
            .RequireAuthorization(AccountingPermissionManifestV1.PeriodsRead)
            .WithName("GetAccountingPeriodsV1")
            .Produces<PeriodPageV1>(StatusCodes.Status200OK)
            .WithReadProblems(notFound: false);

        group.MapGet("/periods/{periodId:guid}", async (
                Guid periodId,
                AccountingApiService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response.Headers);
                return ToHttpResult(httpContext, await service.GetPeriodAsync(periodId, cancellationToken), null);
            })
            .RequireCompanyContext()
            .RequireAuthorization(AccountingPermissionManifestV1.PeriodsRead)
            .WithName("GetAccountingPeriodV1")
            .Produces<PeriodViewV1>(StatusCodes.Status200OK)
            .WithReadProblems(notFound: true);

        group.MapPost("/periods", async (
                ProposePeriodRequestV1 request,
                AccountingApiService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await WriteAsync(httpContext, key => service.ProposePeriodAsync(request, key, cancellationToken),
                period => $"/api/v1/accounting/periods/{period.PeriodId:D}"))
            .RequireCompanyContext()
            .RequireAuthorization(AccountingPermissionManifestV1.PeriodsPropose)
            .WithIdempotencyContract(StatusCodes.Status201Created)
            .WithName("ProposeAccountingPeriodV1")
            .Accepts<ProposePeriodRequestV1>("application/json")
            .Produces<PeriodViewV1>(StatusCodes.Status201Created)
            .WithWriteProblems(notFound: false);

        group.MapPost("/periods/{periodId:guid}/approval", async (
                Guid periodId,
                PeriodCommandRequestV1 request,
                AccountingApiService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await WriteAsync(httpContext, key => service.ApprovePeriodAsync(periodId, request, key, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(AccountingPermissionManifestV1.PeriodsApprove)
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict)
            .WithName("ApproveAccountingPeriodV1")
            .Accepts<PeriodCommandRequestV1>("application/json")
            .Produces<PeriodViewV1>(StatusCodes.Status200OK)
            .WithWriteProblems(notFound: true);

        group.MapPost("/periods/{periodId:guid}/close", async (
                Guid periodId,
                PeriodCommandRequestV1 request,
                AccountingApiService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await WriteAsync(httpContext, key => service.ClosePeriodAsync(periodId, request, key, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(AccountingPermissionManifestV1.PeriodsClose)
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict)
            .WithName("CloseAccountingPeriodV1")
            .Accepts<PeriodCommandRequestV1>("application/json")
            .Produces<PeriodViewV1>(StatusCodes.Status200OK)
            .WithWriteProblems(notFound: true);

        group.MapGet("/journals", async (
                Guid? periodId,
                int? pageSize,
                string? cursor,
                AccountingApiService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response.Headers);
                return cursor is { Length: 0 or > MaximumCursorLength }
                    ? Problem(StatusCodes.Status400BadRequest, AccountingApiCodes.RequestInvalid, "cursor")
                    : ToHttpResult(httpContext, await service.ListJournalsAsync(periodId, pageSize, cursor, cancellationToken), null);
            })
            .RequireCompanyContext()
            .RequireAuthorization(AccountingPermissionManifestV1.JournalsRead)
            .WithName("GetAccountingJournalsV1")
            .Produces<JournalPageV1>(StatusCodes.Status200OK)
            .WithReadProblems(notFound: false);

        group.MapGet("/journals/{journalId:guid}", async (
                Guid journalId,
                AccountingApiService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response.Headers);
                return ToHttpResult(httpContext, await service.GetJournalAsync(journalId, cancellationToken), null);
            })
            .RequireCompanyContext()
            .RequireAuthorization(AccountingPermissionManifestV1.JournalsRead)
            .WithName("GetAccountingJournalV1")
            .Produces<JournalDetailV1>(StatusCodes.Status200OK)
            .WithReadProblems(notFound: true);

        group.MapGet("/trial-balance", async (
                AccountingTrialBalanceServiceV1 service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response.Headers);
                if (!TryReadTrialBalanceRequest(httpContext.Request.Query, out var request, out var field))
                {
                    return Problem(StatusCodes.Status400BadRequest, AccountingApiCodes.RequestInvalid, field);
                }

                return ToHttpResult(httpContext, await service.ReadAsync(request, cancellationToken), null);
            })
            .RequireCompanyContext()
            .RequireAuthorization(AccountingPermissionManifestV1.JournalsRead)
            .WithName("GetAccountingTrialBalanceV1")
            .Produces<TrialBalanceReportV1>(StatusCodes.Status200OK)
            .WithReadProblems(notFound: false);

        group.MapPost("/normal-journal-drafts", async (
                CreateNormalJournalDraftRequestV1 request,
                NormalJournalDraftPublicServiceV1 service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await WriteAsync(httpContext,
                key => service.CreateAsync(request, key, cancellationToken),
                draft => $"/api/v1/accounting/normal-journal-drafts/{draft.DraftId:D}"))
            .RequireCompanyContext()
            .RequireAuthorization(NormalJournalPermissionNamesV1.DraftCreate)
            .WithIdempotencyContract(StatusCodes.Status201Created)
            .WithName("CreateAccountingNormalJournalDraftV1")
            .Accepts<CreateNormalJournalDraftRequestV1>("application/json")
            .Produces<NormalJournalDraftViewV1>(StatusCodes.Status201Created)
            .WithWriteProblems(notFound: false, normalJournal: true);

        group.MapGet("/normal-journal-drafts/{draftId:guid}", async (
                Guid draftId,
                NormalJournalDraftPublicServiceV1 service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response.Headers);
                return ToHttpResult(httpContext, await service.ReadAsync(draftId, cancellationToken), null);
            })
            .RequireCompanyContext()
            .WithName("GetAccountingNormalJournalDraftV1")
            .Produces<NormalJournalDraftViewV1>(StatusCodes.Status200OK)
            .WithReadProblems(notFound: true);

        group.MapPost("/normal-journal-drafts/{draftId:guid}/approval", async (
                Guid draftId,
                ApproveNormalJournalDraftRequestV1 request,
                NormalJournalDraftPublicServiceV1 service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await WriteAsync(httpContext,
                key => service.ApproveAsync(draftId, request, key, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(NormalJournalPermissionNamesV1.DraftApprove)
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict)
            .WithName("ApproveAccountingNormalJournalDraftV1")
            .Accepts<ApproveNormalJournalDraftRequestV1>("application/json")
            .Produces<NormalJournalDraftViewV1>(StatusCodes.Status200OK)
            .WithWriteProblems(notFound: true, normalJournal: true);

        group.MapGet("/posting-queues", async (
                AccountingApiService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response.Headers);
                return ToHttpResult(httpContext, await service.GetPostingQueuesAsync(cancellationToken), null);
            })
            .RequireCompanyContext()
            .RequireAuthorization(AccountingPermissionManifestV1.PostingQueuesRead)
            .WithName("GetAccountingPostingQueuesV1")
            .Produces<PostingQueuesV1>(StatusCodes.Status200OK)
            .WithReadProblems(notFound: false);

        return endpoints;
    }

    private static RouteHandlerBuilder WithReadProblems(this RouteHandlerBuilder builder, bool notFound)
    {
        builder
            .ProducesErpBadRequest(BadRequestCodes)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
        return notFound ? builder.ProducesErpCodeLessNotFound() : builder;
    }

    private static RouteHandlerBuilder WithWriteProblems(this RouteHandlerBuilder builder, bool notFound,
        bool normalJournal = false)
    {
        var conflictCodes = normalJournal ? NormalJournalConflictCodes : PeriodConflictCodes;
        var unprocessableCodes = normalJournal ? NormalJournalUnprocessableCodes : PeriodUnprocessableCodes;
        builder
            .ProducesErpBadRequest(BadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(conflictCodes)
            .ProducesErpUnprocessableEntity(unprocessableCodes)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
        return notFound ? builder.ProducesErpCodeLessNotFound() : builder;
    }

    private static async Task<IResult> WriteAsync<T>(HttpContext httpContext,
        Func<string, Task<AccountingApiResult<T>>> write, Func<T, string>? location = null)
        where T : class
    {
        SetPrivateNoStore(httpContext.Response.Headers);
        if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var key))
        {
            return Problem(StatusCodes.Status400BadRequest, AccountingApiCodes.RequestInvalid, "idempotencyKey");
        }

        var result = await write(key);
        return ToHttpResult(httpContext, result, result.Resource is { } resource && location is not null
            ? location(resource)
            : null);
    }

    private static bool TryReadTrialBalanceRequest(
        IQueryCollection query,
        out TrialBalanceRequestV1 request,
        out string field)
    {
        request = default!;
        field = "request";
        if (query.Any(pair => !TrialBalanceQueryNames.Contains(pair.Key) || pair.Value.Count != 1))
        {
            return false;
        }

        field = "accountingDate";
        if (!TryReadExactDate(query, "fromAccountingDate", out var fromAccountingDate)
            || !TryReadExactDate(query, "throughAccountingDate", out var throughAccountingDate)
            || throughAccountingDate < fromAccountingDate)
        {
            return false;
        }

        Guid? currencyId = null;
        if (query.TryGetValue("currencyId", out var currencyValues))
        {
            field = "currencyId";
            if (!Guid.TryParseExact(currencyValues[0], "D", out var parsedCurrencyId)
                || parsedCurrencyId == Guid.Empty)
            {
                return false;
            }

            currencyId = parsedCurrencyId;
        }

        request = new TrialBalanceRequestV1(fromAccountingDate, throughAccountingDate, currencyId);
        return true;
    }

    private static bool TryReadExactDate(IQueryCollection query, string name, out DateOnly value)
    {
        value = default;
        return query.TryGetValue(name, out var values)
               && values.Count == 1
               && DateOnly.TryParseExact(values[0], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                   DateTimeStyles.None, out value);
    }

    private static IResult ToHttpResult<T>(HttpContext httpContext, AccountingApiResult<T> result, string? location)
        where T : class
    {
        if (result.IsReplay)
        {
            httpContext.Response.Headers["Idempotency-Replayed"] = "true";
        }

        if (result.OriginalCorrelationId is { } original)
        {
            // ADR-011 §4: an idempotency replay names the first attempt's correlation.
            httpContext.Response.Headers["Idempotency-Original-Correlation-Id"] = original;
        }

        return result.Status switch
        {
            AccountingApiStatus.Created => Results.Created(location, result.Resource),
            AccountingApiStatus.Ok => Results.Ok(result.Resource),
            AccountingApiStatus.Invalid => Problem(StatusCodes.Status400BadRequest,
                result.Code ?? AccountingApiCodes.RequestInvalid, result.Field),
            // One 403 shape for every denial, Ö4's self-approval included (the Finance bank-account precedent):
            // the screen offers no approval of one's own proposal, and the server refuses it regardless.
            AccountingApiStatus.Denied => Problem(StatusCodes.Status403Forbidden, ErpProblemCodes.Forbidden, null),
            AccountingApiStatus.NotFound => Results.Problem(
                title: "Kayıt bulunamadı.",
                detail: "İstenen kayıt bulunamadı veya bu bağlamda kullanılamıyor.",
                statusCode: StatusCodes.Status404NotFound),
            AccountingApiStatus.Conflict => Problem(StatusCodes.Status409Conflict, result.Code!, result.Field,
                result.ExpectedVersion, result.CurrentVersion),
            AccountingApiStatus.Unprocessable => Problem(StatusCodes.Status422UnprocessableEntity, result.Code!,
                result.Field),
            AccountingApiStatus.Unavailable => Problem(StatusCodes.Status503ServiceUnavailable,
                ErpProblemCodes.ServiceUnavailable, null),
            _ => throw new InvalidOperationException("Tanımsız muhasebe sonucu."),
        };
    }

    private static IResult Problem(int statusCode, string code, string? field, long? expectedVersion = null,
        long? currentVersion = null)
    {
        var extensions = new Dictionary<string, object?> { ["code"] = code };
        if (field is not null)
        {
            extensions["field"] = field;
        }

        if (expectedVersion is { } expected && currentVersion is { } current)
        {
            extensions["expectedVersion"] = expected.ToString(CultureInfo.InvariantCulture);
            extensions["currentVersion"] = current.ToString(CultureInfo.InvariantCulture);
        }

        return Results.Problem(statusCode: statusCode, extensions: extensions);
    }

    private static bool TryReadIdempotencyKey(IHeaderDictionary headers, out string idempotencyKey)
    {
        idempotencyKey = string.Empty;
        if (!headers.TryGetValue(IdempotencyHeaderName, out var values) || values.Count != 1
            || values[0] is not { } rawValue)
        {
            return false;
        }

        var canonical = rawValue.Trim();
        if (canonical.Length is 0 or > MaximumIdempotencyKeyLength || canonical.Contains(',', StringComparison.Ordinal))
        {
            return false;
        }

        idempotencyKey = canonical;
        return true;
    }

    private static void SetPrivateNoStore(IHeaderDictionary headers)
    {
        headers[HeaderNames.CacheControl] = "private, no-store";
        headers[HeaderNames.Pragma] = "no-cache";
    }
}
