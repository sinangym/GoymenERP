using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Api.ProductRuntime;

/// <summary>
/// MOD-02: the Cari owner's answer for a customer-specific price list (<see cref="IProductPriceListCustomerPortV1"/>), over the
/// authorized Counterparty party read — the <c>CounterpartySalesPartyAccountPortV1</c> pattern: the party, the account of
/// that party in the server company, and both active flags. Denied when the server context is not the request's or the owner
/// refuses the read; NotFound when the party or the account is not the owner's; Unavailable when the Counterparty module is
/// not composed in this host (fail-closed). Product stores the two identities only.
/// </summary>
public sealed class CounterpartyProductPriceListCustomerPortV1(IServiceProvider services, ICompanyContext companyContext,
    ICurrentUser currentUser) : IProductPriceListCustomerPortV1
{
    public async Task<ProductPriceListCustomerAnswerV1> GetAuthorizedAsync(Guid partyId, Guid companyAccountId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrent() || partyId == Guid.Empty || companyAccountId == Guid.Empty)
        {
            return new(ProductPriceListCustomerOutcomeV1.Denied, null);
        }

        if (services.GetService<ICounterpartyReadService>() is not { } counterparties)
        {
            return new(ProductPriceListCustomerOutcomeV1.Unavailable, null);
        }

        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId;
        CounterpartyDetailView? party;
        try
        {
            party = await counterparties.GetAuthorizedPartyAsync(partyId, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return new(ProductPriceListCustomerOutcomeV1.Denied, null);
        }

        // The owner answered for the context captured before the await — the company AND the actor; a changed context releases nothing.
        if (!IsCurrent() || companyContext.CompanyId != companyId || !string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal))
        {
            return new(ProductPriceListCustomerOutcomeV1.Denied, null);
        }

        if (party is null || party.Id != partyId)
        {
            return new(ProductPriceListCustomerOutcomeV1.NotFound, null);
        }

        var accounts = party.CompanyAccounts.Where(account => account.Id == companyAccountId && account.CompanyId == companyId).ToArray();
        if (accounts.Length != 1)
        {
            return new(ProductPriceListCustomerOutcomeV1.NotFound, null);
        }

        var account = accounts[0];
        return new(ProductPriceListCustomerOutcomeV1.Found,
            new ProductPriceListCustomerV1(party.Id, account.Id, account.CompanyId, party.IsActive, account.IsActive));
    }

    private bool IsCurrent() =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId) && companyContext.IsResolved
        && companyContext.CompanyId != Guid.Empty && currentUser.CanAccessCompany(companyContext.CompanyId);
}
