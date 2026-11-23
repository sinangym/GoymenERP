using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

namespace GoymenERP.Sales.UnitTests;

/// <summary>WP4: the fake owner store of the remaining Sales Work inbox sources (SENTETIK rows only).</summary>
internal sealed class SalesWorkInboxApprovalFakeStore : ISalesWorkInboxApprovalStoreV1
{
    public IReadOnlyList<SalesWorkInboxRequestV1> Requests { get; set; } = [];
    public IReadOnlyList<SalesWorkInboxInvoiceV1> Invoices { get; set; } = [];
    public IReadOnlyList<SalesWorkInboxInvoiceV1> SubcontractInvoices { get; set; } = [];
    public IReadOnlyList<SalesWorkInboxCustomerReturnV1> Returns { get; set; } = [];
    public IReadOnlyList<SalesWorkInboxContractV1> Contracts { get; set; } = [];
    public Exception? Failure { get; set; }
    public List<(string Query, Guid CompanyId, int Take)> Calls { get; } = [];

    public Task<IReadOnlyList<SalesWorkInboxRequestV1>> ListSubmittedRequestsAsync(Guid companyId, int take,
        CancellationToken cancellationToken) => Answer(nameof(ListSubmittedRequestsAsync), companyId, take, Requests);

    public Task<IReadOnlyList<SalesWorkInboxInvoiceV1>> ListPendingInvoicesAsync(Guid companyId, int take,
        CancellationToken cancellationToken) => Answer(nameof(ListPendingInvoicesAsync), companyId, take, Invoices);

    public Task<IReadOnlyList<SalesWorkInboxInvoiceV1>> ListPendingSubcontractInvoicesAsync(Guid companyId, int take,
        CancellationToken cancellationToken) =>
        Answer(nameof(ListPendingSubcontractInvoicesAsync), companyId, take, SubcontractInvoices);

    public Task<IReadOnlyList<SalesWorkInboxCustomerReturnV1>> ListPendingCustomerReturnsAsync(Guid companyId, int take,
        CancellationToken cancellationToken) => Answer(nameof(ListPendingCustomerReturnsAsync), companyId, take, Returns);

    public Task<IReadOnlyList<SalesWorkInboxContractV1>> ListPendingDiscountContractsAsync(Guid companyId, int take,
        CancellationToken cancellationToken) => Answer(nameof(ListPendingDiscountContractsAsync), companyId, take, Contracts);

    private Task<IReadOnlyList<T>> Answer<T>(string query, Guid companyId, int take, IReadOnlyList<T> rows)
    {
        Calls.Add((query, companyId, take));
        return Failure is not null
            ? Task.FromException<IReadOnlyList<T>>(Failure)
            : Task.FromResult<IReadOnlyList<T>>([.. rows.Take(take)]);
    }
}
