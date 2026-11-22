using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;

/// <summary>
/// W1-A (AI-D31, AI-D32): the normal journal drafts waiting for approval, as Accounting lists them for the Work inbox. A
/// draft waits while its status is Draft (the approve command refuses anything else, <c>..._draft_not_pending</c>) since it
/// was prepared; it is approved and posted on the draft's approval screen by a company-wide holder of
/// <c>accounting.normal-journal.draft.approve</c>.
/// <para>
/// Maker ≠ checker as the owner enforces it: the normal approval policy deliberately permits self-approval with the approve
/// grant, so an ordinary draft names no maker (<see cref="WorkInboxItemV1.PreparedBy"/> null). Only a draft whose source is
/// a credit movement proposal refuses its preparer (Q9, <c>accounting_credit_journal_self_approval</c>); there the preparer
/// is the maker. The approve command accepts only a company-wide grant (D11: no scoped approver).
/// </para>
/// </summary>
internal sealed class AccountingNormalJournalDraftWorkInboxSourceV1(
    AccountingDbContext dbContext,
    AccountingWorkInboxFence fence) : IWorkInboxSourceV1
{
    public const string ModuleName = "accounting";
    public const string KindName = "accounting.normal-journal-draft.v1";
    public const string ScreenPath = "/muhasebe/normal-fis-onay";

    /// <summary>A normal journal draft is posted only with the approve grant.</summary>
    public const string ApprovalRequiredRule = "accounting.normal-journal-draft-approval-required";

    /// <summary>The draft comes from a credit movement: its preparer may not approve it (Q9).</summary>
    public const string CreditMakerCheckerRule = "accounting.credit-journal-maker-checker";

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => NormalJournalPermissionNamesV1.DraftApprove;

    public Task<WorkInboxSourceResultV1> ListPendingAsync(
        WorkInboxSourceContextV1 context,
        int maxCount,
        CancellationToken cancellationToken) =>
        fence.ListAsync(context, ApproverPermission, maxCount,
            async token =>
            {
                var company = context.CompanyId;
                var drafts = await dbContext.NormalJournalDrafts.AsNoTracking()
                    .Where(row => row.CompanyId == company && row.Status == (int)NormalJournalDraftStatusV1.Draft)
                    .OrderBy(row => row.PreparedAtUtc).ThenBy(row => row.Id)
                    .Take(maxCount + 1)
                    .ToListAsync(token);
                var sourceIds = drafts.Take(maxCount).Select(row => row.SourceId).Distinct().ToArray();
                var creditSources = await dbContext.CreditMovementProposals.AsNoTracking()
                    .Where(row => row.CompanyId == company && sourceIds.Contains(row.Id))
                    .Select(row => row.Id)
                    .ToListAsync(token);
                return (drafts, credit: creditSources.ToHashSet());
            },
            snapshot => AccountingWorkInboxFence.Page(snapshot.drafts, maxCount,
                row => Item(row, snapshot.credit.Contains(row.SourceId))),
            cancellationToken);

    private static WorkInboxItemV1? Item(NormalJournalDraftRow row, bool creditSource)
    {
        if (row.Id == Guid.Empty || row.PreparedByUserId == Guid.Empty || row.SourceId == Guid.Empty
            || row.CurrencyId == Guid.Empty || row.ApprovedByUserId is not null || row.PostedJournalId is not null)
        {
            return null;
        }

        var rules = new List<WorkApprovalRuleResultV1>(2) { new(ApprovalRequiredRule, null, null, null, null) };
        var references = new List<WorkApprovalReferenceV1>(2)
        {
            creditSource
                ? new("credit-movement-proposal", AccountingWorkInboxFence.ShortReference(row.SourceId), row.SourceId, null)
                : new("source-record", AccountingWorkInboxFence.ShortReference(row.SourceId), row.SourceId, null),
            new("currency", AccountingWorkInboxFence.ShortReference(row.CurrencyId), row.CurrencyId, null),
        };
        if (creditSource)
        {
            rules.Add(new(CreditMakerCheckerRule, null, null, null, null));
        }

        return new WorkInboxItemV1(ModuleName, KindName, row.Id, AccountingWorkInboxFence.ShortReference(row.Id),
            row.PreparedAtUtc.ToUniversalTime(), AccountingWorkInboxFence.Link(ScreenPath, row.Id),
            creditSource ? AccountingWorkInboxFence.Actor(row.PreparedByUserId) : null, rules, references);
    }
}
