namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

internal interface ICounterpartyReadSqlObserver
{
    void Observe(CounterpartyReadSqlOperation operation, string commandText);
}

internal enum CounterpartyReadSqlOperation
{
    Page = 1,
    Accounts = 2,
    Count = 3,
    DetailAccounts = 4,
}

internal sealed class NullCounterpartyReadSqlObserver : ICounterpartyReadSqlObserver
{
    public void Observe(CounterpartyReadSqlOperation operation, string commandText)
    {
    }
}
