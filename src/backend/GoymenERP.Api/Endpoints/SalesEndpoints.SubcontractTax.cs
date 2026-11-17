using System.Text.Json;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

public static partial class SalesEndpoints
{
    private static void MapSubcontractTax(RouteGroupBuilder drafts)
    {
        drafts.MapGet("/{id:guid}/subcontract-tax-options", async (Guid id, HttpContext context,
            [FromServices] ISalesDraftService service, ICorrelationContext correlation, CancellationToken token) =>
        {
            SetPrivateNoStore(context.Response);
            if (context.Request.Query.Count != 0) return DraftProblem(400, "validation_failed", correlation);
            if (service is not ISalesSubcontractTaxServiceV1 owner) return DraftProblem(503, "service_unavailable", correlation);
            try { return Results.Json(await owner.GetSubcontractTaxOptionsAsync(id, token), SalesDraftJson.Options); }
            catch (SalesDraftServiceException exception) { return DraftProblem(exception, correlation); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception) { return DraftProblem(503, "service_unavailable", correlation); }
        }).WithName("GetSalesSubcontractTaxOptionsV1").Produces<SalesSubcontractTaxOptionsV1>().ProducesErpBadRequest("validation_failed")
            .ProducesErpCodeLessNotFound().ProducesErpUnprocessableEntity("validation_failed").ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
        drafts.MapPost("/{id:guid}/subcontract-tax-revision", async (Guid id, HttpContext context,
            [FromServices] ISalesDraftService service, ICorrelationContext correlation, CancellationToken token) =>
        {
            SetPrivateNoStore(context.Response);
            if (context.Request.Query.Count != 0) return DraftProblem(400, "validation_failed", correlation);
            if (!TryReadIdempotencyKey(context.Request.Headers, out var key) || !correlation.IsResolved)
                return DraftProblem(400, SalesDraftProblemCodesV1.CommandMetadataInvalid, correlation);
            if (!context.Request.HasJsonContentType()) return Results.StatusCode(415);
            SalesSubcontractTaxRevisionV1 body;
            try
            {
                using var reader = new StreamReader(context.Request.Body);
                var json = await reader.ReadToEndAsync(token); using var document = JsonDocument.Parse(json);
                RequireUniqueSubcontractTaxFields(document.RootElement);
                body = JsonSerializer.Deserialize<SalesSubcontractTaxRevisionV1>(json, SalesDraftJson.Options) ?? throw new JsonException();
            }
            catch (JsonException) { return DraftProblem(400, "validation_failed", correlation); }
            if (service is not ISalesSubcontractTaxServiceV1 owner) return DraftProblem(503, "service_unavailable", correlation);
            try
            {
                var response = await owner.ReviseSubcontractTaxAsync(id, body, new(key, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow), token);
                if (response.IsReplay) ApplyReplayHeaders(context, response.OriginalCorrelationId);
                return Results.Json(response, SalesDraftJson.Options, statusCode: SalesDraftApiV1.StateCommandStatus);
            }
            catch (SalesDraftServiceException exception)
            {
                if (exception.IsReplay) ApplyReplayHeaders(context, exception.OriginalCorrelationId ?? throw new InvalidOperationException());
                return DraftProblem(exception, correlation);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception) { return DraftProblem(503, "service_unavailable", correlation); }
        }).WithName("ReviseSalesSubcontractTaxV1").Accepts<SalesSubcontractTaxRevisionV1>("application/json")
            .WithIdempotencyContract(SalesDraftApiV1.StateCommandStatus, 409).Produces<SalesOrderDraftResponse>(SalesDraftApiV1.StateCommandStatus)
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.CommandMetadataInvalid).AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType().ProducesErpCodeLessNotFound().ProducesErpUnprocessableEntity("validation_failed")
            .ProducesErpConflict(SalesDraftProblemCodesV1.VersionConflict, SalesDraftProblemCodesV1.StateConflict,
                SalesDraftProblemCodesV1.SameKeyPayloadMismatch, SalesDraftProblemCodesV1.ConcurrentChange)
            .ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
    }
    private static void RequireUniqueSubcontractTaxFields(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new JsonException();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in value.EnumerateObject())
        {
            if (!names.Add(item.Name)) throw new JsonException();
            if (item.Value.ValueKind == JsonValueKind.Object) RequireUniqueSubcontractTaxFields(item.Value);
        }
    }
}
