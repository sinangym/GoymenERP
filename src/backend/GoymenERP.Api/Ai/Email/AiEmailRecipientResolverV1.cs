using GoymenERP.Modules.Counterparty.Application.Contracts;

namespace GoymenERP.Api.Ai.Email;

/// <summary>Only the company-scoped contact owner may establish registered recipients. No model/address lookup.</summary>
public sealed class AiEmailRecipientResolverV1(ICounterpartyContactInformationService contacts, IAiEmailAuthorityV1 authority)
{
    public async Task<bool> MatchesAsync(AiEmailScopeV1 scope, IReadOnlyList<AiEmailRecipientV1> recipients,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (recipients.Count == 0 || recipients.Distinct().Count() != recipients.Count) return false;
        foreach (var recipient in recipients)
        {
            if (!await authority.IsCurrentAsync(scope, cancellationToken)) return false;
            cancellationToken.ThrowIfCancellationRequested();
            var view = await contacts.GetContactInformationAsync(recipient.PartyId, cancellationToken);
            if (!await authority.IsCurrentAsync(scope, cancellationToken)) return false;
            cancellationToken.ThrowIfCancellationRequested();
            if (view is null || !view.IsActive || view.CompanyId != scope.CompanyId
                || view.PartyId != recipient.PartyId || view.AccountId != recipient.AccountId
                || view.Version != recipient.AccountVersion) return false;
            // This first slice admits only the owner's explicit company-account email, not contact-channel inference.
            if (string.IsNullOrWhiteSpace(recipient.Address)
                || !string.Equals(view.Information.Email, recipient.Address, StringComparison.Ordinal)) return false;
        }
        return true;
    }
}
