using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// Central read composition for Inventory-owned quantities and Product-owned display facts. The
/// Product port is invoked once for the bounded Inventory page; neither owner gains the other's
/// authority and no fallback identity or technical value is synthesized.
/// </summary>
public static class InventoryStockPositionDisplayHost
{
    public static async Task<InventoryStockPositionPageV1> ComposeAsync(
        InventoryStockPositionsV1 source,
        IProductStockDisplayReferencesV1 products,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(source.Items);
        ArgumentNullException.ThrowIfNull(products);

        if (source.Items.Count == 0)
        {
            return new([], source.SearchWindowFull, source.NextCursor);
        }

        if (source.Items.Any(item => item.ProductId == Guid.Empty
            || string.IsNullOrWhiteSpace(item.ProductVersion)))
        {
            throw Unavailable();
        }

        var groups = source.Items.GroupBy(item => item.ProductId).ToArray();
        if (groups.Any(group => group.Select(item => item.ProductVersion)
                .Distinct(StringComparer.Ordinal).Count() != 1))
        {
            throw Unavailable();
        }

        var selections = groups.Select(group => new ProductStockDisplaySelectionV1(
            group.Key, group.First().ProductVersion!)).ToArray();
        var productPage = await products.ReadAsync(new(selections), cancellationToken);
        if (productPage.Items.Count != selections.Length
            || productPage.Items.Select(item => item.ProductId).Distinct().Count() != productPage.Items.Count)
        {
            throw Unavailable();
        }

        var byProduct = productPage.Items.ToDictionary(item => item.ProductId);
        foreach (var selection in selections)
        {
            if (!byProduct.TryGetValue(selection.ProductId, out var item)
                || !string.Equals(item.ExpectedProductVersion, selection.ExpectedProductVersion,
                    StringComparison.Ordinal))
            {
                throw Unavailable();
            }
        }

        return new(
            [.. source.Items.Select(item => View(item, byProduct[item.ProductId]))],
            source.SearchWindowFull,
            source.NextCursor);
    }

    private static InventoryStockPositionDisplayV1 View(
        InventoryStockPositionV1 item,
        ProductStockDisplayResultV1 product) => new(
        item.CompanyId,
        item.LabelId,
        item.LabelVersion,
        item.LabelCode,
        item.ProductId,
        item.ProductVersion,
        item.Classification,
        item.Lot,
        item.HeatNumber,
        item.CoilNumber,
        item.DocumentStatus,
        item.OwnerKind,
        item.OwnerId,
        item.CustodianKind,
        item.CustodianId,
        item.LocationId,
        item.PhysicalKg,
        item.FreePhysicalKg,
        item.BlockedPhysicalKg,
        item.ActiveAllocationKg,
        item.AvailableKg,
        product);

    private static InventoryStockPositionException Unavailable() =>
        new(StatusCodes.Status503ServiceUnavailable, InventoryStockPositionCodesV1.Unavailable);
}

public sealed record InventoryStockPositionPageV1(
    IReadOnlyList<InventoryStockPositionDisplayV1> Items,
    bool SearchWindowFull,
    string? NextCursor = null);

public sealed record InventoryStockPositionDisplayV1(
    Guid CompanyId,
    Guid LabelId,
    string LabelVersion,
    string LabelCode,
    Guid ProductId,
    string? ProductVersion,
    string? Classification,
    InventoryLotV1 Lot,
    string? HeatNumber,
    string? CoilNumber,
    string DocumentStatus,
    string OwnerKind,
    Guid OwnerId,
    string CustodianKind,
    Guid CustodianId,
    Guid LocationId,
    string PhysicalKg,
    string FreePhysicalKg,
    string BlockedPhysicalKg,
    string ActiveAllocationKg,
    string AvailableKg,
    ProductStockDisplayResultV1 ProductDisplay);
