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
        InvoiceWorkspaceRead(group.MapPost("/{id:guid}/mapping-references", (Guid id, HttpContext context,
            [FromServices] IPurchaseInvoiceImportMappingV1 service, ICorrelationContext correlation, CancellationToken token) =>
            ImportMappingReadAsync<PurchaseInvoiceImportPreviewRequestV1>(context, correlation,
                async request => await service.ReferencesAsync(id, request, token), token))
            .RequireAuthorization(PurchaseIncomingInvoicePermissionNamesV1.Import)
            .WithName("GetPurchaseInvoiceImportMappingReferencesV1").Accepts<PurchaseInvoiceImportPreviewRequestV1>("application/json")
            .Produces<PurchaseInvoiceImportReferencesV1>());
        InvoiceWorkspaceRead(group.MapPost("/{id:guid}/supplier-resolution", (Guid id, HttpContext context,
            [FromServices] IPurchaseInvoiceImportMappingV1 service, ICorrelationContext correlation, CancellationToken token) =>
            ImportMappingReadAsync<PurchaseInvoiceImportSupplierRequestV1>(context, correlation,
                async request => await service.SupplierAsync(id, request, token), token))
            .RequireAuthorization(PurchaseIncomingInvoicePermissionNamesV1.Import)
            .WithName("ResolvePurchaseInvoiceImportSupplierV1").Accepts<PurchaseInvoiceImportSupplierRequestV1>("application/json")
            .Produces<PurchaseInvoiceImportSupplierV1>());
        InvoiceWorkspaceRead(group.MapPost("/{id:guid}/product-candidates", (Guid id, HttpContext context,
            [FromServices] IPurchaseInvoiceImportMappingV1 service, ICorrelationContext correlation, CancellationToken token) =>
            ImportMappingReadAsync<PurchaseInvoiceImportProductsRequestV1>(context, correlation,
                async request => await service.ProductsAsync(id, request, token), token))
            .RequireAuthorization(PurchaseIncomingInvoicePermissionNamesV1.Import)
            .WithName("GetPurchaseInvoiceImportProductCandidatesV1").Accepts<PurchaseInvoiceImportProductsRequestV1>("application/json")
            .Produces<PurchaseInvoiceImportProductsV1>());
        InvoiceMutation(group.MapPost("/{id:guid}/records", (Guid id, HttpContext context,
            [FromServices] IPurchaseInvoiceImportAdmissionV1 service, ICorrelationContext correlation, CancellationToken token) =>
            MutateAsync(context, correlation, ReadImportAdmission,
                async (request, metadata) => PurchaseIncomingInvoiceWireV1.Response(await service.RecordAsync(id, request, metadata, token)), token))
            .RequireAuthorization(PurchaseIncomingInvoicePermissionNamesV1.Import)
            .WithName("RecordPurchaseInvoiceImportV1").Accepts<PurchaseInvoiceImportAdmissionRequestV1>("application/json"));
        InvoiceWorkspaceRead(group.MapGet("", async (int? pageIndex, HttpContext context, [FromServices] IPurchaseInvoiceImportV1 service,
            ICorrelationContext correlation, CancellationToken token) =>
        {
            SetPrivateNoStore(context.Response);
            if (context.Request.Query.Keys.Any(key => key != "pageIndex") || context.Request.Query.Any(item => item.Value.Count != 1))
                return Problem(400, "validation_failed", correlation);
            try { return Results.Json(await service.ListAsync(pageIndex ?? 0, token), PurchasingJson.Options); }
            catch (PurchasingServiceException exception) { return Problem(context, exception, correlation); }
        }).WithName("ListPurchaseInvoiceImportsV1").Produces<PurchaseInvoiceImportReservationPageV1>());
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

    private static PurchaseInvoiceImportAdmissionRequestV1 ReadImportAdmission(string text) => ReadImportJson<PurchaseInvoiceImportAdmissionRequestV1>(text);
    private static T ReadImportJson<T>(string text)
    {
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 12 });
        static void Unique(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                { if (!names.Add(property.Name)) throw new JsonException(); Unique(property.Value); }
            }
            else if (value.ValueKind == JsonValueKind.Array) foreach (var child in value.EnumerateArray()) Unique(child);
        }
        Unique(document.RootElement);
        return document.Deserialize<T>(ImportJson) ?? throw new JsonException();
    }

    private static async Task<IResult> ImportMappingReadAsync<T>(HttpContext context, ICorrelationContext correlation,
        Func<T, Task<object>> action, CancellationToken token)
    {
        SetPrivateNoStore(context.Response);
        if (!context.Request.HasJsonContentType()) return Results.StatusCode(415);
        if (context.Request.Query.Count != 0) return Problem(400, "validation_failed", correlation);
        try { return Results.Json(await action(ReadImportJson<T>(await ReadImportBodyAsync(context, token))), PurchasingJson.Options); }
        catch (JsonException) { return Problem(400, "validation_failed", correlation); }
        catch (DecoderFallbackException) { return Problem(400, "validation_failed", correlation); }
        catch (PurchasingServiceException error) { return Problem(context, error, correlation); }
    }

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
