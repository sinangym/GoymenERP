namespace GoymenERP.Modules.Counterparty.Application.Contracts;

public interface ICounterpartyReadService
{
    Task<IReadOnlyList<PartyCoreView>> GetAuthorizedPartiesAsync(
        Guid? companyId,
        CancellationToken cancellationToken = default);

    Task<PartyCoreView?> GetAuthorizedPartyAsync(
        Guid partyId,
        Guid? companyId,
        CancellationToken cancellationToken = default);
}
