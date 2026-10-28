using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.PriceLists;

namespace GoymenERP.Modules.Product.Infrastructure.PriceLists;

/// <summary>
/// The owner's answer to the Sales order line (<see cref="IProductPriceReferenceReaderV1"/>): the list prices of the
/// context company's Sales-channel lists that price the product on the date, through the store's company filter. The
/// port carries no authority (the Logistics D6 pattern): Sales measures its own price name before asking. No company
/// context is refused before any read. Nothing is created, cached or copied.
/// <para>
/// MOD-02: the V1 readers answer company-wide lists only (what they always meant: ASSUMPTION-PRL-4's scope); a
/// customer-specific list is answered by <see cref="IProductQuantityPriceReferenceReaderV2"/>'s tier chain alone.
/// </para>
/// </summary>
internal sealed class ProductPriceReferenceReader(ProductPriceListStore store, ICompanyContext companyContext)
    : IProductPriceReferenceReaderV1, IProductQuantityPriceReferenceReaderV1, IProductQuantityPriceReferenceReaderV2
{
    public async Task<IReadOnlyList<ProductPriceReferenceV1>> SearchAsync(Guid productId, IReadOnlyCollection<Guid> priceUnitIds,
        string currencyCode, DateOnly on, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(priceUnitIds);
        RequireCompany();
        if (productId == Guid.Empty || priceUnitIds.Count == 0 || string.IsNullOrWhiteSpace(currencyCode))
        {
            return [];
        }

        var lists = await store.FindListsPricingAsync(productId, currencyCode, cancellationToken);
        var references = new List<ProductPriceReferenceV1>();
        lists = [.. lists.Where(IsCompanyWide)];
        foreach (var list in lists.OrderBy(list => list.Name, StringComparer.CurrentCulture).ThenBy(list => list.Id))
        {
            if (list.ResolveOn(on) is not { } version)
            {
                continue;
            }

            foreach (var unitId in priceUnitIds.Distinct())
            {
                if (version.LineFor(productId, unitId) is { } line)
                {
                    references.Add(Reference(list, version, line));
                }
            }
        }

        return references;
    }

    public async Task<ProductPriceReferenceV1?> GetAsync(Guid priceListId, int versionNo, Guid productId, Guid priceUnitId, DateOnly on,
        CancellationToken cancellationToken = default)
    {
        RequireCompany();
        var list = await store.LoadAsync(priceListId, cancellationToken);
        if (list is null || !IsCompanyWide(list) || list.Channel != PriceListChannel.Sales || list.ResolveOn(on) is not { } version
            || version.VersionNo != versionNo)
        {
            return null;
        }

        return version.LineFor(productId, priceUnitId) is { } line ? Reference(list, version, line) : null;
    }

    public async Task<IReadOnlyList<ProductPriceReferenceV1>> SearchAsync(Guid productId, Guid priceUnitId, decimal quantity,
        string currencyCode, DateOnly on, CancellationToken cancellationToken = default)
    {
        RequireCompany();
        if (productId == Guid.Empty || priceUnitId == Guid.Empty || quantity <= 0m || string.IsNullOrWhiteSpace(currencyCode))
        {
            return [];
        }

        var lists = await store.FindListsPricingAsync(productId, currencyCode, cancellationToken);
        return [.. lists.Where(IsCompanyWide).OrderBy(list => list.Name, StringComparer.CurrentCulture).ThenBy(list => list.Id)
            .Select(list => (List: list, Version: list.ResolveOn(on)))
            .Where(pair => pair.Version is not null)
            .Select(pair => (pair.List, Version: pair.Version!, Line: pair.Version!.LineFor(productId, priceUnitId, quantity)))
            .Where(pair => pair.Line is not null)
            .Select(pair => Reference(pair.List, pair.Version, pair.Line!))];
    }

    public async Task<ProductPriceReferenceV1?> GetAsync(Guid priceListId, int versionNo, Guid productId, Guid priceUnitId,
        decimal quantity, DateOnly on, CancellationToken cancellationToken = default)
    {
        RequireCompany();
        if (quantity <= 0m)
        {
            return null;
        }

        var list = await store.LoadAsync(priceListId, cancellationToken);
        if (list is null || !IsCompanyWide(list) || list.Channel != PriceListChannel.Sales || list.ResolveOn(on) is not { } version
            || version.VersionNo != versionNo)
        {
            return null;
        }

        return version.LineFor(productId, priceUnitId, quantity) is { } line ? Reference(list, version, line) : null;
    }

    /// <summary>
    /// MOD-02 (SAT-D9 / SAT-D10): the tier chain for one line on its business date. The store loads the company-wide lists
    /// and — only when a customer is given — that customer's own lists, under the company filter; the chain
    /// (<see cref="PriceTierChain"/>) decides. Invalid input answers NoPrice (never a default); no company is refused.
    /// </summary>
    public async Task<ProductPriceResolutionV2> ResolveAsync(ProductPriceResolutionRequestV2 request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var companyId = RequireCompany();
        IReadOnlyList<PriceList> lists = [];
        var customer = request.Customer is { PartyId: var party, CompanyAccountId: var account } && party != Guid.Empty && account != Guid.Empty
            ? new PriceTierCustomer(party, account)
            : null;
        if (request.ProductId != Guid.Empty && request.PriceUnitId != Guid.Empty && request.Quantity > 0m
            && !string.IsNullOrWhiteSpace(request.CurrencyCode))
        {
            lists = await store.FindListsPricingAsync(request.ProductId, request.CurrencyCode, cancellationToken, customer?.CompanyAccountId);
        }

        // The answer is for the company captured before the read; a changed context releases nothing.
        if (RequireCompany() != companyId)
        {
            throw new InvalidOperationException("Fiyat referansı okunurken şirket bağlamı değişti.");
        }

        var chain = PriceTierChain.Resolve(lists, companyId, request.ProductId, request.PriceUnitId, request.Quantity,
            request.CurrencyCode ?? string.Empty, request.On, customer);
        var candidates = chain.Candidates.Select(candidate => ReferenceV2(candidate)).ToList();
        return new ProductPriceResolutionV2(
            (ProductPriceResolutionOutcomeV2)(int)chain.Outcome,
            chain.On,
            PriceListBusinessDay.TimeZoneId,
            chain.MatchedTier is { } tier ? (ProductPriceTierV2)(int)tier : null,
            chain.Outcome == PriceTierChainOutcome.Resolved ? candidates[0] : null,
            candidates,
            [.. chain.Tiers.Select(evaluation => new ProductPriceTierEvaluationV2((ProductPriceTierV2)(int)evaluation.Tier,
                (ProductPriceTierStatusV2)(int)evaluation.Status, evaluation.Reason))],
            // No package-standard master exists (master §7.3 / U-07): nothing to snapshot, nothing invented.
            new ProductPackageStandardSnapshotV2(ProductPackageStandardStatusV2.Unavailable,
                ProductPriceResolutionReasonsV2.PackageStandardOwnerMissing));
    }

    private static bool IsCompanyWide(PriceList list) => list.Scope.Kind == PriceListScopeKind.Company;

    private static ProductPriceReferenceV2 ReferenceV2(PriceTierCandidate candidate) =>
        new((ProductPriceTierV2)(int)candidate.Tier, candidate.List.Id, candidate.List.Name, candidate.Version.Id,
            candidate.Version.VersionNo, candidate.Version.ValidFrom, candidate.Version.ValidThrough, candidate.Line.ProductId,
            candidate.Line.PriceUnitId, candidate.List.CurrencyCode, candidate.List.Channel.ToString(), candidate.List.VatBasis.ToString(),
            candidate.Line.UnitPrice, candidate.Line.FloorUnitPrice, candidate.Line.LowerQuantity, candidate.Line.UpperQuantity);

    private Guid RequireCompany()
    {
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            throw new InvalidOperationException("Fiyat referansı şirket bağlamı olmadan okunmaz.");
        }

        return companyContext.CompanyId;
    }

    private static ProductPriceReferenceV1 Reference(PriceList list, PriceListVersion version, PriceListLine line) =>
        new(list.Id, list.Name, version.Id, version.VersionNo, version.ValidFrom, version.ValidThrough, line.ProductId, line.PriceUnitId,
            list.CurrencyCode, list.Channel.ToString(), list.VatBasis.ToString(), line.UnitPrice, line.FloorUnitPrice,
            line.LowerQuantity, line.UpperQuantity);
}
