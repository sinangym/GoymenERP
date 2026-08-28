namespace GoymenERP.Modules.Counterparty.Application.Contracts;

public interface ICounterpartyService
{
    Task<PartyPage> GetPartiesAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<PartyCoreView?> GetPartyAsync(
        Guid partyId,
        CancellationToken cancellationToken);

    Task<CreatePartyExecutionResult> CreatePartyAsync(
        CreatePartyCommand command,
        CounterpartyCommandMetadata metadata,
        CancellationToken cancellationToken);
}
