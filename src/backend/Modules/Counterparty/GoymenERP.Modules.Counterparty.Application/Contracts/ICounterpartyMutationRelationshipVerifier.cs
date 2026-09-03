namespace GoymenERP.Modules.Counterparty.Application.Contracts;

/// <summary>
/// Verifies the immutable account-to-party relationship before a mutation
/// response crosses the HTTP boundary. This contract intentionally does not
/// apply read permissions; the endpoint has already enforced the mutation
/// policies and needs an integrity check against authoritative persistence.
/// </summary>
public interface ICounterpartyMutationRelationshipVerifier
{
    Task<bool> IsAccountOwnedByPartyAsync(
        Guid partyId,
        Guid accountId,
        Guid companyId,
        CancellationToken cancellationToken);
}
