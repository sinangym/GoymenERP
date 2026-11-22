using GoymenERP.Api.DocumentsRuntime;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.Api.SalesRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.DocumentGeneration.Pdf;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// WP13 (DALGA_PLANI_v2 1.14): <c>GET /api/v1/sales/quotes/{id}/pdf</c> — the sales quote (API-22) as a PDF attachment, rendered on
/// request and stored nowhere. Under the quote read (<c>sales.quotes.read</c>, measured by the Sales quote service): another company's
/// quote is 404, without the read 403 (code-less). The quoted prices are printed only when the quote read disclosed them (the order's
/// price read); the currency code is then the Settings money profile's for the quote's date. The customer is the Cari owner's display.
/// The quote is read again after the other owner calls; another version is 409. Nothing is computed (no totals). An answer that
/// cannot be printed faithfully is a code-less 503; a quote beyond the page bound is 422 <c>sales_quote_pdf_too_large</c>.
/// </summary>
public static class SalesQuotePdfEndpoints
{
    public const string TooLargeCode = "sales_quote_pdf_too_large";

    public static IEndpointRouteBuilder MapSalesQuotePdfEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet($"{SalesQuoteApiV1.QuotePath}/{{id:guid}}/pdf", async (
                Guid id,
                HttpContext context,
                [FromServices] ISalesQuoteService quotes,
                [FromServices] ISalesPartyDisplaySourceV1 parties,
                [FromServices] ISettingsPrerequisiteProvider settings,
                [FromServices] ISettingsUnitIdentityCatalogV1 units,
                [FromServices] IStringLocalizer<SalesQuotePdfComposer> texts,
                [FromServices] ILogger<SalesQuotePdfComposer> logger,
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
                    var detail = await quotes.GetQuoteDetailAsync(id, cancellationToken);
                    frame.Require();
                    var companyId = frame.CompanyId;
                    if (detail.Quote.Id != id || detail.Quote.CompanyId != companyId)
                    {
                        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
                    }

                    var answer = await parties.GetAuthorizedBatchAsync(
                        new SalesPartyDisplayBatchRequestV1(companyId, user.UserId!, [detail.Quote.PartyId]), cancellationToken);
                    if (answer.Outcome == SalesPartyDisplayBatchOutcomeV1.Denied)
                    {
                        return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
                    }

                    if (answer.Outcome != SalesPartyDisplayBatchOutcomeV1.Success
                        || answer.Items.SingleOrDefault(item => item.PartyId == detail.Quote.PartyId) is not { } party)
                    {
                        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
                    }

                    string? currencyCode = null;
                    if (detail.Lines.Count > 0 && detail.Lines.All(line => line.PriceDisclosure == SalesPriceDisclosure.Visible))
                    {
                        var profile = await settings.GetMoneyProfileAsync(detail.Quote.CurrencyId, SalesMoneyOperations.OrderDraftV1,
                            detail.Quote.BusinessDate, cancellationToken);
                        if (profile.CompanyId != companyId || profile.CurrencyId != detail.Quote.CurrencyId
                            || string.IsNullOrWhiteSpace(profile.CurrencyCode))
                        {
                            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
                        }

                        currencyCode = profile.CurrencyCode;
                    }

                    var unitCodes = (await units.ListAsync(cancellationToken)).ToDictionary(unit => unit.UnitId, unit => unit.Code);

                    // The quote and its price disclosure are fresh after the other owner calls.
                    var fresh = await quotes.GetQuoteDetailAsync(id, cancellationToken);
                    frame.Require();
                    if (!string.Equals(fresh.Quote.Version, detail.Quote.Version, StringComparison.Ordinal)
                        || fresh.Lines.Select(line => line.PriceDisclosure).SequenceEqual(detail.Lines.Select(line => line.PriceDisclosure)) is false)
                    {
                        return Results.Problem(statusCode: StatusCodes.Status409Conflict);
                    }

                    var (request, _) = new SalesQuotePdfComposer(texts).Compose(Guid.NewGuid(), fresh, party, currencyCode, unitCodes);
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
                    return Results.Problem(statusCode: error.Status is 401 or 403 ? error.Status : 503);
                }
                catch (SalesDraftServiceException error)
                {
                    return Results.Problem(statusCode: error.Status is 401 or 403 or 404 or 409 ? error.Status : 503);
                }
                catch (SettingsPrerequisiteException)
                {
                    return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
                }
                catch (DocumentPdfPageLimitException exception)
                {
                    logger.LogWarning("Sales quote PDF exceeds the page bound. QuoteId={QuoteId} PageLimit={PageLimit}", id, exception.PageLimit);
                    return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
                        extensions: new Dictionary<string, object?> { ["code"] = TooLargeCode });
                }
                catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or FormatException
                                                      or OverflowException)
                {
                    logger.LogWarning("Sales quote PDF was not rendered. QuoteId={QuoteId} Reason={Reason}", id, exception.GetType().Name);
                    return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
                }
            })
            .RequireCompanyContext()
            .RequireAuthorization()
            .WithTags("Sales")
            .WithName("GetSalesQuotePdfV1")
            .Produces<Stream>(StatusCodes.Status200OK, TechnicalPdfGenerator.PdfMediaType)
            .ProducesErpCodeLessNotFound()
            .ProducesErpCodeLessConflict()
            .ProducesErpUnprocessableEntity(TooLargeCode)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
        return endpoints;
    }
}
