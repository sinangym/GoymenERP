using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Labels;
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
                .Select(row => new { row.Id, row.Code, row.HeatNumber, row.CoilNumber })
                .ToDictionaryAsync(row => row.Id, cancellationToken);
            var entries = await EntriesAsync(companyId, labelIds, cancellationToken);

            var items = new List<InventoryStockAgingPositionV1>(page.Length);
            foreach (var position in page)
            {
                // A balance without its own label row is a broken store, never a guessed label.
                if (!labels.TryGetValue(position.LabelId, out var label))
                {
                    return new(InventoryStockAgingOutcomeV1.Unavailable, companyId, [], false);
                }

                var (entry, inherited) = entries[position.LabelId];
                items.Add(new InventoryStockAgingPositionV1(position.LabelId, label.Code, position.ProductId, position.LocationId,
                    label.HeatNumber, label.CoilNumber, Kilograms(position.Kg), entry, inherited));
            }

            return new(InventoryStockAgingOutcomeV1.Found, companyId, items, positions.Length > query.PageSize);
        }
        catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException
            or System.Data.Common.DbException or TimeoutException)
        {
            return new(InventoryStockAgingOutcomeV1.Unavailable, companyId, [], false);
        }
    }

    /// <summary>Technical walk bound (not a business rule): a longer split/merge chain answers an unknown entry.</summary>
    internal const int MaximumAncestorDepth = 32;

    /// <summary>
    /// The entry of each label: walk split/merge edges (never manufacturing) back to the roots in batched reads, take each root's
    /// first incoming movement, and give every descendant the oldest entry among its parents.
    /// </summary>
    private async Task<Dictionary<Guid, (DateTimeOffset? Entry, bool Inherited)>> EntriesAsync(Guid companyId, IReadOnlyCollection<Guid> labelIds,
        CancellationToken cancellationToken)
    {
        var parents = new Dictionary<Guid, List<Guid>>();
        var seen = new HashSet<Guid>(labelIds);
        var frontier = labelIds.ToArray();
        var truncated = new HashSet<Guid>();
        for (var depth = 0; frontier.Length > 0; depth++)
        {
            if (depth == MaximumAncestorDepth)
            {
                truncated.UnionWith(frontier);
                break;
            }

            var current = frontier;
            var edges = await (from output in database.LabelTransformationOutputs.AsNoTracking()
                    join transformation in database.LabelTransformations.AsNoTracking()
                        on new { output.CompanyId, Id = output.TransformationId } equals new { transformation.CompanyId, transformation.Id }
                    join input in database.LabelTransformationInputs.AsNoTracking()
                        on new { output.CompanyId, output.TransformationId } equals new { input.CompanyId, input.TransformationId }
                    where output.CompanyId == companyId && current.Contains(output.LabelId)
                        && (transformation.Kind == (int)InventoryLabelTransformationKind.Split
                            || transformation.Kind == (int)InventoryLabelTransformationKind.Merge)
                    select new { Child = output.LabelId, Parent = input.LabelId })
                .ToArrayAsync(cancellationToken);
            var next = new List<Guid>();
            foreach (var edge in edges)
            {
                if (!parents.TryGetValue(edge.Child, out var list)) parents[edge.Child] = list = [];
                if (!list.Contains(edge.Parent)) list.Add(edge.Parent);
                if (seen.Add(edge.Parent)) next.Add(edge.Parent);
            }

            frontier = [.. next];
        }

        var roots = seen.Where(label => !parents.ContainsKey(label) && !truncated.Contains(label)).ToArray();
        var firstIncoming = await (from line in database.MovementLines.AsNoTracking()
                join movement in database.Movements.AsNoTracking()
                    on new { line.CompanyId, Id = line.MovementId } equals new { movement.CompanyId, movement.Id }
                where line.CompanyId == companyId && roots.Contains(line.LabelId) && line.Direction == (int)StockDirection.In
                group movement by line.LabelId into grouped
                select new { LabelId = grouped.Key, First = grouped.Min(item => item.OccurredAtUtc) })
            .ToDictionaryAsync(row => row.LabelId, row => row.First, cancellationToken);

        return InventoryStockAgingEntryResolver.Resolve(labelIds, parents, firstIncoming, truncated);
    }

    private static string Kilograms(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
}

/// <summary>
/// WP14: the pure entry resolution over an already-read genealogy. A root's entry is its first incoming movement; a split/merge
/// child takes the OLDEST entry of its parents (merge: NEEDS-SINAN); any unknown or truncated parent makes the child unknown —
/// an oldest date is never taken from a partial set. A cycle (a broken store) is unknown.
/// </summary>
internal static class InventoryStockAgingEntryResolver
{
    public static Dictionary<Guid, (DateTimeOffset? Entry, bool Inherited)> Resolve(IReadOnlyCollection<Guid> labelIds,
        IReadOnlyDictionary<Guid, List<Guid>> parents, IReadOnlyDictionary<Guid, DateTimeOffset> firstIncoming, IReadOnlySet<Guid> truncated)
    {
        var memo = new Dictionary<Guid, DateTimeOffset?>();
        var onPath = new HashSet<Guid>();
        DateTimeOffset? Resolve(Guid label)
        {
            if (memo.TryGetValue(label, out var known)) return known;
            if (!onPath.Add(label)) return null;
            DateTimeOffset? entry;
            if (truncated.Contains(label))
            {
                entry = null;
            }
            else if (!parents.TryGetValue(label, out var list))
            {
                entry = firstIncoming.TryGetValue(label, out var first) ? first : null;
            }
            else
            {
                var resolved = list.Select(Resolve).ToArray();
                entry = resolved.Any(item => item is null) ? null : resolved.Min();
            }

            onPath.Remove(label);
            memo[label] = entry;
            return entry;
        }

        return labelIds.Distinct().ToDictionary(label => label, label => (Resolve(label), parents.ContainsKey(label)));
    }
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
