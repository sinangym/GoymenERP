namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>One native Approve/Reject remainder decision. The request and every other native command are outside this boundary.</summary>
public interface ISalesRemainderDecisionSafetyStoreV1
{
    bool RemainderDecisionEntryIsClean { get; }
    Task<SalesOrderDraftResponse> RunRemainderDecisionAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
        bool cleanBeforeAuthority, Func<CancellationToken, Task<SalesOrderDraftResponse>> execute,
        Func<CancellationToken, Task<SalesOrderDraftResponse?>> readOriginalKey,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken);
}
