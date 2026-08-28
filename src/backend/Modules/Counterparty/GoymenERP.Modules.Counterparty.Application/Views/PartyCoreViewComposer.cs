using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;

namespace GoymenERP.Modules.Counterparty.Application.Views;

public static class PartyCoreViewComposer
{
    public static PartyCoreView? ComposeAuthorized(
        Party party,
        IEnumerable<CompanyPartyAccount> accounts,
        IReadOnlySet<Guid> authorizedCompanyIds)
    {
        ArgumentNullException.ThrowIfNull(party);
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(authorizedCompanyIds);

        var visibleAccounts = accounts
            .Where(account =>
                account.PartyId == party.Id
                && authorizedCompanyIds.Contains(account.CompanyId))
            .OrderBy(account => account.CompanyId)
            .ThenBy(account => account.Id)
            .Select(account => new CompanyPartyAccountView(
                account.Id,
                account.CompanyId,
                account.Roles,
                account.ExternalIntegrationCode,
                account.IsActive,
                account.Version))
            .ToArray();

        if (visibleAccounts.Length == 0)
        {
            return null;
        }

        return new PartyCoreView(
            party.Id,
            party.Code.Value,
            party.Kind,
            party.LegalName,
            party.TradeName,
            party.IsActive,
            party.Version,
            visibleAccounts);
    }
}
