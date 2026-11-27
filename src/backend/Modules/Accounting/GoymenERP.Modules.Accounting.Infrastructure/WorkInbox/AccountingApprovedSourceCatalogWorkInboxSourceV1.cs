using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;

/// <summary>The exact Prepared native packages. Separate activation authority deliberately permits the preparer.</summary>
internal sealed partial class AccountingApprovedSourceCatalogWorkInboxSourceV1(AccountingDbContext dbContext, AccountingWorkInboxFence fence,
    IApprovedAccountingSourceCatalogDecisionReadV1? owner = null) : IWorkInboxSourceV1
{
    public const string ModuleName = AccountingApprovedSourceCatalogDecisionBindingV1.ModuleName;
    public const string KindName = AccountingApprovedSourceCatalogDecisionBindingV1.KindName;
    public const string ScreenPath = AccountingApprovedSourceCatalogDecisionBindingV1.PurposePath;
    public string Module => ModuleName;
    public string Kind => KindName;
    public string ApproverPermission => ApprovedAccountingSourceCatalogPermissionNamesV1.Activate;
    public async Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 context, int maxCount, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context); ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount); token.ThrowIfCancellationRequested();
        if (context.GrantScope is not null) return WorkInboxSourceResultV1.Denied();
        var answer = await fence.ListAsync(context, ApproverPermission, maxCount, async ct =>
            await dbContext.ApprovedSourceCatalogs.AsNoTracking()
                .Where(row => row.CompanyId == context.CompanyId && row.Status == (int)ApprovedAccountingSourceCatalogStatusV1.Prepared)
                .OrderBy(row => row.PreparedAtUtc).ThenBy(row => row.Id).Take(checked(maxCount + 1))
                .Select(row => new PendingCatalog(row.CompanyId, row.Id, row.Version, row.PreparedByUserId, row.PreparedAtUtc,
                    row.CatalogVersion, row.ActivatedByUserId, row.ActivatedAtUtc)).ToListAsync(ct),
            rows => AccountingWorkInboxFence.Page(rows, maxCount, row => row.CompanyId != context.CompanyId || row.Version <= 0
                || row.CatalogVersion is not null || row.ActivatedBy is not null || row.ActivatedAtUtc is not null ? null
                : AccountingApprovedSourceCatalogDecisionBindingV1.Item(row.PackageId, row.PreparedBy, row.PreparedAtUtc)), token);
        token.ThrowIfCancellationRequested(); return answer;
    }
    private sealed record PendingCatalog(Guid CompanyId, Guid PackageId, long Version, Guid PreparedBy, DateTimeOffset PreparedAtUtc,
        long? CatalogVersion, Guid? ActivatedBy, DateTimeOffset? ActivatedAtUtc);
}
