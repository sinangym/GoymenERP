using System.Text;
using System.Text.Json;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

public static partial class SalesEndpoints
{
    private static void MapSubcontractInvoiceLifecycleEndpoints(RouteGroupBuilder routes)
    {
        routes.MapGet("/by-order/{salesOrderId:guid}", async (Guid salesOrderId, int? pageSize, string? cursor, HttpContext context,
            [FromServices] ISalesSubcontractInvoiceListServiceV1 service, ICorrelationContext correlation, CancellationToken token) =>
        {
            SetPrivateNoStore(context.Response);
            if (context.Request.Query.Any(item => item.Key is not ("pageSize" or "cursor") || item.Value.Count != 1))
                return DraftProblem(400, "validation_failed", correlation);
            try
            {
                return Results.Json(await service.ListSubcontractInvoicesAsync(salesOrderId,
                    new(pageSize ?? SalesInvoiceApiV1.DefaultPageSize, cursor), token), SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception) { return DraftProblem(exception, correlation); }
        }).WithName("ListSalesSubcontractServiceInvoicesOfOrderV1")
            .Produces<SalesSubcontractInvoiceListV1>()
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.InvalidCursor).AllowsEmptyErpBadRequestBody()
            .ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable(SalesDraftProblemCodesV1.ReferenceProviderInvalid)
            .ProducesErpAuthenticatedProblems();

        foreach (var (action, name) in new[] { ("submit", "Submit"), ("approve", "Approve"), ("reject", "Reject"),
            ("cancel", "Cancel"), ("match", "Match"), ("finalize", "Finalize"), ("resend-finance", "ResendFinance") })
        {
            var endpoint = routes.MapPost("/{id:guid}/" + action, async (Guid id, HttpContext context,
                [FromServices] ISalesSubcontractInvoiceLifecycleServiceV1 service, ICorrelationContext correlation, CancellationToken token) =>
            {
                SetPrivateNoStore(context.Response);
                if (!TryReadIdempotencyKey(context.Request.Headers, out var key) || !correlation.IsResolved)
                    return DraftProblem(400, SalesDraftProblemCodesV1.CommandMetadataInvalid, correlation);
                if (!context.Request.HasJsonContentType()) return Results.StatusCode(415);
                try
                {
                    using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
                    var json = await reader.ReadToEndAsync(token);
                    var metadata = new SalesCommandMetadata(key, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow);
                    SalesSubcontractServiceInvoiceCommandResult result;
                    if (action == "match") result = await service.MatchSubcontractInvoiceAsync(id, SalesInvoiceStateJson.ReadMatch(json), metadata, token);
                    else
                    {
                        var request = action is "reject" or "cancel" ? SalesInvoiceStateJson.ReadWithReason(json) : SalesInvoiceStateJson.ReadVersionOnly(json);
                        result = action switch
                        {
                            "submit" => await service.SubmitSubcontractInvoiceAsync(id, request, metadata, token),
                            "approve" => await service.ApproveSubcontractInvoiceAsync(id, request, metadata, token),
                            "reject" => await service.RejectSubcontractInvoiceAsync(id, request, metadata, token),
                            "cancel" => await service.CancelSubcontractInvoiceAsync(id, request, metadata, token),
                            "finalize" => await service.FinalizeSubcontractInvoiceAsync(id, request, metadata, token),
                            _ => await service.ResendSubcontractInvoiceToFinanceAsync(id, request, metadata, token),
                        };
                    }
                    if (result.IsReplay) ApplyReplayHeaders(context, result.OriginalCorrelationId);
                    return Results.Json(result.Invoice, SalesDraftJson.Options);
                }
                catch (JsonException) { return DraftProblem(400, "validation_failed", correlation); }
                catch (SalesDraftServiceException exception) { return DraftProblem(exception, correlation); }
            })
                .WithName(name + "SalesSubcontractServiceInvoiceV1")
                .WithIdempotencyContract(200)
                .Produces<SalesSubcontractServiceInvoiceDetailView>()
                .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.CommandMetadataInvalid)
                .AllowsEmptyErpBadRequestBody().ProducesErpUnsupportedMediaType()
                .ProducesErpConflict(SalesDraftProblemCodesV1.VersionConflict, SalesDraftProblemCodesV1.StateConflict,
                    SalesDraftProblemCodesV1.SameKeyPayloadMismatch, SalesDraftProblemCodesV1.ConcurrentChange,
                    SalesInvoiceProblemCodesV1.SelfApproval, SalesInvoiceProblemCodesV1.OfficialDocumentAlreadyMatched,
                    SalesInvoiceProblemCodesV1.OfficialDocumentNotFound, SalesInvoiceProblemCodesV1.InvoiceNotReady,
                    SalesInvoiceProblemCodesV1.OfficialMatchMismatch, SalesInvoiceProblemCodesV1.DifferenceApprovalRequired,
                    SalesInvoiceProblemCodesV1.MatchRequired, SalesInvoiceProblemCodesV1.MatchDifferenceOpen,
                    SalesInvoiceProblemCodesV1.MatchSourceProofRequired)
                .ProducesErpCodeLessNotFound()
                .ProducesErpServiceUnavailable(SalesInvoiceProblemCodesV1.OfficialInvoiceProviderUnavailable,
                    SalesDraftProblemCodesV1.ReferenceProviderInvalid)
                .ProducesErpAuthenticatedProblems();
            if (action == "match") endpoint.Accepts<MatchSalesInvoiceRequest>("application/json");
            else endpoint.Accepts<SalesDraftStateCommandRequest>("application/json");
        }
    }
}
