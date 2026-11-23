using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;

/// <summary>
/// W1-A (AI-D31, AI-D32): the source document (invoice) voucher drafts waiting for approval (B-04b), as Accounting lists
/// them for the Work inbox. A draft waits while its status is 1 (draft) and its source invoice has no cancellation receipt
/// (<c>AccountingSourceVoucherRulesV1.RequireApproval</c>) since it was created; it is approved and posted on the source
/// vouchers screen by a company-wide holder of <c>accounting.normal-journal.draft.approve</c> (the permission the owner
/// binds to this approval). The owner refuses the person who finalized the source invoice ("Kaynak faturayı kesinleştiren
/// kişi fişini onaylayamaz"); the draft itself is made by the system from that invoice, so that person is its maker. No
/// one else is refused. The approve command accepts only a company-wide grant (D11: no scoped approver).
/// <para>
/// The rule carries the stored functional payable total as the actual value; Accounting stores the currency identity, not
/// its code, so the unit is left empty. When no open period covers the document date the approve command refuses
/// (<c>source_voucher_period_waiting</c>); that is shown as a second rule.
/// </para>
/// </summary>
internal sealed class AccountingSourceVoucherWorkInboxSourceV1(
    AccountingDbContext dbContext,
    AccountingWorkInboxFence fence) : IWorkInboxSourceV1
{
    public const string ModuleName = "accounting";
    public const string KindName = "accounting.source-voucher.v1";
    public const string ScreenPath = "/muhasebe/kaynak-fisler";

    /// <summary>The stored draft status the approve command accepts.</summary>
    internal const int DraftStatus = 1;

    /// <summary>The stored status of an open accounting period.</summary>
    internal const int OpenPeriodStatus = 1;

    /// <summary>An invoice's voucher is posted only with an approval by someone other than the invoice's finalizer.</summary>
    public const string ApprovalRequiredRule = "accounting.source-voucher-approval-required";

    /// <summary>No open period covers the document date yet; approval would be refused until one opens.</summary>
    public const string PeriodWaitingRule = "accounting.source-voucher-period-waiting";

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => NormalJournalPermissionNamesV1.DraftApprove;

    public Task<WorkInboxSourceResultV1> ListPendingAsync(
        WorkInboxSourceContextV1 context,
        int maxCount,
        CancellationToken cancellationToken) =>
        fence.ListAsync(context, ApproverPermission, maxCount,
            token =>
            {
                var company = context.CompanyId;
                var cancellations = dbContext.SourceInvoiceCancellations.AsNoTracking().Where(row => row.CompanyId == company);
                var openPeriods = dbContext.Periods.AsNoTracking()
                    .Where(row => row.CompanyId == company && row.Status == OpenPeriodStatus);
                return dbContext.SourceDocumentVoucherDrafts.AsNoTracking()
                    .Where(row => row.CompanyId == company && row.Status == DraftStatus
                        && !cancellations.Any(cancellation => cancellation.SourceDocumentId == row.SourceDocumentId))
                    .OrderBy(row => row.CreatedAtUtc).ThenBy(row => row.Id)
                    .Select(row => new Pending(row,
                        openPeriods.Any(period => period.StartsOn <= row.DocumentDate && period.EndsOn >= row.DocumentDate)))
                    .Take(maxCount + 1)
                    .ToListAsync(token);
            },
            rows => AccountingWorkInboxFence.Page(rows, maxCount, Item),
            cancellationToken);

    private static WorkInboxItemV1? Item(Pending pending)
    {
        var row = pending.Row;
        if (row.Id == Guid.Empty || row.SourceDocumentId == Guid.Empty || row.InvoiceId == Guid.Empty
            || row.SourceActorId == Guid.Empty || row.PartyId == Guid.Empty || row.CurrencyId == Guid.Empty
            || row.ApprovedBy is not null || row.JournalId is not null)
        {
            return null;
        }

        var rules = new List<WorkApprovalRuleResultV1>(2)
        {
            new(ApprovalRequiredRule, null, WorkInboxRulesV1.Decimal(row.FunctionalPayable), null, null),
        };
        if (!pending.PeriodOpen)
        {
            rules.Add(new(PeriodWaitingRule, null, null, null, null));
        }

        var label = AccountingWorkInboxFence.Label(row.OfficialInvoiceNo, row.InvoiceId);
        return new WorkInboxItemV1(ModuleName, KindName, row.Id, label, row.CreatedAtUtc.ToUniversalTime(), ScreenPath,
            AccountingWorkInboxFence.Actor(row.SourceActorId), rules,
            [
                new WorkApprovalReferenceV1("invoice", label, row.InvoiceId, null),
                new WorkApprovalReferenceV1("source-document", AccountingWorkInboxFence.ShortReference(row.SourceDocumentId),
                    row.SourceDocumentId, null),
                new WorkApprovalReferenceV1("party", AccountingWorkInboxFence.ShortReference(row.PartyId), row.PartyId, null),
                new WorkApprovalReferenceV1("currency", AccountingWorkInboxFence.ShortReference(row.CurrencyId), row.CurrencyId, null),
            ]);
    }

    private sealed record Pending(AccountingSourceDocumentVoucherDraftRow Row, bool PeriodOpen);
}
