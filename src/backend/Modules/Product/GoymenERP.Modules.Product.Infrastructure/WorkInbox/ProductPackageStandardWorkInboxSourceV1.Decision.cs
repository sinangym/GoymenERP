using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Product.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Product.Infrastructure.WorkInbox;

internal sealed partial class ProductPackageStandardWorkInboxSourceV1
{
    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId,
        CancellationToken token)
    {
        await Fence();
        var header = await database.PackageStandardVersions.AsNoTracking().SingleOrDefaultAsync(row =>
            row.CompanyId == context.CompanyId && row.VersionId == itemId && row.RequiresApproval, token);
        await Fence();
        if (header is null) return null;
        var decided = await database.PackageStandardDecisions.AsNoTracking().AnyAsync(row =>
            row.CompanyId == context.CompanyId && row.Version == header.Version, token);
        await Fence();
        // The owner's existing input bound. A larger table is unavailable, never a partial human approval picture.
        var lines = await database.PackageStandardLines.AsNoTracking().Where(row => row.CompanyId == context.CompanyId
            && row.Version == header.Version).OrderBy(row => row.ProductId)
            .Take(ProductPackageStandardInputLimitsV2.MaximumRows + 1).ToListAsync(token);
        await Fence();
        var customers = await database.PackageStandardCustomerLines.AsNoTracking().Where(row => row.CompanyId == context.CompanyId
            && row.Version == header.Version).OrderBy(row => row.CustomerId).ThenBy(row => row.ProductId)
            .Take(ProductPackageStandardInputLimitsV2.MaximumRows + 1).ToListAsync(token);
        await Fence();
        if (header.Version <= 0 || string.IsNullOrWhiteSpace(header.CreatedBy)
            || lines.Count > ProductPackageStandardInputLimitsV2.MaximumRows || customers.Count > ProductPackageStandardInputLimitsV2.MaximumRows
            || lines.Count != header.EntryCount || customers.Count != header.CustomerEntryCount
            || lines.Any(row => row.ProductId == Guid.Empty || row.PiecesPerPackage <= 0)
            || customers.Any(row => row.ProductId == Guid.Empty || row.CustomerId == Guid.Empty || row.PiecesPerPackage <= 0))
            throw new InvalidOperationException("Package table proof unavailable.");
        var version = header.Version.ToString(CultureInfo.InvariantCulture);
        var link = header.EditedProductId is { } productId ? WorkInboxCompanyWideFenceV1.Link(ProductScreenPath, productId) : ProductScreenPath;
        List<WorkApprovalReferenceV1> references = [];
        if (header.EditedProductId is { } product) references.Add(new("product", WorkInboxCompanyWideFenceV1.ShortReference(product), product, link));
        if (header.EditedCustomerId is { } customer) references.Add(new("party", WorkInboxCompanyWideFenceV1.ShortReference(customer), customer, null));
        WorkApprovalRuleResultV1[] rules = [new(ApprovalRequiredRule, null, null, null, null),
            new(EntryCountRule, null, header.EntryCount.ToString(CultureInfo.InvariantCulture), null, null),
            new(CustomerEntryCountRule, null, header.CustomerEntryCount.ToString(CultureInfo.InvariantCulture), null, null)];
        var table = new WorkProductPackageStandardPreviewV1(header.SourceKind, header.SourceLabel, header.FileName,
            header.Reason, header.CreatedBy, header.CreatedAtUtc.ToUniversalTime(), header.EntryCount, header.CustomerEntryCount,
            [.. lines.Select(row => new WorkProductPackageStandardEntryV1(row.ProductId, null, row.PiecesPerPackage)),
             .. customers.Select(row => new WorkProductPackageStandardEntryV1(row.ProductId, row.CustomerId, row.PiecesPerPackage))]);
        return new(context.CompanyId, new(ModuleName, KindName, itemId, "v" + version, table.PreparedAtUtc, WorkInboxCompanyWideFenceV1.Link(WorkDecisionPath, itemId),
            header.CreatedBy, rules, references), version, !decided,
            ProductPackageStandardProof: new(context.CompanyId, itemId, version, table));

        async Task Fence()
        {
            var refusal = await fence.CheckAsync(context, Permissions, token);
            if (refusal?.Outcome == NativeApprovalQueueOutcomeV1.Denied) throw new UnauthorizedAccessException();
            if (refusal is not null) throw new InvalidOperationException("Package decision read unavailable.");
        }
    }
}
