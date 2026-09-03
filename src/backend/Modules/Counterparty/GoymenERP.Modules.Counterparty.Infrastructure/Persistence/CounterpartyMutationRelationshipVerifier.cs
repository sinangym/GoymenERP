using GoymenERP.Modules.Counterparty.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

internal sealed class CounterpartyMutationRelationshipVerifier(
    CounterpartyDbContext dbContext)
    : ICounterpartyMutationRelationshipVerifier
{
    public Task<bool> IsAccountOwnedByPartyAsync(
        Guid partyId,
        Guid accountId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        if (partyId == Guid.Empty || accountId == Guid.Empty || companyId == Guid.Empty)
        {
            return Task.FromResult(false);
        }

        return dbContext.CompanyPartyAccounts
            .AsNoTracking()
            .AnyAsync(
                account => account.Id == accountId
                    && account.PartyId == partyId
                    && account.CompanyId == companyId,
                cancellationToken);
    }
}
