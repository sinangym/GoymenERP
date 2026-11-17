using System.Text.Json;
using System.Text;
using System.Text.Json.Serialization;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

public static partial class PurchasingEndpoints
{
    private static readonly JsonSerializerOptions ImportJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.Strict, MaxDepth = 12,
    };

    private static void MapInvoiceImports(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/purchasing/invoice-imports").WithTags("Purchasing").RequireAuthorization().RequireCompanyContext();
        group.MapPost("/reservations", (HttpContext context, [FromServices] IPurchaseInvoiceImportV1 service,
            ICorrelationContext correlation, CancellationToken token) => MutateAsync(context, correlation,
                json => { using var doc = JsonDocument.Parse(json); if (doc.RootElement.ValueKind != JsonValueKind.Object
                    || doc.RootElement.EnumerateObject().Any()) throw new JsonException(); return new InvoiceImportReservationBody(); },
                (_, metadata) => service.ReserveAsync(metadata, token), token))
            .WithName("ReservePurchaseInvoiceImportV1").WithIdempotencyContract(PurchasingApiV1.MutationStatus, 409)
            .Produces<PurchaseInvoiceImportReservationResultV1>()
            .ProducesErpBadRequest("validation_failed", PurchasingProblemCodesV1.CommandMetadataInvalid).AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType().ProducesErpConflict(PurchasingProblemCodesV1.SameKeyPayloadMismatch, PurchasingProblemCodesV1.ConcurrentChange)
            .ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

        InvoiceWorkspaceRead(group.MapGet("/{id:guid}", async (Guid id, HttpContext context, [FromServices] IPurchaseInvoiceImportV1 service,
            ICorrelationContext correlation, CancellationToken token) =>
        {
            SetPrivateNoStore(context.Response);
            if (context.Request.Query.Count != 0) return Problem(400, "validation_failed", correlation);
            try { return Results.Json(await service.ReadAsync(id, token), PurchasingJson.Options); }
            catch (PurchasingServiceException exception) { return Problem(context, exception, correlation); }
        }).WithName("GetPurchaseInvoiceImportV1").Produces<PurchaseInvoiceImportReservationV1>());

        group.MapPost("/{id:guid}/preview", async (Guid id, HttpContext context, [FromServices] IPurchaseInvoiceImportV1 service,
            ICorrelationContext correlation, CancellationToken token) =>
        {
            SetPrivateNoStore(context.Response);
            if (!context.Request.HasJsonContentType()) return Results.StatusCode(415);
            if (context.Request.Query.Count != 0) return Problem(400, "validation_failed", correlation);
            try
            {
                var text = await ReadImportBodyAsync(context, token);
                using var json = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 12 });
                static void Unique(JsonElement value)
                {
                    if (value.ValueKind == JsonValueKind.Object)
                    {
                        var names = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var property in value.EnumerateObject()) { if (!names.Add(property.Name)) throw new JsonException(); Unique(property.Value); }
                    }
                    else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) Unique(item);
                }
                Unique(json.RootElement);
                var request = json.Deserialize<PurchaseInvoiceImportPreviewRequestV1>(ImportJson) ?? throw new JsonException();
                return Results.Json(await service.PreviewAsync(id, request, token), PurchasingJson.Options);
            }
            catch (JsonException) { return Problem(400, "validation_failed", correlation); }
            catch (DecoderFallbackException) { return Problem(400, "validation_failed", correlation); }
            catch (PurchasingServiceException exception) { return Problem(context, exception, correlation); }
        }).WithName("PreviewPurchaseInvoiceImportV1").Accepts<PurchaseInvoiceImportPreviewRequestV1>("application/json")
            .Produces<PurchaseInvoiceImportPreviewV1>().ProducesErpBadRequest("validation_failed").AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType().ProducesErpCodeLessNotFound()
            .ProducesErpConflict(PurchasingProblemCodesV1.VersionConflict, "purchasing_invoice_source_changed")
            .ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
    }

    private sealed record InvoiceImportReservationBody;

    private static async Task<string> ReadImportBodyAsync(HttpContext context, CancellationToken token)
    {
        using var reader = new StreamReader(context.Request.Body, new UTF8Encoding(false, true));
        var buffer = new char[4096]; var text = new StringBuilder();
        while (await reader.ReadAsync(buffer.AsMemory(), token) is var count && count != 0)
        {
            if (text.Length + count > 1024 * 1024) throw new JsonException();
            text.Append(buffer, 0, count);
        }
        return text.ToString();
    }
}
