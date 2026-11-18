using System.Text.Json;
using GoymenERP.Api.DocumentsRuntime;
using GoymenERP.Api.LogisticsRuntime;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.DocumentGeneration.Pdf;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Extensions.Localization;

namespace GoymenERP.Api.Endpoints;

public static class LogisticsDispatchPdfEndpoints
{
    /// <summary>Read-only producer, like the Sales PDF. No stock, money, document-vault write or external e-Dispatch send.</summary>
    public static IEndpointRouteBuilder MapLogisticsDispatchPdfEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/logistics/loading-instructions/{instructionId:guid}/dispatch-notes/{dispatchNoteId:guid}/pdf",
            async (Guid instructionId, Guid dispatchNoteId, HttpContext context, ILogisticsInstructionService instructions,
                ILogisticsPartyAccountPortV1 parties, IProductLogisticsDocumentDisplayV1 products, ISettingsUnitIdentityCatalogV1 units,
                ILogisticsWeighingDifferenceServiceV1 weighings, PublishedDocumentBrandingReader templates, ICompanyContext company, ICurrentUser user,
                IStringLocalizer<LogisticsDispatchPdfComposer> texts, CancellationToken cancellationToken) =>
            {
                context.Response.Headers.CacheControl = "no-store, private";
                context.Response.Headers.Pragma = "no-cache";
                var frame = new DocumentPdfProducerFrame(company, user);
                try
                {
                    frame.Require();
                    var detail = await instructions.GetInstructionDetailAsync(instructionId, cancellationToken);
                    frame.Require();
                    var note = detail.DispatchNotes.SingleOrDefault(x => x.DispatchNoteId == dispatchNoteId && x.Status == "Approved");
                    if (note is null) return Results.Problem(statusCode: 404);
                    if (detail.Instruction.Id != instructionId || detail.Instruction.CompanyId != frame.CompanyId)
                        return Results.Problem(statusCode: 503);
                    var companyId = frame.CompanyId;
                    var customer = await parties.GetAuthorizedAsync(detail.Instruction.CustomerPartyId, detail.Instruction.CompanyAccountId, cancellationToken);
                    if (customer.Outcome == LogisticsPartyAccountOutcomeV1.Denied) return Results.Problem(statusCode: 403);
                    if (customer.Outcome != LogisticsPartyAccountOutcomeV1.Found || customer.Account is not { } account
                        || account.CompanyId != companyId || account.PartyId != detail.Instruction.CustomerPartyId
                        || account.CompanyAccountId != detail.Instruction.CompanyAccountId || string.IsNullOrWhiteSpace(account.DisplayName))
                        return Results.Problem(statusCode: 503);
                    var display = new Dictionary<Guid, ProductLogisticsDocumentDisplayV1>();
                    foreach (var id in note.Lines.Select(x => x.ProductId).Distinct())
                    {
                        var product = await products.ReadAsync(companyId, id, cancellationToken);
                        if (product?.Outcome == ProductLogisticsDocumentDisplayOutcomeV1.Forbidden) return Results.Problem(statusCode: 403);
                        if (product is null || product.Outcome != ProductLogisticsDocumentDisplayOutcomeV1.Available
                            || product.CompanyId != companyId || product.ProductId != id) return Results.Problem(statusCode: 503);
                        display.Add(id, product);
                    }
                    var unitNames = (await units.ListAsync(cancellationToken)).ToDictionary(x => x.UnitId, x => x.Code);
                    var kilograms = await ReadWeighing();
                    var branding = await templates.ReadAsync(companyId, DocumentTemplateModuleV1.Logistics,
                        ErpPermissions.LogisticsLoadingInstructionsRead, cancellationToken);
                    // Real record/scope and the independent kilogram version are re-measured after all other owner calls.
                    var fresh = await instructions.GetInstructionDetailAsync(instructionId, cancellationToken);
                    var freshKg = await ReadWeighing();
                    frame.Require();
                    var freshNote = fresh.DispatchNotes.SingleOrDefault(x => x.DispatchNoteId == dispatchNoteId && x.Status == "Approved");
                    if (freshNote is null) return Results.Problem(statusCode: 404);
                    if (!company.IsResolved || company.CompanyId != companyId || fresh.Instruction.CompanyId != companyId)
                        return Results.Problem(statusCode: 403);
                    if (fresh.Instruction.Version != detail.Instruction.Version || JsonSerializer.Serialize(freshNote) != JsonSerializer.Serialize(note)
                        || freshKg?.RecordId != kilograms?.RecordId || freshKg?.Version != kilograms?.Version)
                        return Results.Problem(statusCode: 409);
                    var request = new LogisticsDispatchPdfComposer(texts).Compose(fresh.Instruction, freshNote, account.DisplayName,
                        display, unitNames, freshKg, branding.Render);
                    var pdf = new DocumentPdfGenerator().Generate(request);
                    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                    context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
                    return Results.File(pdf.Content, pdf.MediaType, pdf.VisibleFileName);

                    async Task<WeighingDifferenceViewV1?> ReadWeighing()
                    {
                        try { return await weighings.GetByDispatchNoteAsync(dispatchNoteId, cancellationToken); }
                        catch (LogisticsWeighingDifferenceServiceException error) when (error.Status == 404) { return null; }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (DocumentBrandingServiceException error) { return Results.Problem(statusCode: error.Status is 401 or 403 ? error.Status : 503); }
                catch (LogisticsInstructionServiceException error) { return Results.Problem(statusCode: error.Status is 401 or 403 or 404 or 409 ? error.Status : 503); }
                catch (LogisticsWeighingDifferenceServiceException error) { return Results.Problem(statusCode: error.Status is 401 or 403 or 404 or 409 ? error.Status : 503); }
                catch (DocumentPdfPageLimitException) { return Results.Problem(statusCode: 422, extensions: new Dictionary<string, object?> { ["code"] = "logistics_dispatch_pdf_too_large" }); }
                catch { return Results.Problem(statusCode: 503); }
            }).RequireCompanyContext().RequireAuthorization(ErpPermissions.LogisticsLoadingInstructionsRead)
            .WithTags("Logistics").WithName("GetLogisticsDispatchPdfV1")
            .Produces<Stream>(200, TechnicalPdfGenerator.PdfMediaType).ProducesErpCodeLessNotFound()
            .ProducesErpCodeLessConflict().ProducesErpUnprocessableEntity("logistics_dispatch_pdf_too_large")
            .ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
        return endpoints;
    }
}
