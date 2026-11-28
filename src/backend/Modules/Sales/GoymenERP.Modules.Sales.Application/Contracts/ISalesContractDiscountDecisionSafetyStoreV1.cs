namespace GoymenERP.Modules.Sales.Application.Contracts;

// The native contract command owns its unchanged mutation; the store owns only its atomic execution boundary.
public interface ISalesContractDiscountDecisionSafetyStoreV1
{
    bool ContractDiscountDecisionEntryIsClean { get; }
    Task<SalesContractCommandResult> RunContractDiscountDecisionAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        string commandName, bool cleanBeforeAuthority,
        Func<CancellationToken, Task<SalesContractCommandResult>> execute,
        Func<CancellationToken, Task<SalesContractCommandResult?>> readOriginalKey,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken);
}
