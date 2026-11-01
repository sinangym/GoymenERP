using GoymenERP.Api.OpenApi;
using GoymenERP.Api.SalesRuntime;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.DocumentGeneration.Pdf;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace GoymenERP.Api.Endpoints;

public static partial class SalesEndpoints
{
    /// <summary>
    /// MOD-07: GET …/{id}/pdf — the order's current revision as a PDF attachment, rendered on request from two Sales reads
    /// and stored nowhere (the Document Vault keeps what people upload; nothing here writes). Under the draft read: another
    /// company's order is 404, without the read 403. The prices are printed only under the existing
    /// <c>sales.orders.price.read</c> decision, asked last; without it the price-less variant is answered (no price column,
    /// no currency), never an empty or zero price. The two reads must describe the same version of the order, otherwise
    /// the order changed in between and the answer is 409 <c>concurrent_change</c> (try again). No total, tax or exchange is
    /// printed: they do not exist yet (API-03, T28). Every order the domain accepts lays out (rows and fields taller than a page
    /// continue on the next pages); an order longer than the PDF component's page bound is 422 <c>sales_order_pdf_too_large</c>
    /// (stable: a retry gives the same answer), never a cut document. A missing or malformed text and missing time-zone rules
    /// are a code-less 503. SAT-D165 (EK23): when the customer rejected the current approved revision, the revision the
    /// customer last accepted is printed (its lines, quantities, prices, revision and file-name version) with the note
    /// "Rev.N müşteri tarafından reddedildi; sipariş Rev.M ile sürer" — never the rejection's internal reason.
    /// </summary>
    private static void MapOrderPdf(RouteGroupBuilder drafts)
    {
        drafts.MapGet("/{id:guid}/pdf", async (
            Guid id,
            HttpContext httpContext,
            [FromServices] ISalesDraftReadServiceV1 detailService,
            [FromServices] ISalesOrderPrintReadServiceV1 printService,
            [FromServices] IStringLocalizer<SalesOrderPdfComposer> texts,
            [FromServices] ILogger<SalesOrderPdfComposer> logger,
            ICorrelationContext correlation,
            [FromServices] ISalesOrderPdfBusinessTimeZoneSource? timeZoneSource,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            SalesOrderDraftReadDetailViewV1 detail;
            SalesOrderPrintPricingV1 pricing;
            try
            {
                detail = await detailService.GetDraftDetailAsync(id, cancellationToken);
                // The price decision is the freshest word: asked after the detail, never cached across the two.
                pricing = await printService.GetOrderPrintPricingAsync(id, cancellationToken);
            }
            catch (SalesDraftServiceException exception)
            {
                // A read has no request to fix: an owner refusal that is not about authority is the print being unavailable.
                return exception.Status == StatusCodes.Status422UnprocessableEntity
                    ? Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable)
                    : DraftProblem(exception, correlation);
            }

            if (detail.Draft.Id != id || pricing is null)
            {
                logger.LogWarning(
                    "Sales order PDF read answered another record. DraftId={DraftId} CorrelationId={CorrelationId}",
                    id, correlation.IsResolved ? correlation.CorrelationId : null);
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            if (!string.Equals(detail.Draft.Version, pricing.Version, StringComparison.Ordinal))
            {
                logger.LogInformation(
                    "Sales order PDF reads met two versions; answered as a concurrent change. DraftId={DraftId} CorrelationId={CorrelationId}",
                    id, correlation.IsResolved ? correlation.CorrelationId : null);
                return DraftProblem(StatusCodes.Status409Conflict, SalesDraftProblemCodesV1.ConcurrentChange, correlation);
            }

            GeneratedPdf pdf;
            try
            {
                var zone = timeZoneSource is null ? SalesOrderPdfComposer.SystemBusinessTimeZone : timeZoneSource.BusinessTimeZone;
                var (request, _) = new SalesOrderPdfComposer(texts, zone).Compose(Guid.NewGuid(), detail, pricing);
                pdf = new DocumentPdfGenerator().Generate(request);
            }
            catch (DocumentPdfPageLimitException exception)
            {
                // A valid order too long for one synchronous PDF: said to the person (422, stable code), never retried as a 503.
                logger.LogWarning(
                    "Sales order PDF exceeds the page bound. DraftId={DraftId} PageLimit={PageLimit} CorrelationId={CorrelationId}",
                    id, exception.PageLimit, correlation.IsResolved ? correlation.CorrelationId : null);
                return DraftProblem(StatusCodes.Status422UnprocessableEntity, SalesDraftProblemCodesV1.OrderPdfTooLarge, correlation);
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or FormatException)
            {
                // An answer the composer cannot print faithfully (or that cannot be laid out uncut) is not printed. The log
                // carries the reason's type and message (no order content: the messages name rules, never values).
                logger.LogWarning(
                    "Sales order PDF was not rendered. DraftId={DraftId} Reason={Reason}: {Message} CorrelationId={CorrelationId}",
                    id, exception is ArgumentException ? "layout" : exception is FormatException ? "text-format" : "composition", exception.Message, correlation.IsResolved ? correlation.CorrelationId : null);
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            // An attachment of the stored type only, never sniffed or rendered as a page of this origin.
            var headers = httpContext.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
            return Results.File(pdf.Content, pdf.MediaType, pdf.VisibleFileName);
        })
            .WithName("GetSalesOrderDraftPdfV1")
            .Produces<Stream>(StatusCodes.Status200OK, TechnicalPdfGenerator.PdfMediaType)
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(SalesDraftProblemCodesV1.ConcurrentChange)
            .ProducesErpUnprocessableEntity(SalesDraftProblemCodesV1.OrderPdfTooLarge)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }
}
