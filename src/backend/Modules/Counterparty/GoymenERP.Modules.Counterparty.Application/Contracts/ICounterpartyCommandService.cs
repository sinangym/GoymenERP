namespace GoymenERP.Modules.Counterparty.Application.Contracts;

public interface ICounterpartyCommandService
{
    Task<CounterpartyCommandExecutionResult> CreateAsync(
        CreateCounterpartyCommand command,
        CounterpartyCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<CounterpartyCommandExecutionResult> SetRolesAsync(
        Guid accountId,
        SetCompanyPartyAccountRolesCommand command,
        CounterpartyCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<CounterpartyCommandExecutionResult> SetExternalCodeAsync(
        Guid accountId,
        SetCompanyPartyAccountExternalCodeCommand command,
        CounterpartyCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<CounterpartyCommandExecutionResult> SetActiveStateAsync(
        Guid accountId,
        SetCompanyPartyAccountActiveStateCommand command,
        CounterpartyCommandMetadata metadata,
        CancellationToken cancellationToken = default);
}

public interface IPartyCodeAllocator
{
    Task<long> NextAsync(CancellationToken cancellationToken = default);
}
