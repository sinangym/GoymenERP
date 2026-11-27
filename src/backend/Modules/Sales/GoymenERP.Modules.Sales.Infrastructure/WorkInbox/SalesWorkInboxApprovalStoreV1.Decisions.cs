using System.Text.Json;
using GoymenERP.Modules.Sales.Domain.Invoices;
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
    /// <summary>Only immutable maker/submission metadata and the current owner version; no browse grant or price payload.</summary>
    public async Task<SalesWorkInboxInvoiceDecisionV1?> FindSubcontractInvoiceDecisionAsync(Guid companyId, Guid invoiceId, CancellationToken cancellationToken)
    {
        var row = await (
            from invoice in dbContext.SubcontractServiceInvoices.AsNoTracking()
            where invoice.CompanyId == companyId && invoice.Id == invoiceId
            join lifecycle in dbContext.SubcontractInvoiceLifecycles.AsNoTracking()
                on new { invoice.CompanyId, InvoiceId = invoice.Id } equals new { lifecycle.CompanyId, lifecycle.InvoiceId }
            where lifecycle.CompanyId == companyId && lifecycle.Sequence == dbContext.SubcontractInvoiceLifecycles
                .Where(other => other.CompanyId == companyId && other.InvoiceId == invoice.Id).Max(other => other.Sequence)
            join order in dbContext.Drafts.AsNoTracking()
                on new { invoice.CompanyId, Id = invoice.SalesOrderId } equals new { order.CompanyId, order.Id }
            where order.CompanyId == companyId
            select new { invoice.Id, invoice.CompanyId, invoice.DocumentNo, invoice.CreatedBy, invoice.Version, invoice.State,
                invoice.SalesOrderId, OrderDocumentNo = order.DocumentNo, lifecycle.FactsJson,
                LifecycleVersion = lifecycle.InvoiceVersion, LifecycleState = lifecycle.State })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null) return null;
        if (row.Version < 1 || !Enum.IsDefined((SalesSubcontractServiceInvoiceState)row.State)
            || row.LifecycleVersion != row.Version || row.LifecycleState != row.State)
            throw new InvalidOperationException("Fason fatura karar sürümü yaşam döngüsüyle uyuşmuyor.");
        var facts = JsonSerializer.Deserialize<SalesSubcontractInvoiceLifecycleFacts>(row.FactsJson, Json)
            ?? throw new InvalidOperationException("Fason fatura karar kanıtı okunamadı.");
        if (facts.Decision is null || string.IsNullOrWhiteSpace(facts.Decision.SubmittedBy) || facts.Decision.SubmittedAtUtc is null) return null;
        return new(new(row.Id, row.CompanyId, row.DocumentNo, row.CreatedBy, facts.Decision.SubmittedBy,
            facts.Decision.SubmittedAtUtc, row.SalesOrderId, row.OrderDocumentNo), row.Version, row.State == SubcontractPending);
    }

}
