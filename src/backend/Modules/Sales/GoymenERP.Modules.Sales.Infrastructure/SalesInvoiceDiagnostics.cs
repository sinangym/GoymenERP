using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.Extensions.Logging;

namespace GoymenERP.Modules.Sales.Infrastructure;

/// <summary>Logs a Finance hand-off failure of the finalisation (identifiers and the exception only; the commit stands).</summary>
internal sealed class SalesInvoiceDiagnostics(ILogger<SalesInvoiceDiagnostics>? logger) : ISalesInvoiceDiagnosticsV1
{
    public void FinanceHandOffFailed(Guid companyId, Guid invoiceId, string stage, Exception exception) => logger?.LogWarning(exception,
        "Sales invoice Finance hand-off failed at {Stage} for invoice {InvoiceId} of company {CompanyId}; the finalisation stands and the invoice waits for Finance.",
        stage, invoiceId, companyId);
}
