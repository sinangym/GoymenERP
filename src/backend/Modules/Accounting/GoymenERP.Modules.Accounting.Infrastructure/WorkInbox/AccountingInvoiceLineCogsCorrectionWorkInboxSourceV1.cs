using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;

/// <summary>
/// W1-A (AI-D31, AI-D32): the invoice line COGS corrections prepared and waiting for approval, as Accounting lists them for
/// the Work inbox. A proposal waits while no approved revision names it and the line's latest revision is still the one it
/// was prepared on (the approve command refuses an approved proposal and a stale line version); it waits since it was
/// prepared. It is approved on the invoice line COGS approval screen by a company-wide holder of
/// <c>accounting.journal.reverse.approve</c> (the permission the owner binds to this approval) other than its preparer
/// (<c>SelfApproval</c>); no other user is refused.
/// <para>
/// The approve command also re-reads the finalized invoice line from its owner; that check stays on the owner's screen. The
/// approve command accepts only a company-wide grant (D11: no scoped approver).
/// </para>
/// </summary>
internal sealed class AccountingInvoiceLineCogsCorrectionWorkInboxSourceV1(
    AccountingDbContext dbContext,
    AccountingWorkInboxFence fence) : IWorkInboxSourceV1
{
    public const string ModuleName = "accounting";
    public const string KindName = "accounting.invoice-line-cogs-correction.v1";
    public const string ScreenPath = "/muhasebe/fatura-satiri-smm-onay";

    /// <summary>A COGS account correction of an invoice line is posted only with a different person's approval.</summary>
    public const string ApprovalRequiredRule = "accounting.invoice-line-cogs-correction-approval-required";

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => AccountingJournalReversalPermissionNamesV1.Approve;

    public Task<WorkInboxSourceResultV1> ListPendingAsync(
        WorkInboxSourceContextV1 context,
        int maxCount,
        CancellationToken cancellationToken) =>
        fence.ListAsync(context, ApproverPermission, maxCount,
            token =>
            {
                var company = context.CompanyId;
                var revisions = dbContext.InvoiceLineCogsRevisions.AsNoTracking().Where(revision => revision.CompanyId == company);
                return dbContext.InvoiceLineCogsProposals.AsNoTracking()
                    .Where(row => row.CompanyId == company
                        && !revisions.Any(revision => revision.ProposalId == row.Id)
                        && (revisions.Where(revision => revision.InvoiceId == row.InvoiceId
                                && revision.InvoiceLineId == row.InvoiceLineId)
                            .Max(revision => (long?)revision.Version) ?? 0) == row.ExpectedLineVersion)
                    .OrderBy(row => row.PreparedAtUtc).ThenBy(row => row.Id)
                    .Take(maxCount + 1)
                    .ToListAsync(token);
            },
            rows => AccountingWorkInboxFence.Page(rows, maxCount, Item),
            cancellationToken);

    private static WorkInboxItemV1? Item(InvoiceLineCogsProposalRow row)
    {
        if (row.Id == Guid.Empty || row.InvoiceId == Guid.Empty || row.InvoiceLineId == Guid.Empty
            || row.PreparedBy == Guid.Empty || row.ExpectedLineVersion < 0)
        {
            return null;
        }

        return new WorkInboxItemV1(ModuleName, KindName, row.Id, AccountingWorkInboxFence.ShortReference(row.Id),
            row.PreparedAtUtc.ToUniversalTime(), ScreenPath, AccountingWorkInboxFence.Actor(row.PreparedBy),
            [new WorkApprovalRuleResultV1(ApprovalRequiredRule, null, null, null, null)],
            [
                new WorkApprovalReferenceV1("invoice", AccountingWorkInboxFence.ShortReference(row.InvoiceId), row.InvoiceId, null),
                new WorkApprovalReferenceV1("invoice-line", AccountingWorkInboxFence.ShortReference(row.InvoiceLineId),
                    row.InvoiceLineId, null),
            ]);
    }
}
