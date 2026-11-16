using System.Text.Json;
using GoymenERP.Api.InventoryRuntime;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.DocumentGeneration.Pdf;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;
using Microsoft.Extensions.Localization;

namespace GoymenERP.Api.Endpoints;

public static partial class InventoryEndpoints
{
    private static void MapInventoryLabelPdfEndpoint(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(InventoryLabelPdfApiV1.Path, async (Guid labelId, HttpContext context,
            IInventoryLabelDetailsV1 labels, ICompanyContext company, ICorrelationContext correlation,
            IGeneratedDocumentStorePortV1 documents, IStringLocalizer<InventoryLabelPdfComposer> texts,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(context.Response);
            if (!TryReadIdempotencyKey(context.Request.Headers, out var key) || !correlation.IsResolved)
                return OpeningProblem(400, InventoryLabelPdfApiV1.InvalidRequest, correlation);
            if (!context.Request.HasJsonContentType()) return Results.StatusCode(415);
            GenerateInventoryLabelPdfV1? request;
            try
            {
                request = await JsonSerializer.DeserializeAsync<GenerateInventoryLabelPdfV1>(context.Request.Body,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken);
            }
            catch (JsonException) { return OpeningProblem(400, InventoryLabelPdfApiV1.InvalidRequest, correlation); }
            if (request is null || request.RequestedAtUtc == default || request.RequestedAtUtc.Offset != TimeSpan.Zero)
                return OpeningProblem(400, InventoryLabelPdfApiV1.InvalidRequest, correlation);
            InventoryLabelDetailV1 detail;
            try { detail = await labels.GetAsync(labelId, cancellationToken); }
            catch (InventoryLabelDetailException exception)
            {
                return Results.Problem(statusCode: exception.StatusCode is 401 or 403 or 404 ? exception.StatusCode : 503);
            }
            if (!company.IsResolved || detail.Identity.CompanyId != company.CompanyId || detail.Identity.LabelId != labelId)
                return Results.Problem(statusCode: 503);
            if (!BarcodeLabelPdfGenerator.CanEncode(detail.Identity.Code))
                return OpeningProblem(422, InventoryLabelPdfApiV1.BarcodeUnsupported, correlation);
            GeneratedPdf pdf;
            try
            {
                pdf = new BarcodeLabelPdfGenerator().Generate(new InventoryLabelPdfComposer(texts)
                    .Compose(detail.Identity, request.RequestedAtUtc));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException)
            { return Results.Problem(statusCode: 503); }
            var stored = await documents.StoreAsync(new(company.CompanyId, DocumentOwnerLinkKindsV1.InventoryMaterialLabel,
                labelId, pdf.VisibleFileName, pdf.Content, InventoryLabelPdfApiV1.TemplateCode, InventoryLabelPdfApiV1.TemplateVersion,
                key, correlation.CorrelationId, correlation.CausationId, ErpPermissions.InventoryLabelsRead,
                InventoryLabelPdfApiV1.Command), cancellationToken);
            if (!stored.IsStored || stored.DocumentId is not { } documentId || stored.VersionNo is null)
            {
                return stored.Outcome switch
                {
                    GeneratedDocumentStoreOutcomeV1.Unauthenticated => Results.Problem(statusCode: 401),
                    GeneratedDocumentStoreOutcomeV1.Forbidden => Results.Problem(statusCode: 403),
                    GeneratedDocumentStoreOutcomeV1.OwnerNotFound => Results.Problem(statusCode: 404),
                    GeneratedDocumentStoreOutcomeV1.IdempotencyConflict or GeneratedDocumentStoreOutcomeV1.ConcurrentChange =>
                        OpeningProblem(409, "inventory_label_pdf_document_conflict", correlation),
                    _ => Results.Problem(statusCode: 503),
                };
            }
            if (stored.Outcome == GeneratedDocumentStoreOutcomeV1.Replayed)
            {
                if (string.IsNullOrWhiteSpace(stored.OriginalCorrelationId)) return Results.Problem(statusCode: 503);
                ApplyReplayHeaders(context, stored.OriginalCorrelationId);
            }
            context.Response.Headers["X-Document-Id"] = documentId.ToString("D");
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
            return Results.File(pdf.Content, pdf.MediaType, pdf.VisibleFileName);
        })
        .RequireCompanyContext()
        .RequireAuthorization(ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead)
        .WithTags("Inventory")
        .WithName("GenerateInventoryLabelPdfV1")
        .WithIdempotencyContract(StatusCodes.Status200OK)
        .Accepts<GenerateInventoryLabelPdfV1>("application/json")
        .Produces<Stream>(StatusCodes.Status200OK, TechnicalPdfGenerator.PdfMediaType)
        .ProducesErpBadRequest(InventoryLabelPdfApiV1.InvalidRequest)
        .ProducesErpUnsupportedMediaType()
        .ProducesErpCodeLessNotFound()
        .ProducesErpConflict("inventory_label_pdf_document_conflict")
        .ProducesErpUnprocessableEntity(InventoryLabelPdfApiV1.BarcodeUnsupported)
        .ProducesErpServiceUnavailable()
        .ProducesErpAuthenticatedProblems();
    }
}
