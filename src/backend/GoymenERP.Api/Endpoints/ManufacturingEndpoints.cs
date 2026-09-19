using System.Globalization;
using GoymenERP.Api.Errors;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
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
/// code-less 404 as an unknown id. No close, list or cost here.
/// </summary>
public static class ManufacturingEndpoints
{
    private const string IdempotencyHeaderName = "Idempotency-Key";
    private const int MaximumIdempotencyKeyLength = 128;

    private static readonly string[] BadRequestCodes = [ManufacturingApiCodes.RequestInvalid];

    private static readonly string[] ConflictCodes =
    [
        ManufacturingApiCodes.IdempotencyConflict,
        ManufacturingRuleCodes.VersionConflict,
        ManufacturingStoreCodes.PlanIdConflict,
        ManufacturingStoreCodes.OrderIdConflict,
        ManufacturingStoreCodes.FactIdConflict,
        ManufacturingStoreCodes.ConcurrentChange,
    ];

    public static IEndpointRouteBuilder MapManufacturingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/manufacturing")
            .RequireAuthorization()
            .WithTags("Manufacturing");

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
            .WithIdempotencyContract(StatusCodes.Status201Created)
            .WithName("SaveManufacturingPlanV1")
            .Accepts<SavePlanRequestV1>("application/json")
            .Produces<PlanViewV1>(StatusCodes.Status201Created)
            .WithWriteProblems(notFound: false);

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

        MapFact(group, "/orders/{orderId:guid}/input-issues", "IssueManufacturingInputV1",
            (service, orderId, request, key, cancellationToken) =>
                service.IssueInputAsync(orderId, request, key, ErpPermissions.ManufacturingFactsRecord, cancellationToken));
        MapFact(group, "/orders/{orderId:guid}/input-returns", "ReturnManufacturingInputV1",
            (service, orderId, request, key, cancellationToken) =>
                service.ReturnUnusedInputAsync(orderId, request, key, ErpPermissions.ManufacturingFactsRecord,
                    cancellationToken));
        MapFact(group, "/orders/{orderId:guid}/wip-measurements", "RecordManufacturingWorkInProgressV1",
            (service, orderId, request, key, cancellationToken) =>
                service.RecordWorkInProgressAsync(orderId, request, key, ErpPermissions.ManufacturingFactsRecord,
                    cancellationToken));

        group.MapPost("/orders/{orderId:guid}/outputs", async (
                Guid orderId,
                DeclareOutputRequestV1 request,
                ManufacturingCommandService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await WriteAsync(httpContext, key => service.DeclareOutputAsync(orderId, request, key,
                ErpPermissions.ManufacturingFactsRecord, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.ManufacturingFactsRecord)
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict)
            .WithName("DeclareManufacturingOutputV1")
            .Accepts<DeclareOutputRequestV1>("application/json")
            .Produces<OrderViewV1>(StatusCodes.Status200OK)
            .WithWriteProblems(notFound: true);

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
    ];
}
