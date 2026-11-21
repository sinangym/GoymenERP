using System.Text;
using System.Text.Json;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// S-11 (SAT-D28, NS-13, SAT-D346): the sales contract routes over <see cref="ISalesContractService"/>. A contract records the
/// number, the version, the state, the validity window, the installment payment plan, the line/total discount rules and the clauses
/// (the PDF vault link is out of scope). A discount above the configured limit waits for an authorized approval; with no limit
/// configured every discount waits (the screen says "sınır tanımlı değil"). The draft routes' wire rules apply unchanged: exactly one
/// canonical <c>Idempotency-Key</c> (else 400 <c>command_metadata_invalid</c>), JSON only (415), the strict body (400
/// <c>validation_failed</c>), 201 + <c>Location</c>, the replay headers on the same key again (ADR-011 §4), another company's
/// contract 404, and every 401/403/404/503 code-less. SAT-D362: <c>validThrough</c> is nullable (null = open-ended) and is still
/// sent, as null, by the strict body. SAT-D361: every contract answer carries <c>amountDisclosure</c>; without the company-wide
/// sales price read the installment amounts and the planned total are null (an unregistered price read is the code-less 503).
/// </summary>
public static partial class SalesEndpoints
{
    public const string ContractsPath = SalesContractApiV1.ContractsPath;

    private static void MapContractEndpoints(IEndpointRouteBuilder endpoints)
    {
        var contracts = endpoints.MapGroup(ContractsPath)
            .WithTags("Sales")
            .RequireAuthorization()
            .RequireCompanyContext();

        // SAT-D28: record a contract under `sales.contracts.write`. The company is the server's; the party/account are read from
        // Counterparty, the currency's money profile from Settings, the discount limit from its owner (none = every discount waits).
        contracts.MapPost("/", async (
            HttpContext httpContext,
            [FromServices] ISalesContractService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var idempotencyKey) || !correlation.IsResolved)
            {
                return DraftProblem(StatusCodes.Status400BadRequest, SalesDraftProblemCodesV1.CommandMetadataInvalid, correlation);
            }

            if (!httpContext.Request.HasJsonContentType())
            {
                return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
            }

            CreateSalesContractRequest request;
            try
            {
                using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
                request = SalesContractJson.ReadCreate(await reader.ReadToEndAsync(cancellationToken));
            }
            catch (JsonException)
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                var result = await service.CreateContractAsync(request,
                    new SalesCommandMetadata(idempotencyKey, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow),
                    cancellationToken);
                if (result.IsReplay)
                {
                    ApplyReplayHeaders(httpContext, result.OriginalCorrelationId);
                }

                httpContext.Response.Headers.Location =
                    $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{httpContext.Request.PathBase}{ContractsPath}/{result.Contract.Id:D}";
                return Results.Json(result.Contract, SalesDraftJson.Options, statusCode: SalesContractApiV1.CreatedStatus);
            }
            catch (SalesDraftServiceException exception)
            {
                return RequestProblem(httpContext, exception, correlation);
            }
        })
            .WithName("CreateSalesContractV1")
            .WithIdempotencyContract(SalesContractApiV1.CreatedStatus)
            .Accepts<CreateSalesContractRequest>("application/json")
            .Produces<SalesContractView>(SalesContractApiV1.CreatedStatus)
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(SalesDraftProblemCodesV1.AccountState, SalesDraftProblemCodesV1.SameKeyPayloadMismatch,
                SalesDraftProblemCodesV1.ConcurrentChange)
            .ProducesErpUnprocessableEntity("validation_failed", SalesContractProblemCodesV1.ValidityInvalid,
                SalesContractProblemCodesV1.InstallmentsInvalid, SalesContractProblemCodesV1.DiscountInvalid,
                SalesContractProblemCodesV1.ClausesTooLong, SalesContractProblemCodesV1.MoneyProfileNotInEffect,
                SalesDraftProblemCodesV1.UnsupportedCurrency)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        // The company's contracts, newest number first (technical bound SalesContractApiV1.MaximumListed), under
        // `sales.contracts.read`. No query parameter is accepted.
        contracts.MapGet("/", async (
            HttpContext httpContext,
            [FromServices] ISalesContractService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (httpContext.Request.Query.Count > 0)
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                return Results.Json(await service.GetContractsAsync(cancellationToken), SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return RequestProblem(httpContext, exception, correlation);
            }
        })
            .WithName("GetSalesContractsV1")
            .Produces<SalesContractListResponse>()
            .ProducesErpBadRequest("validation_failed")
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        contracts.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext httpContext,
            [FromServices] ISalesContractService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            try
            {
                return Results.Json(await service.GetContractAsync(id, cancellationToken), SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return RequestProblem(httpContext, exception, correlation);
            }
        })
            .WithName("GetSalesContractV1")
            .Produces<SalesContractView>()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        // NS-13: the discount approval under `sales.contracts.discount.approve`, never by the contract's creator (recorded 409).
        MapContractDecision(contracts, "discount-approval", "ApproveSalesContractDiscountV1", withReason: false,
            (service, id, body, metadata, cancellationToken) => service.ApproveContractDiscountAsync(id, body, metadata, cancellationToken));

        // NS-13: the discount rejection under the same permission, with a required reason (422 when blank or too long). Rejected is terminal.
        MapContractDecision(contracts, "discount-rejection", "RejectSalesContractDiscountV1", withReason: true,
            (service, id, body, metadata, cancellationToken) => service.RejectContractDiscountAsync(id, body, metadata, cancellationToken));
    }

    private static void MapContractDecision(RouteGroupBuilder contracts, string segment, string name, bool withReason,
        Func<ISalesContractService, Guid, SalesDraftStateCommandRequest, SalesCommandMetadata, CancellationToken,
            Task<SalesContractCommandResult>> command)
    {
        var route = contracts.MapPost($"/{{id:guid}}/{segment}", async (
            Guid id,
            HttpContext httpContext,
            [FromServices] ISalesContractService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var idempotencyKey) || !correlation.IsResolved)
            {
                return DraftProblem(StatusCodes.Status400BadRequest, SalesDraftProblemCodesV1.CommandMetadataInvalid, correlation);
            }

            if (!httpContext.Request.HasJsonContentType())
            {
                return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
            }

            SalesDraftStateCommandRequest body;
            try
            {
                using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
                var json = await reader.ReadToEndAsync(cancellationToken);
                body = withReason ? SalesContractJson.ReadRejection(json) : SalesContractJson.ReadApproval(json);
            }
            catch (JsonException)
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                var result = await command(service, id, body,
                    new SalesCommandMetadata(idempotencyKey, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow),
                    cancellationToken);
                if (result.IsReplay)
                {
                    ApplyReplayHeaders(httpContext, result.OriginalCorrelationId);
                }

                return Results.Json(result.Contract, SalesDraftJson.Options, statusCode: SalesContractApiV1.CommandStatus);
            }
            catch (SalesDraftServiceException exception)
            {
                return RequestProblem(httpContext, exception, correlation);
            }
        })
            .WithName(name)
            .WithIdempotencyContract(SalesContractApiV1.CommandStatus, StatusCodes.Status409Conflict)
            .Accepts<SalesDraftStateCommandRequest>("application/json")
            .Produces<SalesContractView>(SalesContractApiV1.CommandStatus)
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(SalesDraftProblemCodesV1.VersionConflict, SalesDraftProblemCodesV1.StateConflict,
                SalesContractProblemCodesV1.SelfDecision, SalesDraftProblemCodesV1.SameKeyPayloadMismatch,
                SalesDraftProblemCodesV1.ConcurrentChange)
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable();

        if (withReason)
        {
            // The rejection reason is required (422 validation_failed) and at most SalesContract.MaximumReasonLength characters
            // (422 ReasonTooLong); neither refusal is recorded, so a corrected body under a new key is a fresh attempt.
            route.ProducesErpUnprocessableEntity("validation_failed", SalesContractProblemCodesV1.ReasonTooLong);
        }

        route.ProducesErpAuthenticatedProblems();
    }
}
