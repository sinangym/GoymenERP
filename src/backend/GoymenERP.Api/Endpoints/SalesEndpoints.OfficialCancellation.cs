using System.Text;
using System.Text.Json;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

public static partial class SalesEndpoints
{
    private static void MapOfficialInvoiceCancellationEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/sales").WithTags("Sales").RequireAuthorization().RequireCompanyContext();
        group.MapGet("/official-cancellations/candidates", async (string kind, string? cursor, HttpContext context,
            [FromServices] ISalesOfficialInvoiceCancellationServiceV1 service, ICorrelationContext correlation, CancellationToken ct) =>
        {
            SetPrivateNoStore(context.Response);
            if (kind is not ("Normal" or "Subcontract") || context.Request.Query.Any(x => x.Key is not ("kind" or "cursor") || x.Value.Count != 1))
                return DraftProblem(400, "validation_failed", correlation);
            try { return Results.Json(await service.ListCandidatesAsync(Enum.Parse<SalesOfficialInvoiceKindV1>(kind), cursor, ct), SalesDraftJson.Options); }
            catch (SalesDraftServiceException ex) { return DraftProblem(ex, correlation); }
        }).WithName("ListSalesOfficialInvoiceCancellationCandidatesV1").Produces<SalesOfficialCancellationCandidatesV1>()
            .ProducesErpBadRequest("validation_failed", "invalid_cursor").ProducesErpServiceUnavailable("official_cancellation_source_unavailable")
            .ProducesErpAuthenticatedProblems();
        foreach (var (path, kind, name) in new[] { ("invoices", SalesOfficialInvoiceKindV1.Normal, "SalesInvoice"),
                     ("subcontract-service-invoices", SalesOfficialInvoiceKindV1.Subcontract, "SalesSubcontractInvoice") })
        {
            group.MapGet("/" + path + "/{id:guid}/official-cancellation", async (Guid id, HttpContext context,
                [FromServices] ISalesOfficialInvoiceCancellationServiceV1 service, ICorrelationContext correlation, CancellationToken ct) =>
            {
                SetPrivateNoStore(context.Response);
                if (context.Request.Query.Count != 0) return DraftProblem(400, "validation_failed", correlation);
                try { return Results.Json(await service.GetForInvoiceAsync(kind, id, ct), SalesDraftJson.Options); }
                catch (SalesDraftServiceException ex) { return DraftProblem(ex, correlation); }
            }).WithName("Get" + name + "OfficialCancellationV1").Produces<SalesOfficialInvoiceCancellationViewV1>()
                .ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable("official_cancellation_source_unavailable")
                .ProducesErpBadRequest("validation_failed").ProducesErpAuthenticatedProblems();

            group.MapPost("/" + path + "/{id:guid}/official-cancellation", async (Guid id, HttpContext context,
                [FromServices] ISalesOfficialInvoiceCancellationServiceV1 service, ICorrelationContext correlation, CancellationToken ct) =>
            {
                SetPrivateNoStore(context.Response);
                if (!TryReadIdempotencyKey(context.Request.Headers, out var key) || !correlation.IsResolved)
                    return DraftProblem(400, SalesDraftProblemCodesV1.CommandMetadataInvalid, correlation);
                if (!context.Request.HasJsonContentType()) return Results.StatusCode(415);
                try
                {
                    using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
                    var request = SalesOfficialCancellationJsonV1.ReadRecord(await reader.ReadToEndAsync(ct));
                    var answer = await service.RecordAsync(kind, id, request,
                        new(key, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow), ct);
                    if (answer.IsReplay) ApplyReplayHeaders(context, answer.OriginalCorrelationId);
                    return Results.Json(answer.Cancellation, SalesDraftJson.Options);
                }
                catch (JsonException) { return DraftProblem(400, "validation_failed", correlation); }
                catch (SalesDraftServiceException ex) { return DraftProblem(ex, correlation); }
            }).WithName("Record" + name + "OfficialCancellationV1").WithIdempotencyContract(200)
                .Accepts<RecordSalesOfficialInvoiceCancellationV1>("application/json").Produces<SalesOfficialInvoiceCancellationViewV1>()
                .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.CommandMetadataInvalid)
                .AllowsEmptyErpBadRequestBody().ProducesErpUnsupportedMediaType().ProducesErpCodeLessNotFound()
                .ProducesErpConflict("official_cancellation_not_confirmed", SalesDraftProblemCodesV1.VersionConflict,
                    SalesDraftProblemCodesV1.StateConflict, SalesDraftProblemCodesV1.SameKeyPayloadMismatch, SalesDraftProblemCodesV1.ConcurrentChange)
                .ProducesErpServiceUnavailable("official_cancellation_source_unavailable", "official_cancellation_evidence_unavailable")
                .ProducesErpAuthenticatedProblems();
        }
        group.MapPost("/official-cancellations/{id:guid}/finance-retry", async (Guid id, HttpContext context,
            [FromServices] ISalesOfficialInvoiceCancellationServiceV1 service, ICorrelationContext correlation, CancellationToken ct) =>
        {
            SetPrivateNoStore(context.Response);
            if (!TryReadIdempotencyKey(context.Request.Headers, out var key) || !correlation.IsResolved)
                return DraftProblem(400, SalesDraftProblemCodesV1.CommandMetadataInvalid, correlation);
            if (!context.Request.HasJsonContentType()) return Results.StatusCode(415);
            try
            {
                using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
                var request = SalesOfficialCancellationJsonV1.ReadRetry(await reader.ReadToEndAsync(ct));
                var answer = await service.RetryFinanceAsync(id, request,
                    new(key, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow), ct);
                if (answer.IsReplay) ApplyReplayHeaders(context, answer.OriginalCorrelationId);
                return Results.Json(answer.Cancellation, SalesDraftJson.Options);
            }
            catch (JsonException) { return DraftProblem(400, "validation_failed", correlation); }
            catch (SalesDraftServiceException ex) { return DraftProblem(ex, correlation); }
        }).WithName("RetrySalesOfficialInvoiceCancellationFinanceV1").WithIdempotencyContract(200)
            .Accepts<RetrySalesOfficialInvoiceCancellationFinanceV1>("application/json").Produces<SalesOfficialInvoiceCancellationViewV1>()
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody().ProducesErpUnsupportedMediaType().ProducesErpCodeLessNotFound()
            .ProducesErpConflict(SalesDraftProblemCodesV1.VersionConflict, SalesDraftProblemCodesV1.SameKeyPayloadMismatch, SalesDraftProblemCodesV1.ConcurrentChange)
            .ProducesErpServiceUnavailable("official_cancellation_source_unavailable", "official_cancellation_finance_unavailable")
            .ProducesErpAuthenticatedProblems();
    }
}
internal static class SalesOfficialCancellationJsonV1
{
    internal static RecordSalesOfficialInvoiceCancellationV1 ReadRecord(string json)
    {
        using var document = Read(json, ["expectedVersion", "reason"]); var root = document.RootElement;
        if (RequiredJsonField(root, "expectedVersion").ValueKind != JsonValueKind.String || RequiredJsonField(root, "reason").ValueKind != JsonValueKind.String) throw new JsonException();
        return new(RequiredJsonField(root, "expectedVersion").GetString()!, RequiredJsonField(root, "reason").GetString()!);
    }
    internal static RetrySalesOfficialInvoiceCancellationFinanceV1 ReadRetry(string json)
    {
        using var document = Read(json, ["expectedDeliveryVersion", "releaseActiveAllocations"]); var root = document.RootElement;
        if (RequiredJsonField(root, "expectedDeliveryVersion").ValueKind != JsonValueKind.Number || !RequiredJsonField(root, "expectedDeliveryVersion").TryGetInt64(out var version)
            || RequiredJsonField(root, "releaseActiveAllocations").ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new JsonException();
        return new(version, RequiredJsonField(root, "releaseActiveAllocations").GetBoolean());
    }
    private static JsonDocument Read(string json, string[] names)
    {
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Object)
        {
            var props = root.EnumerateObject().ToArray();
            if (props.Length == names.Length && props.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() == names.Length
                && props.All(x => names.Contains(x.Name, StringComparer.Ordinal))) return doc;
        }
        doc.Dispose(); throw new JsonException();
    }
    // Required JSON lookup retains missing-key failure and never uses runtime reflection.
    private static JsonElement RequiredJsonField(JsonElement value, string name) =>
        value.TryGetProperty(name, out var field) ? field : throw new KeyNotFoundException();

}
