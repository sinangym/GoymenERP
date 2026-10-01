using System.Text.Json;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

public static class FinanceEndpoints
{
    public const string DraftContextPath = "/api/v1/finance/manual-receipts/draft-context";
    public const string ManualReceiptsPath = "/api/v1/finance/manual-receipts";
    public const string DraftsPath = "/api/v1/finance/manual-receipts/drafts";
    public const string ReversalProposalsPath = "/api/v1/finance/manual-receipts/reversal-proposals";
    public const string BankAccountsPath = "/api/v1/finance/bank-accounts";
    public const string BankAccountMasterPath = "/api/v1/finance/bank-accounts/master";
    public const string BankAccountCurrencyCandidatesPath = "/api/v1/finance/bank-accounts/currency-candidates";
    public const string ManualBankPaymentsPath = "/api/v1/finance/manual-bank-payments";
    public const string AllocationsPath = "/api/v1/finance/allocations";
    public const string CustomerInstrumentsPath = "/api/v1/finance/customer-instruments";
    public const string PartyLedgerPath = "/api/v1/finance/party-ledger";
    public const string OfficialInvoiceImportsPath = "/api/v1/finance/official-invoice-imports";
    public const string OfficialInvoiceImportBatchesPath = "/api/v1/finance/official-invoice-import-batches";
    private const string IdempotencyHeaderName = "Idempotency-Key";
    private const int MaximumIdempotencyKeyLength = 128;
    private static readonly JsonSerializerOptions ContractJson = FinanceJson.CreateOptions();

    public static IEndpointRouteBuilder MapFinanceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var handler = endpoints.MapGet(DraftContextPath, async (
            [FromQuery] Guid treasuryAccountId,
            [FromQuery] Guid partyId,
            [FromQuery] Guid companyAccountId,
            [FromQuery] DateOnly occurredOn,
            IFinanceDraftContextEndpointService service,
            ICorrelationContext correlationContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            httpContext.Response.Headers.CacheControl = "private, no-store";
            httpContext.Response.Headers.Pragma = "no-cache";
            // The binder above already refused a missing, repeated or impossible date. It also
            // accepts lenient forms (month-first, unpadded, a dropped time part); only the exact
            // ISO text may reach the reader, as the contract's format: date says.
            var rawOccurredOn = httpContext.Request.Query["occurredOn"];
            if (rawOccurredOn.Count != 1 || !FinanceIsoDate.TryParse(rawOccurredOn[0], out var isoOccurredOn)
                || isoOccurredOn != occurredOn)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await service.GetAsync(
                new(treasuryAccountId, partyId, companyAccountId, occurredOn),
                correlationContext.CorrelationId,
                cancellationToken);
            return result.Outcome switch
            {
                DraftReadOutcome.Found when result.Resource is not null => Results.Ok(result.Resource),
                DraftReadOutcome.InvalidRequest => Results.Problem(statusCode: StatusCodes.Status400BadRequest),
                DraftReadOutcome.Denied => Results.Problem(statusCode: StatusCodes.Status403Forbidden),
                DraftReadOutcome.NotFound => Results.Problem(statusCode: StatusCodes.Status404NotFound),
                DraftReadOutcome.Unavailable => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable),
                _ => throw new InvalidOperationException(
                    "Finance draft context returned an invalid outcome/resource combination."),
            };
        });

        handler.WithName("GetFinanceManualReceiptDraftContextV1")
            .WithTags("Finance")
            .RequireAuthorization()
            .RequireCompanyContext()
            .Produces<DraftReadContextView>()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable();

        MapManualReceiptEndpoints(endpoints);
        MapManualBankPaymentEndpoints(endpoints);
        MapFinanceAllocationEndpoints(endpoints);
        MapCustomerInstrumentEndpoints(endpoints);
        MapBankAccountEndpoints(endpoints);
        MapOfficialInvoiceImportEndpoints(endpoints);
        return endpoints;
    }

    private static void MapOfficialInvoiceImportEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost($"{OfficialInvoiceImportsPath}/preview", async (
                HttpContext httpContext,
                IFinanceOfficialInvoiceImportApplicationServiceV1 application,
                IFinanceOfficialInvoiceImportApprovalAuthorityV1 authority,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                if (await ReadBodyAsync<FinanceOfficialInvoiceImportPreviewRequestV1>(httpContext, cancellationToken)
                    is not { } request)
                {
                    return httpContext.Request.HasJsonContentType()
                        ? Results.Problem(statusCode: StatusCodes.Status400BadRequest)
                        : Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
                }

                var context = Context(companyContext, currentUser, correlation);
                var permission = await CheckOfficialInvoicePreviewAuthorityAsync(
                    authority, context, request.BatchId, cancellationToken);
                if (permission != PermissionDecision.Allowed)
                {
                    return Results.Problem(statusCode: permission == PermissionDecision.Denied
                        ? StatusCodes.Status403Forbidden
                        : StatusCodes.Status503ServiceUnavailable);
                }

                return Results.Json(application.Preview(context, request), ContractJson);
            })
            .WithName("PreviewFinanceOfficialInvoiceImportV1")
            .WithTags("Finance")
            .RequireAuthorization()
            .RequireCompanyContext()
            .Accepts<FinanceOfficialInvoiceImportPreviewRequestV1>("application/json")
            .Produces<FinanceOfficialInvoiceImportPreviewV1>()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpServiceUnavailable();

        endpoints.MapPost(OfficialInvoiceImportBatchesPath, async (
                HttpContext httpContext,
                IFinanceOfficialInvoiceImportApprovalServiceV1 approval,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var key))
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
                if (await ReadBodyAsync<FinanceOfficialInvoiceImportPreviewRequestV1>(httpContext, cancellationToken)
                    is not { } request)
                    return httpContext.Request.HasJsonContentType()
                        ? Results.Problem(statusCode: StatusCodes.Status400BadRequest)
                        : Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);

                var result = await approval.UploadAsync(Context(companyContext, currentUser, correlation),
                    new CommandMetadata(key), request, cancellationToken);
                return Command(httpContext, result, result.Outcome, result.IsReplay, result.OriginalCorrelationId);
            })
            .WithName("UploadFinanceOfficialInvoiceImportBatchV1")
            .WithTags("Finance")
            .RequireAuthorization()
            .RequireCompanyContext()
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .Accepts<FinanceOfficialInvoiceImportPreviewRequestV1>("application/json")
            .Produces<FinanceOfficialInvoiceImportApprovalResultV1>(StatusCodes.Status200OK)
            .Produces<FinanceOfficialInvoiceImportApprovalResultV1>(StatusCodes.Status409Conflict)
            .Produces<FinanceOfficialInvoiceImportApprovalResultV1>(StatusCodes.Status422UnprocessableEntity)
            .ProducesErpAuthenticatedProblems()
            .ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpServiceUnavailable();

        endpoints.MapGet($"{OfficialInvoiceImportBatchesPath}/pending", async (
                [FromQuery] string? cursor,
                HttpContext httpContext,
                IFinanceOfficialInvoiceImportPendingApprovalReadServiceV1 pending,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                return Read(await pending.ListAsync(Context(companyContext, currentUser, correlation), cursor,
                    cancellationToken));
            })
            .WithName("GetPendingFinanceOfficialInvoiceImportBatchesV1")
            .WithTags("Finance")
            .RequireAuthorization()
            .RequireCompanyContext()
            .Produces<FinanceOfficialInvoiceImportPendingBatchPageV1>()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable();

        endpoints.MapGet($"{OfficialInvoiceImportBatchesPath}/{{batchId:guid}}", async (
                Guid batchId,
                HttpContext httpContext,
                IFinanceOfficialInvoiceImportPendingApprovalReadServiceV1 pending,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                return Read(await pending.GetAsync(Context(companyContext, currentUser, correlation), batchId,
                    cancellationToken));
            })
            .WithName("GetFinanceOfficialInvoiceImportPendingBatchV1")
            .WithTags("Finance")
            .RequireAuthorization()
            .RequireCompanyContext()
            .Produces<FinanceOfficialInvoiceImportPendingBatchDetailV1>()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable();

        endpoints.MapPost($"{OfficialInvoiceImportBatchesPath}/{{batchId:guid}}/approval", async (
                Guid batchId,
                HttpContext httpContext,
                IFinanceOfficialInvoiceImportApprovalServiceV1 approval,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var key))
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
                if (await ReadBodyAsync<FinanceOfficialInvoiceImportApproveRequestV1>(httpContext, cancellationToken)
                    is not { } request || request.BatchId != batchId)
                    return httpContext.Request.HasJsonContentType()
                        ? Results.Problem(statusCode: StatusCodes.Status400BadRequest)
                        : Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);

                var result = await approval.ApproveAsync(Context(companyContext, currentUser, correlation),
                    new CommandMetadata(key), request, cancellationToken);
                return Command(httpContext, result, result.Outcome, result.IsReplay, result.OriginalCorrelationId);
            })
            .WithName("ApproveFinanceOfficialInvoiceImportBatchV1")
            .WithTags("Finance")
            .RequireAuthorization()
            .RequireCompanyContext()
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .Accepts<FinanceOfficialInvoiceImportApproveRequestV1>("application/json")
            .Produces<FinanceOfficialInvoiceImportApprovalResultV1>(StatusCodes.Status200OK)
            .Produces<FinanceOfficialInvoiceImportApprovalResultV1>(StatusCodes.Status409Conflict)
            .Produces<FinanceOfficialInvoiceImportApprovalResultV1>(StatusCodes.Status422UnprocessableEntity)
            .ProducesErpAuthenticatedProblems()
            .ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpServiceUnavailable();

        endpoints.MapGet($"{OfficialInvoiceImportsPath}/approved-sources", async (
                [FromQuery] string? cursor,
                HttpContext httpContext,
                IFinanceOpenItemSourceSelectionService selection,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                return Read(await selection.ListApprovedAsync(Context(companyContext, currentUser, correlation), cursor,
                    cancellationToken));
            })
            .WithName("GetFinanceApprovedOfficialInvoiceImportSourcesV1")
            .WithTags("Finance")
            .RequireAuthorization()
            .RequireCompanyContext()
            .Produces<ApprovedFinanceOpenItemSourcePage>()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpServiceUnavailable();

        endpoints.MapPost($"{OfficialInvoiceImportsPath}/apply", async (
                HttpContext httpContext,
                IFinanceOfficialInvoiceImportApplicationServiceV1 application,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var key))
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
                if (await ReadBodyAsync<FinanceOfficialInvoiceImportPreviewRequestV1>(httpContext, cancellationToken)
                    is not { } request)
                    return httpContext.Request.HasJsonContentType()
                        ? Results.Problem(statusCode: StatusCodes.Status400BadRequest)
                        : Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);

                var result = await application.ApplyAsync(Context(companyContext, currentUser, correlation),
                    new CommandMetadata(key), request, cancellationToken);
                return OfficialInvoiceApply(httpContext, result);
            })
            .WithName("ApplyFinanceOfficialInvoiceImportV1")
            .WithTags("Finance")
            .RequireAuthorization()
            .RequireCompanyContext()
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .Accepts<FinanceOfficialInvoiceImportPreviewRequestV1>("application/json")
            .Produces<FinanceOfficialInvoiceImportApplyResultV1>(StatusCodes.Status200OK)
            .Produces<FinanceOfficialInvoiceImportApplyResultV1>(StatusCodes.Status409Conflict)
            .Produces<FinanceOfficialInvoiceImportApplyResultV1>(StatusCodes.Status422UnprocessableEntity)
            .ProducesErpAuthenticatedProblems()
            .ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable();

        endpoints.MapGet($"{OfficialInvoiceImportsPath}/{{batchId:guid}}", async (
                Guid batchId,
                HttpContext httpContext,
                IFinanceOfficialInvoiceImportApplicationServiceV1 application,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                return Read(await application.ReopenAsync(Context(companyContext, currentUser, correlation), batchId,
                    cancellationToken));
            })
            .WithName("GetFinanceOfficialInvoiceImportV1")
            .WithTags("Finance")
            .RequireAuthorization()
            .RequireCompanyContext()
            .Produces<FinanceOfficialInvoiceAppliedBatchV1>()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable();
    }

    internal static async Task<PermissionDecision> CheckOfficialInvoicePreviewAuthorityAsync(
        IFinanceOfficialInvoiceImportApprovalAuthorityV1 authority,
        FinanceContext context,
        Guid batchId,
        CancellationToken cancellationToken)
    {
        var upload = await authority.CheckAsync(context,
            FinanceOfficialInvoiceImportAuthorityActionV1.Upload, batchId, cancellationToken);
        if (upload == PermissionDecision.Allowed)
        {
            return PermissionDecision.Allowed;
        }

        var approve = await authority.CheckAsync(context,
            FinanceOfficialInvoiceImportAuthorityActionV1.Approve, batchId, cancellationToken);
        if (approve == PermissionDecision.Allowed)
        {
            return PermissionDecision.Allowed;
        }

        return upload == PermissionDecision.Unavailable || approve == PermissionDecision.Unavailable
            ? PermissionDecision.Unavailable
            : PermissionDecision.Denied;
    }

    /// <summary>
    /// DEC-FIN-016 first release: the customer cheque / note portfolio. Receive (no money effect), a status change
    /// without a money effect, and the collection — the only money effect (one recorded receipt source, one money
    /// event). Each command needs an <c>Idempotency-Key</c> and answers like every Finance command (200 / 422 / 409;
    /// 403 / 503 code-less); the route's instrument id and the body's must agree, else 400.
    /// </summary>
    private static void MapCustomerInstrumentEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(CustomerInstrumentsPath, async (
                HttpContext httpContext,
                ICustomerInstrumentService service,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var key))
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
                }

                if (await ReadBodyAsync<CustomerInstrumentReceiveRequest>(httpContext, cancellationToken) is not { } request)
                {
                    return httpContext.Request.HasJsonContentType()
                        ? Results.Problem(statusCode: StatusCodes.Status400BadRequest)
                        : Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
                }

                var result = await service.ReceiveAsync(Context(companyContext, currentUser, correlation),
                    new CommandMetadata(key), request, cancellationToken);
                return Command(httpContext, result, result.Outcome, result.IsReplay, result.OriginalCorrelationId);
            })
            .WithName("ReceiveFinanceCustomerInstrumentV1")
            .WithTags("Finance")
            .RequireAuthorization()
            .RequireCompanyContext()
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .Accepts<CustomerInstrumentReceiveRequest>("application/json")
            .Produces<CustomerInstrumentCommandResult>(StatusCodes.Status200OK)
            .Produces<CustomerInstrumentCommandResult>(StatusCodes.Status422UnprocessableEntity)
            .Produces<CustomerInstrumentCommandResult>(StatusCodes.Status409Conflict)
            .ProducesErpAuthenticatedProblems()
            .ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpServiceUnavailable();

        endpoints.MapPost(CustomerInstrumentsPath + "/{instrumentId:guid}/status-changes", async (
                Guid instrumentId,
                HttpContext httpContext,
                ICustomerInstrumentService service,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var key))
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
                }

                if (await ReadBodyAsync<CustomerInstrumentStatusChangeRequest>(httpContext, cancellationToken)
                    is not { } request)
                {
                    return httpContext.Request.HasJsonContentType()
                        ? Results.Problem(statusCode: StatusCodes.Status400BadRequest)
                        : Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
                }

                if (request.InstrumentId != instrumentId)
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
                }

                var result = await service.ChangeStatusAsync(Context(companyContext, currentUser, correlation),
                    new CommandMetadata(key), request, cancellationToken);
                return Command(httpContext, result, result.Outcome, result.IsReplay, result.OriginalCorrelationId);
            })
            .WithName("ChangeFinanceCustomerInstrumentStatusV1")
            .WithTags("Finance")
            .RequireAuthorization()
            .RequireCompanyContext()
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .Accepts<CustomerInstrumentStatusChangeRequest>("application/json")
            .Produces<CustomerInstrumentCommandResult>(StatusCodes.Status200OK)
            .Produces<CustomerInstrumentCommandResult>(StatusCodes.Status422UnprocessableEntity)
            .Produces<CustomerInstrumentCommandResult>(StatusCodes.Status409Conflict)
            .ProducesErpAuthenticatedProblems()
            .ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpServiceUnavailable();

        // The one money effect of the portfolio: the period follows collectedOn (K1); a closed period is a 422
        // finance_period_not_open and an undecidable rule a 422 finance_period_rule_unavailable, both before any write.
        endpoints.MapPost(CustomerInstrumentsPath + "/{instrumentId:guid}/collection", async (
                Guid instrumentId,
                HttpContext httpContext,
                ICustomerInstrumentService service,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var key))
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
                }

                if (await ReadBodyAsync<CustomerInstrumentCollectRequest>(httpContext, cancellationToken) is not { } request)
                {
                    return httpContext.Request.HasJsonContentType()
                        ? Results.Problem(statusCode: StatusCodes.Status400BadRequest)
                        : Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
                }

                if (request.InstrumentId != instrumentId)
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
                }

                var result = await service.CollectAsync(Context(companyContext, currentUser, correlation),
                    new CommandMetadata(key), request, cancellationToken);
                return Command(httpContext, result, result.Outcome, result.IsReplay, result.OriginalCorrelationId);
            })
            .WithName("CollectFinanceCustomerInstrumentV1")
            .WithTags("Finance")
            .RequireAuthorization()
            .RequireCompanyContext()
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .Accepts<CustomerInstrumentCollectRequest>("application/json")
            .Produces<CustomerInstrumentCommandResult>(StatusCodes.Status200OK)
            .Produces<CustomerInstrumentCommandResult>(StatusCodes.Status422UnprocessableEntity)
            .Produces<CustomerInstrumentCommandResult>(StatusCodes.Status409Conflict)
            .ProducesErpAuthenticatedProblems()
            .ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpServiceUnavailable();

        endpoints.MapGet(CustomerInstrumentsPath, async (
                [FromQuery] string? cursor,
                HttpContext httpContext,
                ICustomerInstrumentService service,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                var result = await service.ListAuthorizedAsync(Context(companyContext, currentUser, correlation), cursor,
                    cancellationToken);
                // The list has no "not found": the service answers NotFound only for a malformed or foreign cursor.
                return result.Outcome == ReadOutcome.NotFound
                    ? Results.Problem(statusCode: StatusCodes.Status400BadRequest)
                    : Read(result);
            })
            .WithName("GetFinanceCustomerInstrumentsV1")
            .WithTags("Finance")
            .RequireAuthorization()
            .RequireCompanyContext()
            .Produces<CustomerInstrumentPage>()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpServiceUnavailable();

        // DEC-FIN-016 risk tracking (user decision 2026-09-27): read-only facts per customer account and currency as of
        // an exact ISO date; one customer account needs both ids. No money effect, no limit (OPEN-010).
        endpoints.MapGet(CustomerInstrumentsPath + "/risk", async (
                [FromQuery] DateOnly asOf,
                [FromQuery] Guid? partyId,
                [FromQuery] Guid? companyAccountId,
                HttpContext httpContext,
                ICustomerInstrumentService service,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                var rawAsOf = httpContext.Request.Query["asOf"];
                if (rawAsOf.Count != 1 || !FinanceIsoDate.TryParse(rawAsOf[0], out var isoAsOf) || isoAsOf != asOf)
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
                }

                var result = await service.RiskAuthorizedAsync(Context(companyContext, currentUser, correlation),
                    new CustomerInstrumentRiskRequest(asOf, partyId, companyAccountId), cancellationToken);
                // The view has no "not found": the service answers NotFound only for a half-named customer scope.
                return result.Outcome == ReadOutcome.NotFound
                    ? Results.Problem(statusCode: StatusCodes.Status400BadRequest)
                    : Read(result);
            })
            .WithName("GetFinanceCustomerInstrumentRiskV1")
            .WithTags("Finance")
            .RequireAuthorization()
            .RequireCompanyContext()
            .Produces<CustomerInstrumentRiskView>()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpServiceUnavailable();

        // First-release party ledger (cari hesap ekstresi): one party account's recorded Finance facts in an inclusive
        // range of exact ISO dates, per currency, with a running balance from zero (OPEN-018: no carry-forward).
        endpoints.MapGet(PartyLedgerPath, async (
                [FromQuery] Guid partyId,
                [FromQuery] Guid companyAccountId,
                [FromQuery] DateOnly from,
                [FromQuery] DateOnly to,
                HttpContext httpContext,
                IPartyLedgerService service,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                var rawFrom = httpContext.Request.Query["from"];
                var rawTo = httpContext.Request.Query["to"];
                if (rawFrom.Count != 1 || !FinanceIsoDate.TryParse(rawFrom[0], out var isoFrom) || isoFrom != from
                    || rawTo.Count != 1 || !FinanceIsoDate.TryParse(rawTo[0], out var isoTo) || isoTo != to)
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
                }

                var result = await service.ReadAuthorizedAsync(Context(companyContext, currentUser, correlation),
                    new PartyLedgerRequest(partyId, companyAccountId, from, to), cancellationToken);
                return result.Outcome switch
                {
                    PartyLedgerOutcome.Found when result.Ledger is not null => Results.Json(result.Ledger, ContractJson),
                    PartyLedgerOutcome.Invalid => Results.Problem(statusCode: StatusCodes.Status400BadRequest),
                    PartyLedgerOutcome.TooManyEntries => Results.Problem(
                        statusCode: StatusCodes.Status422UnprocessableEntity,
                        extensions: new Dictionary<string, object?> { ["code"] = PartyLedgerRules.TooManyEntriesCode }),
                    PartyLedgerOutcome.Denied => Results.Problem(statusCode: StatusCodes.Status403Forbidden),
                    PartyLedgerOutcome.Unavailable => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable),
                    _ => throw new InvalidOperationException("Party ledger read returned an invalid outcome/resource combination."),
                };
            })
            .WithName("GetFinancePartyLedgerV1")
            .WithTags("Finance")
            .RequireAuthorization()
            .RequireCompanyContext()
            .Produces<PartyLedgerView>()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnprocessableEntity(PartyLedgerRules.TooManyEntriesCode)
            .ProducesErpServiceUnavailable();

        endpoints.MapGet(CustomerInstrumentsPath + "/{instrumentId:guid}", async (
                Guid instrumentId,
                HttpContext httpContext,
                ICustomerInstrumentService service,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                return Read(await service.DetailAuthorizedAsync(Context(companyContext, currentUser, correlation),
                    instrumentId, cancellationToken));
            })
            .WithName("GetFinanceCustomerInstrumentV1")
            .WithTags("Finance")
            .RequireAuthorization()
            .RequireCompanyContext()
            .Produces<CustomerInstrumentDetail>()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable();
    }

    private static void MapFinanceAllocationEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(AllocationsPath, async (
                HttpContext httpContext,
                IFinanceAllocationReadService service,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                return Read(await service.ListAuthorizedAsync(
                    Context(companyContext, currentUser, correlation), cancellationToken));
            })
            .WithName("GetFinanceAllocationsV1")
            .WithTags("Finance")
            .RequireAuthorization(ErpPermissions.FinanceAllocationRead)
            .RequireCompanyContext()
            .Produces<FinanceAllocationPage>()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpServiceUnavailable();

        endpoints.MapGet(AllocationsPath + "/{allocationId:guid}", async (
                Guid allocationId,
                HttpContext httpContext,
                IFinanceAllocationReadService service,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                return Read(await service.GetAuthorizedAsync(
                    Context(companyContext, currentUser, correlation), allocationId, cancellationToken));
            })
            .WithName("GetFinanceAllocationV1")
            .WithTags("Finance")
            .RequireAuthorization(ErpPermissions.FinanceAllocationRead)
            .RequireCompanyContext()
            .Produces<FinanceAllocationDetail>()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable();

        endpoints.MapPost(AllocationsPath, async (
                HttpContext httpContext,
                IFinanceAllocationService service,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var key))
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
                if (await ReadBodyAsync<CreateFinanceAllocationRequest>(httpContext, cancellationToken)
                    is not { } request)
                    return httpContext.Request.HasJsonContentType()
                        ? Results.Problem(statusCode: StatusCodes.Status400BadRequest)
                        : Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);

                var result = await service.CreateAsync(Context(companyContext, currentUser, correlation),
                    new CommandMetadata(key), request, cancellationToken);
                return Command(httpContext, result, result.Outcome, result.IsReplay, result.OriginalCorrelationId);
            })
            .WithName("CreateFinanceAllocationV1")
            .WithTags("Finance")
            .RequireAuthorization(ErpPermissions.FinanceAllocationWrite)
            .RequireCompanyContext()
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .Accepts<CreateFinanceAllocationRequest>("application/json")
            .Produces<FinanceAllocationCommandResult>(StatusCodes.Status200OK)
            .Produces<FinanceAllocationCommandResult>(StatusCodes.Status409Conflict)
            .Produces<FinanceAllocationCommandResult>(StatusCodes.Status422UnprocessableEntity)
            .ProducesErpAuthenticatedProblems()
            .ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpServiceUnavailable();

        endpoints.MapPost(AllocationsPath + "/{allocationId:guid}/release", async (
                Guid allocationId,
                HttpContext httpContext,
                IFinanceAllocationService service,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var key))
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
                if (await ReadBodyAsync<ReleaseFinanceAllocationRequest>(httpContext, cancellationToken)
                    is not { } request || request.AllocationId != allocationId)
                    return httpContext.Request.HasJsonContentType()
                        ? Results.Problem(statusCode: StatusCodes.Status400BadRequest)
                        : Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);

                var result = await service.ReleaseAsync(Context(companyContext, currentUser, correlation),
                    new CommandMetadata(key), request, cancellationToken);
                return Command(httpContext, result, result.Outcome, result.IsReplay, result.OriginalCorrelationId);
            })
            .WithName("ReleaseFinanceAllocationV1")
            .WithTags("Finance")
            .RequireAuthorization(ErpPermissions.FinanceAllocationWrite)
            .RequireCompanyContext()
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .Accepts<ReleaseFinanceAllocationRequest>("application/json")
            .Produces<FinanceAllocationCommandResult>(StatusCodes.Status200OK)
            .Produces<FinanceAllocationCommandResult>(StatusCodes.Status409Conflict)
            .Produces<FinanceAllocationCommandResult>(StatusCodes.Status422UnprocessableEntity)
            .ProducesErpAuthenticatedProblems()
            .ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpServiceUnavailable();
    }

    private static void MapManualBankPaymentEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(ManualBankPaymentsPath, async (
                HttpContext httpContext,
                IManualBankPaymentService service,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var key))
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
                if (await ReadBodyAsync<ManualBankPaymentDraftSaveRequest>(httpContext, cancellationToken)
                    is not { } request)
                    return httpContext.Request.HasJsonContentType()
                        ? Results.Problem(statusCode: StatusCodes.Status400BadRequest)
                        : Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
                var result = await service.DraftSaveAsync(Context(companyContext, currentUser, correlation),
                    new CommandMetadata(key), request, cancellationToken);
                return Command(httpContext, result, result.Outcome, result.IsReplay, result.OriginalCorrelationId);
            })
            .WithName("SaveFinanceManualBankPaymentDraftV1")
            .WithTags("Finance").RequireAuthorization().RequireCompanyContext()
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .Accepts<ManualBankPaymentDraftSaveRequest>("application/json")
            .Produces<ManualBankPaymentCommandResult>(StatusCodes.Status200OK)
            .Produces<ManualBankPaymentCommandResult>(StatusCodes.Status409Conflict)
            .Produces<ManualBankPaymentCommandResult>(StatusCodes.Status422UnprocessableEntity)
            .ProducesErpAuthenticatedProblems().ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody().AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpServiceUnavailable();

        endpoints.MapPost(ManualBankPaymentsPath + "/{paymentId:guid}/approval", async (
                Guid paymentId, HttpContext httpContext, IManualBankPaymentService service,
                ICompanyContext companyContext, ICurrentUser currentUser, ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var key))
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
                if (await ReadBodyAsync<ApproveManualBankPaymentRequest>(httpContext, cancellationToken)
                    is not { } request || request.PaymentId != paymentId)
                    return httpContext.Request.HasJsonContentType()
                        ? Results.Problem(statusCode: StatusCodes.Status400BadRequest)
                        : Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
                var result = await service.ApproveAsync(Context(companyContext, currentUser, correlation),
                    new CommandMetadata(key), request, cancellationToken);
                return Command(httpContext, result, result.Outcome, result.IsReplay, result.OriginalCorrelationId);
            })
            .WithName("ApproveFinanceManualBankPaymentV1")
            .WithTags("Finance").RequireAuthorization().RequireCompanyContext()
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .Accepts<ApproveManualBankPaymentRequest>("application/json")
            .Produces<ManualBankPaymentCommandResult>(StatusCodes.Status200OK)
            .Produces<ManualBankPaymentCommandResult>(StatusCodes.Status409Conflict)
            .Produces<ManualBankPaymentCommandResult>(StatusCodes.Status422UnprocessableEntity)
            .ProducesErpAuthenticatedProblems().ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody().AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpServiceUnavailable();

        endpoints.MapPost(ManualBankPaymentsPath + "/{paymentId:guid}/record", async (
                Guid paymentId, HttpContext httpContext, IManualBankPaymentService service,
                ICompanyContext companyContext, ICurrentUser currentUser, ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var key))
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
                if (await ReadBodyAsync<RecordManualBankPaymentRequest>(httpContext, cancellationToken)
                    is not { } request || request.PaymentId != paymentId)
                    return httpContext.Request.HasJsonContentType()
                        ? Results.Problem(statusCode: StatusCodes.Status400BadRequest)
                        : Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
                var result = await service.RecordAsync(Context(companyContext, currentUser, correlation),
                    new CommandMetadata(key), request, cancellationToken);
                return Command(httpContext, result, result.Outcome, result.IsReplay, result.OriginalCorrelationId);
            })
            .WithName("RecordFinanceManualBankPaymentV1")
            .WithTags("Finance").RequireAuthorization().RequireCompanyContext()
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .Accepts<RecordManualBankPaymentRequest>("application/json")
            .Produces<ManualBankPaymentCommandResult>(StatusCodes.Status200OK)
            .Produces<ManualBankPaymentCommandResult>(StatusCodes.Status409Conflict)
            .Produces<ManualBankPaymentCommandResult>(StatusCodes.Status422UnprocessableEntity)
            .ProducesErpAuthenticatedProblems().ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody().AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpServiceUnavailable();

        endpoints.MapGet(ManualBankPaymentsPath, async (
                [FromQuery] string? cursor, HttpContext httpContext, IManualBankPaymentService service,
                ICompanyContext companyContext, ICurrentUser currentUser, ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                var result = await service.ListAuthorizedAsync(Context(companyContext, currentUser, correlation),
                    cursor, cancellationToken);
                return result.Outcome == ReadOutcome.NotFound
                    ? Results.Problem(statusCode: StatusCodes.Status400BadRequest)
                    : Read(result);
            })
            .WithName("GetFinanceManualBankPaymentsV1")
            .WithTags("Finance").RequireAuthorization().RequireCompanyContext()
            .Produces<ManualBankPaymentPage>().ProducesErpAuthenticatedProblems().ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody().AllowsCodeLessErpBadRequestBody()
            .ProducesErpServiceUnavailable();

        endpoints.MapGet(ManualBankPaymentsPath + "/{paymentId:guid}", async (
                Guid paymentId, HttpContext httpContext, IManualBankPaymentService service,
                ICompanyContext companyContext, ICurrentUser currentUser, ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                return Read(await service.DetailAuthorizedAsync(
                    Context(companyContext, currentUser, correlation), paymentId, cancellationToken));
            })
            .WithName("GetFinanceManualBankPaymentV1")
            .WithTags("Finance").RequireAuthorization().RequireCompanyContext()
            .Produces<ManualBankPaymentView>().ProducesErpAuthenticatedProblems().ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable();
    }

    /// <summary>
    /// FIN-T04b: the manual-receipt draft over HTTP, faithful to the #258 bodies. The company is only
    /// the server context (<c>X-Company-Id</c> + access); the actor and correlation are the host's.
    /// <list type="bullet">
    /// <item><b>Save</b> returns the #258 <c>CommandResult</c> for the outcomes a client acts on: Succeeded
    /// 200, ValidationFailed 422 (its issues), VersionConflict / IdempotencyConflict 409. Denied and
    /// Unavailable are the platform problems 403 <c>forbidden</c> and 503 <c>service_unavailable</c>. A replay
    /// (ADR-011) carries <c>Idempotency-Replayed</c> and <c>Idempotency-Original-Correlation-Id</c>: a stored
    /// success, or a recorded terminal refusal (§7, FIN-UI-1: draft not found 422, stale version 409).</item>
    /// <item><b>Reads</b> return the resource on 200; NotFound 404, Denied 403 and Unavailable 503 are
    /// platform problems. A malformed or foreign list cursor is 400.</item>
    /// </list>
    /// Permission is not a route policy: the services ask the Finance permission port afresh at every
    /// fence (#276), so a revocation in flight is seen. Record exists since FIN-T05a and decides its period from occurredOn (K1, #66 5748258299); Reverse has no route (FIN-T06).
    /// </summary>
    private static void MapManualReceiptEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(DraftsPath, async (
                HttpContext httpContext,
                IManualReceiptService service,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var key))
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
                }

                if (!httpContext.Request.HasJsonContentType())
                {
                    return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
                }

                DraftSaveRequest? request;
                try
                {
                    request = await JsonSerializer.DeserializeAsync<DraftSaveRequest>(httpContext.Request.Body, ContractJson,
                        cancellationToken);
                }
                catch (JsonException)
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
                }

                if (request is null)
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
                }

                var result = await service.DraftSaveAsync(Context(companyContext, currentUser, correlation),
                    new CommandMetadata(key), request, cancellationToken);
                return Command(httpContext, result, result.Outcome, result.IsReplay, result.OriginalCorrelationId);
            })
            .WithName("SaveFinanceManualReceiptDraftV1")
            .WithTags("Finance")
            .RequireAuthorization()
            .RequireCompanyContext()
            // The replayable results: the stored success (200) and the recorded terminal refusals (§7).
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .Accepts<DraftSaveRequest>("application/json")
            .Produces<CommandResult>(StatusCodes.Status200OK)
            .Produces<CommandResult>(StatusCodes.Status422UnprocessableEntity)
            .Produces<CommandResult>(StatusCodes.Status409Conflict)
            .ProducesErpAuthenticatedProblems()
            .ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpServiceUnavailable();

        // FIN-T05a: the Record of a realized manual bank receipt (card #66 5743678389): one money event, exactly once,
        // under the narrow Record name (Ö4). The route's source id and the body's draftId name the same source. The
        // period follows occurredOn (K1, #66 5748258299); a closed period is a 422 finance_period_not_open and a rule
        // that could not decide is a 422 finance_period_rule_unavailable — different answers, both before any write.
        endpoints.MapPost(ManualReceiptsPath + "/{sourceId:guid}/record", async (
                Guid sourceId,
                HttpContext httpContext,
                IManualReceiptService service,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var key))
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
                }

                if (!httpContext.Request.HasJsonContentType())
                {
                    return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
                }

                var request = await ReadBodyAsync<RecordRequest>(httpContext, cancellationToken);
                if (request is null || request.DraftId != sourceId)
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
                }

                var result = await service.RecordAsync(Context(companyContext, currentUser, correlation),
                    new CommandMetadata(key), request, cancellationToken);
                return Command(httpContext, result, result.Outcome, result.IsReplay, result.OriginalCorrelationId);
            })
            .WithName("RecordFinanceManualReceiptV1")
            .WithTags("Finance")
            .RequireAuthorization()
            .RequireCompanyContext()
            // The replayable results: the stored success (200) and the recorded terminal refusals (§7: a missing source
            // is a 422, a stale version a 409).
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .Accepts<RecordRequest>("application/json")
            .Produces<CommandResult>(StatusCodes.Status200OK)
            .Produces<CommandResult>(StatusCodes.Status422UnprocessableEntity)
            .Produces<CommandResult>(StatusCodes.Status409Conflict)
            .ProducesErpAuthenticatedProblems()
            .ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpServiceUnavailable();

        endpoints.MapPost(ManualReceiptsPath + "/{sourceId:guid}/reversal-proposals", async (
                Guid sourceId,
                HttpContext httpContext,
                IReversalProposalService service,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var key))
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
                }
                if (await ReadBodyAsync<ProposeReversalRequest>(httpContext, cancellationToken) is not { } request)
                {
                    return httpContext.Request.HasJsonContentType()
                        ? Results.Problem(statusCode: StatusCodes.Status400BadRequest)
                        : Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
                }
                if (request.SourceId != sourceId)
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
                }
                var result = await service.ProposeAsync(Context(companyContext, currentUser, correlation),
                    new CommandMetadata(key), request, cancellationToken);
                return Command(httpContext, result, result.Outcome, result.IsReplay, result.OriginalCorrelationId);
            })
            .WithName("ProposeFinanceManualReceiptReversalV1")
            .WithTags("Finance")
            .RequireAuthorization()
            .RequireCompanyContext()
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .Accepts<ProposeReversalRequest>("application/json")
            .Produces<ReversalProposalCommandResult>(StatusCodes.Status200OK)
            .Produces<ReversalProposalCommandResult>(StatusCodes.Status422UnprocessableEntity)
            .Produces<ReversalProposalCommandResult>(StatusCodes.Status409Conflict)
            .ProducesErpAuthenticatedProblems()
            .ProducesErpBadRequest()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpServiceUnavailable();

        endpoints.MapPost(ReversalProposalsPath + "/{proposalId:guid}/approval", async (
                Guid proposalId,
                HttpContext httpContext,
                IReversalProposalService service,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var key))
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
                }
                if (await ReadBodyAsync<ApproveReversalRequest>(httpContext, cancellationToken) is not { } request)
                {
                    return httpContext.Request.HasJsonContentType()
                        ? Results.Problem(statusCode: StatusCodes.Status400BadRequest)
                        : Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
                }
                if (request.ProposalId != proposalId)
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
                }
                var result = await service.ApproveAsync(Context(companyContext, currentUser, correlation),
                    new CommandMetadata(key), request, cancellationToken);
                return Command(httpContext, result, result.Outcome, result.IsReplay, result.OriginalCorrelationId);
            })
            .WithName("ApproveFinanceManualReceiptReversalV1")
            .WithTags("Finance")
            .RequireAuthorization()
            .RequireCompanyContext()
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .Accepts<ApproveReversalRequest>("application/json")
            .Produces<ReversalProposalCommandResult>(StatusCodes.Status200OK)
            .Produces<ReversalProposalCommandResult>(StatusCodes.Status422UnprocessableEntity)
            .Produces<ReversalProposalCommandResult>(StatusCodes.Status409Conflict)
            .ProducesErpAuthenticatedProblems()
            .ProducesErpBadRequest()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpServiceUnavailable();

        endpoints.MapPost(ReversalProposalsPath + "/{proposalId:guid}/rejection", async (
                Guid proposalId,
                HttpContext httpContext,
                IReversalProposalService service,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var key))
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
                }
                if (await ReadBodyAsync<RejectReversalRequest>(httpContext, cancellationToken) is not { } request)
                {
                    return httpContext.Request.HasJsonContentType()
                        ? Results.Problem(statusCode: StatusCodes.Status400BadRequest)
                        : Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
                }
                if (request.ProposalId != proposalId)
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
                }
                var result = await service.RejectAsync(Context(companyContext, currentUser, correlation),
                    new CommandMetadata(key), request, cancellationToken);
                return Command(httpContext, result, result.Outcome, result.IsReplay, result.OriginalCorrelationId);
            })
            .WithName("RejectFinanceManualReceiptReversalV1")
            .WithTags("Finance")
            .RequireAuthorization()
            .RequireCompanyContext()
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .Accepts<RejectReversalRequest>("application/json")
            .Produces<ReversalProposalCommandResult>(StatusCodes.Status200OK)
            .Produces<ReversalProposalCommandResult>(StatusCodes.Status422UnprocessableEntity)
            .Produces<ReversalProposalCommandResult>(StatusCodes.Status409Conflict)
            .ProducesErpAuthenticatedProblems()
            .ProducesErpBadRequest()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpServiceUnavailable();

        endpoints.MapGet(ReversalProposalsPath + "/pending", async (
                HttpContext httpContext,
                IReversalProposalService service,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                return Read(await service.PendingAuthorizedAsync(
                    Context(companyContext, currentUser, correlation), cancellationToken));
            })
            .WithName("GetPendingFinanceManualReceiptReversalsV1")
            .WithTags("Finance")
            .RequireAuthorization()
            .RequireCompanyContext()
            .Produces<ReversalProposalPage>()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpServiceUnavailable();

        endpoints.MapGet(ReversalProposalsPath + "/{proposalId:guid}", async (
                Guid proposalId,
                HttpContext httpContext,
                IReversalProposalService service,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                return Read(await service.DetailAuthorizedAsync(
                    Context(companyContext, currentUser, correlation), proposalId, cancellationToken));
            })
            .WithName("GetFinanceManualReceiptReversalV1")
            .WithTags("Finance")
            .RequireAuthorization()
            .RequireCompanyContext()
            .Produces<ReversalProposalView>()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable();

        endpoints.MapGet(ManualReceiptsPath, async (
                [FromQuery] string? cursor,
                HttpContext httpContext,
                IManualReceiptService service,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                var result = await service.ListAuthorizedAsync(Context(companyContext, currentUser, correlation),
                    new SourceListRequest(cursor), cancellationToken);
                // The list has no "not found": the service answers NotFound only for a malformed or foreign cursor.
                return result.Outcome == ReadOutcome.NotFound
                    ? Results.Problem(statusCode: StatusCodes.Status400BadRequest)
                    : Read(result);
            })
            .WithName("GetFinanceManualReceiptsV1")
            .WithTags("Finance")
            .RequireAuthorization()
            .RequireCompanyContext()
            .Produces<SourcePage>()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpServiceUnavailable();

        endpoints.MapGet(ManualReceiptsPath + "/{sourceId:guid}", async (
                Guid sourceId,
                HttpContext httpContext,
                IManualReceiptService service,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                return Read(await service.DetailAuthorizedAsync(Context(companyContext, currentUser, correlation),
                    new SourceDetailRequest(sourceId), cancellationToken));
            })
            .WithName("GetFinanceManualReceiptV1")
            .WithTags("Finance")
            .RequireAuthorization()
            .RequireCompanyContext()
            .Produces<SourceDetail>()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable();

        endpoints.MapGet(BankAccountsPath, async (
                HttpContext httpContext,
                IFinanceBankAccountReader banks,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                return Read(await banks.ListAuthorizedAsync(Context(companyContext, currentUser, correlation),
                    cancellationToken));
            })
            .WithName("GetFinanceBankAccountsV1")
            .WithTags("Finance")
            .RequireAuthorization()
            .RequireCompanyContext()
            .Produces<BankAccountPage>()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpServiceUnavailable();
    }

    /// <summary>
    /// FIN-UI-1: the bank-account master (FS0) over HTTP, for F-04. The company is only the server
    /// context; the proposer and approver are the authenticated actors.
    /// <list type="bullet">
    /// <item><b>Propose / approve</b> answer the <c>BankAccountCommandResult</c> with the save's mapping:
    /// Succeeded 200, ValidationFailed 422, VersionConflict / IdempotencyConflict 409, Denied 403 and
    /// Unavailable 503 as the platform problems, the ADR-011 replay headers on a replay. Ö4 holds in the
    /// service and the database: the proposer's own approval is Denied (403).</item>
    /// <item><b>Master list</b> (read authority): the company's accounts, proposals included, each with
    /// whether the caller proposed it.</item>
    /// <item><b>Currency candidates</b> (propose authority): Settings' candidates as given; empty is
    /// "no candidates", never a default.</item>
    /// </list>
    /// </summary>
    private static void MapBankAccountEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(BankAccountsPath, async (
                HttpContext httpContext,
                IFinanceBankAccountMaster master,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var key))
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
                }

                if (await ReadBodyAsync<BankAccountProposeRequest>(httpContext, cancellationToken) is not { } request)
                {
                    return httpContext.Request.HasJsonContentType()
                        ? Results.Problem(statusCode: StatusCodes.Status400BadRequest)
                        : Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
                }

                var result = await master.ProposeAsync(Context(companyContext, currentUser, correlation),
                    new CommandMetadata(key), request, cancellationToken);
                return Command(httpContext, result, result.Outcome, result.IsReplay, result.OriginalCorrelationId);
            })
            .WithName("ProposeFinanceBankAccountV1")
            .WithTags("Finance")
            .RequireAuthorization()
            .RequireCompanyContext()
            .WithIdempotencyContract(StatusCodes.Status200OK)
            .Accepts<BankAccountProposeRequest>("application/json")
            .Produces<BankAccountCommandResult>(StatusCodes.Status200OK)
            .Produces<BankAccountCommandResult>(StatusCodes.Status422UnprocessableEntity)
            .Produces<BankAccountCommandResult>(StatusCodes.Status409Conflict)
            .ProducesErpAuthenticatedProblems()
            .ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpServiceUnavailable();

        endpoints.MapPost(BankAccountsPath + "/{accountId:guid}/approval", async (
                Guid accountId,
                HttpContext httpContext,
                IFinanceBankAccountMaster master,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var key))
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
                }

                if (await ReadBodyAsync<BankAccountApprovalBody>(httpContext, cancellationToken) is not { } body)
                {
                    return httpContext.Request.HasJsonContentType()
                        ? Results.Problem(statusCode: StatusCodes.Status400BadRequest)
                        : Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
                }

                var result = await master.ApproveAsync(Context(companyContext, currentUser, correlation),
                    new CommandMetadata(key), new BankAccountApproveRequest(accountId, body.ExpectedVersion), cancellationToken);
                return Command(httpContext, result, result.Outcome, result.IsReplay, result.OriginalCorrelationId);
            })
            .WithName("ApproveFinanceBankAccountV1")
            .WithTags("Finance")
            .RequireAuthorization()
            .RequireCompanyContext()
            // The replayable results: the stored approval (200) and the recorded terminal refusals (§7).
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .Accepts<BankAccountApprovalBody>("application/json")
            .Produces<BankAccountCommandResult>(StatusCodes.Status200OK)
            .Produces<BankAccountCommandResult>(StatusCodes.Status422UnprocessableEntity)
            .Produces<BankAccountCommandResult>(StatusCodes.Status409Conflict)
            .ProducesErpAuthenticatedProblems()
            .ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpServiceUnavailable();

        // D4 (#66 5749046401): the IBAN change's own Ö4 pair. Two routes rather than one PATCH, because
        // proposing and approving are two decisions by two people — the same reason the card itself has
        // two. They carry the EXISTING propose/approve permission names; nothing new is granted here.
        endpoints.MapPost(BankAccountsPath + "/{accountId:guid}/iban-change", async (
                Guid accountId,
                HttpContext httpContext,
                IFinanceBankAccountMaster master,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var key))
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
                }

                if (await ReadBodyAsync<BankAccountIbanChangeBody>(httpContext, cancellationToken) is not { } body)
                {
                    return httpContext.Request.HasJsonContentType()
                        ? Results.Problem(statusCode: StatusCodes.Status400BadRequest)
                        : Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
                }

                var result = await master.ProposeIbanChangeAsync(Context(companyContext, currentUser, correlation),
                    new CommandMetadata(key),
                    new BankAccountIbanChangeProposeRequest(accountId, body.ExpectedVersion, body.Iban, body.Reason),
                    cancellationToken);
                return Command(httpContext, result, result.Outcome, result.IsReplay, result.OriginalCorrelationId);
            })
            .WithName("ProposeFinanceBankAccountIbanChangeV1")
            .WithTags("Finance")
            .RequireAuthorization()
            .RequireCompanyContext()
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .Accepts<BankAccountIbanChangeBody>("application/json")
            .Produces<BankAccountCommandResult>(StatusCodes.Status200OK)
            .Produces<BankAccountCommandResult>(StatusCodes.Status422UnprocessableEntity)
            .Produces<BankAccountCommandResult>(StatusCodes.Status409Conflict)
            .ProducesErpAuthenticatedProblems()
            .ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpServiceUnavailable();

        endpoints.MapPost(BankAccountsPath + "/{accountId:guid}/iban-change/approval", async (
                Guid accountId,
                HttpContext httpContext,
                IFinanceBankAccountMaster master,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var key))
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
                }

                if (await ReadBodyAsync<BankAccountApprovalBody>(httpContext, cancellationToken) is not { } body)
                {
                    return httpContext.Request.HasJsonContentType()
                        ? Results.Problem(statusCode: StatusCodes.Status400BadRequest)
                        : Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
                }

                var result = await master.ApproveIbanChangeAsync(Context(companyContext, currentUser, correlation),
                    new CommandMetadata(key), new BankAccountIbanChangeApproveRequest(accountId, body.ExpectedVersion),
                    cancellationToken);
                return Command(httpContext, result, result.Outcome, result.IsReplay, result.OriginalCorrelationId);
            })
            .WithName("ApproveFinanceBankAccountIbanChangeV1")
            .WithTags("Finance")
            .RequireAuthorization()
            .RequireCompanyContext()
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .Accepts<BankAccountApprovalBody>("application/json")
            .Produces<BankAccountCommandResult>(StatusCodes.Status200OK)
            .Produces<BankAccountCommandResult>(StatusCodes.Status422UnprocessableEntity)
            .Produces<BankAccountCommandResult>(StatusCodes.Status409Conflict)
            .ProducesErpAuthenticatedProblems()
            .ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpServiceUnavailable();

        endpoints.MapGet(BankAccountMasterPath, async (
                HttpContext httpContext,
                IFinanceBankAccountMaster master,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                return Read(await master.ListMasterAsync(Context(companyContext, currentUser, correlation), cancellationToken));
            })
            .WithName("GetFinanceBankAccountMasterV1")
            .WithTags("Finance")
            .RequireAuthorization()
            .RequireCompanyContext()
            .Produces<BankAccountMasterPage>()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpServiceUnavailable();

        endpoints.MapGet(BankAccountCurrencyCandidatesPath, async (
                HttpContext httpContext,
                IFinanceBankAccountMaster master,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                return Read(await master.CurrencyCandidatesAsync(Context(companyContext, currentUser, correlation),
                    cancellationToken));
            })
            .WithName("GetFinanceBankAccountCurrencyCandidatesV1")
            .WithTags("Finance")
            .RequireAuthorization()
            .RequireCompanyContext()
            .Produces<BankAccountCurrencyCandidates>()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpServiceUnavailable();
    }

    /// <summary>
    /// A command's answer: the outcomes a client acts on carry the result (Succeeded 200,
    /// ValidationFailed 422, VersionConflict / IdempotencyConflict 409); Denied and Unavailable are the
    /// platform's problems (403 <c>forbidden</c>, 503 <c>service_unavailable</c>), as on every route. A
    /// replay (ADR-011 §4) carries <c>Idempotency-Replayed</c> and <c>Idempotency-Original-Correlation-Id</c>.
    /// </summary>
    private static IResult Command<T>(HttpContext httpContext, T result, CommandOutcome outcome, bool isReplay,
        string originalCorrelationId)
    {
        if (isReplay)
        {
            httpContext.Response.Headers["Idempotency-Replayed"] = "true";
            httpContext.Response.Headers["Idempotency-Original-Correlation-Id"] = originalCorrelationId;
        }

        return outcome switch
        {
            CommandOutcome.Succeeded => Results.Json(result, ContractJson, statusCode: StatusCodes.Status200OK),
            CommandOutcome.ValidationFailed =>
                Results.Json(result, ContractJson, statusCode: StatusCodes.Status422UnprocessableEntity),
            CommandOutcome.VersionConflict or CommandOutcome.IdempotencyConflict =>
                Results.Json(result, ContractJson, statusCode: StatusCodes.Status409Conflict),
            CommandOutcome.Denied => Results.Problem(statusCode: StatusCodes.Status403Forbidden),
            CommandOutcome.Unavailable => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => throw new InvalidOperationException("Finance command returned an undefined outcome."),
        };
    }

    private static IResult OfficialInvoiceApply(HttpContext httpContext,
        FinanceOfficialInvoiceImportApplyResultV1 result)
    {
        if (result.Admission is { IsReplay: true } admission)
        {
            httpContext.Response.Headers["Idempotency-Replayed"] = "true";
            httpContext.Response.Headers["Idempotency-Original-Correlation-Id"] = admission.OriginalCorrelationId;
        }

        if (!result.Preview.IsValid) return Results.Json(result, ContractJson,
            statusCode: StatusCodes.Status422UnprocessableEntity);
        if (result.ApprovedSource is { Outcome: not ReadOutcome.Found } source)
            return OfficialInvoiceReadFailure(source.Outcome);
        if (result.Admission is { Outcome: not CommandOutcome.Succeeded } command)
            return command.Outcome switch
            {
                CommandOutcome.ValidationFailed => Results.Json(result, ContractJson,
                    statusCode: StatusCodes.Status422UnprocessableEntity),
                CommandOutcome.VersionConflict or CommandOutcome.IdempotencyConflict => Results.Json(result, ContractJson,
                    statusCode: StatusCodes.Status409Conflict),
                CommandOutcome.Denied => Results.Problem(statusCode: StatusCodes.Status403Forbidden),
                CommandOutcome.Unavailable => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable),
                _ => throw new InvalidOperationException("Finance official invoice admission returned an invalid outcome."),
            };
        if (result.FreshRead is { Outcome: not ReadOutcome.Found } read)
            return OfficialInvoiceReadFailure(read.Outcome);
        if (!result.IsSucceeded)
            throw new InvalidOperationException("Finance official invoice import returned an incomplete success graph.");
        return Results.Json(result, ContractJson);
    }

    private static IResult OfficialInvoiceReadFailure(ReadOutcome outcome) => outcome switch
    {
        ReadOutcome.NotFound => Results.Problem(statusCode: StatusCodes.Status404NotFound),
        ReadOutcome.Denied => Results.Problem(statusCode: StatusCodes.Status403Forbidden),
        ReadOutcome.Unavailable => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable),
        _ => throw new InvalidOperationException("Finance official invoice read returned an invalid outcome."),
    };

    /// <summary>The strict contract body (FinanceJson: unknown members and missing required ones refused); null when absent, not JSON, or malformed.</summary>
    private static async Task<T?> ReadBodyAsync<T>(HttpContext httpContext, CancellationToken cancellationToken) where T : class
    {
        if (!httpContext.Request.HasJsonContentType())
        {
            return null;
        }

        try
        {
            return await JsonSerializer.DeserializeAsync<T>(httpContext.Request.Body, ContractJson, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The request's server context: company, actor and correlation are never request data.</summary>
    private static FinanceContext Context(ICompanyContext companyContext, ICurrentUser currentUser,
        ICorrelationContext correlation) =>
        new FinanceContext(companyContext.CompanyId, currentUser.UserId ?? string.Empty, correlation.CorrelationId,
            correlation.CausationId);

    private static IResult Read<T>(ReadResult<T> result) where T : class => result.Outcome switch
    {
        ReadOutcome.Found when result.Resource is not null => Results.Json(result.Resource, ContractJson),
        ReadOutcome.NotFound => Results.Problem(statusCode: StatusCodes.Status404NotFound),
        ReadOutcome.Denied => Results.Problem(statusCode: StatusCodes.Status403Forbidden),
        ReadOutcome.Unavailable => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable),
        _ => throw new InvalidOperationException("Finance read returned an invalid outcome/resource combination."),
    };

    private static void SetPrivateNoStore(HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = "private, no-store";
        httpContext.Response.Headers.Pragma = "no-cache";
    }

    /// <summary>Exactly one Idempotency-Key: non-blank, canonical (no surrounding whitespace), at most 128 characters.</summary>
    private static bool TryReadIdempotencyKey(IHeaderDictionary headers, out string idempotencyKey)
    {
        idempotencyKey = string.Empty;
        if (!headers.TryGetValue(IdempotencyHeaderName, out var values) || values.Count != 1 || values[0] is not { } value
            || string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Length > MaximumIdempotencyKeyLength || value.Contains(',', StringComparison.Ordinal))
        {
            return false;
        }

        idempotencyKey = value;
        return true;
    }
}

public interface IFinanceDraftReadBinding
{
    string? MoneyOperation { get; }
}

public sealed class ManualReceiptDraftReadBinding : IFinanceDraftReadBinding
{
    public string? MoneyOperation => FinanceMoneyOperations.ManualBankReceiptV1;
}

public interface IFinanceDraftContextEndpointService
{
    Task<DraftReadContextResult> GetAsync(DraftReadContextRequest request, string correlationId,
        CancellationToken cancellationToken = default);
}

public sealed class FinanceDraftContextEndpointService(DraftReadContextReader reader,
    IFinanceDraftReadBinding binding) : IFinanceDraftContextEndpointService
{
    public Task<DraftReadContextResult> GetAsync(DraftReadContextRequest request, string correlationId,
        CancellationToken cancellationToken = default) =>
        reader.GetAsync(request, binding.MoneyOperation, correlationId, cancellationToken);
}

public sealed class CounterpartyFinancePartyAccountPort(ICompanyContext companyContext,
    ICurrentUser currentUser, ICounterpartyReadService counterparties) : IFinancePartyAccountPort
{
    public async Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext context,
        Guid partyId, Guid companyAccountId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrent(context))
        {
            return new(ReadOutcome.Denied, null);
        }

        CounterpartyDetailView? party;
        try
        {
            party = await counterparties.GetAuthorizedPartyAsync(partyId, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return new(ReadOutcome.Denied, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (!IsCurrent(context)) return new(ReadOutcome.Denied, null);
            throw;
        }

        // The owner answered for the context captured before the await. If company or actor
        // changed meanwhile, neither its data nor its NotFound may be released.
        if (!IsCurrent(context))
        {
            return new(ReadOutcome.Denied, null);
        }

        if (party is null || party.Id != partyId)
        {
            return new(ReadOutcome.NotFound, null);
        }

        var accounts = party.CompanyAccounts.Where(account => account.Id == companyAccountId
            && account.CompanyId == context.CompanyId).ToArray();
        if (accounts.Length != 1)
        {
            return new(ReadOutcome.NotFound, null);
        }

        var account = accounts[0];
        return new(ReadOutcome.Found, new(
            party.Id,
            account.Id,
            account.CompanyId,
            account.IsActive,
            new(party.Id, account.Id, party.Code, party.LegalName, party.TradeName,
                party.Version, account.Version),
            party.IsActive));
    }

    private bool IsCurrent(FinanceContext context) =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && currentUser.UserId == context.ActorId && companyContext.IsResolved
        && companyContext.CompanyId != Guid.Empty && companyContext.CompanyId == context.CompanyId
        && currentUser.CanAccessCompany(context.CompanyId);
}
