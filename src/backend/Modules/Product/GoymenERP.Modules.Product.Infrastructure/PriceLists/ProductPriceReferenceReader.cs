using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.PriceLists;

namespace GoymenERP.Modules.Product.Infrastructure.PriceLists;

/// <summary>
/// The owner's answer to the Sales order line (<see cref="IProductPriceReferenceReaderV1"/>): the list prices of the
/// context company's Sales-channel lists that price the product on the date, through the store's company filter. The
/// port carries no authority (the Logistics D6 pattern): Sales measures its own price name before asking. No company
/// context is refused before any read. Nothing is created, cached or copied.
/// </summary>
internal sealed class ProductPriceReferenceReader(ProductPriceListStore store, ICompanyContext companyContext) : IProductPriceReferenceReaderV1
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
        if (list is null || list.Channel != PriceListChannel.Sales || list.ResolveOn(on) is not { } version || version.VersionNo != versionNo)
        {
            return null;
        }

        return version.LineFor(productId, priceUnitId) is { } line ? Reference(list, version, line) : null;
    }

    private void RequireCompany()
    {
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            throw new InvalidOperationException("Fiyat referansı şirket bağlamı olmadan okunmaz.");
        }
    }

    private static ProductPriceReferenceV1 Reference(PriceList list, PriceListVersion version, PriceListLine line) =>
        new(list.Id, list.Name, version.Id, version.VersionNo, version.ValidFrom, version.ValidThrough, line.ProductId, line.PriceUnitId,
            list.CurrencyCode, list.Channel.ToString(), list.VatBasis.ToString(), line.UnitPrice, line.FloorUnitPrice);
}
