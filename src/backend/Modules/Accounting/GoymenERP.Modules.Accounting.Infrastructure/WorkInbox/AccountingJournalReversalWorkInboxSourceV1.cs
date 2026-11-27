using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;

/// <summary>
/// W1-A (AI-D31, AI-D32): the journal reversal proposals waiting for a decision (ACC-V1), as Accounting lists them for the
/// Work inbox. A proposal waits while its status is Pending (the decision refuses anything else) since it was proposed; it
/// is decided on the reversal approval screen by a company-wide holder of <c>accounting.journal.reverse.approve</c> other
/// than its proposer (<c>MakerCheckerRequired</c>); KLN-D17 additionally excludes the actual invoice cancellation source actor.
/// Purpose reads obtain that additional actor from the same owner predicate as the native command. The reversal proposals an invoiced-shipment
/// cost cancellation creates are the same records and are listed here too.
/// The decision command accepts only a company-wide grant (D11: no scoped approver).
/// </summary>
internal sealed partial class AccountingJournalReversalWorkInboxSourceV1(
    AccountingDbContext dbContext,
    AccountingWorkInboxFence fence,
    IAccountingJournalReversalDecisionReadV1? owner = null) : IWorkInboxSourceV1
{
    public const string ModuleName = "accounting";
    public const string KindName = "accounting.journal-reversal.v1";
    public const string ScreenPath = "/onaylarim/accounting.journal-reversal.v1";
    public const string JournalScreenPath = "/muhasebe/fisler";

    /// <summary>A posted journal is reversed only with a different person's approval (maker ≠ checker).</summary>
    public const string ApprovalRequiredRule = "accounting.journal-reversal-approval-required";

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => AccountingJournalReversalPermissionNamesV1.Approve;

    public async Task<WorkInboxSourceResultV1> ListPendingAsync(
        WorkInboxSourceContextV1 context,
        int maxCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context); ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);
        cancellationToken.ThrowIfCancellationRequested();
        if (context.GrantScope is not null) return WorkInboxSourceResultV1.Denied();
        var answer = await fence.ListAsync(context, ApproverPermission, maxCount,
            async token =>
            {
                var rows = await dbContext.JournalReversalProposals.AsNoTracking()
                    .Where(row => row.CompanyId == context.CompanyId
                        && row.Status == (int)AccountingJournalReversalProposalStatusV1.Pending)
                    .OrderBy(row => row.ProposedAtUtc).ThenBy(row => row.Id)
                    .Take(maxCount + 1).ToListAsync(token);
                var journals = rows.Take(maxCount).Select(row => row.SourceJournalId).Distinct().ToArray();
                if (journals.Length == 0) return new PendingPage(rows, []);
                await FreshReadFence(token);
                // Same company-bound KLN-D17 predicate as native approval; one bulk read, never N+1 human reads.
                var actors = await dbContext.SourceDocumentVoucherDrafts.AsNoTracking()
                    .Where(voucher => voucher.CompanyId == context.CompanyId && voucher.Status == 2
                        && voucher.JournalId != null && journals.Contains(voucher.JournalId.Value)
                        && dbContext.SourceInvoiceCancellations.Any(cancellation => cancellation.CompanyId == context.CompanyId
                            && cancellation.SourceDocumentId == voucher.SourceDocumentId && cancellation.InvoiceId == voucher.InvoiceId
                            && cancellation.FinalizedVersion == voucher.FinalizedVersion))
                    .Select(voucher => new PendingCancellationActor(voucher.JournalId!.Value, voucher.SourceActorId))
                    .ToListAsync(token);
                await FreshReadFence(token);
                return new PendingPage(rows, actors);
            },
            page => BuildPendingPage(page.Rows, page.Actors, maxCount), cancellationToken);
        // The existing shared fence can receive a normal-completing, cancelled provider response.
        cancellationToken.ThrowIfCancellationRequested();
        return answer;
        async Task FreshReadFence(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (await fence.CheckAsync(context, ApproverPermission, token) is { } refused)
            {
                if (refused.Outcome == NativeApprovalQueueOutcomeV1.Denied) throw new UnauthorizedAccessException();
                throw new InvalidOperationException("Journal reversal pending authority unavailable.");
            }
            token.ThrowIfCancellationRequested();
        }
    }

    internal static (IReadOnlyList<WorkInboxItemV1>? Items, bool HasMore) BuildPendingPage(
        IReadOnlyList<AccountingJournalReversalProposalRow> rows, IReadOnlyList<PendingCancellationActor> actors, int maxCount)
    {
        var journalIds = rows.Take(maxCount).Select(row => row.SourceJournalId).ToHashSet();
        var groups = actors.GroupBy(actor => actor.JournalId).ToArray();
        // Approved voucher JournalId equals its primary Id. Never guess if malformed/legacy rows violate uniqueness.
        if (groups.Any(group => group.Count() != 1) || actors.Any(actor => actor.ActorId == Guid.Empty || !journalIds.Contains(actor.JournalId)))
            return (null, false);
        var actorByJournal = groups.ToDictionary(group => group.Key, group => group.Single().ActorId);
        return AccountingWorkInboxFence.Page(rows, maxCount, row =>
            Item(row, actorByJournal.TryGetValue(row.SourceJournalId, out var actor) ? actor : null));
    }
    internal sealed record PendingCancellationActor(Guid JournalId, Guid ActorId);
    private sealed record PendingPage(IReadOnlyList<AccountingJournalReversalProposalRow> Rows, IReadOnlyList<PendingCancellationActor> Actors);

    private static WorkInboxItemV1? Item(AccountingJournalReversalProposalRow row, Guid? cancellationActor)
    {
        if (row.Id == Guid.Empty || row.SourceJournalId == Guid.Empty || row.PeriodId == Guid.Empty
            || row.ProposedByUserId == Guid.Empty || row.DecidedByUserId is not null || row.ReversalJournalId is not null)
        {
            return null;
        }

        return IdentityItem(row.Id, row.SourceJournalId, row.PeriodId, row.ProposedByUserId, row.ProposedAtUtc, cancellationActor);
    }

    private static WorkInboxItemV1? IdentityItem(Guid proposalId, Guid sourceJournalId, Guid periodId,
        Guid proposedBy, DateTimeOffset proposedAtUtc, Guid? cancellationActor)
    {
        if (proposalId == Guid.Empty || sourceJournalId == Guid.Empty || periodId == Guid.Empty || proposedBy == Guid.Empty
            || cancellationActor == Guid.Empty) return null;
        return new WorkInboxItemV1(ModuleName, KindName, proposalId, AccountingWorkInboxFence.ShortReference(proposalId),
            proposedAtUtc.ToUniversalTime(), AccountingWorkInboxFence.Link(ScreenPath, proposalId),
            AccountingWorkInboxFence.Actor(proposedBy),
            [new WorkApprovalRuleResultV1(ApprovalRequiredRule, null, null, null, null)],
            [
                new WorkApprovalReferenceV1("journal", AccountingWorkInboxFence.ShortReference(sourceJournalId),
                    sourceJournalId, AccountingWorkInboxFence.Link(JournalScreenPath, sourceJournalId)),
                new WorkApprovalReferenceV1("period", AccountingWorkInboxFence.ShortReference(periodId), periodId, null),
            ], cancellationActor is { } actor && actor != proposedBy ? [AccountingWorkInboxFence.Actor(actor)] : null);
    }
}
