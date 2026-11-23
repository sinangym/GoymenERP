using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

/// <summary>
/// W1-A (AI-D31, AI-D32, YOL-D18): the proposals to reverse a recorded manual bank receipt (FIN-T06/B1-B2) waiting for a
/// decision, as Finance lists them for the Work inbox. A proposal waits while its status is Pending (the approve and
/// reject commands accept only a pending proposal) from the moment it was proposed; it is never edited. It is decided on
/// the reversal approval screen by a holder of <c>finance.manual-receipt.reverse.approve</c> other than the proposer (the
/// approve command refuses only the proposer). The owner's permission port answers only a company-wide grant, so scoped
/// approvers are not accepted. Every pending proposal of the company is returned, the caller's own included: Work hides a
/// maker's own item.
/// <para>
/// The one rule carries the original receipt's stored amount as the actual value; no threshold exists. A proposal whose
/// original receipt Finance does not find is unreadable as the owner's invariants say, so the answer is Unavailable.
/// </para>
/// </summary>
internal sealed class FinanceManualReceiptReversalWorkInboxSourceV1(
    FinanceDbContext dbContext,
    FinanceWorkInboxFence fence) : IWorkInboxSourceV1
{
    public const string ModuleName = "finance";
    public const string KindName = "finance.manual-receipt-reversal.v1";
    public const string ScreenPath = "/finans/ters-kayit-onaylari";

    /// <summary>The receipt's own screen (<c>/finans/tahsilatlar/{sourceId}</c>), linked from the receipt reference.</summary>
    public const string ReceiptScreenPath = "/finans/tahsilatlar";

    /// <summary>Every reversal of a recorded manual receipt needs one approval by someone other than its proposer.</summary>
    public const string ApprovalRequiredRule = "finance.manual-receipt-reversal-approval-required";

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => FinancePermissionNames.ReverseApprove;

    public Task<WorkInboxSourceResultV1> ListPendingAsync(
        WorkInboxSourceContextV1 context,
        int maxCount,
        CancellationToken cancellationToken) =>
        FinanceWorkInboxRead.ListAsync(fence, context, ApproverPermission, maxCount, ReadAsync, Item, cancellationToken);

    private async Task<(List<ReversalProposalRow> Rows, Lookup Lookup)> ReadAsync(
        Guid companyId, int maxCount, CancellationToken cancellationToken)
    {
        var rows = await dbContext.ReversalProposals.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.Status == ReversalProposalStatuses.Pending)
            .OrderBy(row => row.ProposedAtUtc).ThenBy(row => row.Id)
            .Take(maxCount + 1)
            .ToListAsync(cancellationToken);
        var receiptIds = rows.Take(maxCount).Select(row => row.OriginalSourceId).Distinct().ToArray();
        var receipts = await dbContext.ReceiptDrafts.AsNoTracking()
            .Where(row => row.CompanyId == companyId && receiptIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        var bankIds = receipts.Values.Select(row => row.TreasuryAccountId).Distinct().ToArray();
        var banks = await dbContext.BankAccounts.AsNoTracking()
            .Where(row => row.CompanyId == companyId && bankIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.DisplayName, cancellationToken);
        return (rows, new Lookup(receipts, banks));
    }

    private static WorkInboxItemV1? Item(ReversalProposalRow row, Lookup lookup)
    {
        if (row.Id == Guid.Empty || row.OriginalSourceId == Guid.Empty || string.IsNullOrWhiteSpace(row.ProposedBy)
            || !lookup.Receipts.TryGetValue(row.OriginalSourceId, out var receipt)
            || receipt.PartyId == Guid.Empty || receipt.TreasuryAccountId == Guid.Empty || receipt.CurrencyId == Guid.Empty
            || FinanceWorkInboxRead.Amount(receipt.AmountText) is not { } amount)
        {
            return null;
        }

        return new WorkInboxItemV1(ModuleName, KindName, row.Id, FinanceWorkInboxRead.Label(receipt.DocumentNumber, row.Id),
            row.ProposedAtUtc.ToUniversalTime(), ScreenPath, row.ProposedBy,
            [new WorkApprovalRuleResultV1(ApprovalRequiredRule, null, amount, null, null)],
            [
                FinanceWorkInboxRead.Reference("manual-receipt", receipt.Id,
                    FinanceWorkInboxRead.Label(receipt.DocumentNumber, receipt.Id),
                    FinanceWorkInboxFence.Link(ReceiptScreenPath, receipt.Id)),
                FinanceWorkInboxRead.Reference("party", receipt.PartyId),
                FinanceWorkInboxRead.Reference("bank-account", receipt.TreasuryAccountId,
                    FinanceWorkInboxRead.Name(lookup.Banks.GetValueOrDefault(receipt.TreasuryAccountId), receipt.TreasuryAccountId)),
                FinanceWorkInboxRead.Reference("currency", receipt.CurrencyId),
            ]);
    }

    private sealed record Lookup(
        IReadOnlyDictionary<Guid, ReceiptDraftRow> Receipts,
        IReadOnlyDictionary<Guid, string> Banks);
}
