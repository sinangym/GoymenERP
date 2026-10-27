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
    /// printed: they do not exist yet (API-03, T28).
    /// </summary>
    private static void MapOrderPdf(RouteGroupBuilder drafts)
    {
        drafts.MapGet("/{id:guid}/pdf", async (
            Guid id,
            HttpContext httpContext,
            [FromServices] ISalesDraftReadServiceV1 detailService,
            [FromServices] ISalesOrderPrintReadServiceV1 printService,
            [FromServices] IStringLocalizer<SalesOrderPdfComposer> texts,
            ICorrelationContext correlation,
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
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            if (!string.Equals(detail.Draft.Version, pricing.Version, StringComparison.Ordinal))
            {
                return DraftProblem(StatusCodes.Status409Conflict, SalesDraftProblemCodesV1.ConcurrentChange, correlation);
            }

            GeneratedPdf pdf;
            try
            {
                var (request, _) = new SalesOrderPdfComposer(texts).Compose(Guid.NewGuid(), detail, pricing);
                pdf = new DocumentPdfGenerator().Generate(request);
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            {
                // An answer the composer cannot print faithfully (or that cannot be laid out uncut) is not printed.
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
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }
}
