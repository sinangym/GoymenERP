using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using Microsoft.Extensions.Logging;

namespace GoymenERP.Modules.Purchasing.Infrastructure;

internal sealed class PurchaseIncomingInvoiceDiagnostics(ILogger<PurchaseIncomingInvoiceDiagnostics> logger)
    : IPurchaseIncomingInvoiceDiagnosticsV1
{
    public void FinanceHandoffFailed(Guid companyId, Guid invoiceId, string phase, Exception exception) =>
        logger.LogWarning("Purchasing invoice Finance handoff failed in {Phase} for invoice {InvoiceId} of company {CompanyId} ({ErrorType}); the invoice remains pending.",
            phase, invoiceId, companyId, SafeCategory(exception));
    // Preserve safe diagnostic categories without reflecting over arbitrary provider exception types.
    // Unknown subclasses deliberately disclose only Exception, never their message or stack.
    private static string SafeCategory(Exception exception) => exception switch
    {
        PurchasingServiceException => nameof(PurchasingServiceException),
        DbUpdateConcurrencyException => nameof(DbUpdateConcurrencyException),
        DbUpdateException => nameof(DbUpdateException),
        JsonException => nameof(JsonException),
        ArgumentException => nameof(ArgumentException),
        InvalidOperationException => nameof(InvalidOperationException),
        TimeoutException => nameof(TimeoutException),
        OperationCanceledException => nameof(OperationCanceledException),
        _ => nameof(Exception)
    };
}
