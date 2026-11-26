using GoymenERP.Modules.Logistics.Domain;
using GoymenERP.Modules.Logistics.Domain.Loading;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence;

public sealed partial class LogisticsStore
{
    private async Task RequireNewShareIdentitiesAsync(Guid companyId, IEnumerable<LoadingShare> shares,
        CancellationToken cancellationToken)
    {
        var identities = shares.Select(share => share.ShareId).ToArray();
        if (identities.Length == 0) return;
        // No status filter: cancellation never releases an existing movement identity (LOJ-D21).
        if (await dbContext.Shares.AsNoTracking().AnyAsync(share => share.CompanyId == companyId
                && identities.Contains(share.ShareId), cancellationToken))
        {
            dbContext.ChangeTracker.Clear();
            throw new LogisticsRuleException(LogisticsRuleCodes.DuplicateShareId, "Bu hareket payı kimliği aynı şirkette zaten kayıtlı.");
        }
    }
}
