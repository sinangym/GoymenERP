using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Api.ProductRuntime;

/// <summary>
/// SAT-D382 (STK-SC36-c): the Cari owner's answer for a package standard override's customer
/// (<see cref="IProductPackageStandardCustomerPortV3"/>), over the authorized, company-scoped Counterparty display read (missing,
/// foreign and inactive parties are omitted alike). Denied when the server context is not current or changed during the read;
/// Unavailable when the Counterparty module is not composed in this host (fail-closed). Product stores the party identity only.
/// </summary>
public sealed class CounterpartyProductPackageStandardCustomerPortV3(IServiceProvider services, ICompanyContext companyContext,
    ICurrentUser currentUser) : IProductPackageStandardCustomerPortV3
{
    public async Task<ProductPackageStandardCustomerAnswerV3> ReadAsync(IReadOnlyList<Guid> customerIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customerIds);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrent())
        {
            return new(ProductPackageStandardCustomerOutcomeV3.Denied, []);
        }

        if (services.GetService<ICounterpartyPartyDisplayBatchReadServiceV1>() is not { } parties)
        {
            return new(ProductPackageStandardCustomerOutcomeV3.Unavailable, []);
        }

        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId!;
        var asked = customerIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        var answer = await parties.ReadAuthorizedAsync(
            new CounterpartyPartyDisplayBatchReadRequestV1(companyId, actorId, asked), cancellationToken);

        // The owner answered for the context captured before the await; a changed context releases nothing.
        if (!IsCurrent() || companyContext.CompanyId != companyId
            || !string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal))
        {
            return new(ProductPackageStandardCustomerOutcomeV3.Denied, []);
        }

        return answer.Outcome switch
        {
            CounterpartyPartyDisplayBatchReadOutcomeV1.Found => new(ProductPackageStandardCustomerOutcomeV3.Found,
                [.. answer.Items
                    .Where(item => asked.Contains(item.PartyId))
                    .Select(item => new ProductPackageStandardCustomerV3(item.PartyId, item.Code,
                        string.IsNullOrWhiteSpace(item.TradeName) ? item.LegalName : item.TradeName))]),
            CounterpartyPartyDisplayBatchReadOutcomeV1.Denied => new(ProductPackageStandardCustomerOutcomeV3.Denied, []),
            _ => new(ProductPackageStandardCustomerOutcomeV3.Unavailable, []),
        };
    }

    private bool IsCurrent() =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId) && companyContext.IsResolved
        && companyContext.CompanyId != Guid.Empty && currentUser.CanAccessCompany(companyContext.CompanyId);
}
