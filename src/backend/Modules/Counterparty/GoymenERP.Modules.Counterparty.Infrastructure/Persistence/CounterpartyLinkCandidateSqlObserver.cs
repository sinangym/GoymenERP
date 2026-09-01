namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

internal interface ICounterpartyLinkCandidateSqlObserver
{
    void Observe(CounterpartyLinkCandidateSqlOperation operation, string commandText);
}

internal enum CounterpartyLinkCandidateSqlOperation
{
    Page = 1,
    SourceAccounts = 2,
}

internal sealed class NullCounterpartyLinkCandidateSqlObserver
    : ICounterpartyLinkCandidateSqlObserver
{
    public void Observe(
        CounterpartyLinkCandidateSqlOperation operation,
        string commandText)
    {
    }
}
