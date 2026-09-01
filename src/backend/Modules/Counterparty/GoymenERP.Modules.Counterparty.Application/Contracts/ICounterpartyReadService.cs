namespace GoymenERP.Modules.Counterparty.Application.Contracts;

public interface ICounterpartyReadService
{
    Task<CounterpartyPageExecution> GetAuthorizedPageAsync(
        CounterpartyReadRequest request,
        CancellationToken cancellationToken = default);

    Task<CounterpartyDetailView?> GetAuthorizedPartyAsync(
        Guid partyId,
        Guid? companyId,
        CancellationToken cancellationToken = default);
}
