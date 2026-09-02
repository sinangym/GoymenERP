namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

internal readonly record struct CounterpartyActiveStateCommandObservation(
    Guid CompanyId,
    Guid PartyId,
    long ExpectedVersion,
    long LoadedVersion,
    string CorrelationId,
    string IdempotencyKeyHash);

internal interface ICounterpartyActiveStateCommandProbe
{
    ValueTask AfterExpectedVersionValidatedAsync(
        CounterpartyActiveStateCommandObservation observation,
        CancellationToken cancellationToken);
}

internal sealed class NullCounterpartyActiveStateCommandProbe
    : ICounterpartyActiveStateCommandProbe
{
    public ValueTask AfterExpectedVersionValidatedAsync(
        CounterpartyActiveStateCommandObservation observation,
        CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
