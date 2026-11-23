using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.PriceLists;
using GoymenERP.Modules.Product.Infrastructure.PriceLists;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Product.Infrastructure.WorkInbox;

/// <summary>
/// WP7-2 (AI-D31, AI-D32, YOL-D18; SAT-D382, SAT-D416): the package standard table version waiting for its decision, as
/// Product lists it for the Work inbox. At most one version waits: the newest version that requires approval and has no
/// decision — exactly what <c>ApproveProductPackageStandardVersionV3</c> accepts. It is decided on the product screen's package
/// approval panel by a holder of <c>product.package-standards.approve</c> granted company-wide, never by its preparer
/// (SAT-D416). The rules carry the version's product and customer entry counts as Product stored them.
/// </summary>
internal sealed class ProductPackageStandardWorkInboxSourceV1(
    ProductPriceListDbContext database,
    WorkInboxCompanyWideFenceV1 fence) : IWorkInboxSourceV1
{
    public const string ModuleName = "product";
    public const string KindName = "product.package-standard-version.v1";

    /// <summary>A package standard version takes effect only through an approval decision by someone other than its preparer.</summary>
    public const string ApprovalRequiredRule = "product.package-standard-approval-required";

    /// <summary>The number of product entries (pieces per package) the version holds.</summary>
    public const string EntryCountRule = "product.package-standard-entries";

    /// <summary>The number of customer override entries the version holds.</summary>
    public const string CustomerEntryCountRule = "product.package-standard-customer-entries";

    /// <summary>The product screens (<c>PRODUCT_ROUTE productsList</c> / <c>productsDetail</c>); the approval panel is on the detail.</summary>
    internal const string ProductScreenPath = "/urun";

    private static readonly IReadOnlyList<string> Permissions = [ProductPackageStandardPermissionsV3.Approve];

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => ProductPackageStandardPermissionsV3.Approve;

    public async Task<WorkInboxSourceResultV1> ListPendingAsync(
        WorkInboxSourceContextV1 context,
        int maxCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);
        cancellationToken.ThrowIfCancellationRequested();
        if (await fence.CheckAsync(context, Permissions, cancellationToken) is { } refused)
        {
            return refused;
        }

        ProductPackageStandardVersionRow? pending;
        try
        {
            // The owner's own reading rule (ProductPackageStandardService.PendingHeaderAsync).
            pending = await database.PackageStandardVersions.AsNoTracking()
                .Where(version => version.CompanyId == context.CompanyId && version.RequiresApproval
                    && !database.PackageStandardDecisions.Any(decision =>
                        decision.CompanyId == version.CompanyId && decision.Version == version.Version))
                .OrderByDescending(version => version.Version)
                .FirstOrDefaultAsync(cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return await fence.CheckAsync(context, Permissions, cancellationToken) ?? WorkInboxSourceResultV1.Unavailable();
        }

        if (await fence.CheckAsync(context, Permissions, cancellationToken) is { } afterRead)
        {
            return afterRead;
        }

        if (pending is null)
        {
            return WorkInboxSourceResultV1.Found(context.CompanyId, [], false);
        }

        if (pending.VersionId == Guid.Empty || string.IsNullOrWhiteSpace(pending.CreatedBy) || pending.Version <= 0
            || pending.EntryCount < 0 || pending.CustomerEntryCount < 0)
        {
            return WorkInboxSourceResultV1.Unavailable();
        }

        var link = pending.EditedProductId is { } productId
            ? WorkInboxCompanyWideFenceV1.Link(ProductScreenPath, productId)
            : ProductScreenPath;
        var references = new List<WorkApprovalReferenceV1>(2);
        if (pending.EditedProductId is { } edited)
        {
            references.Add(new("product", WorkInboxCompanyWideFenceV1.ShortReference(edited), edited, link));
        }

        if (pending.EditedCustomerId is { } customer)
        {
            references.Add(new("party", WorkInboxCompanyWideFenceV1.ShortReference(customer), customer, null));
        }

        WorkApprovalRuleResultV1[] rules =
        [
            new(ApprovalRequiredRule, null, null, null, null),
            new(EntryCountRule, null, pending.EntryCount.ToString(CultureInfo.InvariantCulture), null, null),
            new(CustomerEntryCountRule, null, pending.CustomerEntryCount.ToString(CultureInfo.InvariantCulture), null, null),
        ];
        var item = new WorkInboxItemV1(ModuleName, KindName, pending.VersionId,
            "v" + pending.Version.ToString(CultureInfo.InvariantCulture), pending.CreatedAtUtc.ToUniversalTime(), link,
            pending.CreatedBy, rules, references);
        return WorkInboxSourceResultV1.Found(context.CompanyId, maxCount >= 1 ? [item] : [], false);
    }
}

/// <summary>
/// WP7-2 (AI-D31, AI-D32, YOL-D18): the price list versions waiting for publication, as Product lists them for the Work inbox. A
/// version waits while it is a Draft — what <c>PublishPriceListVersionV1</c> accepts; it is published on the price list's own
/// screen by a holder of <c>product.price-lists.publish</c> granted company-wide ("write never implies publish"). The owner's
/// rule (<see cref="PriceList.Publish"/>): no one who created or edited the version may publish it — its creator is the maker and
/// every other recorded preparer is in the "may not decide" set (D4).
/// </summary>
internal sealed class ProductPriceListVersionWorkInboxSourceV1(
    ProductPriceListDbContext database,
    WorkInboxCompanyWideFenceV1 fence) : IWorkInboxSourceV1
{
    public const string ModuleName = "product";
    public const string KindName = "product.price-list-version.v1";

    /// <summary>A draft price version is published by someone who neither created nor edited it.</summary>
    public const string PublicationRequiredRule = "product.price-list-version-publication-required";

    /// <summary>The version would replace an earlier published version of the same list.</summary>
    public const string NextVersionRule = "product.price-list-next-version";

    /// <summary>The price list screen (<c>PRICE_LIST_ROUTE detail</c>, <c>/fiyat-listeleri/:priceListId</c>).</summary>
    internal const string ScreenPath = "/fiyat-listeleri";

    private const int MaxLabelNameLength = 160;

    private static readonly IReadOnlyList<string> Permissions = [ProductPriceListPermissionsV1.Publish];

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => ProductPriceListPermissionsV1.Publish;

    public async Task<WorkInboxSourceResultV1> ListPendingAsync(
        WorkInboxSourceContextV1 context,
        int maxCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);
        cancellationToken.ThrowIfCancellationRequested();
        if (await fence.CheckAsync(context, Permissions, cancellationToken) is { } refused)
        {
            return refused;
        }

        List<Pending> rows;
        int total;
        try
        {
            var draft = (int)PriceListVersionState.Draft;
            var pending =
                from version in database.Versions.AsNoTracking()
                where version.CompanyId == context.CompanyId && version.State == draft
                join list in database.PriceLists.AsNoTracking()
                    on new { version.CompanyId, Id = version.PriceListId } equals new { list.CompanyId, list.Id }
                select new Pending(version.VersionId, version.PriceListId, list.Name, version.VersionNo, version.CreatedAtUtc,
                    version.CreatedBy, version.PreparerActorIdsJson);
            total = await pending.CountAsync(cancellationToken);
            rows = await pending.OrderBy(row => row.CreatedAtUtc).ThenBy(row => row.VersionId)
                .Take(maxCount).ToListAsync(cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return await fence.CheckAsync(context, Permissions, cancellationToken) ?? WorkInboxSourceResultV1.Unavailable();
        }

        if (await fence.CheckAsync(context, Permissions, cancellationToken) is { } afterRead)
        {
            return afterRead;
        }

        var items = new List<WorkInboxItemV1>(rows.Count);
        foreach (var row in rows)
        {
            if (!TryItem(row, out var item))
            {
                return WorkInboxSourceResultV1.Unavailable();
            }

            items.Add(item);
        }

        return WorkInboxSourceResultV1.Found(context.CompanyId, items, total > rows.Count);
    }

    private static bool TryItem(Pending row, out WorkInboxItemV1 item)
    {
        item = null!;
        string[]? preparers;
        try
        {
            preparers = JsonSerializer.Deserialize<string[]>(row.PreparerActorIdsJson);
        }
        catch (JsonException)
        {
            return false;
        }

        if (row.VersionId == Guid.Empty || row.PriceListId == Guid.Empty || row.VersionNo <= 0
            || string.IsNullOrWhiteSpace(row.CreatedBy) || string.IsNullOrWhiteSpace(row.Name) || row.Name.Length > MaxLabelNameLength
            || preparers is null || preparers.Any(string.IsNullOrWhiteSpace))
        {
            return false;
        }

        var excluded = preparers.Where(actor => !string.Equals(actor, row.CreatedBy, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal).ToArray();
        if (excluded.Length > WorkInboxRulesV1.MaxExcludedDecidersPerItem)
        {
            return false;
        }

        var rules = new List<WorkApprovalRuleResultV1>(2) { new(PublicationRequiredRule, null, null, null, null) };
        if (row.VersionNo > 1)
        {
            rules.Add(new(NextVersionRule, null, null, null, null));
        }

        var link = WorkInboxCompanyWideFenceV1.Link(ScreenPath, row.PriceListId);
        var label = row.Name + " v" + row.VersionNo.ToString(CultureInfo.InvariantCulture);
        item = new WorkInboxItemV1(ModuleName, KindName, row.VersionId, label, row.CreatedAtUtc.ToUniversalTime(), link,
            row.CreatedBy, rules, [new("price-list", row.Name, row.PriceListId, link)], excluded);
        return true;
    }

    private sealed record Pending(Guid VersionId, Guid PriceListId, string Name, int VersionNo, DateTimeOffset CreatedAtUtc,
        string CreatedBy, string PreparerActorIdsJson);
}
