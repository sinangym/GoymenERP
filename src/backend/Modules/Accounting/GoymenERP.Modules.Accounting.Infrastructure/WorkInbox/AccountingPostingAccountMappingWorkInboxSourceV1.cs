using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;

/// <summary>Bounded scalar-only Prepared projection; full rows and account-plan facts stay in the purpose owner read.</summary>
internal sealed partial class AccountingPostingAccountMappingWorkInboxSourceV1(AccountingDbContext dbContext, AccountingWorkInboxFence fence,
    IAccountingPostingAccountMappingDecisionReadV1? owner = null) : IWorkInboxSourceV1
{
    public const string ModuleName = AccountingPostingAccountMappingDecisionBindingV1.ModuleName;
    public const string KindName = AccountingPostingAccountMappingDecisionBindingV1.KindName;
    public const string ScreenPath = AccountingPostingAccountMappingDecisionBindingV1.NativeScreenPath;
    public const string WorkDecisionPath = AccountingPostingAccountMappingDecisionBindingV1.PurposePath;
    public string Module => ModuleName;
    public string Kind => KindName;
    public string ApproverPermission => AccountingPostingAccountMappingPermissionNamesV1.Activate;
    public async Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 context, int maxCount, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context); ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount); token.ThrowIfCancellationRequested();
        if (context.GrantScope is not null) return WorkInboxSourceResultV1.Denied();
        var answer = await fence.ListAsync(context, ApproverPermission, maxCount, async ct =>
            await dbContext.PostingAccountMappingVersions.AsNoTracking()
                .Where(row => row.CompanyId == context.CompanyId && row.Status == (int)AccountingPostingAccountMappingStatusV1.Prepared)
                .OrderBy(row => row.PreparedAtUtc).ThenBy(row => row.MappingVersion).Take(checked(maxCount + 1))
                .Select(row => new PendingMapping(row.CompanyId, row.MappingVersion, row.Version, row.PreparedByUserId, row.PreparedAtUtc,
                    row.ActivatedByUserId, row.ActivatedAtUtc)).ToListAsync(ct),
            rows => AccountingWorkInboxFence.Page(rows, maxCount, row => row.CompanyId != context.CompanyId || row.Version != 1
                || row.ActivatedBy is not null || row.ActivatedAtUtc is not null ? null
                : AccountingPostingAccountMappingDecisionBindingV1.Item(row.MappingVersion, row.PreparedBy, row.PreparedAtUtc)), token);
        token.ThrowIfCancellationRequested(); return answer;
    }
    private sealed record PendingMapping(Guid CompanyId, long MappingVersion, long Version, Guid PreparedBy, DateTimeOffset PreparedAtUtc,
        Guid? ActivatedBy, DateTimeOffset? ActivatedAtUtc);
}
