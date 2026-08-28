using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Application.Views;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

internal sealed class CounterpartyReadService(
    CounterpartyDbContext dbContext,
    ICurrentUser currentUser,
    IErpAuthorizationService authorizationService)
    : ICounterpartyReadService
{
    public async Task<IReadOnlyList<PartyCoreView>> GetAuthorizedPartiesAsync(
        Guid? companyId,
        CancellationToken cancellationToken = default)
    {
        var authorizedCompanyIds = await ResolveAuthorizedCompanyIdsAsync(
            companyId,
            cancellationToken);
        if (authorizedCompanyIds.Count == 0)
        {
            return [];
        }

        var companyIdArray = authorizedCompanyIds.ToArray();
        var accounts = await dbContext.CompanyPartyAccounts
            .AsNoTracking()
            .Where(account => companyIdArray.Contains(account.CompanyId))
            .OrderBy(account => account.CompanyId)
            .ThenBy(account => account.Id)
            .ToListAsync(cancellationToken);
        if (accounts.Count == 0)
        {
            return [];
        }

        var partyIds = accounts
            .Select(account => account.PartyId)
            .Distinct()
            .ToArray();
        var parties = await dbContext.Parties
            .AsNoTracking()
            .Where(party => partyIds.Contains(party.Id))
            .OrderBy(party => party.Code)
            .ToListAsync(cancellationToken);

        return parties
            .Select(party => PartyCoreViewComposer.ComposeAuthorized(
                party,
                accounts,
                authorizedCompanyIds))
            .OfType<PartyCoreView>()
            .ToArray();
    }

    public async Task<PartyCoreView?> GetAuthorizedPartyAsync(
        Guid partyId,
        Guid? companyId,
        CancellationToken cancellationToken = default)
    {
        if (partyId == Guid.Empty)
        {
            return null;
        }

        var authorizedCompanyIds = await ResolveAuthorizedCompanyIdsAsync(
            companyId,
            cancellationToken);
        if (authorizedCompanyIds.Count == 0)
        {
            return null;
        }

        var companyIdArray = authorizedCompanyIds.ToArray();
        var accounts = await dbContext.CompanyPartyAccounts
            .AsNoTracking()
            .Where(account =>
                account.PartyId == partyId
                && companyIdArray.Contains(account.CompanyId))
            .OrderBy(account => account.CompanyId)
            .ThenBy(account => account.Id)
            .ToListAsync(cancellationToken);
        if (accounts.Count == 0)
        {
            return null;
        }

        var party = await dbContext.Parties
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == partyId, cancellationToken);
        return party is null
            ? null
            : PartyCoreViewComposer.ComposeAuthorized(
                party,
                accounts,
                authorizedCompanyIds);
    }

    private async Task<HashSet<Guid>> ResolveAuthorizedCompanyIdsAsync(
        Guid? requestedCompanyId,
        CancellationToken cancellationToken)
    {
        IEnumerable<Guid> candidates = currentUser.AllowedCompanyIds;
        if (requestedCompanyId.HasValue)
        {
            if (requestedCompanyId.Value == Guid.Empty
                || !currentUser.CanAccessCompany(requestedCompanyId.Value))
            {
                return [];
            }

            candidates = [requestedCompanyId.Value];
        }

        var result = new HashSet<Guid>();
        foreach (var candidate in candidates.Distinct())
        {
            if (await authorizationService.HasPermissionAsync(
                    ErpPermissions.CounterpartyPartiesRead,
                    candidate,
                    cancellationToken))
            {
                result.Add(candidate);
            }
        }

        return result;
    }
}
