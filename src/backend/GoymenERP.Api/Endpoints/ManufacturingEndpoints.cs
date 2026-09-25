using System.Globalization;
using GoymenERP.Api.Errors;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// MFG-API (#313, approved card): the authorized Manufacturing surface for the server-resolved
/// company — a slitting plan, its approval, an order from it, the realization facts, and the
/// order's roll-by-roll balance. Every endpoint needs the company context (<c>X-Company-Id</c>) and
/// its own company-wide permission; a write also needs an <c>Idempotency-Key</c> (ADR-011: one
/// attempt per command and key, replayed with <c>Idempotency-Replayed</c> and
/// <c>Idempotency-Original-Correlation-Id</c>; another request or actor under the key is 409) and is
/// measured again against fresh authority before it is stored. Another company's plan or order is the same
/// code-less 404 as an unknown id. MFG-ORD added the order list and the close; no cost here.
/// </summary>
public static class ManufacturingEndpoints
{
    private const string IdempotencyHeaderName = "Idempotency-Key";
    private const int MaximumIdempotencyKeyLength = 128;

    private static readonly HashSet<string> OutputProductQueryNames =
        new(["q", "effectiveOn", "limit"], StringComparer.Ordinal);

    private static readonly HashSet<string> OutputLocationQueryNames =
        new(["q", "limit"], StringComparer.Ordinal);

    private static readonly string[] BadRequestCodes =
    [
        ManufacturingApiCodes.RequestInvalid,
        InventoryManufacturingRealizationCodesV1.InvalidRequest,
        InventoryManufacturingRealizationCodesV1.InvalidVersion,
        InventoryManufacturingRealizationCodesV1.InvalidQuantity,
    ];

    private static readonly string[] ConflictCodes =
    [
        ManufacturingApiCodes.IdempotencyConflict,
        ManufacturingRuleCodes.VersionConflict,
        ManufacturingStoreCodes.PlanIdConflict,
        ManufacturingStoreCodes.OrderIdConflict,
        ManufacturingStoreCodes.FactIdConflict,
        ManufacturingStoreCodes.ConcurrentChange,
        InventoryManufacturingRealizationCodesV1.LabelVersionConflict,
        InventoryManufacturingRealizationCodesV1.ParentLabelNotReleasable,
        InventoryManufacturingRealizationCodesV1.ParentLabelContextConflict,
        InventoryManufacturingRealizationCodesV1.PhysicalStockInsufficient,
        InventoryManufacturingRealizationCodesV1.ParentInputNotIssued,
        InventoryManufacturingRealizationCodesV1.ReturnExceedsIssued,
        InventoryManufacturingRealizationCodesV1.OutputLabelAlreadyRegistered,
        InventoryManufacturingRealizationCodesV1.OutputLotOrLabelConflict,
        InventoryManufacturingRealizationCodesV1.IdempotencyConflict,
        InventoryManufacturingRealizationCodesV1.ConcurrentChange,
    ];

    private static readonly string[] SavePlanConflictCodes =
    [
        .. ConflictCodes,
        InventoryParentRollCodesV1.VersionConflict,
        InventoryParentRollCodesV1.ReadConflict,
    ];

    private static readonly string[] SavePlanUnprocessableCodes =
    [
        .. ManufacturingProblemCodes.Unprocessable,
        InventoryParentRollCodesV1.ContextRequired,
        InventoryParentRollCodesV1.NoPhysicalPosition,
    ];

    public static IEndpointRouteBuilder MapManufacturingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/manufacturing")
            .RequireAuthorization()
            .WithTags("Manufacturing");

        group.MapGet("/references/output-products", async (
                HttpContext httpContext,
                [FromServices] IManufacturingOutputSelectionV1 selections,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response.Headers);
                if (!TryReadOutputProductQuery(httpContext.Request.Query, out var query))
                {
                    return Problem(StatusCodes.Status400BadRequest,
                        ManufacturingOutputSelectionCodesV1.InvalidQuery, null);
                }

                try
                {
                    return Results.Ok(await selections.SearchProductsAsync(query, cancellationToken));
                }
                catch (ManufacturingOutputSelectionException exception)
                {
                    return OutputSelectionProblem(exception);
                }
            })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.ManufacturingFactsRecord)
            .WithName("GetManufacturingOutputProductCandidatesV1")
            .Produces<ManufacturingOutputProductCandidatesV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(ManufacturingOutputSelectionCodesV1.InvalidQuery)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/references/output-locations", async (
                HttpContext httpContext,
                [FromServices] IManufacturingOutputSelectionV1 selections,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response.Headers);
                if (!TryReadOutputLocationQuery(httpContext.Request.Query, out var query))
                {
                    return Problem(StatusCodes.Status400BadRequest,
                        ManufacturingOutputSelectionCodesV1.InvalidQuery, null);
                }

                try
                {
                    return Results.Ok(await selections.SearchLocationsAsync(query, cancellationToken));
                }
                catch (ManufacturingOutputSelectionException exception)
                {
                    return OutputSelectionProblem(exception);
                }
            })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.ManufacturingFactsRecord)
            .WithName("GetManufacturingOutputLocationCandidatesV1")
            .Produces<ManufacturingOutputLocationCandidatesV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(ManufacturingOutputSelectionCodesV1.InvalidQuery)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/plans", async (
                SavePlanRequestV1 request,
                ManufacturingCommandService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await WriteAsync(httpContext, key => service.SavePlanAsync(request, key,
                ErpPermissions.ManufacturingPlansWrite, cancellationToken),
                plan => $"/api/v1/manufacturing/plans/{plan.PlanId:D}"))
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.ManufacturingPlansWrite)
            .WithIdempotencyContract(
                StatusCodes.Status201Created,
                StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict)
            .WithName("SaveManufacturingPlanV1")
            .Accepts<SavePlanRequestV1>("application/json")
            .Produces<PlanViewV1>(StatusCodes.Status201Created)
            .WithSavePlanProblems();

        group.MapPost("/plans/{planId:guid}/approval", async (
                Guid planId,
                ApprovePlanRequestV1 request,
                ManufacturingCommandService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await WriteAsync(httpContext, key => service.ApprovePlanAsync(planId, request, key,
                ErpPermissions.ManufacturingPlansApprove, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.ManufacturingPlansApprove)
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict)
            .WithName("ApproveManufacturingPlanV1")
            .Accepts<ApprovePlanRequestV1>("application/json")
            .Produces<PlanViewV1>(StatusCodes.Status200OK)
            .WithWriteProblems(notFound: true);

        // K4.4: the plan read. Until this the surface could CREATE and APPROVE a plan and never answer for
        // it again, so a created plan could not be reopened — the missing piece was an answer, not a screen.
        // Its own narrow name: `manufacturing.plans.read` is company-wide because this module narrows no read
        // by scope, so a site-scoped grant would pass the policy and then return every plan in the company.
        // Another company's plan is the same code-less 404 as an unknown id (never 403, which would confirm
        // the row exists), and a read that still meets concurrent commits is a retryable 503 — 409 belongs to
        // commands.
        group.MapGet("/plans/{planId:guid}", async (
                Guid planId,
                ManufacturingCommandService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response.Headers);
                return ToHttpResult(httpContext, await service.GetPlanAsync(planId, cancellationToken), location: null);
            })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.ManufacturingPlansRead)
            .WithName("GetManufacturingPlanV1")
            .Produces<PlanViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/orders", async (
                CreateOrderRequestV1 request,
                ManufacturingCommandService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await WriteAsync(httpContext, key => service.CreateOrderAsync(request, key,
                ErpPermissions.ManufacturingOrdersCreate, cancellationToken),
                order => $"/api/v1/manufacturing/orders/{order.OrderId:D}"))
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.ManufacturingOrdersCreate)
            .WithIdempotencyContract(StatusCodes.Status201Created, StatusCodes.Status404NotFound)
            .WithName("CreateManufacturingOrderV1")
            .Accepts<CreateOrderRequestV1>("application/json")
            .Produces<OrderViewV1>(StatusCodes.Status201Created)
            .WithWriteProblems(notFound: true);

        group.MapPost("/orders/{orderId:guid}/input-issues", async (
                Guid orderId,
                IssueInventoryInputRequestV1 request,
                ManufacturingCommandService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await WriteAsync(httpContext, key => service.IssueInventoryInputAsync(orderId, request, key,
                ErpPermissions.ManufacturingFactsRecord, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.ManufacturingFactsRecord)
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict)
            .WithName("IssueManufacturingInputV1")
            .Accepts<IssueInventoryInputRequestV1>("application/json")
            .Produces<OrderViewV1>(StatusCodes.Status200OK)
            .WithWriteProblems(notFound: true);

        group.MapPost("/orders/{orderId:guid}/input-returns", async (
                Guid orderId,
                ReturnInventoryInputRequestV1 request,
                ManufacturingCommandService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await WriteAsync(httpContext, key => service.ReturnInventoryInputAsync(orderId, request, key,
                ErpPermissions.ManufacturingFactsRecord, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.ManufacturingFactsRecord)
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict)
            .WithName("ReturnManufacturingInputV1")
            .Accepts<ReturnInventoryInputRequestV1>("application/json")
            .Produces<OrderViewV1>(StatusCodes.Status200OK)
            .WithWriteProblems(notFound: true);
        MapFact(group, "/orders/{orderId:guid}/wip-measurements", "RecordManufacturingWorkInProgressV1",
            (service, orderId, request, key, cancellationToken) =>
                service.RecordWorkInProgressAsync(orderId, request, key, ErpPermissions.ManufacturingFactsRecord,
                    cancellationToken));

        group.MapPost("/orders/{orderId:guid}/outputs", async (
                Guid orderId,
                DeclareInventoryOutputRequestV1 request,
                ManufacturingCommandService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await WriteAsync(httpContext, key => service.DeclareInventoryOutputAsync(orderId, request, key,
                ErpPermissions.ManufacturingFactsRecord, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.ManufacturingFactsRecord)
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict)
            .WithName("DeclareManufacturingOutputV1")
            .Accepts<DeclareInventoryOutputRequestV1>("application/json")
            .Produces<OrderViewV1>(StatusCodes.Status200OK)
            .WithWriteProblems(notFound: true);

        // MFG-ORD: the close of an order at the version the caller read. The rule is the domain's
        // (MFG-4, Business 5728619644): no tolerance, no write-off, and an unexplained difference is a
        // 422 naming its code. One attempt per Idempotency-Key, like every other write here.
        group.MapPost("/orders/{orderId:guid}/close", async (
                Guid orderId,
                CloseOrderRequestV1 request,
                ManufacturingCommandService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await WriteAsync(httpContext, key => service.CloseOrderAsync(orderId, request, key,
                ErpPermissions.ManufacturingOrdersClose, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.ManufacturingOrdersClose)
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict)
            .WithName("CloseManufacturingOrderV1")
            .Accepts<CloseOrderRequestV1>("application/json")
            .Produces<OrderViewV1>(StatusCodes.Status200OK)
            .WithWriteProblems(notFound: true);

        // MFG-ORD: the company's orders, newest first, from the stored rows only — no balance and no
        // roll label (the label is Inventory's read). The same read grant as the order screen.
        group.MapGet("/orders", async (
                int? pageSize,
                string? cursor,
                string? status,
                ManufacturingCommandService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response.Headers);
                return ToHttpResult(httpContext, await service.ListOrdersAsync(pageSize, cursor, status, cancellationToken),
                    location: null);
            })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.ManufacturingOrdersRead)
            .WithName("ListManufacturingOrdersV1")
            .Produces<OrderListViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(BadRequestCodes)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/orders/{orderId:guid}", async (
                Guid orderId,
                ManufacturingCommandService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response.Headers);
                return ToHttpResult(httpContext, await service.GetOrderAsync(orderId, cancellationToken), location: null);
            })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.ManufacturingOrdersRead)
            .WithName("GetManufacturingOrderV1")
            .Produces<OrderViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        // BAL slice 2: the order's GDC-BAL/1 read — the same order, context, permission and answers as the order read,
        // computed by the one mass-balance calculator the close uses. A read: nothing is stored.
        group.MapGet("/orders/{orderId:guid}/mass-balance", async (
                Guid orderId,
                ManufacturingCommandService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response.Headers);
                return ToHttpResult(httpContext, await service.GetMassBalanceAsync(orderId, cancellationToken), location: null);
            })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.ManufacturingOrdersRead)
            .WithName("GetManufacturingOrderMassBalanceV1")
            .Produces<MassBalanceViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        return endpoints;
    }

    private static void MapFact(RouteGroupBuilder group, string pattern, string name,
        Func<ManufacturingCommandService, Guid, RecordFactRequestV1, string, CancellationToken,
            Task<ManufacturingApiResult<OrderViewV1>>> record)
    {
        group.MapPost(pattern, async (
                Guid orderId,
                RecordFactRequestV1 request,
                ManufacturingCommandService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await WriteAsync(httpContext, key => record(service, orderId, request, key, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.ManufacturingFactsRecord)
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict)
            .WithName(name)
            .Accepts<RecordFactRequestV1>("application/json")
            .Produces<OrderViewV1>(StatusCodes.Status200OK)
            .WithWriteProblems(notFound: true);
    }

    private static RouteHandlerBuilder WithWriteProblems(this RouteHandlerBuilder builder, bool notFound)
    {
        builder
            .ProducesErpBadRequest(BadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(ConflictCodes)
            .ProducesErpUnprocessableEntity(ManufacturingProblemCodes.Unprocessable)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
        return notFound ? builder.ProducesErpCodeLessNotFound() : builder;
    }

    private static RouteHandlerBuilder WithSavePlanProblems(this RouteHandlerBuilder builder) =>
        builder
            .ProducesErpBadRequest(BadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(SavePlanConflictCodes)
            .ProducesErpUnprocessableEntity(SavePlanUnprocessableCodes)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

    private static async Task<IResult> WriteAsync<T>(HttpContext httpContext,
        Func<string, Task<ManufacturingApiResult<T>>> write, Func<T, string>? location = null)
        where T : class
    {
        SetPrivateNoStore(httpContext.Response.Headers);
        if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var key))
        {
            return Problem(StatusCodes.Status400BadRequest, ManufacturingApiCodes.RequestInvalid, "idempotencyKey");
        }

        var result = await write(key);
        return ToHttpResult(httpContext, result, result.Resource is { } resource && location is not null
            ? location(resource)
            : null);
    }

    private static IResult ToHttpResult<T>(HttpContext httpContext, ManufacturingApiResult<T> result, string? location)
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
            ManufacturingApiStatus.Created => Results.Created(location, result.Resource),
            ManufacturingApiStatus.Ok => Results.Ok(result.Resource),
            ManufacturingApiStatus.Invalid => Problem(StatusCodes.Status400BadRequest,
                result.Code ?? ManufacturingApiCodes.RequestInvalid, result.Field),
            ManufacturingApiStatus.Denied => Problem(StatusCodes.Status403Forbidden, ErpProblemCodes.Forbidden, null),
            ManufacturingApiStatus.NotFound => Results.Problem(
                title: "Kayıt bulunamadı.",
                detail: "İstenen kayıt bulunamadı veya bu bağlamda kullanılamıyor.",
                statusCode: StatusCodes.Status404NotFound),
            ManufacturingApiStatus.Conflict => Problem(StatusCodes.Status409Conflict, result.Code!, result.Field,
                result.ExpectedVersion, result.CurrentVersion),
            ManufacturingApiStatus.Unprocessable => Problem(StatusCodes.Status422UnprocessableEntity, result.Code!,
                result.Field),
            ManufacturingApiStatus.Unavailable => Problem(StatusCodes.Status503ServiceUnavailable,
                ErpProblemCodes.ServiceUnavailable, null),
            _ => throw new InvalidOperationException("Tanımsız üretim komutu sonucu."),
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

    private static bool TryReadOutputProductQuery(
        IQueryCollection values,
        out ManufacturingOutputProductQueryV1 query)
    {
        query = new(null, default);
        if (!HasOnlySingleKnownValues(values, OutputProductQueryNames)
            || !values.TryGetValue("effectiveOn", out var effectiveOnValues)
            || !DateOnly.TryParseExact(effectiveOnValues[0], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var effectiveOn)
            || !TryReadOutputLimit(values, out var limit))
        {
            return false;
        }

        query = new(values.TryGetValue("q", out var q) ? q[0] : null, effectiveOn, limit);
        return true;
    }

    private static bool TryReadOutputLocationQuery(
        IQueryCollection values,
        out ManufacturingOutputLocationQueryV1 query)
    {
        query = new(null);
        if (!HasOnlySingleKnownValues(values, OutputLocationQueryNames)
            || !TryReadOutputLimit(values, out var limit))
        {
            return false;
        }

        query = new(values.TryGetValue("q", out var q) ? q[0] : null, limit);
        return true;
    }

    private static bool HasOnlySingleKnownValues(IQueryCollection query, IReadOnlySet<string> names) =>
        query.Keys.All(names.Contains) && query.All(item => item.Value.Count == 1);

    private static bool TryReadOutputLimit(IQueryCollection query, out int limit)
    {
        limit = 25;
        return !query.TryGetValue("limit", out var values)
               || int.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out limit)
               && limit is >= 1 and <= 100;
    }

    private static IResult OutputSelectionProblem(ManufacturingOutputSelectionException exception) =>
        exception.Failure switch
        {
            ManufacturingOutputSelectionFailure.Invalid => Problem(StatusCodes.Status400BadRequest,
                ManufacturingOutputSelectionCodesV1.InvalidQuery, null),
            ManufacturingOutputSelectionFailure.Denied => Problem(StatusCodes.Status403Forbidden,
                ErpProblemCodes.Forbidden, null),
            ManufacturingOutputSelectionFailure.Unavailable => Problem(StatusCodes.Status503ServiceUnavailable,
                ErpProblemCodes.ServiceUnavailable, null),
            _ => throw new InvalidOperationException("Tanımsız üretim çıktı seçim sonucu."),
        };

    private static void SetPrivateNoStore(IHeaderDictionary headers)
    {
        headers[HeaderNames.CacheControl] = "private, no-store";
        headers[HeaderNames.Pragma] = "no-cache";
    }
}

/// <summary>The stable 422 codes of the Manufacturing surface: the domain's, the store's and Settings'.</summary>
internal static class ManufacturingProblemCodes
{
    public static readonly string[] Unprocessable =
    [
        ManufacturingRuleCodes.KilogramPrecisionExceeded,
        ManufacturingRuleCodes.PlanWithoutInputs,
        ManufacturingRuleCodes.DuplicateRollInPlan,
        ManufacturingRuleCodes.PlanNotDraft,
        ManufacturingRuleCodes.PlanNotApproved,
        ManufacturingRuleCodes.OrderClosed,
        ManufacturingRuleCodes.UnexplainedDifference,
        ManufacturingRuleCodes.RollNotInOrder,
        ManufacturingRuleCodes.RollNotIssued,
        ManufacturingRuleCodes.InputAlreadyIssued,
        ManufacturingRuleCodes.ReturnAlreadyRecorded,
        ManufacturingRuleCodes.ReturnExceedsIssued,
        ManufacturingRuleCodes.OutputAlreadyDeclared,
        ManufacturingRuleCodes.WipAlreadyMeasured,
        ManufacturingRuleCodes.WipMeasurementNotLatest,
        ManufacturingRuleCodes.StoredRecordInvalid,
        ManufacturingStoreCodes.WorkCenterNotFound,
        ManufacturingStoreCodes.ValueDoesNotFit,
        ManufacturingMassCodes.MassNegative,
        ManufacturingMassCodes.ProfileCompanyMismatch,
        ManufacturingMassCodes.ProfileInvalid,
        ManufacturingMassCodes.ProfileNotInEffect,
        ManufacturingMassCodes.ScaleExceeded,
        GoymenERP.Modules.Settings.Application.Contracts.V1.SettingsPrerequisiteCodesV1.UnitProfileNotFound,
        InventoryOpeningCodesV1.ProductIneligible,
        InventoryOpeningCodesV1.LocationNotFound,
        InventoryOpeningCodesV1.LocationInactive,
    ];
}
