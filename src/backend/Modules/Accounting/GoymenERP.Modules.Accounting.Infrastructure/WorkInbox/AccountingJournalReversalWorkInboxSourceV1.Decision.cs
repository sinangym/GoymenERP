using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;

internal sealed partial class AccountingJournalReversalWorkInboxSourceV1 : IWorkApprovalDecisionSourceV1
{
    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context); await Fence(token);
        if (owner is null) throw new InvalidOperationException("Journal reversal decision owner unavailable.");
        var answer = await owner.ReadDecisionAsync(itemId, Fence, token); await Fence(token);
        if (answer is null) throw new InvalidOperationException("Journal reversal decision answer unavailable.");
        if (answer.Status == AccountingApiStatus.Denied && answer.Resource is null) throw new UnauthorizedAccessException();
        if (answer.Status == AccountingApiStatus.NotFound && answer.Resource is null) return null;
        if (answer.Status != AccountingApiStatus.Ok || answer.Resource is not { } detail || !ValidDetail(detail)
            || detail.Proposal.CompanyId != context.CompanyId || detail.Proposal.ProposalId != itemId
            || DecisionItem(detail.Proposal, detail.CancellationSourceActorId) is not { } item)
            throw new InvalidOperationException("Journal reversal decision facts unavailable.");
        if (item.Excludes(context.UserId)) throw new UnauthorizedAccessException();
        var view = detail.Proposal;
        return new(context.CompanyId, item, view.Version.ToString(CultureInfo.InvariantCulture), view.Status == "Pending")
        {
            JournalReversalProof = new(context.CompanyId, itemId, view.SourceJournalId, view.PeriodId, view.ProposedBy,
                view.ProposedAtUtc, view.Status, view.Version, detail.CancellationSourceActorId, detail.SourceFactsDigest, detail.PreviewDigest),
        };
        async Task Fence(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (context.GrantScope is not null) throw new UnauthorizedAccessException();
            var refusal = await fence.CheckAsync(context, ApproverPermission, ct); ct.ThrowIfCancellationRequested();
            if (refusal?.Outcome == NativeApprovalQueueOutcomeV1.Denied) throw new UnauthorizedAccessException();
            if (refusal is not null) throw new InvalidOperationException("Journal reversal authority unavailable.");
        }
    }
    internal static bool ValidDetail(AccountingJournalReversalDecisionDetailV1 detail) =>
        detail.Proposal is { } view && detail.CancellationSourceActorProofEstablished && ValidView(view)
        && detail.CancellationSourceActorId != Guid.Empty
        && detail.SourceFactsDigest == AccountingJournalReversalServiceV1.JournalReversalSourceFactsDigest(view)
        && detail.PreviewDigest == AccountingJournalReversalServiceV1.JournalReversalPreviewDigest(view);
    internal static bool ValidView(AccountingJournalReversalProposalViewV1 view) =>
        view.CompanyId != Guid.Empty && view.ProposalId != Guid.Empty && view.SourceJournalId != Guid.Empty && view.PeriodId != Guid.Empty
        && view.ProposedBy != Guid.Empty && view.ProposedAtUtc.Offset == TimeSpan.Zero && view.Version > 0
        && view.TransactionDate != default && view.AccountingDate != default && (view.ValueDate is null || view.ValueDate != default(DateOnly))
        && !string.IsNullOrWhiteSpace(view.Reason) && view.Reason == view.Reason.Trim()
        && view.Reason.Length <= AccountingJournalReversalPolicyV1.ReasonMaximumLength
        && (view.Status == "Pending" && view.ReversalJournalId is null && view.DecidedBy is null && view.DecidedAtUtc is null && view.DecisionReason is null
            || view.Status == "Approved" && view.ReversalJournalId is { } reversal && reversal != Guid.Empty && reversal != view.SourceJournalId
            && view.DecidedBy is { } checker && checker != Guid.Empty && checker != view.ProposedBy
            && view.DecidedAtUtc is { Offset: var offset } && offset == TimeSpan.Zero && view.DecisionReason is null);
    internal static WorkInboxItemV1? DecisionItem(AccountingJournalReversalProposalViewV1 view, Guid? cancellationActor) =>
        !ValidView(view) ? null : IdentityItem(view.ProposalId, view.SourceJournalId, view.PeriodId, view.ProposedBy, view.ProposedAtUtc, cancellationActor);
    internal static WorkInboxItemV1? ProofItem(WorkJournalReversalProofV1 proof) =>
        IdentityItem(proof.ProposalId, proof.SourceJournalId, proof.PeriodId, proof.ProposedBy, proof.ProposedAtUtc, proof.CancellationSourceActorId);
}

/// <summary>Native owner formatter equivalence. The scalar precommand proof does not contain reason/dates and cannot certify their digest.</summary>
public static class AccountingJournalReversalDecisionBindingV1
{
    public static bool MatchesDecisionProof(WorkInboxItemV1? item, WorkJournalReversalProofV1? proof) =>
        proof is not null && proof.CompanyId != Guid.Empty && proof.Version > 0 && proof.Status is "Pending" or "Approved"
        && proof.ProposedAtUtc.Offset == TimeSpan.Zero && Digest(proof.SourceFactsDigest) && Digest(proof.PreviewDigest)
        && AccountingJournalReversalWorkInboxSourceV1.ProofItem(proof) is { } expected
        && AccountingPeriodOpeningDecisionBindingV1.SameItem(item, expected);

    public static bool MatchesDecisionItem(WorkInboxItemV1? item, AccountingJournalReversalDecisionDetailV1? detail) =>
        detail is not null && AccountingJournalReversalWorkInboxSourceV1.ValidDetail(detail)
        && AccountingJournalReversalWorkInboxSourceV1.DecisionItem(detail.Proposal, detail.CancellationSourceActorId) is { } expected
        && AccountingPeriodOpeningDecisionBindingV1.SameItem(item, expected);

    public static bool MatchesDecisionItem(WorkInboxItemV1? item, AccountingJournalReversalProposalViewV1? original,
        AccountingJournalReversalApprovalReceiptV1? receipt) =>
        original is not null && receipt is { CancellationSourceActorProofEstablished: true }
        && AccountingJournalReversalWorkInboxSourceV1.ValidView(original) && original.Status == "Approved"
        && receipt.CompanyId == original.CompanyId && receipt.ProposalId == original.ProposalId
        && receipt.SourceJournalId == original.SourceJournalId && receipt.ReversalJournalId == original.ReversalJournalId
        && receipt.PeriodId == original.PeriodId && receipt.ActorId == original.DecidedBy && receipt.ApprovedAtUtc == original.DecidedAtUtc
        && receipt.ExpectedVersion > 0 && receipt.ExpectedVersion < int.MaxValue && receipt.ResultVersion == original.Version
        && receipt.ResultVersion == receipt.ExpectedVersion + 1 && receipt.CommandName == AccountingJournalReversalCommandNamesV1.Approve
        && Digest(receipt.IdempotencyKeyHash) && !string.IsNullOrWhiteSpace(receipt.OriginalCorrelationId)
        && receipt.PayloadHash == AccountingJournalReversalServiceV1.JournalReversalApprovalPayloadHash(original.ProposalId, receipt.ExpectedVersion)
        && receipt.SourceFactsDigest == AccountingJournalReversalServiceV1.JournalReversalSourceFactsDigest(original)
        && receipt.OriginalResultDigest == AccountingJournalReversalServiceV1.JournalReversalOriginalResultDigest(new(AccountingApiStatus.Ok, original, false, null))
        && AccountingJournalReversalWorkInboxSourceV1.DecisionItem(original, receipt.CancellationSourceActorId) is { } expected
        && AccountingPeriodOpeningDecisionBindingV1.SameItem(item, expected);
    private static bool Digest(string? value) => value is { Length: 64 } && value.All(c => char.IsAsciiDigit(c) || c is >= 'A' and <= 'F');
}
