using System.Text.Json;
using GoymenERP.Modules.Sales.Domain.CustomerReturns;
using GoymenERP.Modules.Sales.Domain.Invoices;
using GoymenERP.Modules.Sales.Domain.Requests;
using GoymenERP.Modules.Sales.Domain.SalesContracts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

/// <summary>A Submitted sales request (born waiting for its approval; No 160, 161).</summary>
internal sealed record SalesWorkInboxRequestV1(
    Guid Id,
    Guid CompanyId,
    string DocumentNo,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// A sales invoice or a subcontract service invoice in PendingApproval: its preparer, its submitter and when it was submitted.
/// <see cref="OrderId"/>/<see cref="OrderDocumentNo"/> are the subcontract invoice's order (null for a sales invoice).
/// </summary>
internal sealed record SalesWorkInboxInvoiceV1(
    Guid Id,
    Guid CompanyId,
    string DocumentNo,
    string CreatedBy,
    string? SubmittedBy,
    DateTimeOffset? SubmittedAtUtc,
    Guid? OrderId = null,
    string? OrderDocumentNo = null);

/// <summary>A customer return in PendingApproval with its original invoice.</summary>
internal sealed record SalesWorkInboxCustomerReturnV1(
    Guid Id,
    Guid CompanyId,
    string CustomerDocumentNo,
    Guid OriginalInvoiceId,
    string OriginalDocumentNo,
    string CreatedBy,
    string? SubmittedBy,
    DateTimeOffset? SubmittedAtUtc);

/// <summary>A sales contract in PendingDiscountApproval with its discount rules and the limit snapshotted at recording.</summary>
internal sealed record SalesWorkInboxContractV1(
    Guid Id,
    Guid CompanyId,
    int ContractNo,
    string CreatedBy,
    DateTimeOffset CreatedAtUtc,
    decimal? DiscountLimitPercent,
    IReadOnlyList<SalesContractDiscountRule> DiscountRules);

/// <summary>
/// WP4: the read-only queries of the remaining Sales Work inbox sources over Sales's own tables. Every query filters
/// <c>CompanyId == companyId</c> explicitly beside the context's company query filter, tracks nothing and writes nothing, and
/// selects exactly the records the owner's decision command accepts (its state guard). Oldest first, then id;
/// <c>take</c> is the caller's page plus one.
/// </summary>
internal interface ISalesWorkInboxApprovalStoreV1
{
    Task<IReadOnlyList<SalesWorkInboxRequestV1>> ListSubmittedRequestsAsync(Guid companyId, int take,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SalesWorkInboxInvoiceV1>> ListPendingInvoicesAsync(Guid companyId, int take,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SalesWorkInboxInvoiceV1>> ListPendingSubcontractInvoicesAsync(Guid companyId, int take,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SalesWorkInboxCustomerReturnV1>> ListPendingCustomerReturnsAsync(Guid companyId, int take,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SalesWorkInboxContractV1>> ListPendingDiscountContractsAsync(Guid companyId, int take,
        CancellationToken cancellationToken);
}

internal sealed class SalesWorkInboxApprovalStoreV1(SalesDbContext dbContext) : ISalesWorkInboxApprovalStoreV1
{
    private const int RequestSubmitted = (int)SalesRequestState.Submitted;
    private const int InvoicePending = (int)SalesInvoiceState.PendingApproval;
    private const int SubcontractPending = (int)SalesSubcontractServiceInvoiceState.PendingApproval;
    private const int ReturnPending = (int)SalesCustomerReturnState.PendingApproval;
    private const int ContractPending = (int)SalesContractState.PendingDiscountApproval;

    /// <summary>The lifecycle envelope's own serializer settings (SalesStore's Web defaults).</summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<SalesWorkInboxRequestV1>> ListSubmittedRequestsAsync(Guid companyId, int take,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        return await dbContext.Requests.AsNoTracking()
            .Where(request => request.CompanyId == companyId && request.State == RequestSubmitted)
            .OrderBy(request => request.CreatedAtUtc).ThenBy(request => request.Id)
            .Select(request => new SalesWorkInboxRequestV1(request.Id, request.CompanyId, request.DocumentNo, request.CreatedAtUtc))
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SalesWorkInboxInvoiceV1>> ListPendingInvoicesAsync(Guid companyId, int take,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        return await dbContext.Invoices.AsNoTracking()
            .Where(invoice => invoice.CompanyId == companyId && invoice.State == InvoicePending)
            .OrderBy(invoice => invoice.SubmittedAtUtc).ThenBy(invoice => invoice.Id)
            .Select(invoice => new SalesWorkInboxInvoiceV1(invoice.Id, invoice.CompanyId, invoice.DocumentNo, invoice.CreatedBy,
                invoice.SubmittedBy, invoice.SubmittedAtUtc, null, null))
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SalesWorkInboxInvoiceV1>> ListPendingSubcontractInvoicesAsync(Guid companyId, int take,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);

        // The submitter and the submission time live in the newest lifecycle envelope (the header holds the state only). While
        // PendingApproval the newest envelope is the submission (approve/reject/cancel leave the state), so its time is the
        // submission time; the order is read for its number and the link (the lifecycle panel is on the order screen).
        var rows = await (
                from invoice in dbContext.SubcontractServiceInvoices.AsNoTracking()
                where invoice.CompanyId == companyId && invoice.State == SubcontractPending
                join lifecycle in dbContext.SubcontractInvoiceLifecycles.AsNoTracking()
                    on new { invoice.CompanyId, InvoiceId = invoice.Id } equals new { lifecycle.CompanyId, lifecycle.InvoiceId }
                where lifecycle.CompanyId == companyId
                    && lifecycle.Sequence == dbContext.SubcontractInvoiceLifecycles
                        .Where(other => other.CompanyId == companyId && other.InvoiceId == invoice.Id)
                        .Max(other => other.Sequence)
                join order in dbContext.Drafts.AsNoTracking()
                    on new { invoice.CompanyId, Id = invoice.SalesOrderId } equals new { order.CompanyId, order.Id }
                where order.CompanyId == companyId
                orderby lifecycle.RecordedAtUtc, invoice.Id
                select new
                {
                    invoice.Id,
                    invoice.CompanyId,
                    invoice.DocumentNo,
                    invoice.CreatedBy,
                    invoice.Version,
                    invoice.State,
                    LifecycleVersion = lifecycle.InvoiceVersion,
                    LifecycleState = lifecycle.State,
                    lifecycle.FactsJson,
                    lifecycle.RecordedAtUtc,
                    invoice.SalesOrderId,
                    OrderDocumentNo = order.DocumentNo,
                })
            .Take(take)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row =>
        {
            if (row.LifecycleVersion != row.Version || row.LifecycleState != row.State)
            {
                throw new InvalidOperationException("Fason faturanın yaşam döngüsü sürümü başlıkla uyuşmuyor.");
            }

            var facts = JsonSerializer.Deserialize<SalesSubcontractInvoiceLifecycleFacts>(row.FactsJson, Json)
                ?? throw new InvalidOperationException("Fason fatura yaşam döngüsü okunamadı.");
            return new SalesWorkInboxInvoiceV1(row.Id, row.CompanyId, row.DocumentNo, row.CreatedBy,
                facts.Decision.SubmittedBy, facts.Decision.SubmittedAtUtc ?? row.RecordedAtUtc, row.SalesOrderId,
                row.OrderDocumentNo);
        })];
    }

    public async Task<IReadOnlyList<SalesWorkInboxCustomerReturnV1>> ListPendingCustomerReturnsAsync(Guid companyId, int take,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        return await dbContext.CustomerReturns.AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.State == ReturnPending)
            .OrderBy(item => item.SubmittedAtUtc).ThenBy(item => item.Id)
            .Select(item => new SalesWorkInboxCustomerReturnV1(item.Id, item.CompanyId, item.CustomerDocumentNo,
                item.OriginalInvoiceId, item.OriginalDocumentNo, item.CreatedBy, item.SubmittedBy, item.SubmittedAtUtc))
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SalesWorkInboxContractV1>> ListPendingDiscountContractsAsync(Guid companyId, int take,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        var rows = await dbContext.Contracts.AsNoTracking()
            .Where(contract => contract.CompanyId == companyId && contract.State == ContractPending)
            .OrderBy(contract => contract.CreatedAtUtc).ThenBy(contract => contract.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

        // The owner's own mapping restores the rules and checks the stored facts (a broken row is an internal error).
        return [.. rows.Select(SalesContractRows.Restore).Select(contract => new SalesWorkInboxContractV1(contract.Id,
            contract.CompanyId, contract.ContractNo, contract.CreatedBy, contract.CreatedAtUtc, contract.DiscountLimitPercent,
            contract.DiscountRules))];
    }
}
