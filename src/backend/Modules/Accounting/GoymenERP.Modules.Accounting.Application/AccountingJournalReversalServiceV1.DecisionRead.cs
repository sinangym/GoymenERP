using System.Text.Json;
using GoymenERP.Modules.Accounting.Domain;

namespace GoymenERP.Modules.Accounting.Application;

public sealed partial class AccountingJournalReversalServiceV1 : IAccountingJournalReversalDecisionReadV1
{
    public async Task<AccountingApiResult<AccountingJournalReversalDecisionDetailV1>> ReadDecisionAsync(Guid proposalId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshConsumerFence); cancellationToken.ThrowIfCancellationRequested();
        if (proposalId == Guid.Empty) return new(AccountingApiStatus.Invalid, null, false, AccountingApiCodes.RequestInvalid, "proposalId");
        if (!HasContext() || ActorGuid() is not { } actor) return Denied<AccountingJournalReversalDecisionDetailV1>();
        var company = companyContext.CompanyId; var actorId = currentUser.UserId!;
        try
        {
            await ReadFence(cancellationToken);
            if (proposals is not IAccountingJournalReversalDecisionStoreV1 reader) return new(AccountingApiStatus.Unavailable, null, false, null);
            var source = await reader.ReadJournalReversalDecisionAsync(proposalId, ReadFence, cancellationToken);
            await ReadFence(cancellationToken);
            if (source is null) return new(AccountingApiStatus.NotFound, null, false, null);
            if (!ValidJournalReversalSource(source, company, proposalId)) return new(AccountingApiStatus.Unavailable, null, false, null);
            if (source.Proposal.ProposedBy == actor || source.CancellationSourceActorId == actor)
                return Denied<AccountingJournalReversalDecisionDetailV1>();
            var view = View(source.Proposal);
            return new(AccountingApiStatus.Ok, new(view, JournalReversalSourceFactsDigest(view), JournalReversalPreviewDigest(view))
                { CancellationSourceActorId = source.CancellationSourceActorId, CancellationSourceActorProofEstablished = true }, false, null);
        }
        catch (AccountingJournalReversalAuthorityDeniedException) { return Denied<AccountingJournalReversalDecisionDetailV1>(); }
        async Task ReadFence(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); await freshConsumerFence(token); token.ThrowIfCancellationRequested();
            // Final native check follows the last external await; a normal-completing revocation cannot release data.
            await FenceAsync(AccountingJournalReversalPermissionNamesV1.Approve, company, actor, actorId, token);
            token.ThrowIfCancellationRequested();
        }
    }
    public static string JournalReversalSourceFactsDigest(AccountingJournalReversalProposalViewV1 view) =>
        AccountingApiService.Sha256(JsonSerializer.Serialize(new
        {
            view.ProposalId, view.CompanyId, view.SourceJournalId, view.PeriodId,
            view.TransactionDate, view.AccountingDate, view.ValueDate, view.Reason, view.ProposedBy, view.ProposedAtUtc,
        }, Json));
    public static string JournalReversalPreviewDigest(AccountingJournalReversalProposalViewV1 view) =>
        AccountingApiService.Sha256(JsonSerializer.Serialize(view, Json));
    private static bool ValidJournalReversalSource(AccountingJournalReversalDecisionSourceV1 source, Guid company, Guid id)
    {
        var p = source.Proposal;
        return p.CompanyId == company && p.ProposalId == id && company != Guid.Empty && id != Guid.Empty
            && p.SourceJournalId != Guid.Empty && p.PeriodId != Guid.Empty && p.ProposedBy != Guid.Empty
            && p.ProposedAtUtc.Offset == TimeSpan.Zero && p.TransactionDate != default && p.AccountingDate != default
            && (p.ValueDate is null || p.ValueDate != default(DateOnly))
            && !string.IsNullOrWhiteSpace(p.Reason) && p.Reason == p.Reason.Trim() && p.Reason.Length <= AccountingJournalReversalPolicyV1.ReasonMaximumLength
            && p.Version > 0 && source.SourceJournalExists && source.CancellationSourceActorId != Guid.Empty
            && (p.Status == AccountingJournalReversalProposalStatusV1.Pending && p.ReversalJournalId is null
                && p.DecidedBy is null && p.DecidedAtUtc is null && p.DecisionReason is null
                || p.Status == AccountingJournalReversalProposalStatusV1.Approved && p.ReversalJournalId is { } reversal && reversal != Guid.Empty
                && p.DecidedBy is { } checker && checker != Guid.Empty && checker != p.ProposedBy
                && p.DecidedAtUtc is { Offset: var offset } && offset == TimeSpan.Zero && p.DecisionReason is null && source.ReversalJournalBound);
    }
}
