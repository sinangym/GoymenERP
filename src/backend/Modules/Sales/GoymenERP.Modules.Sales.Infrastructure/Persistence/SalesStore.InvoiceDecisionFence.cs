using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Invoices;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

public sealed partial class SalesStore
{
    public async Task<SalesInvoiceStoreResult?> DecideInvoiceGuardedAsync(SalesDraftWriteEnvelope envelope,
        string requestHash, string commandName, Guid invoiceId, Action<SalesInvoice, DateTimeOffset> mutate,
        SalesDraftStateAudit audit, Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshFence);
        if (System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("An invoice decision requires its own Sales transaction.");
        var companyId = RequireResolvedCompany();
        async Task Fence(CancellationToken token)
        {
            RequireCompany(companyId);
            await freshFence(token);
            RequireCompany(companyId);
        }
        await Fence(cancellationToken);
        // Reuse the existing clean, serializable Sales owner transaction and its rollback/concurrency handling.
        return await InSubcontractTransactionAsync(async () =>
        {
            var result = await DecideInvoiceCoreAsync(envelope, requestHash, commandName, invoiceId, mutate, audit, Fence, cancellationToken);
            await Fence(cancellationToken);
            return result;
        }, cancellationToken);
    }
}
