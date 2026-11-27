using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

internal sealed partial class FinanceBankAccountWorkInboxSourceV1
{
    // Exact company/id only; retained proposer facts survive later IBAN changes. No extra master-read grant or card disclosure.
    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (itemId == Guid.Empty || await fence.CheckAsync(context, ApproverPermission, token) is not null) return null;
        var row = await dbContext.BankAccounts.AsNoTracking().SingleOrDefaultAsync(r => r.CompanyId == context.CompanyId && r.Id == itemId, token);
        if (await fence.CheckAsync(context, ApproverPermission, token) is not null || row is null) return null;
        if (row.Version < 1 || row.Status is not (FinanceBankAccountService.Proposed or FinanceBankAccountService.Approved)
            || row.ProposedAtUtc == default || row.ProposedAtUtc.Offset != TimeSpan.Zero || string.IsNullOrWhiteSpace(row.DisplayName)
            || row.DisplayName.Length > 200 || Item(row, false) is not { } item)
            throw new InvalidOperationException("Bank account approval source is unavailable.");
        var view = FinanceBankAccountService.View(row);
        var proof = new WorkBankAccountProofV1(row.CompanyId, view.Id, view.CurrencyId, view.DisplayName,
            view.IsActive, view.IsUsableForReceipt, row.ProposedBy, row.ProposedAtUtc);
        return new(context.CompanyId, item, row.Version.ToString(CultureInfo.InvariantCulture), row.Status == FinanceBankAccountService.Proposed,
            BankAccountProof: proof);
    }
}
