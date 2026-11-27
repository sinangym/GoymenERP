using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

internal sealed partial class FinanceManualBankPaymentWorkInboxSourceV1
{
    // Purpose read only. Terminal state is retained so the native command can replay its original approval receipt.
    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(
        WorkInboxSourceContextV1 context, Guid itemId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (itemId == Guid.Empty || await fence.CheckAsync(context, ApproverPermission, token) is not null) return null;
        var row = await dbContext.ManualBankPayments.AsNoTracking()
            .SingleOrDefaultAsync(r => r.CompanyId == context.CompanyId && r.Id == itemId, token);
        if (await fence.CheckAsync(context, ApproverPermission, token) is not null || row is null) return null;
        var banks = await dbContext.BankAccounts.AsNoTracking()
            .Where(r => r.CompanyId == context.CompanyId && r.Id == row.TreasuryAccountId)
            .ToDictionaryAsync(r => r.Id, r => r.DisplayName, token);
        var revisers = await FinanceDraftRevisers.ManualBankPaymentsAsync(dbContext, context.CompanyId, [itemId], token);
        if (await fence.CheckAsync(context, ApproverPermission, token) is not null) return null;
        if (row.Version < 1 || row.Version == long.MaxValue
            || row.Status is not (ManualBankPaymentStatuses.Draft or ManualBankPaymentStatuses.Approved or ManualBankPaymentStatuses.Recorded)
            || row.PreparedAtUtc == default || revisers is null || !TryItem(row, banks, revisers, out var item))
            throw new InvalidOperationException("Manual bank payment approval source is unavailable.");
        return new(context.CompanyId, item, row.Version.ToString(CultureInfo.InvariantCulture), row.Status == ManualBankPaymentStatuses.Draft);
    }
}
