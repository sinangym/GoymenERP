using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

internal sealed record SalesWorkInboxInvoiceDecisionV1(SalesWorkInboxInvoiceV1 Invoice, long Version, bool Pending);

internal sealed partial class SalesWorkInboxApprovalStoreV1
{
    /// <summary>Owner metadata only, under the decision source's approve + price-read fence. Completed records remain readable for native receipt replay.</summary>
    public async Task<SalesWorkInboxInvoiceDecisionV1?> FindInvoiceDecisionAsync(Guid companyId, Guid invoiceId, CancellationToken cancellationToken)
    {
        var row = await dbContext.Invoices.AsNoTracking().SingleOrDefaultAsync(invoice => invoice.CompanyId == companyId && invoice.Id == invoiceId,
            cancellationToken);
        return row is null || row.SubmittedAtUtc is null || string.IsNullOrWhiteSpace(row.SubmittedBy) ? null
            : new(new(row.Id, row.CompanyId, row.DocumentNo, row.CreatedBy, row.SubmittedBy, row.SubmittedAtUtc), row.Version, row.State == InvoicePending);
    }
}
