namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>Owned native discount-exception approval only; no new price or order approval policy.</summary>
public interface ISalesDiscountApprovalStoreV1
{
    bool DiscountApprovalEntryIsClean { get; }

    Task<SalesOrderDraftResponse> RunDiscountApprovalAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        bool cleanBeforeAuthority, Func<CancellationToken, Task<SalesOrderDraftResponse>> execute,
        Func<CancellationToken, Task<SalesOrderDraftResponse?>> readOriginalKey,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken);
}
