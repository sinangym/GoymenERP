using System.Globalization;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// WP15 (DALGA_PLANI_v2 1.16): read-only incoming invoice duplicate check. Both answers are warnings
/// (<c>behavior = "warn"</c>); neither records nor blocks anything (NEEDS-SINAN WP15-1).
/// <c>GET /api/v1/purchasing/invoices/duplicate-check</c> checks the facts of an invoice about to be recorded
/// (import permission); <c>GET /api/v1/purchasing/invoices/{id}/duplicates</c> checks a recorded invoice against the
/// others (read permission).
/// </summary>
public static partial class PurchasingEndpoints
{
    private static readonly string[] DuplicateCheckQueryKeys = ["supplierPartyId", "officialNumber", "ettn", "invoiceDate", "currencyId", "payable"];

    private static void MapIncomingInvoiceDuplicateChecks(RouteGroupBuilder group)
    {
        InvoiceWorkspaceRead(group.MapGet("/duplicate-check", async (HttpContext context,
            [FromServices] IPurchaseInvoiceDuplicateCheckV1 service, ICorrelationContext correlation, CancellationToken token) =>
        {
            SetPrivateNoStore(context.Response);
            var query = context.Request.Query;
            if (query.Keys.Any(key => !DuplicateCheckQueryKeys.Contains(key, StringComparer.Ordinal)) || query.Any(pair => pair.Value.Count != 1 && pair.Value.Count != 0)
                || !TryGuid(query["supplierPartyId"].ToString(), out var supplier) || !TryGuid(query["currencyId"].ToString(), out var currency)
                || !DateOnly.TryParseExact(query["invoiceDate"].ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return Problem(400, "validation_failed", correlation);
            Guid? ettn = null;
            if (query.ContainsKey("ettn"))
            {
                if (!TryGuid(query["ettn"].ToString(), out var parsed)) return Problem(400, "validation_failed", correlation);
                ettn = parsed;
            }

            try
            {
                return Results.Json(await service.CheckAsync(new PurchaseInvoiceDuplicateCheckRequestV1(supplier,
                    query["officialNumber"].ToString(), ettn, date, currency, query["payable"].ToString()), token), PurchasingJson.Options);
            }
            catch (PurchasingServiceException exception) { return Problem(context, exception, correlation); }
        }).WithName("CheckPurchaseIncomingInvoiceDuplicatesV1").Produces<PurchaseInvoiceDuplicateCheckResponseV1>());

        InvoiceWorkspaceRead(group.MapGet("/{id:guid}/duplicates", async (Guid id, HttpContext context,
            [FromServices] IPurchaseInvoiceDuplicateCheckV1 service, ICorrelationContext correlation, CancellationToken token) =>
        {
            SetPrivateNoStore(context.Response);
            if (context.Request.Query.Count != 0) return Problem(400, "validation_failed", correlation);
            try { return Results.Json(await service.CheckRecordedAsync(id, token), PurchasingJson.Options); }
            catch (PurchasingServiceException exception) { return Problem(context, exception, correlation); }
        }).WithName("GetPurchaseIncomingInvoiceDuplicatesV1").Produces<PurchaseInvoiceDuplicateCheckResponseV1>());
    }

    /// <summary>Canonical lowercase "D" form only, never the empty identity.</summary>
    private static bool TryGuid(string text, out Guid value) =>
        Guid.TryParseExact(text, "D", out value) && value != Guid.Empty && value.ToString("D", CultureInfo.InvariantCulture) == text;
}
