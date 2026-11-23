using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;

/// <summary>
/// W1-A (AI-D31, AI-D32): the journal reversal proposals waiting for a decision (ACC-V1), as Accounting lists them for the
/// Work inbox. A proposal waits while its status is Pending (the decision refuses anything else) since it was proposed; it
/// is decided on the reversal approval screen by a company-wide holder of <c>accounting.journal.reverse.approve</c> other
/// than its proposer (<c>MakerCheckerRequired</c>); no other user is refused. The reversal proposals an invoiced-shipment
/// cost cancellation creates are the same records and are listed here too.
/// The decision command accepts only a company-wide grant (D11: no scoped approver).
/// </summary>
internal sealed class AccountingJournalReversalWorkInboxSourceV1(
    AccountingDbContext dbContext,
    AccountingWorkInboxFence fence) : IWorkInboxSourceV1
{
    public const string ModuleName = "accounting";
    public const string KindName = "accounting.journal-reversal.v1";
    public const string ScreenPath = "/muhasebe/ters-kayit-onay";
    public const string JournalScreenPath = "/muhasebe/fisler";

    /// <summary>A posted journal is reversed only with a different person's approval (maker ≠ checker).</summary>
    public const string ApprovalRequiredRule = "accounting.journal-reversal-approval-required";

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => AccountingJournalReversalPermissionNamesV1.Approve;

    public Task<WorkInboxSourceResultV1> ListPendingAsync(
        WorkInboxSourceContextV1 context,
        int maxCount,
        CancellationToken cancellationToken) =>
        fence.ListAsync(context, ApproverPermission, maxCount,
            token => dbContext.JournalReversalProposals.AsNoTracking()
                .Where(row => row.CompanyId == context.CompanyId
                    && row.Status == (int)AccountingJournalReversalProposalStatusV1.Pending)
                .OrderBy(row => row.ProposedAtUtc).ThenBy(row => row.Id)
                .Take(maxCount + 1)
                .ToListAsync(token),
            rows => AccountingWorkInboxFence.Page(rows, maxCount, Item),
            cancellationToken);

    private static WorkInboxItemV1? Item(AccountingJournalReversalProposalRow row)
    {
        if (row.Id == Guid.Empty || row.SourceJournalId == Guid.Empty || row.PeriodId == Guid.Empty
            || row.ProposedByUserId == Guid.Empty || row.DecidedByUserId is not null || row.ReversalJournalId is not null)
        {
            return null;
        }

        return new WorkInboxItemV1(ModuleName, KindName, row.Id, AccountingWorkInboxFence.ShortReference(row.Id),
            row.ProposedAtUtc.ToUniversalTime(), AccountingWorkInboxFence.Link(ScreenPath, row.Id),
            AccountingWorkInboxFence.Actor(row.ProposedByUserId),
            [new WorkApprovalRuleResultV1(ApprovalRequiredRule, null, null, null, null)],
            [
                new WorkApprovalReferenceV1("journal", AccountingWorkInboxFence.ShortReference(row.SourceJournalId),
                    row.SourceJournalId, AccountingWorkInboxFence.Link(JournalScreenPath, row.SourceJournalId)),
                new WorkApprovalReferenceV1("period", AccountingWorkInboxFence.ShortReference(row.PeriodId), row.PeriodId, null),
            ]);
    }
}
