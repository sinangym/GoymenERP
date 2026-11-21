using System.Globalization;
using GoymenERP.Api.Errors;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
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
    internal const string FixtureDiagnosticHeaderName = "X-Browser-Fixture-Diagnostic";
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

    // URT-S21 (URT-D62): the output declaration gives the output label its own code; a code already used in the company
    // (entered, or no free number in the Ayarlar format) is its 409, a missing or unusable Ayarlar format its 422, and
    // (URT-D98) an entered code while an Ayarlar format is defined its 422.
    private static readonly string[] DeclareOutputConflictCodes =
    [
        .. ConflictCodes,
        ManufacturingStoreCodes.OutputLabelCodeConflict,
    ];

    private static readonly string[] DeclareOutputUnprocessableCodes =
    [
        .. ManufacturingProblemCodes.Unprocessable,
        ManufacturingStoreCodes.OutputLabelCodeRequired,
        ManufacturingStoreCodes.OutputLabelCodeFormatInvalid,
        ManufacturingStoreCodes.OutputLabelCodeEntryNotAllowed,
    ];

    // F-009 D3 (API-25, URT-S2): the output correction meets Inventory's refusal to take the mass back from the output
    // position (each named by its Manufacturing code) and an output label Inventory could not read as its 409; the
    // domain's correction rules are its 422.
    private static readonly string[] CorrectOutputConflictCodes =
    [
        .. ConflictCodes,
        .. ManufacturingOutputCorrectionCodesV1.All,
    ];

    private static readonly string[] CorrectOutputUnprocessableCodes =
    [
        .. ManufacturingProblemCodes.Unprocessable,
        ManufacturingRuleCodes.OutputNotDeclared,
        ManufacturingRuleCodes.OutputLineMissing,
        ManufacturingRuleCodes.OutputReversalAlreadyRecorded,
        ManufacturingRuleCodes.OutputReversalExceedsDeclared,
    ];

    private static readonly string[] SavePlanConflictCodes =
    [
        .. ConflictCodes,
        InventoryParentRollCodesV1.VersionConflict,
        InventoryParentRollCodesV1.ReadConflict,
        ManufacturingPaintCodesV1.VersionConflict,
    ];

    private static readonly string[] SavePlanUnprocessableCodes =
    [
        .. ManufacturingProblemCodes.Unprocessable,
        InventoryParentRollCodesV1.ContextRequired,
        InventoryParentRollCodesV1.NoPhysicalPosition,
        // Üretim U2-a: the slitting spec's net usable width is wider than a planned parent coil.
        ManufacturingRuleCodes.PlanNetWidthExceedsParent,
        ManufacturingRuleCodes.PlanLabelNotUsable,
        ManufacturingPaintCodesV1.Required,
        ManufacturingPaintCodesV1.Ineligible,
    ];

    // Üretim U3-a (URT-D21, URT-D3, B-01): the approval allocates the plan's inputs in Inventory with the approval, at the
    // label version read from Inventory's label detail; Inventory's allocation refusals and the label read's join its 409
    // set. Declared on the approval only.
    private static readonly string[] ApprovePlanConflictCodes =
    [
        .. SavePlanConflictCodes,
        InventoryManufacturingRealizationCodesV1.AllocationInsufficientFreeStock,
        InventoryManufacturingRealizationCodesV1.AllocationOwnerMismatch,
        InventoryLabelDetailCodesV1.ReadConflict,
        ManufacturingPlanAllocationCodesV1.LabelNotFound,
        ManufacturingPlanAllocationCodesV1.LabelIdentityChanged,
    ];

    // Üretim U3-a (API-21): an input issue from its plan allocation may meet the allocation's own refusals.
    private static readonly string[] IssueInputConflictCodes =
    [
        .. ConflictCodes,
        InventoryManufacturingRealizationCodesV1.AllocationNotUsable,
        InventoryManufacturingRealizationCodesV1.AllocationExceeded,
    ];

    // Üretim U2-a (URT-D2, API-10/11): the rejection's and the cancellation's own 422 set — the write set plus the two
    // lifecycle rules. Declared on those endpoints only, so no existing endpoint's contract moves.
    private static readonly string[] PlanDecisionUnprocessableCodes =
    [
        .. ManufacturingProblemCodes.Unprocessable,
        ManufacturingRuleCodes.PlanCancelled,
        ManufacturingRuleCodes.PlanHasOrder,
    ];

    // Üretim U2-d (URT-D2): the approval's and the rejection's 422 sets add "the preparer of the current version may not
    // decide". Declared on those two endpoints only, so the cancellation and every other endpoint keep their contract.
    // URT-D66 (2): the approval also refuses a plan without a termin. URT-D70: and a plan whose termin is before the
    // Istanbul business day.
    private static readonly string[] ApprovePlanUnprocessableCodes =
    [
        .. ManufacturingProblemCodes.Unprocessable,
        ManufacturingPaintCodesV1.Required,
        ManufacturingPaintCodesV1.Ineligible,
        ManufacturingRuleCodes.PlanSelfApproval,
        ManufacturingRuleCodes.PlanDueDateRequired,
        ManufacturingRuleCodes.DueDateInPast,
    ];

    private static readonly string[] RejectPlanUnprocessableCodes =
    [
        .. PlanDecisionUnprocessableCodes,
        ManufacturingRuleCodes.PlanSelfApproval,
    ];

    // Üretim U2-b / API-16 (SAT-D219): the sales order link's own 409 and 422 sets — a moved link revision is a conflict; a
    // cancelled plan, a closed order, no change, a customer-owned input of another party and the line Sales did not approve
    // or does not know are refusals. Declared on that endpoint only, so no existing endpoint's contract moves.
    private static readonly string[] PlanSalesOrderLinkConflictCodes =
    [
        .. ConflictCodes,
        ManufacturingRuleCodes.PlanSalesOrderLinkChanged,
    ];

    private static readonly string[] PlanSalesOrderLinkUnprocessableCodes =
    [
        .. ManufacturingProblemCodes.Unprocessable,
        ManufacturingRuleCodes.PlanCancelled,
        ManufacturingRuleCodes.PlanSalesOrderLinkOrderClosed,
        ManufacturingRuleCodes.PlanSalesOrderLinkNoChange,
        ManufacturingRuleCodes.PlanSalesOrderPartyMismatch,
        ManufacturingRuleCodes.PlanSalesOrderLineNotApproved,
        ManufacturingRuleCodes.PlanSalesOrderLineNotFound,
        ManufacturingRuleCodes.PlanCustomerGoodsRequireSubcontractOrder,
    ];

    // Üretim F-010a (URT-D29): the hand-entered termin's own 409 and 422 sets — a moved termin revision is a conflict; a
    // cancelled plan, a closed order and no change are refusals. Declared on that endpoint only, so no existing endpoint's
    // contract moves.
    private static readonly string[] PlanDueDateConflictCodes =
    [
        .. ConflictCodes,
        ManufacturingRuleCodes.PlanDueDateChanged,
    ];

    private static readonly string[] PlanDueDateUnprocessableCodes =
    [
        .. ManufacturingProblemCodes.Unprocessable,
        ManufacturingRuleCodes.PlanCancelled,
        ManufacturingRuleCodes.PlanDueDateOrderClosed,
        ManufacturingRuleCodes.PlanDueDateNoChange,
        // URT-D66 (1)/(2): a day before the Istanbul business day; an approved plan's termin removed.
        ManufacturingRuleCodes.DueDateInPast,
        ManufacturingRuleCodes.PlanDueDateRequired,
    ];

    // URT-D66 (3): the order termin's own 409 and 422 sets — a moved order termin revision is a conflict; a closed order,
    // no change and a day before the Istanbul business day are refusals. Declared on that endpoint only.
    private static readonly string[] OrderDueDateConflictCodes =
    [
        .. ConflictCodes,
        ManufacturingRuleCodes.OrderDueDateChanged,
    ];

    private static readonly string[] OrderDueDateUnprocessableCodes =
    [
        .. ManufacturingProblemCodes.Unprocessable,
        ManufacturingRuleCodes.OrderDueDateOrderClosed,
        ManufacturingRuleCodes.OrderDueDateNoChange,
        ManufacturingRuleCodes.DueDateInPast,
    ];

    // URT-S13 (URT-D47, URT-D65): the monthly scrap price entry's own 409 and 422 sets — a month that already has a price
    // is a conflict (a first entry never replaces it; URT-D72 corrects it); a fifth decimal and (URT-D73) a future month
    // are refusals. Declared on that endpoint only.
    private static readonly string[] ScrapPriceConflictCodes =
    [
        .. ConflictCodes,
        ManufacturingRuleCodes.ScrapPriceAlreadyRecorded,
    ];

    private static readonly string[] ScrapPriceUnprocessableCodes =
    [
        .. ManufacturingProblemCodes.Unprocessable,
        ManufacturingRuleCodes.ScrapPricePrecisionExceeded,
        ManufacturingRuleCodes.ScrapPriceFutureMonth,
    ];

    // URT-D72, URT-D73: the scrap price correction's 422 set — the same price again, a fifth decimal and a future month.
    // Its 409 is the write set (a stale expected version, a lost race).
    private static readonly string[] ScrapPriceCorrectionUnprocessableCodes =
    [
        .. ManufacturingProblemCodes.Unprocessable,
        ManufacturingRuleCodes.ScrapPriceUnchanged,
        ManufacturingRuleCodes.ScrapPricePrecisionExceeded,
        ManufacturingRuleCodes.ScrapPriceFutureMonth,
    ];

    // Üretim U2-a (URT-D1): the order creation's 422 set — the write set plus "the plan already opened its order".
    // URT-D70: and "the plan's termin is before the Istanbul business day".
    // F-009 D5 (URT-D35): and "the plan's order was cancelled; re-approve the plan first".
    private static readonly string[] CreateOrderUnprocessableCodes =
    [
        .. ManufacturingProblemCodes.Unprocessable,
        ManufacturingRuleCodes.PlanOrderExists,
        ManufacturingRuleCodes.DueDateInPast,
        ManufacturingRuleCodes.PlanReapprovalRequired,
    ];

    // F-009 D5 (URT-D33/D34): the order cancel's 422 set — the write set plus "the order still has a net effect".
    private static readonly string[] CancelOrderUnprocessableCodes =
    [
        .. ManufacturingProblemCodes.Unprocessable,
        ManufacturingRuleCodes.OrderCancelHasNetEffect,
    ];

    // F-009 D5 (URT-D35/D39): the re-approval's 422 set — the approval's gate refusals plus a cancelled plan, an order
    // still open on the plan, no release waiting, the preparer and a changed Settings profile. Its 409 set is the
    // approval's (the inputs are allocated again in Inventory).
    private static readonly string[] ReapprovePlanUnprocessableCodes =
    [
        .. ManufacturingProblemCodes.Unprocessable,
        ManufacturingPaintCodesV1.Required,
        ManufacturingPaintCodesV1.Ineligible,
        ManufacturingRuleCodes.PlanCancelled,
        ManufacturingRuleCodes.PlanOrderExists,
        ManufacturingRuleCodes.PlanSelfApproval,
        ManufacturingRuleCodes.PlanReapprovalNotRequired,
        ManufacturingRuleCodes.PlanReapprovalSettingsChanged,
        ManufacturingRuleCodes.PlanReapprovalLabelMismatch,
    ];

    // MFG-PLAN-EDIT: the revision's own 422 set — the plan save's, plus the three revision rules. Declared on the
    // revision endpoint only, so no existing endpoint's contract moves.
    private static readonly string[] RevisePlanUnprocessableCodes =
    [
        .. SavePlanUnprocessableCodes,
        ManufacturingRuleCodes.PlanProcessFixed,
        ManufacturingRuleCodes.PlanSlittingSpecFixed,
        ManufacturingRuleCodes.PlanReapprovalSettingsChanged,
        ManufacturingRuleCodes.PlanSalesOrderPartyMismatch,
        ManufacturingRuleCodes.PlanEngineRefused,
        ManufacturingRuleCodes.PlanWindingDoesNotFit,
        ManufacturingRuleCodes.PlanRevisionNoChange,
    ];

    public static IEndpointRouteBuilder MapManufacturingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/manufacturing")
            .RequireAuthorization()
            .WithTags("Manufacturing");

        group.MapGet("/references/paint-selections", async (HttpContext context,
                ICompanyContext company, CancellationToken cancellationToken, [FromServices] IManufacturingPaintSelectionSourceV1? source = null) =>
            {
                SetPrivateNoStore(context.Response.Headers);
                if (context.Request.Query.Keys.Any(key => key != "q") || context.Request.Query["q"].Count > 1)
                    return Problem(400, ManufacturingApiCodes.RequestInvalid, null);
                if (source is null) return Results.Problem(statusCode: 503);
                try { return Results.Ok(await source.ListAsync(company.CompanyId, context.Request.Query["q"].FirstOrDefault(), cancellationToken)); }
                catch (OperationCanceledException) { throw; }
                catch (UnauthorizedAccessException) { return Results.Problem(statusCode: 403); }
                catch (ArgumentException) { return Problem(400, ManufacturingApiCodes.RequestInvalid, null); }
                catch (Exception) { return Results.Problem(statusCode: 503); }
            })
            .RequireCompanyContext().RequireAuthorization(ErpPermissions.ManufacturingPlansWrite)
            .WithName("GetManufacturingPaintSelectionsV1")
            .Produces<ManufacturingPaintCatalogV1>(200).ProducesErpBadRequest(ManufacturingApiCodes.RequestInvalid)
            .ProducesErpAuthenticatedProblems().ProducesErpServiceUnavailable();

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

        // AT-061 / AT-062 (DEC-PRDCT-005): the button-less preview of a plan content, under the plan save's own grant
        // (`manufacturing.plans.write`, company-wide; the form that sends it is the create / edit form). It runs the save's
        // checks and the ONE plan calculator and writes nothing: no Idempotency-Key, no command record, no audit. A
        // refusal is the save's own answer for the same content, so the form can say why and on which field.
        group.MapPost("/plans/preview", async (
                PlanPreviewRequestV1 request,
                ManufacturingCommandService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response.Headers);
                return ToHttpResult(httpContext,
                    await service.PreviewPlanAsync(request, ErpPermissions.ManufacturingPlansWrite, cancellationToken),
                    location: null);
            })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.ManufacturingPlansWrite)
            .WithName("PreviewManufacturingPlanV1")
            .Accepts<PlanPreviewRequestV1>("application/json")
            .Produces<PlanPreviewViewV1>(StatusCodes.Status200OK)
            .WithPreviewPlanProblems();

        // URT-D34/D35: advisory current/free/missing kg of the plan's stored inputs only; no reservation is made.
        group.MapGet("/plans/{planId:guid}/input-availability", async (
                Guid planId, ManufacturingCommandService service, HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response.Headers);
                return ToHttpResult(httpContext, await service.GetPlanInputAvailabilityAsync(planId, cancellationToken),
                    location: null);
            })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.ManufacturingPlansRead)
            .WithName("GetManufacturingPlanInputAvailabilityV1")
            .Produces<PlanInputAvailabilityViewV1>(StatusCodes.Status200OK)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpCodeLessNotFound();

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
            .WithApprovePlanProblems();

        // F-009 D5 (URT-D35/D39): the minimal Approved→Approved re-approval of a plan released by an order cancel, under the
        // approval's grant. Settings must be unchanged since the first approval; the inputs are allocated again.
        group.MapPost("/plans/{planId:guid}/reapproval", async (
                Guid planId,
                ReapprovePlanRequestV1 request,
                ManufacturingCommandService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await WriteAsync(httpContext, key => service.ReapprovePlanAsync(planId, request, key,
                ErpPermissions.ManufacturingPlansApprove, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.ManufacturingPlansApprove)
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict)
            .WithName("ReapproveManufacturingPlanV1")
            .Accepts<ReapprovePlanRequestV1>("application/json")
            .Produces<PlanViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(BadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesManufacturingPlanAllocationConflict(ApprovePlanConflictCodes)
            .ProducesErpUnprocessableEntity(ReapprovePlanUnprocessableCodes)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpCodeLessNotFound();

        // Üretim U2-a (URT-D2, API-10): instead of approving a Draft, its approver rejects it with a reason, under the
        // approval's own grant. The plan stays a Draft one version on, with the rejection recorded; ADR-011 as the approval.
        group.MapPost("/plans/{planId:guid}/rejection", async (
                Guid planId,
                RejectPlanRequestV1 request,
                ManufacturingCommandService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await WriteAsync(httpContext, key => service.RejectPlanAsync(planId, request, key,
                ErpPermissions.ManufacturingPlansApprove, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.ManufacturingPlansApprove)
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict)
            .WithName("RejectManufacturingPlanV1")
            .Accepts<RejectPlanRequestV1>("application/json")
            .Produces<PlanViewV1>(StatusCodes.Status200OK)
            .WithPlanDecisionProblems(RejectPlanUnprocessableCodes);

        // Üretim U2-a (API-11): the cancellation of a Draft, or of an approved plan that opened no order, with a reason,
        // under its own grant (`manufacturing.plans.cancel`, company-wide, granted to no role). The plan is kept as
        // Cancelled; a plan with an order is refused (422 manufacturing_plan_has_order). ADR-011 as the approval.
        group.MapPost("/plans/{planId:guid}/cancellation", async (
                Guid planId,
                CancelPlanRequestV1 request,
                ManufacturingCommandService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await WriteAsync(httpContext, key => service.CancelPlanAsync(planId, request, key,
                ErpPermissions.ManufacturingPlansCancel, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.ManufacturingPlansCancel)
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict)
            .WithName("CancelManufacturingPlanV1")
            .Accepts<CancelPlanRequestV1>("application/json")
            .Produces<PlanViewV1>(StatusCodes.Status200OK)
            .WithPlanDecisionProblems();

        // Üretim U2-b / API-16 (SAT-D219): links the plan to an approved sales order line (read from Sales through the host),
        // moves the link to another line, or removes it (neither id), under the plan's write grant. The plan's version does
        // not move; its link revision does (409 when it moved). ADR-011 as the other plan commands.
        group.MapPost("/plans/{planId:guid}/sales-order-link", async (
                Guid planId,
                ChangePlanSalesOrderLinkRequestV1 request,
                ManufacturingCommandService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await WriteAsync(httpContext, key => service.ChangePlanSalesOrderLinkAsync(planId, request, key,
                ErpPermissions.ManufacturingPlansWrite, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.ManufacturingPlansWrite)
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict)
            .WithName("ChangeManufacturingPlanSalesOrderLinkV1")
            .Accepts<ChangePlanSalesOrderLinkRequestV1>("application/json")
            .Produces<PlanViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(BadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(PlanSalesOrderLinkConflictCodes)
            .ProducesErpUnprocessableEntity(PlanSalesOrderLinkUnprocessableCodes)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpCodeLessNotFound();

        // Üretim F-010a (URT-D29): enters, changes or removes (null) the plan's hand-entered termin — a calendar day, never
        // computed — under the plan's write grant. The plan's version does not move; its termin revision does (409 when it
        // moved). ADR-011 as the other plan commands.
        group.MapPost("/plans/{planId:guid}/due-date", async (
                Guid planId,
                ChangePlanDueDateRequestV1 request,
                ManufacturingCommandService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await WriteAsync(httpContext, key => service.ChangePlanDueDateAsync(planId, request, key,
                ErpPermissions.ManufacturingPlansWrite, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.ManufacturingPlansWrite)
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict)
            .WithName("ChangeManufacturingPlanDueDateV1")
            .Accepts<ChangePlanDueDateRequestV1>("application/json")
            .Produces<PlanViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(BadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(PlanDueDateConflictCodes)
            .ProducesErpUnprocessableEntity(PlanDueDateUnprocessableCodes)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpCodeLessNotFound();

        // MFG-PLAN-EDIT: the Draft edit of a plan, under the plan save's own grant (`manufacturing.plans.write`,
        // company-wide): the new content as a whole with the save's checks, a reason, and the version the caller read.
        // Only a Draft with no order is edited; an approved plan, a plan with a slitting spec or a move to another
        // process is refused (422) and a stale version is 409. ADR-011: one attempt per key, replayed.
        group.MapPost("/plans/{planId:guid}/revision", async (
                Guid planId,
                RevisePlanRequestV1 request,
                ManufacturingCommandService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await WriteAsync(httpContext, key => service.RevisePlanAsync(planId, request, key,
                ErpPermissions.ManufacturingPlansWrite, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.ManufacturingPlansWrite)
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict)
            .WithName("ReviseManufacturingPlanV1")
            .Accepts<RevisePlanRequestV1>("application/json")
            .Produces<PlanViewV1>(StatusCodes.Status200OK)
            .WithRevisePlanProblems();

        // URT-S10 (F-010b, SINAN-CEVAP 14 NS-10): the plan's termin SUGGESTION from the work center calendar and the
        // profile's hourly capacity. Read only, under the plan read grant: it sets no termin (the user takes the day
        // through POST /plans/{planId}/due-date). A suggestion that cannot be given is a 200 "unavailable" with its
        // reason; another company's plan is the code-less 404 of the plan read; a retryable failure is 503.
        group.MapGet("/plans/{planId:guid}/due-date-suggestion", async (
                Guid planId,
                ManufacturingCommandService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response.Headers);
                return ToHttpResult(httpContext, await service.GetPlanDueDateSuggestionAsync(planId, cancellationToken),
                    location: null);
            })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.ManufacturingPlansRead)
            .WithName("GetManufacturingPlanDueDateSuggestionV1")
            .Produces<PlanDueDateSuggestionViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        // URT-D97/D103: independent append-only priority revisions; the existing approval grant changes the score.
        group.MapGet("/plans/{planId:guid}/priority", async (Guid planId, ManufacturingCommandService service,
                HttpContext httpContext, CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response.Headers);
                return ToHttpResult(httpContext, await service.GetPlanPriorityAsync(planId, cancellationToken), null);
            })
            .RequireCompanyContext().RequireAuthorization(ErpPermissions.ManufacturingPlansRead)
            .WithName("GetManufacturingPlanPriorityV1").Produces<PlanPriorityViewV1>()
            .ProducesErpBadRequest().ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

        group.MapPost("/plans/{planId:guid}/priority", async (Guid planId, ChangePlanPriorityRequestV1 request,
                ManufacturingCommandService service, HttpContext httpContext, CancellationToken cancellationToken) =>
            await WriteAsync(httpContext, key => service.ChangePlanPriorityAsync(planId, request, key, cancellationToken)))
            .RequireCompanyContext().RequireAuthorization(ErpPermissions.ManufacturingPlansApprove)
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict)
            .WithName("ChangeManufacturingPlanPriorityV1").Accepts<ChangePlanPriorityRequestV1>("application/json")
            .Produces<PlanPriorityViewV1>().ProducesErpBadRequest(BadRequestCodes).AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType().ProducesErpConflict(ConflictCodes)
            .ProducesErpUnprocessableEntity([.. ManufacturingProblemCodes.Unprocessable, ManufacturingRuleCodes.PlanCancelled,
                "manufacturing_plan_priority_reason_required", "manufacturing_plan_priority_unchanged"])
            .ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems().ProducesErpCodeLessNotFound();

        group.MapPost("/plans/{planId:guid}/work-center-queue/reorder", async (Guid planId,
                MoveManufacturingQueuePlanRequestV1 request, ManufacturingCommandService service,
                HttpContext httpContext, CancellationToken cancellationToken) =>
            await WriteAsync(httpContext, key => service.MoveQueuePlanAsync(planId, request, key, cancellationToken)))
            .RequireCompanyContext().RequireAuthorization(ErpPermissions.ManufacturingPlansRead, ErpPermissions.ManufacturingPlansReorder)
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict)
            .WithName("MoveManufacturingWorkCenterQueuePlanV1").Accepts<MoveManufacturingQueuePlanRequestV1>("application/json")
            .Produces<PlanWorkCenterQueueViewV1>().ProducesErpBadRequest(BadRequestCodes).AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType().ProducesErpConflict(ConflictCodes)
            .ProducesErpUnprocessableEntity([.. ManufacturingProblemCodes.Unprocessable, "manufacturing_queue_member_invalid", "manufacturing_queue_unchanged"])
            .ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems().ProducesErpCodeLessNotFound();

        group.MapPost("/orders/{orderId:guid}/reopening", async (Guid orderId, ReopenManufacturingOrderRequestV1 request,
                ManufacturingCommandService service, HttpContext httpContext, CancellationToken cancellationToken) =>
            await WriteAsync(httpContext, key => service.ReopenOrderAsync(orderId, request, key, cancellationToken)))
            .RequireCompanyContext().RequireAuthorization(ErpPermissions.ManufacturingOrdersRead, ErpPermissions.ManufacturingOrdersReopen)
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict)
            .WithName("ReopenManufacturingOrderV1").Accepts<ReopenManufacturingOrderRequestV1>("application/json")
            .Produces<OrderViewV1>().ProducesErpBadRequest(BadRequestCodes).AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType().ProducesErpConflict([.. ConflictCodes, ManufacturingStoreCodes.CostTransferReversalRequired])
            .ProducesErpUnprocessableEntity([.. ManufacturingProblemCodes.Unprocessable, "manufacturing_order_not_closed", "manufacturing_order_reopen_period_not_open"])
            .ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems().ProducesErpCodeLessNotFound();

        // URT-D99/D107: the same server queue used by termin calculation, including its non-blocking over-plan flag.
        group.MapGet("/plans/{planId:guid}/work-center-queue", async (Guid planId, ManufacturingCommandService service,
                HttpContext httpContext, CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response.Headers);
                return ToHttpResult(httpContext, await service.GetPlanWorkCenterQueueAsync(planId, cancellationToken), null);
            })
            .RequireCompanyContext().RequireAuthorization(ErpPermissions.ManufacturingPlansRead)
            .WithName("GetManufacturingPlanWorkCenterQueueV1").Produces<PlanWorkCenterQueueViewV1>()
            .ProducesErpBadRequest().ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

        // MFG-PLAN-EDIT: the plan's history under the plan read's grant — revision 0 as created, then every recorded
        // Draft edit with its reason, actor and time. Another company's plan is the same code-less 404 as an unknown id.
        group.MapGet("/plans/{planId:guid}/revisions", async (
                Guid planId,
                ManufacturingCommandService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response.Headers);
                return ToHttpResult(httpContext, await service.ListPlanRevisionsAsync(planId, cancellationToken),
                    location: null);
            })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.ManufacturingPlansRead)
            .WithName("ListManufacturingPlanRevisionsV1")
            .Produces<PlanRevisionListViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

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

        // MFG-PLAN-LIST: the company's plans, newest first, from the stored rows only — so a saved plan can be found
        // and reopened after its screen was closed. The order list's rules and the plan read's grant
        // (`manufacturing.plans.read`, company-wide); no engine answer, spec or roll label.
        group.MapGet("/plans", async (
                int? pageSize,
                string? cursor,
                string? status,
                ManufacturingCommandService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response.Headers);
                return ToHttpResult(httpContext, await service.ListPlansAsync(pageSize, cursor, status, cancellationToken),
                    location: null);
            })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.ManufacturingPlansRead)
            .WithName("ListManufacturingPlansV1")
            .Produces<PlanListViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(BadRequestCodes)
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
            .WithCreateOrderProblems();

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
            .WithIssueInputProblems();

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
        MapFact(group, "/orders/{orderId:guid}/process-losses", "DeclareManufacturingProcessLossV1",
            (service, orderId, request, key, cancellationToken) =>
                service.RecordProcessLossAsync(orderId, request, key, ErpPermissions.ManufacturingFactsRecord,
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
            .WithDeclareOutputProblems();

        // F-009 D3 (API-25, URT-S2): the correction of a declared output at the order version the caller read. It records
        // an append-only reversal of the output fact and Inventory's manufacturing.output-reversal movement together;
        // the original fact is never changed. Its own permission; one attempt per Idempotency-Key.
        group.MapPost("/orders/{orderId:guid}/facts/{factId:guid}/correction", async (
                Guid orderId,
                Guid factId,
                CorrectOutputRequestV1 request,
                ManufacturingCommandService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await WriteAsync(httpContext, key => service.CorrectOutputAsync(orderId, factId, request, key,
                ErpPermissions.ManufacturingFactsCorrect, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.ManufacturingFactsCorrect)
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict)
            .WithName("CorrectManufacturingOutputV1")
            .Accepts<CorrectOutputRequestV1>("application/json")
            .Produces<OrderViewV1>(StatusCodes.Status200OK)
            .WithCorrectOutputProblems();

        // MFG-ORD: the close of an order at the version the caller read. The persisted Settings snapshot
        // supplies the exact company/process/date tolerance; a reasoned out-of-tolerance close additionally
        // requires the dedicated exception permission. Neither permission supplies a tolerance value.
        // 422 naming its code. One attempt per Idempotency-Key, like every other write here.
        group.MapPost("/orders/{orderId:guid}/close", async (
                Guid orderId,
                CloseOrderRequestV1 request,
                ManufacturingCommandService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await WriteAsync(httpContext, key => service.CloseOrderAsync(orderId, request, key,
                ErpPermissions.ManufacturingOrdersClose,
                ErpPermissions.ManufacturingOrdersCloseExceptionApprove,
                cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.ManufacturingOrdersClose)
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict)
            .WithName("CloseManufacturingOrderV1")
            .Accepts<CloseOrderRequestV1>("application/json")
            .Produces<OrderViewV1>(StatusCodes.Status200OK)
            .WithWriteProblems(notFound: true);

        // F-009 D5 (URT-D33/D34): cancels an open order with no net effect, with a reason, under its own grant
        // (`manufacturing.orders.cancel`, company-wide, granted to no role). The order is kept as Cancelled; its plan is
        // released for re-approval and the plan's allocation is released in Inventory in the same transaction.
        group.MapPost("/orders/{orderId:guid}/cancellation", async (
                Guid orderId,
                CancelOrderRequestV1 request,
                ManufacturingCommandService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await WriteAsync(httpContext, key => service.CancelOrderAsync(orderId, request, key,
                ErpPermissions.ManufacturingOrdersCancel, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.ManufacturingOrdersCancel)
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict)
            .WithName("CancelManufacturingOrderV1")
            .Accepts<CancelOrderRequestV1>("application/json")
            .Produces<OrderViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(BadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(ConflictCodes)
            .ProducesErpUnprocessableEntity(CancelOrderUnprocessableCodes)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpCodeLessNotFound();

        // URT-D66 (3)-(5): changes the open order's termin on the order alone — the plan's termin does not move — under the
        // order edit grant (no termin permission of its own). The order's version does not move; its termin revision does
        // (409 when it moved). A day before the Istanbul business day is 422. ADR-011 as the other order commands.
        group.MapPost("/orders/{orderId:guid}/due-date", async (
                Guid orderId,
                ChangeOrderDueDateRequestV1 request,
                ManufacturingCommandService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await WriteAsync(httpContext, key => service.ChangeOrderDueDateAsync(orderId, request, key,
                ErpPermissions.ManufacturingOrdersWrite, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.ManufacturingOrdersWrite)
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict)
            .WithName("ChangeManufacturingOrderDueDateV1")
            .Accepts<ChangeOrderDueDateRequestV1>("application/json")
            .Produces<OrderViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(BadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(OrderDueDateConflictCodes)
            .ProducesErpUnprocessableEntity(OrderDueDateUnprocessableCodes)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpCodeLessNotFound();

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

        // URT-S22 (API-28, URT-D63, KLN-D44): the order's lineage — every output label with all its input labels and
        // their kg shares from the one share calculator (backward), every input label with the outputs it went into
        // (forward), and one step into other orders. The order read's context, permission and answers; no new
        // permission. A read: nothing is stored.
        group.MapGet("/orders/{orderId:guid}/lineage", async (
                Guid orderId,
                ManufacturingCommandService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response.Headers);
                return ToHttpResult(httpContext, await service.GetOrderLineageAsync(orderId, cancellationToken), location: null);
            })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.ManufacturingOrdersRead)
            .WithName("GetManufacturingOrderLineageV1")
            .Produces<OrderLineageViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        // URT-S19 (B-27, DEC-PRDCT-009, API-42): the order's cost trace — lots, line, fire, subcontract owner, sales-order
        // link and fact moments from the stored facts, and the production cost port's answer (never a second formula, never
        // a zero). Its own grant, registered and granted to no role. A read: nothing is stored.
        group.MapGet("/orders/{orderId:guid}/cost-trace", async (
                Guid orderId,
                ManufacturingCommandService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response.Headers);
                return ToHttpResult(httpContext, await service.GetCostTraceAsync(orderId, cancellationToken), location: null);
            })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.ManufacturingReportsRead)
            .WithName("GetManufacturingOrderCostTraceV1")
            .Produces<CostTraceViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/orders/{orderId:guid}/non-kg-inputs", async (Guid orderId, ManufacturingCommandService service, HttpContext http, CancellationToken token) =>
            {
                SetPrivateNoStore(http.Response.Headers);
                return ToHttpResult(http, await service.GetNonKgInputsAsync(orderId, token), null);
            })
            .RequireCompanyContext().RequireAuthorization(ErpPermissions.ManufacturingOrdersRead)
            .WithName("GetManufacturingOrderNonKgInputsV1").Produces<ManufacturingNonKgInputHistoryV1>()
            .ProducesErpBadRequest().ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

        group.MapPost("/orders/{orderId:guid}/non-kg-inputs", async (Guid orderId, RecordManufacturingNonKgInputRequestV1 request,
                ManufacturingCommandService service, HttpContext http, CancellationToken token) =>
            await WriteAsync(http, key => service.RecordNonKgInputAsync(orderId, request, key, token)))
            .RequireCompanyContext().RequireAuthorization(ErpPermissions.ManufacturingFactsRecord)
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict)
            .WithName("RecordManufacturingOrderNonKgInputV1").Accepts<RecordManufacturingNonKgInputRequestV1>("application/json")
            .Produces<ManufacturingNonKgInputHistoryV1>().ProducesErpBadRequest(BadRequestCodes).AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType().ProducesErpConflict(ConflictCodes).ProducesErpUnprocessableEntity(ManufacturingProblemCodes.Unprocessable)
            .ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

        group.MapGet("/orders/{orderId:guid}/production-cost-usd", async (Guid orderId, ManufacturingCommandService service, HttpContext http, CancellationToken token) =>
            {
                SetPrivateNoStore(http.Response.Headers);
                return ToHttpResult(http, await service.GetProductionUsdCostAsync(orderId, token), location: null);
            })
            .RequireCompanyContext().RequireAuthorization(ErpPermissions.ManufacturingReportsRead)
            .WithName("GetManufacturingOrderProductionUsdCostV1").Produces<ManufacturingProductionUsdCostViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest().ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

        group.MapGet("/orders/{orderId:guid}/cost-generations", async (Guid orderId, ManufacturingCommandService service,
                HttpContext http, CancellationToken token) =>
            {
                SetPrivateNoStore(http.Response.Headers);
                return ToHttpResult(http, await service.GetCostGenerationsAsync(orderId, token), location: null);
            })
            .RequireCompanyContext().RequireAuthorization(ErpPermissions.ManufacturingReportsRead)
            .WithName("GetManufacturingOrderCostGenerationsV1")
            .Produces<ManufacturingCostGenerationHistoryV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest().ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

        group.MapPost("/orders/{orderId:guid}/cost-recognition", async (Guid orderId, RecognizeManufacturingOrderCostRequestV1 request,
                ManufacturingCommandService service, HttpContext http, CancellationToken token) =>
                await WriteAsync(http, key => service.RecognizeCostAsync(orderId, request, key, token)))
            .RequireCompanyContext().RequireAuthorization(ErpPermissions.ManufacturingOrdersClose)
            .WithName("RecognizeManufacturingOrderCostV1")
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict)
            .Accepts<RecognizeManufacturingOrderCostRequestV1>("application/json")
            .Produces<ManufacturingCostGenerationViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(BadRequestCodes).AllowsCodeLessErpBadRequestBody().ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(ConflictCodes).ProducesErpUnprocessableEntity(ManufacturingProblemCodes.Unprocessable)
            .ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

        // URT-S17 (SAT-D317): the order's production cost as the production cost port version 2 records it — the kg cost,
        // the TL per line, the rounding remainder, the currency lines and the inputs that keep the cost from being computed.
        // An order that is not closed is pending (order_not_closed); no port source answers unavailable, never a zero. The
        // reports grant, registered and granted to no role. A read: nothing is stored.
        group.MapGet("/orders/{orderId:guid}/production-cost", async (
                Guid orderId,
                ManufacturingCommandService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response.Headers);
                return ToHttpResult(httpContext, await service.GetProductionCostAsync(orderId, cancellationToken),
                    location: null);
            })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.ManufacturingReportsRead)
            .WithName("GetManufacturingOrderProductionCostV1")
            .Produces<ProductionCostViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        // URT-D42 (3): the company's already-closed orders whose output exceeded input on a parent roll, per roll — listed,
        // never reopened or changed. The order list's paging and the order read's grant; a read: nothing is stored.
        group.MapGet("/reports/output-above-input-closures", async (
                int? pageSize,
                string? cursor,
                ManufacturingCommandService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response.Headers);
                return ToHttpResult(httpContext,
                    await service.ListOutputAboveInputClosuresAsync(pageSize, cursor, cancellationToken), location: null);
            })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.ManufacturingOrdersRead)
            .WithName("GetManufacturingOutputAboveInputClosureReportV1")
            .Produces<OutputAboveInputClosureReportViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(BadRequestCodes)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        // URT-S13 (SAT-D316 (3)-(4), URT-D47, URT-D65): records the company's first scrap price of one calendar month in TL
        // per kg, under its own grant (registered, granted to no role: who enters it is not decided). A recorded month is
        // never replaced by a first entry (409; URT-D72 corrects it); a future month is 422 (URT-D73). ADR-011 as the
        // other commands; only the success is replayed.
        group.MapPost("/scrap-prices", async (
                RecordMonthlyScrapPriceRequestV1 request,
                ManufacturingCommandService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await WriteAsync(httpContext, key => service.RecordMonthlyScrapPriceAsync(request, key,
                ErpPermissions.ManufacturingScrapPricesWrite, cancellationToken),
                price => $"/api/v1/manufacturing/scrap-prices?year={price.Year.ToString(CultureInfo.InvariantCulture)}"))
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.ManufacturingScrapPricesWrite)
            .WithIdempotencyContract(StatusCodes.Status201Created)
            .WithName("RecordManufacturingMonthlyScrapPriceV1")
            .Accepts<RecordMonthlyScrapPriceRequestV1>("application/json")
            .Produces<MonthlyScrapPriceViewV1>(StatusCodes.Status201Created)
            .ProducesErpBadRequest(BadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(ScrapPriceConflictCodes)
            .ProducesErpUnprocessableEntity(ScrapPriceUnprocessableCodes)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        // URT-D72: corrects a month's scrap price with a new version and a reason under the same grant as the entry
        // (URT-D57); the old version stays, the latest is effective and both are audited. A month without a price is 404,
        // a stale expected version 409; the same price, a fifth decimal and a future month (URT-D73) are 422. ADR-011:
        // the success, the not-found and the version conflict are this key's answer.
        group.MapPost("/scrap-prices/{year:int}/{month:int}/corrections", async (
                int year,
                int month,
                CorrectMonthlyScrapPriceRequestV1 request,
                ManufacturingCommandService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await WriteAsync(httpContext, key => service.CorrectMonthlyScrapPriceAsync(year, month, request, key,
                ErpPermissions.ManufacturingScrapPricesWrite, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.ManufacturingScrapPricesWrite)
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict)
            .WithName("CorrectManufacturingMonthlyScrapPriceV1")
            .Accepts<CorrectMonthlyScrapPriceRequestV1>("application/json")
            .Produces<MonthlyScrapPriceViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(BadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(ConflictCodes)
            .ProducesErpUnprocessableEntity(ScrapPriceCorrectionUnprocessableCodes)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpCodeLessNotFound();

        // URT-S13: the company's recorded scrap prices of one year, each as its effective (latest) version (URT-D72), the
        // latest month first. A month that is not listed has
        // no price (its scrap weighings leave the cost pending). A read: nothing is stored.
        group.MapGet("/scrap-prices", async (
                int? year,
                ManufacturingCommandService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response.Headers);
                return ToHttpResult(httpContext, await service.ListMonthlyScrapPricesAsync(year, cancellationToken),
                    location: null);
            })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.ManufacturingScrapPricesRead)
            .WithName("ListManufacturingMonthlyScrapPricesV1")
            .Produces<MonthlyScrapPriceListViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(BadRequestCodes)
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

    private static RouteHandlerBuilder WithWriteProblems(this RouteHandlerBuilder builder, bool notFound,
        string[]? conflictCodes = null)
    {
        builder
            .ProducesErpBadRequest(BadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(conflictCodes ?? ConflictCodes)
            .ProducesErpUnprocessableEntity(ManufacturingProblemCodes.Unprocessable)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
        return notFound ? builder.ProducesErpCodeLessNotFound() : builder;
    }

    private static RouteHandlerBuilder WithDeclareOutputProblems(this RouteHandlerBuilder builder) =>
        builder
            .ProducesErpBadRequest(BadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(DeclareOutputConflictCodes)
            .ProducesErpUnprocessableEntity(DeclareOutputUnprocessableCodes)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpCodeLessNotFound();

    private static RouteHandlerBuilder WithCorrectOutputProblems(this RouteHandlerBuilder builder) =>
        builder
            .ProducesErpBadRequest(BadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(CorrectOutputConflictCodes)
            .ProducesErpUnprocessableEntity(CorrectOutputUnprocessableCodes)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpCodeLessNotFound();

    // SAT-D318 (owner decision of 2026-11-13): the input issue's 409 is the write set's problem plus an optional
    // `stockShortfall` member that names the issue Inventory refused for physical stock. Same status, same codes.
    private static RouteHandlerBuilder WithIssueInputProblems(this RouteHandlerBuilder builder) =>
        builder
            .ProducesErpBadRequest(BadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesManufacturingInputIssueConflict(IssueInputConflictCodes)
            .ProducesErpUnprocessableEntity(ManufacturingProblemCodes.Unprocessable)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpCodeLessNotFound();

    // Üretim U2-a: the approval reads each parent roll's current label for the slitting gate, so Inventory's label version /
    // read conflict (409) joins its write set; its 422 set is the write set (which already names the label refusal).
    private static RouteHandlerBuilder WithApprovePlanProblems(this RouteHandlerBuilder builder) =>
        builder
            .ProducesErpBadRequest(BadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesManufacturingPlanAllocationConflict(ApprovePlanConflictCodes)
            .ProducesErpUnprocessableEntity(ApprovePlanUnprocessableCodes)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpCodeLessNotFound();

    private static RouteHandlerBuilder WithPlanDecisionProblems(this RouteHandlerBuilder builder,
        string[]? unprocessableCodes = null) =>
        builder
            .ProducesErpBadRequest(BadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(ConflictCodes)
            .ProducesErpUnprocessableEntity(unprocessableCodes ?? PlanDecisionUnprocessableCodes)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpCodeLessNotFound();

    private static RouteHandlerBuilder WithCreateOrderProblems(this RouteHandlerBuilder builder) =>
        builder
            .ProducesErpBadRequest(BadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(ConflictCodes)
            .ProducesErpUnprocessableEntity(CreateOrderUnprocessableCodes)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpCodeLessNotFound();

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

    // AT-061: the preview answers only what the save's own checks can answer before its write: the grammar (400), a
    // roll Inventory no longer knows (code-less 404), Inventory's label version / read conflict (409), the save's 422
    // set, an owner that is not there (503). No idempotency or store conflict: nothing is written.
    private static readonly string[] PreviewPlanConflictCodes =
    [
        InventoryParentRollCodesV1.VersionConflict,
        InventoryParentRollCodesV1.ReadConflict,
    ];

    private static RouteHandlerBuilder WithPreviewPlanProblems(this RouteHandlerBuilder builder) =>
        builder
            .ProducesErpBadRequest(BadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(PreviewPlanConflictCodes)
            .ProducesErpUnprocessableEntity(SavePlanUnprocessableCodes)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

    private static RouteHandlerBuilder WithRevisePlanProblems(this RouteHandlerBuilder builder) =>
        builder
            .ProducesErpBadRequest(BadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(SavePlanConflictCodes)
            .ProducesErpUnprocessableEntity(RevisePlanUnprocessableCodes)
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
        SetFixtureDiagnostic(httpContext, result.Status, result.Code);

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
                result.ExpectedVersion, result.CurrentVersion, result.StockShortfall, result.PlanAllocationShortfall),
            ManufacturingApiStatus.Unprocessable => Problem(StatusCodes.Status422UnprocessableEntity, result.Code!,
                result.Field),
            ManufacturingApiStatus.Unavailable => Problem(StatusCodes.Status503ServiceUnavailable,
                ErpProblemCodes.ServiceUnavailable, null),
            _ => throw new InvalidOperationException("Tanımsız üretim komutu sonucu."),
        };
    }

    internal static void SetFixtureDiagnostic(
        HttpContext httpContext,
        ManufacturingApiStatus status,
        string? code)
    {
        if (status != ManufacturingApiStatus.Unavailable
            || FixtureDiagnosticPhase(code) is not { } phase)
        {
            return;
        }

        var environment = httpContext.RequestServices.GetService<IHostEnvironment>();
        var configuration = httpContext.RequestServices.GetService<IConfiguration>();
        if (environment?.IsEnvironment("Testing") != true
            || !string.Equals(configuration?["BrowserSuiteApi:Profile"], "mfg", StringComparison.Ordinal)
            || httpContext.RequestServices.GetService<ISettingsManufacturingToleranceProviderV1>()
                is not C01E2eManufacturingToleranceFixtureProvider)
        {
            return;
        }

        httpContext.Response.Headers[FixtureDiagnosticHeaderName] = phase;
    }

    internal static string? FixtureDiagnosticPhase(string? code) => code switch
    {
        "counterparty_provider_unavailable" => "mfg_plan_party_account",
        InventoryParentRollCodesV1.Unavailable => "mfg_plan_parent_roll",
        "product_manufacturing_technical_snapshot_unavailable"
            or "product_runtime_unconfigured"
            or "product_family_policy_unavailable"
            or "product_family_policy_invalid"
            or "product_manufacturing_measure_undefined" => "mfg_plan_product_snapshot",
        "settings_provider_unavailable"
            or "settings_profile_invalid"
            or "settings_unit_profile_not_found" => "mfg_plan_settings_profile",
        _ => null,
    };

    private static IResult Problem(int statusCode, string code, string? field, long? expectedVersion = null,
        long? currentVersion = null, ManufacturingStockShortfallViewV1? stockShortfall = null,
        ManufacturingPlanAllocationShortfallV1? planAllocationShortfall = null)
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

        // SAT-D318: optional, only on a fresh physical-stock refusal of an input issue; never on a replayed answer.
        if (stockShortfall is not null)
        {
            extensions["stockShortfall"] = stockShortfall;
        }
        if (planAllocationShortfall is not null) extensions["planAllocationShortfall"] = planAllocationShortfall;

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
        ManufacturingRuleCodes.SlittingSpecWithoutLines,
        ManufacturingRuleCodes.PlanParametersUnavailable,
        ManufacturingRuleCodes.PlanEngineRefused,
        ManufacturingRuleCodes.PlanLabelNotUsable,
        ManufacturingRuleCodes.PlanWindingDoesNotFit,
        ManufacturingRuleCodes.OrderClosed,
        // F-009 D5 (URT-D33): a cancelled order takes no new fact and no close.
        ManufacturingRuleCodes.OrderCancelled,
        ManufacturingRuleCodes.UnexplainedDifference,
        GoymenERP.Modules.Manufacturing.Domain.Orders.ClosureAssessmentCodesV1.PolicyInvalid,
        GoymenERP.Modules.Manufacturing.Domain.Orders.ClosureAssessmentCodesV1.PolicyNotEffective,
        GoymenERP.Modules.Manufacturing.Domain.Orders.ClosureAssessmentCodesV1.PolicyScopeMismatch,
        GoymenERP.Modules.Manufacturing.Domain.Orders.ClosureAssessmentCodesV1.FireDeclarationRequired,
        GoymenERP.Modules.Manufacturing.Domain.Orders.ClosureAssessmentCodesV1.ApprovalRequired,
        GoymenERP.Modules.Manufacturing.Domain.Orders.ClosureAssessmentCodesV1.ApprovalInvalid,
        GoymenERP.Modules.Manufacturing.Domain.Orders.ClosureAssessmentCodesV1.ApprovalUnexpected,
        GoymenERP.Modules.Manufacturing.Domain.Orders.ClosureAssessmentCodesV1.RollInputInvalid,
        GoymenERP.Modules.Manufacturing.Domain.Orders.ClosureAssessmentCodesV1.FireMassMismatch,
        GoymenERP.Modules.Manufacturing.Domain.Orders.ClosureAssessmentCodesV1.NetIssuedMassMismatch,
        GoymenERP.Modules.Manufacturing.Domain.Orders.ClosureAssessmentCodesV1.NegativeDifferenceReview,
        GoymenERP.Modules.Manufacturing.Domain.Orders.ClosureAssessmentCodesV1.OutputExceedsInput,
        GoymenERP.Modules.Settings.Application.Contracts.V1.SettingsPrerequisiteCodesV1.ManufacturingToleranceProfileNotFound,
        ManufacturingRuleCodes.RollNotInOrder,
        ManufacturingRuleCodes.RollNotIssued,
        ManufacturingRuleCodes.InputAlreadyIssued,
        ManufacturingRuleCodes.ReturnAlreadyRecorded,
        ManufacturingRuleCodes.ReturnExceedsIssued,
        ManufacturingRuleCodes.ReturnExceedsUnconsumed,
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
