using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

// This position is internal server state. The HTTP cursor must protect it and bind
// it to current authority + all normalized filters before invoking this store.
public sealed record ProductListPosition(Guid Id, string? Text, DateTimeOffset? UpdatedAtUtc);
public sealed record ProductStoredPage(IReadOnlyList<ProductStoredRecord> Items, ProductListPosition? Next);

/// <summary>Product-only relational reads; the application caller rechecks current authority before releasing results.</summary>
public sealed class ProductReadStore(ProductDbContext context, ILogger<ProductReadStore>? logger = null)
    : IProductSalesRecordReader
{
    public async Task<ProductStoredRecord?> ReadForSalesSelectionAsync(ProductSalesReadScope scope, Guid productId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var row = await context.Products.AsNoTracking().SingleOrDefaultAsync(product => product.Id == productId, cancellationToken);
        return row?.ToRecord();
    }

    /// <summary>
    /// The released family definition when it is well formed; null when none is released or the release
    /// is malformed (a read of options is not the place to fail: the catalogue is simply unavailable). A
    /// malformed release is logged with its code (#334 P3-1); a missing one is the normal state and is not.
    /// </summary>
    public Task<ProductFamilyDefinitionV2?> ReadFamilyDefinitionAsync(GroupSharedMasterReadContext authority,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authority);
        return ProductFamilyReleaseRead.WellFormedAsync(new SqlProductFamilyDefinitionSource(context), logger,
            nameof(ProductReadStore), cancellationToken);
    }

    public async Task<ProductStoredRecord?> ReadByIdAsync(GroupSharedMasterReadContext authority, Guid id,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authority);
        var row = await context.Products.AsNoTracking().SingleOrDefaultAsync(product => product.Id == id, cancellationToken);
        return row?.ToRecord();
    }

    public async Task<IReadOnlyList<ProductCodeGroup>> ReadCodeGroupsAsync(GroupSharedMasterReadContext authority,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authority);
        var groups = await context.CodeGroups.AsNoTracking().OrderBy(group => group.Name).ThenBy(group => group.Id)
            .ToListAsync(cancellationToken);
        return groups.Select(group => group.ToContract()).ToArray();
    }

    public async Task<ProductStoredPage> ReadPageAsync(GroupSharedMasterReadContext authority,
        ProductListQuery query, ProductListPosition? after, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authority);
        query = ProductQueryRules.Normalize(query);
        var rows = await BuildQuery(query, after).Take(query.PageSize + 1).ToListAsync(cancellationToken);
        var hasNext = rows.Count > query.PageSize;
        if (hasNext) rows.RemoveAt(rows.Count - 1);
        var last = rows.LastOrDefault();
        ProductListPosition? next = hasNext && last is not null ? new(last.Id,
            query.Sort == "updatedAtUtc" ? null : query.Sort == "name" ? last.Name : last.Code,
            query.Sort == "updatedAtUtc" ? last.UpdatedAtUtc : null) : null;
        return new(rows.Select(row => row.ToRecord()).ToArray(), next);
    }

    public IQueryable<ProductRow> BuildQuery(ProductListQuery query, ProductListPosition? after)
    {
        query = ProductQueryRules.Normalize(query);
        var rows = context.Products.AsNoTracking();
        if (!string.IsNullOrEmpty(query.Q))
        {
            var search = query.Q;
            // EXISTS, not a join: multiple matching aliases never duplicate a Product.
            rows = rows.Where(row => row.Name.Contains(search)
                || EF.Functions.Collate(row.Code, "Turkish_100_CI_AS").Contains(search)
                || context.CodeRegistrations.Any(code => code.ProductId == row.Id
                    && EF.Functions.Collate(code.Code, "Turkish_100_CI_AS").Contains(search)));
        }
        if (query.Lifecycle is not null)
        {
            var lifecycle = query.Lifecycle switch { "Draft" => ProductLifecycle.Draft, "Active" => ProductLifecycle.Active, _ => ProductLifecycle.Inactive };
            rows = rows.Where(row => row.Lifecycle == (int)lifecycle);
        }
        if (query.GroupId.HasValue) rows = rows.Where(row => row.GroupId == query.GroupId);
        if (query.ClassId.HasValue) rows = rows.Where(row => row.ClassId == query.ClassId);
        if (query.FamilyId.HasValue) rows = rows.Where(row => row.FamilyId == query.FamilyId);
        var roles = query.Roles?.Split(',') ?? [];
        var purchase = roles.Contains("purchasable");
        var sales = roles.Contains("sellable");
        var input = roles.Contains("productionInput");
        var output = roles.Contains("productionOutput");
        if (roles.Length > 0)
            rows = query.RoleMatch == "all"
                ? rows.Where(row => (!purchase || row.PurchasableValue == true) && (!sales || row.SellableValue == true)
                    && (!input || row.ProductionInputValue == true) && (!output || row.ProductionOutputValue == true))
                : rows.Where(row => (purchase && row.PurchasableValue == true) || (sales && row.SellableValue == true)
                    || (input && row.ProductionInputValue == true) || (output && row.ProductionOutputValue == true));
        // No approved completeness evaluator is registered yet. Records are
        // NotEvaluated, never silently Complete or Incomplete from missing fields.
        if (query.Completeness is "Complete" or "Incomplete") rows = rows.Where(_ => false);

        var ascending = query.Direction == "asc";
        if (after is not null)
        {
            ProductCursorCodec.ValidatePosition(query, after);
            if (query.Sort == "updatedAtUtc")
                rows = ascending
                    ? rows.Where(row => row.UpdatedAtUtc > after.UpdatedAtUtc || (row.UpdatedAtUtc == after.UpdatedAtUtc && row.Id.CompareTo(after.Id) > 0))
                    : rows.Where(row => row.UpdatedAtUtc < after.UpdatedAtUtc || (row.UpdatedAtUtc == after.UpdatedAtUtc && row.Id.CompareTo(after.Id) < 0));
            else if (query.Sort == "name")
                rows = ascending
                    ? rows.Where(row => string.Compare(row.Name, after.Text) > 0 || (row.Name == after.Text && row.Id.CompareTo(after.Id) > 0))
                    : rows.Where(row => string.Compare(row.Name, after.Text) < 0 || (row.Name == after.Text && row.Id.CompareTo(after.Id) < 0));
            else
                rows = ascending
                    ? rows.Where(row => string.Compare(row.Code, after.Text) > 0 || (row.Code == after.Text && row.Id.CompareTo(after.Id) > 0))
                    : rows.Where(row => string.Compare(row.Code, after.Text) < 0 || (row.Code == after.Text && row.Id.CompareTo(after.Id) < 0));
        }

        return (query.Sort, ascending) switch
        {
            ("name", true) => rows.OrderBy(row => row.Name).ThenBy(row => row.Id),
            ("name", false) => rows.OrderByDescending(row => row.Name).ThenByDescending(row => row.Id),
            ("updatedAtUtc", true) => rows.OrderBy(row => row.UpdatedAtUtc).ThenBy(row => row.Id),
            ("updatedAtUtc", false) => rows.OrderByDescending(row => row.UpdatedAtUtc).ThenByDescending(row => row.Id),
            (_, true) => rows.OrderBy(row => row.Code).ThenBy(row => row.Id),
            _ => rows.OrderByDescending(row => row.Code).ThenByDescending(row => row.Id),
        };
    }
}
