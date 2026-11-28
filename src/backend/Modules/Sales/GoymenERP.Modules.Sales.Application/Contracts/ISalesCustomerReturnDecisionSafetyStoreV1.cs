namespace GoymenERP.Modules.Sales.Application.Contracts;

// The native customer-return command owns its unchanged mutation; the store owns only its atomic execution boundary.
public interface ISalesCustomerReturnDecisionSafetyStoreV1
{
    bool CustomerReturnDecisionEntryIsClean { get; }
    Task<SalesCustomerReturnCommandResult> RunCustomerReturnDecisionAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        string commandName, bool cleanBeforeAuthority,
        Func<CancellationToken, Task<SalesCustomerReturnCommandResult>> execute,
        Func<CancellationToken, Task<SalesCustomerReturnCommandResult?>> readOriginalKey,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken);
}
