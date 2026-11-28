namespace GoymenERP.Modules.Sales.Application.Contracts;

// Only the existing numbered over-delivery request decisions; missing composition stays closed.
public interface ISalesOverDeliveryRequestApprovalStoreV1
{
    bool OverDeliveryRequestApprovalEntryIsClean { get; }
    Task<SalesApprovalRequestCommandResult> RunOverDeliveryRequestApprovalAsync(SalesDraftWriteEnvelope envelope,
        string requestHash, string commandName, bool cleanBeforeAuthority,
        Func<CancellationToken, Task<SalesApprovalRequestCommandResult>> execute,
        Func<CancellationToken, Task<SalesApprovalRequestCommandResult?>> readOriginalKey,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken);
}
