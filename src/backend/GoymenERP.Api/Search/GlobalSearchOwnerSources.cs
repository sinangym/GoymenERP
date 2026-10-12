using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Search;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Api.Search;

/// <summary>
/// AT-002: the Cari parties of the global search, answered by the Cari owner's OWN authorized list read
/// (<see cref="ICounterpartyReadService.GetAuthorizedPageAsync"/>) — the same read, the same <c>counterparty.parties.read</c>
/// measurement and the same matching (code, legal and trade name, Turkish collation) the C-01 list uses, so "more" on the
/// Cari list continues exactly this search. With a resolved company context the owner answers only parties holding an
/// account in THAT company; Forbidden is NotAuthorized. Active parties, as the list's default view; code order.
/// The host adapts because the owner's read binds the actor's session, which only the host resolves.
/// </summary>
public sealed class CounterpartyGlobalSearchSource(
    ICounterpartyReadService counterparties,
    ICurrentUser currentUser,
    ICompanyContext companyContext,
    IActorSessionBindingResolver sessions) : IGlobalSearchSourceV1
{
    public string Kind => GlobalSearchKindsV1.CounterpartyParty;

    public async Task<GlobalSearchSourceAnswerV1> SearchAsync(GlobalSearchRequestV1 request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId) || !companyContext.IsResolved
            || companyContext.CompanyId == Guid.Empty)
        {
            return GlobalSearchSourceAnswerV1.NotAuthorized;
        }

        var companyId = companyContext.CompanyId;
        var binding = await sessions.ResolveAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(binding))
        {
            throw new InvalidOperationException("Cari araması için oturum bağı çözülemedi.");
        }

        var query = new CounterpartyPageQuery(request.Term, null, null, null, null, PartyRole.None, CounterpartyRoleMatch.Any, CounterpartyRecordState.Active,
            CounterpartyRecordState.All, CounterpartySort.Code, CounterpartySortDirection.Ascending, request.Limit, null, false);
        var execution = await counterparties.GetAuthorizedPageAsync(new CounterpartyReadRequest(query, currentUser.UserId, binding),
            cancellationToken);
        switch (execution?.Result)
        {
            case CounterpartyPageResult.Forbidden:
                return GlobalSearchSourceAnswerV1.NotAuthorized;
            case CounterpartyPageResult.Succeeded when execution.Page is { Items: not null } page:
                if (page.Items.Count > request.Limit)
                {
                    throw new InvalidOperationException("Cari araması sınırdan fazla kayıt döndü.");
                }

                var hits = new List<GlobalSearchHitV1>(page.Items.Count);
                foreach (var item in page.Items)
                {
                    // The owner answered for the context company only: a party without an account there is not this search's.
                    if (item is null || item.CompanyAccounts is null || item.CompanyAccounts.All(account => account.CompanyId != companyId))
                    {
                        throw new InvalidOperationException("Cari araması şirket bağlamı dışında kayıt döndü.");
                    }

                    hits.Add(new GlobalSearchHitV1(item.Id, item.Code, item.LegalName, item.TradeName, item.IsActive ? "Active" : "Inactive",
                        null, companyId));
                }

                return GlobalSearchSourceAnswerV1.Answered(hits, page.HasMore);
            default:
                throw new InvalidOperationException("Cari araması yanıt vermedi.");
        }
    }
}

/// <summary>
/// AT-002: the product cards of the global search, answered by the Product owner's OWN list read
/// (<see cref="IProductService.GetPageAsync"/>) — the same group-master read authority and the same code/name matching the
/// U-01 list uses. Product cards are group master data (not company records), so there is no company to check on a hit;
/// the owner's authority decides visibility, and a refusal (<see cref="UnauthorizedAccessException"/>) is NotAuthorized.
/// Code order; a next cursor means "more". Price-free: the card view has no price member.
/// </summary>
public sealed class ProductGlobalSearchSource(IProductService products) : IGlobalSearchSourceV1
{
    public string Kind => GlobalSearchKindsV1.ProductItem;

    public async Task<GlobalSearchSourceAnswerV1> SearchAsync(GlobalSearchRequestV1 request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ProductPage page;
        try
        {
            page = await products.GetPageAsync(new ProductListQuery(Q: request.Term, PageSize: request.Limit), cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return GlobalSearchSourceAnswerV1.NotAuthorized;
        }

        if (page?.Items is null || page.Items.Count > request.Limit)
        {
            throw new InvalidOperationException("Ürün araması sözleşme dışı yanıt verdi.");
        }

        return GlobalSearchSourceAnswerV1.Answered(
            [.. page.Items.Select(item => new GlobalSearchHitV1(item.Id, item.Code, item.Name, null, item.Lifecycle, null, null))],
            page.NextCursor is not null);
    }
}
