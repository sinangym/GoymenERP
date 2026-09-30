using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Documents.Application.Contracts;

namespace GoymenERP.Api.DocumentsRuntime;

/// <summary>
/// AT-065's first owner link: the Cari party. The Cari owner answers through its OWN authorized detail read
/// (<see cref="ICounterpartyReadService.GetAuthorizedPartyAsync"/>) — the same read and the same
/// <c>counterparty.parties.read</c> measurement (company-wide, in the context company) the C-03 card uses — whether the
/// party holds an account in the CONTEXT company and the actor may read it. The vault reads no Cari table. The host
/// adapts because the owner's read binds the request's actor and company, which only the host resolves (the Logistics
/// party-port shape). A context that changed while the owner answered releases nothing.
/// </summary>
public sealed class CounterpartyDocumentOwnerLinkPort(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    ICounterpartyReadService counterparties) : IDocumentOwnerLinkPortV1
{
    public string Kind => DocumentOwnerLinkKindsV1.CounterpartyParty;

    public async Task<DocumentOwnerAccessV1> CheckReadableAsync(Guid ownerId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ownerId == Guid.Empty || !IsCurrent())
        {
            return DocumentOwnerAccessV1.Denied;
        }

        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId;
        CounterpartyDetailView? party;
        try
        {
            party = await counterparties.GetAuthorizedPartyAsync(ownerId, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return DocumentOwnerAccessV1.Denied;
        }

        if (!IsCurrent() || companyContext.CompanyId != companyId || !string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal))
        {
            return DocumentOwnerAccessV1.Denied;
        }

        // The owner answered for the context company only; a party without an account HERE is not this company's record.
        return party is not null && party.Id == ownerId && party.CompanyAccounts.Any(account => account.CompanyId == companyId)
            ? DocumentOwnerAccessV1.Readable
            : DocumentOwnerAccessV1.NotFound;
    }

    private bool IsCurrent() =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId) && companyContext.IsResolved
        && companyContext.CompanyId != Guid.Empty && currentUser.CanAccessCompany(companyContext.CompanyId);
}
