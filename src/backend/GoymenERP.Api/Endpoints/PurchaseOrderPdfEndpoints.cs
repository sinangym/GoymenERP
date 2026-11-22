using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.Api.DocumentsRuntime;
using GoymenERP.Api.PurchasingRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.DocumentGeneration.Pdf;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// WP13 (DALGA_PLANI_v2 1.14): <c>GET /api/v1/purchasing/orders/{id}/pdf</c> — the purchase order as a PDF attachment, rendered on
/// request and stored nowhere (a read-only producer like the Sales order and Logistics dispatch PDFs). The content is the Purchasing
/// owner's authorized order read (the supplier as Cari shows it) and its order totals read (the shared commercial tax calculator), the
/// Product owner's product code/name under the same Purchasing order read, and the Settings unit codes; nothing is computed here.
/// Prices and totals are printed only under <c>purchasing.orders.price.read</c> (both owner reads must say <c>Visible</c>). Another
/// company's order is 404, without the read 403 (code-less). The two owner reads must describe the same order version, read again
/// after the other owner calls, otherwise 409 (try again). An order longer than the page bound is 422
/// <c>purchase_order_pdf_too_large</c>; an answer that cannot be printed faithfully is a code-less 503. No company template is
/// applied (DEC-DOC-010 technical fallback: no Purchasing template exists yet).
/// </summary>
public static class PurchaseOrderPdfEndpoints
{
    public const string TooLargeCode = "purchase_order_pdf_too_large";

    public static IEndpointRouteBuilder MapPurchaseOrderPdfEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet($"{PurchasingApiV1.OrdersPath}/{{id:guid}}/pdf", async (
                Guid id,
                HttpContext context,
                [FromServices] IPurchasingOrderReadServiceV1 orders,
                [FromServices] IPurchasingService purchasing,
                [FromServices] IProductPurchasingDocumentDisplayV1 products,
                [FromServices] ISettingsUnitIdentityCatalogV1 units,
                [FromServices] IStringLocalizer<PurchaseOrderPdfComposer> texts,
                [FromServices] ILogger<PurchaseOrderPdfComposer> logger,
                ICompanyContext company,
                ICurrentUser user,
                CancellationToken cancellationToken) =>
            {
                context.Response.Headers.CacheControl = "no-store, private";
                context.Response.Headers.Pragma = "no-cache";
                var frame = new DocumentPdfProducerFrame(company, user);
                try
                {
                    frame.Require();
                    var detail = await orders.GetOrderDetailAsync(id, cancellationToken);
                    var totals = await purchasing.GetOrderTotalsAsync(id, cancellationToken);
                    frame.Require();
                    var companyId = frame.CompanyId;
                    if (detail.Order.Id != id || detail.Order.CompanyId != companyId || totals.OrderId != id)
                    {
                        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
                    }

                    if (!string.Equals(detail.Order.Version, totals.Version, StringComparison.Ordinal))
                    {
                        return Results.Problem(statusCode: StatusCodes.Status409Conflict);
                    }

                    var display = new Dictionary<Guid, ProductPurchasingDocumentDisplayV1>();
                    foreach (var productId in detail.Lines.Select(line => line.ProductId).Distinct())
                    {
                        var product = await products.ReadAsync(companyId, productId, cancellationToken);
                        if (product?.Outcome == ProductPurchasingDocumentDisplayOutcomeV1.Forbidden)
                        {
                            return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
                        }

                        if (product is null || product.Outcome != ProductPurchasingDocumentDisplayOutcomeV1.Available
                            || product.CompanyId != companyId || product.ProductId != productId)
                        {
                            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
                        }

                        display.Add(productId, product);
                    }

                    var unitCodes = (await units.ListAsync(cancellationToken)).ToDictionary(unit => unit.UnitId, unit => unit.Code);

                    // The owner record, its totals and the price disclosure are fresh after the other owner calls.
                    var fresh = await orders.GetOrderDetailAsync(id, cancellationToken);
                    var freshTotals = await purchasing.GetOrderTotalsAsync(id, cancellationToken);
                    frame.Require();
                    if (fresh.Order.CompanyId != companyId || !company.IsResolved || company.CompanyId != companyId)
                    {
                        return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
                    }

                    if (!string.Equals(fresh.Order.Version, detail.Order.Version, StringComparison.Ordinal)
                        || !string.Equals(freshTotals.Version, detail.Order.Version, StringComparison.Ordinal)
                        || !string.Equals(fresh.PriceDisclosure, detail.PriceDisclosure, StringComparison.Ordinal))
                    {
                        return Results.Problem(statusCode: StatusCodes.Status409Conflict);
                    }

                    var (request, _) = new PurchaseOrderPdfComposer(texts).Compose(Guid.NewGuid(), fresh, freshTotals, display, unitCodes);
                    var pdf = new DocumentPdfGenerator().Generate(request);
                    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                    context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
                    return Results.File(pdf.Content, pdf.MediaType, pdf.VisibleFileName);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (DocumentBrandingServiceException error)
                {
                    // The producer frame: the actor or company changed during the read.
                    return Results.Problem(statusCode: error.Status is 401 or 403 ? error.Status : 503);
                }
                catch (PurchasingServiceException error)
                {
                    return Results.Problem(statusCode: error.Status is 401 or 403 or 404 or 409 ? error.Status : 503);
                }
                catch (DocumentPdfPageLimitException exception)
                {
                    logger.LogWarning("Purchase order PDF exceeds the page bound. OrderId={OrderId} PageLimit={PageLimit}", id, exception.PageLimit);
                    return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
                        extensions: new Dictionary<string, object?> { ["code"] = TooLargeCode });
                }
                catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or FormatException
                                                      or OverflowException or KeyNotFoundException)
                {
                    // An answer the composer cannot print faithfully is not printed; the log names the rule, never order content.
                    logger.LogWarning("Purchase order PDF was not rendered. OrderId={OrderId} Reason={Reason}", id, exception.GetType().Name);
                    return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
                }
            })
            .RequireCompanyContext()
            .RequireAuthorization()
            .WithTags("Purchasing")
            .WithName("GetPurchaseOrderPdfV1")
            .Produces<Stream>(StatusCodes.Status200OK, TechnicalPdfGenerator.PdfMediaType)
            .ProducesErpCodeLessNotFound()
            .ProducesErpCodeLessConflict()
            .ProducesErpUnprocessableEntity(TooLargeCode)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
        return endpoints;
    }
}
