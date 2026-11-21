using System.Data;
using GoymenERP.Modules.Accounting.Application;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class AccountingPostingAccountMappingStoreV1
{
    public async Task<AccountingPostingAccountMappingWorkspaceV1> WorkspaceAsync(CancellationToken cancellationToken = default)
    {
        RequireCompany();
        var captured = companyContext.CompanyId;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var plan = await ActivePlanAsync(cancellationToken);
        var latest = await dbContext.PostingAccountMappingVersions.AsNoTracking().Select(row => (long?)row.MappingVersion)
            .MaxAsync(cancellationToken) ?? 0;
        var versions = await dbContext.PostingAccountMappingVersions.AsNoTracking()
            .Where(row => row.Status == Active || row.Status == Prepared).OrderByDescending(row => row.MappingVersion)
            .ToListAsync(cancellationToken);
        var records = new List<AccountingPostingAccountMappingViewV1>();
        foreach (var version in versions)
            records.Add(AccountingPostingAccountMappingServiceV1.View(await RestoreAsync(version, cancellationToken)));
        RequireCompany();
        if (companyContext.CompanyId != captured || plan is not null && plan.CompanyId != captured)
            throw new AccountingStoreException(AccountingStoreCodes.CompanyContextMismatch, "The mapping workspace company changed.");
        var result = new AccountingPostingAccountMappingWorkspaceV1(captured, latest,
            plan is null ? "Unavailable" : "Available", plan?.SourceVersion, plan?.SourceSha256,
            plan?.Rows.OrderBy(row => row.Code, StringComparer.Ordinal)
                .Select(row => new AccountingPostingMappingAccountV1(row.Account.AccountId, row.Code, row.Name)).ToArray() ?? [],
            AccountingCogsMappingDefaultsV1.FromApprovedPlan(plan),
            records.SingleOrDefault(row => row.Status == nameof(AccountingPostingAccountMappingStatusV1.Active)),
            records.Where(row => row.Status == nameof(AccountingPostingAccountMappingStatusV1.Prepared)).ToArray());
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
