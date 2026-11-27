using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

internal sealed partial class FinancePartyCreditLimitWorkInboxSourceV1
{
    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId,
        CancellationToken token)
    {
        await Fence();
        var row = await dbContext.PartyCreditLimits.AsNoTracking().SingleOrDefaultAsync(record =>
            record.CompanyId == context.CompanyId && record.Id == itemId && !record.IsDirect, token);
        await Fence();
        if (row is null) return null;
        var version = row.Version; var status = row.Status;
        var current = await dbContext.PartyCreditLimits.AsNoTracking().Where(record => record.CompanyId == context.CompanyId
            && record.CompanyAccountId == row.CompanyAccountId && record.Status == FinancePartyCreditLimitService.Approved).ToArrayAsync(token);
        await Fence();
        var revision = await dbContext.PartyCreditLimits.AsNoTracking().Where(record => record.CompanyId == context.CompanyId
            && record.CompanyAccountId == row.CompanyAccountId && record.AccountRevision != null)
            .MaxAsync(record => record.AccountRevision, token) ?? 0;
        await Fence();
        if (version < 1 || status is < 1 or > 4 || current.Length > 1
            || row.BaseRevision < 0 || revision < 0 || row.ProposedAtUtc == default
            || current.Any(record => record.PartyId != row.PartyId)
            || row.Kind == FinancePartyCreditLimitService.SetKind && !FinancePartyCreditLimitService.IsExactAmount(row.AmountText)
            || !TryItem(row, current.SingleOrDefault(), revision, out var item))
            throw new InvalidOperationException("Credit limit proposal proof is unavailable.");
        var final = await dbContext.PartyCreditLimits.AsNoTracking().Where(record => record.CompanyId == context.CompanyId
            && record.Id == itemId && !record.IsDirect).Select(record => new { record.Version, record.Status }).SingleOrDefaultAsync(token);
        await Fence();
        if (final is null || final.Version != version || final.Status != status)
            throw new InvalidOperationException("Credit limit proposal changed during its read.");
        var human = new WorkPartyCreditLimitPreviewV1(row.PartyId, row.CompanyAccountId,
            row.Kind == FinancePartyCreditLimitService.SetKind ? "Set" : "Remove", row.CurrencyId, row.AmountText,
            row.BaseRevision.ToString(CultureInfo.InvariantCulture), revision.ToString(CultureInfo.InvariantCulture),
            row.ProposedBy, row.ProposedAtUtc.ToUniversalTime());
        // Keep the original browse link until the paired purpose UI is published.
        return new(context.CompanyId, item, version.ToString(CultureInfo.InvariantCulture),
            status == FinancePartyCreditLimitService.Proposed, PartyCreditLimitProof: new(context.CompanyId, itemId, human));

        async Task Fence()
        {
            var refusal = await fence.CheckAsync(context, ApproverPermission, token);
            if (refusal?.Outcome == NativeApprovalQueueOutcomeV1.Denied) throw new UnauthorizedAccessException();
            if (refusal is not null) throw new InvalidOperationException("Credit limit decision read unavailable.");
        }
    }
}
