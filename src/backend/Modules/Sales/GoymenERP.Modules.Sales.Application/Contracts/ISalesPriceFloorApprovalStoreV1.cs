namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>Owned native floor-exception approval only; no new price or order approval policy.</summary>
public interface ISalesPriceFloorApprovalStoreV1
{
    bool PriceFloorApprovalEntryIsClean { get; }

    Task<SalesOrderDraftResponse> RunPriceFloorApprovalAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        bool cleanBeforeAuthority, Func<CancellationToken, Task<SalesOrderDraftResponse>> execute,
        Func<CancellationToken, Task<SalesOrderDraftResponse?>> readOriginalKey,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken);
}
