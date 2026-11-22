using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Inventory.Infrastructure;

/// <summary>
/// WP14: the company-filtered owner read of physical stock positions with their label dates. Every query names the
/// server-resolved company explicitly in addition to the tenant filter; a query for another company is <c>Denied</c> before
/// any read. A store failure is <c>Unavailable</c>, never an empty page.
/// </summary>
internal sealed class SqlInventoryStockAgingReadPortV1(InventoryDbContext database, ICompanyContext company)
    : IInventoryStockAgingReadPortV1
{
    public async Task<InventoryStockAgingPageV1> ReadAsync(InventoryStockAgingQueryV1 query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!company.IsResolved || company.CompanyId == Guid.Empty || query.CompanyId != company.CompanyId)
        {
            return new(InventoryStockAgingOutcomeV1.Denied, query.CompanyId, [], false);
        }

        if (query.Page < 1 || query.PageSize < 1 || query.PageSize > IInventoryStockAgingReadPortV1.MaximumPageSize
            || query.ProductId == Guid.Empty || query.LocationId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "The stock-aging page request is outside its technical bounds.");
        }

        var companyId = query.CompanyId;
        try
        {
            var positions = await database.Balances.AsNoTracking()
                .Where(row => row.CompanyId == companyId
                    && (query.ProductId == null || row.ProductId == query.ProductId)
                    && (query.LocationId == null || row.LocationId == query.LocationId))
                .GroupBy(row => new { row.LabelId, row.LocationId, row.ProductId })
                .Select(group => new { group.Key.LabelId, group.Key.LocationId, group.Key.ProductId, Kg = group.Sum(row => row.QuantityKg) })
                .Where(group => group.Kg > 0)
                .OrderBy(group => group.LabelId).ThenBy(group => group.LocationId)
                .Skip(checked((query.Page - 1) * query.PageSize))
                .Take(query.PageSize + 1)
                .ToArrayAsync(cancellationToken);
            var page = positions.Take(query.PageSize).ToArray();
            var labelIds = page.Select(row => row.LabelId).Distinct().ToArray();
            var labels = await database.Labels.AsNoTracking()
                .Where(row => row.CompanyId == companyId && labelIds.Contains(row.Id))
                .Select(row => new { row.Id, row.Code, row.HeatNumber, row.CoilNumber, row.CreatedAtUtc })
                .ToDictionaryAsync(row => row.Id, cancellationToken);
            var firstIncoming = await (from line in database.MovementLines.AsNoTracking()
                    join movement in database.Movements.AsNoTracking()
                        on new { line.CompanyId, Id = line.MovementId } equals new { movement.CompanyId, movement.Id }
                    where line.CompanyId == companyId && labelIds.Contains(line.LabelId) && line.Direction == (int)StockDirection.In
                    group movement by line.LabelId into grouped
                    select new { LabelId = grouped.Key, First = grouped.Min(item => item.OccurredAtUtc) })
                .ToDictionaryAsync(row => row.LabelId, row => row.First, cancellationToken);

            var items = new List<InventoryStockAgingPositionV1>(page.Length);
            foreach (var position in page)
            {
                // A balance without its own label row is a broken store, never a guessed label.
                if (!labels.TryGetValue(position.LabelId, out var label))
                {
                    return new(InventoryStockAgingOutcomeV1.Unavailable, companyId, [], false);
                }

                items.Add(new InventoryStockAgingPositionV1(position.LabelId, label.Code, position.ProductId, position.LocationId,
                    label.HeatNumber, label.CoilNumber, Kilograms(position.Kg), label.CreatedAtUtc,
                    firstIncoming.TryGetValue(position.LabelId, out var first) ? first : null));
            }

            return new(InventoryStockAgingOutcomeV1.Found, companyId, items, positions.Length > query.PageSize);
        }
        catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException
            or System.Data.Common.DbException or TimeoutException)
        {
            return new(InventoryStockAgingOutcomeV1.Unavailable, companyId, [], false);
        }
    }

    private static string Kilograms(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
}

public static class InventoryStockAgingRegistration
{
    /// <summary>WP14: registers the owner read only; no permission, grant or capability is added.</summary>
    public static IServiceCollection AddInventoryStockAgingReadPort(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IInventoryStockAgingReadPortV1, SqlInventoryStockAgingReadPortV1>();
        return services;
    }
}
