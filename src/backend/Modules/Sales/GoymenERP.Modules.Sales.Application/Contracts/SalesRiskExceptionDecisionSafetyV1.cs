namespace GoymenERP.Modules.Sales.Application.Contracts;

// Only the two native risk decisions; composition is enabled after the shared read-fence relay is connected.
public interface ISalesRiskExceptionDecisionSafetyStoreV1
{
    bool RiskExceptionDecisionEntryIsClean { get; }
    Task<SalesRiskExceptionCommandResult> RunRiskExceptionDecisionAsync(SalesDraftWriteEnvelope envelope,
        string requestHash, string commandName, bool cleanBeforeAuthority,
        Func<CancellationToken, Task<SalesRiskExceptionCommandResult>> execute,
        Func<CancellationToken, Task<SalesRiskExceptionCommandResult?>> readOriginalKey,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken);
}
