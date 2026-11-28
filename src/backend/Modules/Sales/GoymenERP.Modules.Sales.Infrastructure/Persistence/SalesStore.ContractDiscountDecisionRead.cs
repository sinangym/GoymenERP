using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

public sealed partial class SalesStore : ISalesContractDiscountDecisionReadStoreV1
{
    private Func<CancellationToken, Task>? contractDiscountDecisionReadFence;
    private async Task ContractDiscountDecisionSourceFenceAsync(CancellationToken token)
    {
        if (contractDiscountDecisionReadFence is not { } fence) return;
        token.ThrowIfCancellationRequested(); var company = RequireResolvedCompany(); await fence(token);
        token.ThrowIfCancellationRequested(); RequireCompany(company);
    }
    private async Task<T> WithContractDiscountDecisionReadAsync<T>(Func<Task<T>> read, Func<CancellationToken, Task> freshFence,
        CancellationToken token, bool allowOwnedContractReceipt = false)
    {
        ArgumentNullException.ThrowIfNull(freshFence); token.ThrowIfCancellationRequested(); var company = RequireResolvedCompany();
        if (contractDiscountDecisionReadFence is not null || riskExceptionDecisionReadFence is not null || quoteDecisionReadFence is not null
            || overDeliveryRequestDecisionReadFence is not null || ownedRequestApproval is not null || ownedQuoteApproval is not null
            || ownedPriceFloorApproval is not null || ownedDiscountApproval is not null || ownedOverDeliveryRequestApproval is not null
            || ownedRiskExceptionDecision is not null || ownedRemainderDecision is not null
            || ownedContractDiscountDecision is not null && !allowOwnedContractReceipt)
            throw new InvalidOperationException("Sözleşme karar okuması başka bir owned/read işlemin içine giremez.");
        contractDiscountDecisionReadFence = Fence;
        try { return await read(); }
        finally { contractDiscountDecisionReadFence = null; }
        async Task Fence(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); RequireCompany(company); await freshFence(ct);
            ct.ThrowIfCancellationRequested(); RequireCompany(company);
        }
    }
    public Task<SalesContractDiscountDecisionSourceV1?> ReadContractDiscountDecisionSourceAsync(Guid contractId,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default) =>
        WithContractDiscountDecisionReadAsync(() => ReadContractDiscountDecisionSourceCoreAsync(contractId, cancellationToken), freshFence, cancellationToken);

    private async Task<SalesContractDiscountDecisionSourceV1?> ReadContractDiscountDecisionSourceCoreAsync(Guid contractId, CancellationToken token)
    {
        var company = RequireResolvedCompany(); await ContractDiscountDecisionSourceFenceAsync(token);
        var rows = await dbContext.Contracts.AsNoTracking().Where(x => x.CompanyId == company && x.Id == contractId).Take(2).ToArrayAsync(token);
        await ContractDiscountDecisionSourceFenceAsync(token);
        if (rows.Length == 0) return null;
        if (rows.Length != 1) throw new InvalidOperationException("Seçili sözleşme olguları belirsiz.");
        return new(company, SalesContractRows.Restore(rows[0]));
    }
}
