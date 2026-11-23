using System.Text.Json;
using GoymenERP.Api.InventoryRuntime;
using GoymenERP.Api.DocumentsRuntime;
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
        endpoints.MapGet("/api/v1/inventory/labels/{labelId:guid}/pdf-template", async (Guid labelId, HttpContext context,
            IInventoryLabelDetailsV1 labels, ICompanyContext company, ICurrentUser user, PublishedDocumentBrandingReader templates,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(context.Response);
            var frame = new DocumentPdfProducerFrame(company, user);
            try
            {
                frame.Require();
                var before = await labels.GetAsync(labelId, cancellationToken);
                frame.Require();
                if (before.Identity.CompanyId != frame.CompanyId || before.Identity.LabelId != labelId)
                    return Results.Problem(statusCode: 503);
                var selected = await templates.ReadAsync(frame.CompanyId, DocumentTemplateModuleV1.Inventory,
                    ErpPermissions.InventoryLabelsRead, cancellationToken);
                var fresh = await labels.GetAsync(labelId, cancellationToken);
                frame.Require();
                if (fresh.Identity.CompanyId != frame.CompanyId || fresh.Identity.LabelId != labelId)
                    return Results.Problem(statusCode: 503);
                return Results.Ok(new InventoryLabelPdfTemplateV1(frame.CompanyId, labelId,
                    selected.Publication?.PublicationId, selected.Publication?.Version));
            }
            catch (InventoryLabelDetailException error) { return Results.Problem(statusCode: error.StatusCode is 401 or 403 or 404 ? error.StatusCode : 503); }
            catch (DocumentBrandingServiceException error) { return Results.Problem(statusCode: error.Status is 401 or 403 ? error.Status : 503); }
        }).RequireCompanyContext().RequireAuthorization(ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead)
            .WithTags("Inventory").WithName("GetInventoryLabelPdfTemplateV1").Produces<InventoryLabelPdfTemplateV1>()
            .ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

        endpoints.MapPost(InventoryLabelPdfApiV1.Path, async (Guid labelId, HttpContext context,
            IInventoryLabelDetailsV1 labels, ICompanyContext company, ICurrentUser user, ICorrelationContext correlation,
            IGeneratedDocumentStorePortV1 documents, IStringLocalizer<InventoryLabelPdfComposer> texts,
            PublishedDocumentBrandingReader templates,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(context.Response);
            var frame = new DocumentPdfProducerFrame(company, user);
            if (!TryReadIdempotencyKey(context.Request.Headers, out var key) || !correlation.IsResolved)
                return OpeningProblem(400, InventoryLabelPdfApiV1.InvalidRequest, correlation);
            if (!context.Request.HasJsonContentType()) return Results.StatusCode(415);
            GenerateInventoryLabelPdfV1? request;
            try
            {
                request = await DocumentBrandingEndpoints.Body<GenerateInventoryLabelPdfV1>(context, cancellationToken);
            }
            catch (JsonException) { return OpeningProblem(400, InventoryLabelPdfApiV1.InvalidRequest, correlation); }
            catch (DocumentBrandingServiceException error) { return error.Status == 415 ? Results.StatusCode(415) : OpeningProblem(400, InventoryLabelPdfApiV1.InvalidRequest, correlation); }
            if (request is null || request.RequestedAtUtc == default || request.RequestedAtUtc.Offset != TimeSpan.Zero
                || (request.PublicationId is null) != (request.PublicationVersion is null)
                || request.PublicationId == Guid.Empty || request.PublicationVersion is not null
                && (!long.TryParse(request.PublicationVersion, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var publicationVersion) || publicationVersion < 1
                    || request.PublicationVersion != publicationVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                return OpeningProblem(400, InventoryLabelPdfApiV1.InvalidRequest, correlation);
            InventoryLabelDetailV1 detail;
            try { frame.Require(); detail = await labels.GetAsync(labelId, cancellationToken); frame.Require(); }
            catch (DocumentBrandingServiceException error) { return Results.Problem(statusCode: error.Status); }
            catch (InventoryLabelDetailException exception)
            {
                return Results.Problem(statusCode: exception.StatusCode is 401 or 403 or 404 ? exception.StatusCode : 503);
            }
            if (detail.Identity.CompanyId != frame.CompanyId || detail.Identity.LabelId != labelId)
                return Results.Problem(statusCode: 503);
            if (!BarcodeLabelPdfGenerator.CanEncode(detail.Identity.Code))
                return OpeningProblem(422, InventoryLabelPdfApiV1.BarcodeUnsupported, correlation);
            PublishedDocumentBranding selected = new(null, null);
            try
            {
                if (request.PublicationId is { } publicationId)
                    selected = await templates.ReadSelectedAsync(frame.CompanyId, DocumentTemplateModuleV1.Inventory,
                        ErpPermissions.InventoryLabelsRead, publicationId, request.PublicationVersion!, cancellationToken);
                // Re-measure the label's real location/record scope after the Documents owner call.
                detail = await labels.GetAsync(labelId, cancellationToken);
                frame.Require();
            }
            catch (DocumentBrandingServiceException error) { return Results.Problem(statusCode: error.Status is 401 or 403 ? error.Status : 503); }
            catch (InventoryLabelDetailException error) { return Results.Problem(statusCode: error.StatusCode is 401 or 403 or 404 ? error.StatusCode : 503); }
            if (detail.Identity.CompanyId != frame.CompanyId || detail.Identity.LabelId != labelId)
                return Results.Problem(statusCode: 503);
            GeneratedPdf pdf;
            try
            {
                pdf = new BarcodeLabelPdfGenerator().Generate(new InventoryLabelPdfComposer(texts)
                    .Compose(detail.Identity, request.RequestedAtUtc, selected.Render));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException)
            { return Results.Problem(statusCode: 503); }
            var stored = await documents.StoreAsync(new(frame.CompanyId, DocumentOwnerLinkKindsV1.InventoryMaterialLabel,
                labelId, pdf.VisibleFileName, pdf.Content, InventoryLabelPdfApiV1.TemplateCode,
                selected.Publication?.TemplateVersion ?? InventoryLabelPdfApiV1.TemplateVersion,
                key, correlation.CorrelationId, correlation.CausationId, ErpPermissions.InventoryLabelsRead,
                InventoryLabelPdfApiV1.Command, selected.Publication), cancellationToken);
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
