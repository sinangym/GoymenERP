using GoymenERP.Modules.Purchasing.Application.Contracts;
using Microsoft.Extensions.Logging;

namespace GoymenERP.Modules.Purchasing.Infrastructure;

internal sealed class PurchaseIncomingInvoiceDiagnostics(ILogger<PurchaseIncomingInvoiceDiagnostics> logger)
    : IPurchaseIncomingInvoiceDiagnosticsV1
{
    public void FinanceHandoffFailed(Guid companyId, Guid invoiceId, string phase, Exception exception) =>
        logger.LogWarning("Purchasing invoice Finance handoff failed in {Phase} for invoice {InvoiceId} of company {CompanyId} ({ErrorType}); the invoice remains pending.",
            phase, invoiceId, companyId, exception.GetType().Name);
}
