using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Idempotency.Domain.Executions;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private static bool ValidManualReceiptReversalProof(string kind, WorkApprovalDecisionSnapshotV1 snapshot)
    {
        if (kind != ManualReceiptReversal) return snapshot.ManualReceiptReversalProof is null;
        return snapshot.ManualReceiptReversalProof is { } p && p.CompanyId == snapshot.CompanyId
            && p.ProposalId == snapshot.Item.ItemId && p.OriginalSourceId != Guid.Empty
            && !string.IsNullOrWhiteSpace(p.ProposedBy) && p.ProposedBy == snapshot.Item.PreparedBy
            && p.ProposedAtUtc.Offset == TimeSpan.Zero && p.ProposedAtUtc == snapshot.Item.WaitingSinceUtc
            && OfficialSalesDigest(p.RecordedFactsDigest) && OfficialSalesDigest(p.PreviewDigest)
            && (snapshot.Pending ? p.Status == "Pending" && snapshot.SourceVersion == "1"
                : (p.Status is "Approved" or "Rejected") && snapshot.SourceVersion == "2")
            && snapshot.Item.Scope is null && snapshot.Item.ExcludedDeciders is not { Count: > 0 }
            && snapshot.Item.Rules.Count == 1 && snapshot.Item.Rules[0].RuleCode == "finance.manual-receipt-reversal-approval-required"
            && snapshot.Item.Rules[0].Threshold is null && snapshot.Item.Rules[0].Deviation is null && snapshot.Item.Rules[0].Unit is null
            && snapshot.Item.References.Count == 4
            && snapshot.Item.References.Count(x => x.Role == "manual-receipt" && x.RecordId == p.OriginalSourceId) == 1
            && new[] { "party", "bank-account", "currency" }.All(role => snapshot.Item.References.Count(x => x.Role == role && x.RecordId is { } id && id != Guid.Empty) == 1);
    }

    private async Task<ReceiptReversalDecisionDetailV1> ManualReceiptReversalHumanPreviewAsync(
        WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (manualReceiptReversals is not IReceiptReversalDecisionReadV1 owner) throw new WorkDecisionException(503);
        ReadResult<ReceiptReversalDecisionDetailV1> result;
        try { result = await owner.ReadDecisionAsync(new(snapshot.CompanyId, actor, correlation.CorrelationId, correlation.CausationId), snapshot.Item.ItemId, Fence, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { await Fence(token); throw new WorkDecisionException(503); }
        await Fence(token);
        if (result is null) throw new WorkDecisionException(503);
        if (result.Outcome == ReadOutcome.Denied && result.Resource is null) throw new WorkDecisionException(403);
        var p = snapshot.ManualReceiptReversalProof!;
        if (result.Outcome != ReadOutcome.Found || result.Resource is not { } detail || detail.Proposal is not { } proposal
            || detail.OriginalReceipt is not { } original || original.SourceId != p.OriginalSourceId
            || original.PartyId == Guid.Empty || original.CompanyAccountId == Guid.Empty || original.TreasuryAccountId == Guid.Empty || original.CurrencyId == Guid.Empty
            || original.OriginalEventId == Guid.Empty || string.IsNullOrWhiteSpace(original.RecordedBy) || original.RecordedAtUtc.Offset != TimeSpan.Zero
            || detail.CompanyId != p.CompanyId || proposal.Id != p.ProposalId || proposal.OriginalSourceId != p.OriginalSourceId
            || proposal.ProposedBy != p.ProposedBy || proposal.ProposedAtUtc != p.ProposedAtUtc
            || proposal.Version != snapshot.SourceVersion || proposal.Status.ToString() != p.Status
            || ReversalProposalService.RecordedFactsDigest(detail) != p.RecordedFactsDigest
            || ReversalProposalService.DecisionPreviewDigest(detail) != p.PreviewDigest
            || !ReversalProposalService.MatchesDecisionSnapshot(detail, snapshot))
            throw new WorkDecisionException(503);
        return detail;
        Task Fence(CancellationToken ct) => RequireDecisionAuthorityAsync(ManualReceiptReversal, FinancePermissionNames.ReverseApprove, snapshot.CompanyId, actor, ct);
    }

    private async Task<WorkDecisionResultV1> DecideManualReceiptReversalAsync(Guid id, WorkDecisionRequestV1 request,
        string key, WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (manualReceiptReversals is null) throw new WorkDecisionException(503);
        ReversalProposalCommandResult result;
        try
        {
            var context = new FinanceContext(snapshot.CompanyId, actor, correlation.CorrelationId, correlation.CausationId);
            var metadata = new CommandMetadata(key);
            result = request.Approve ? await manualReceiptReversals.ApproveAsync(context, metadata, new(id, request.ExpectedVersion), token)
                : await manualReceiptReversals.RejectAsync(context, metadata, new(id, request.ExpectedVersion, request.Reason!), token);
        }
        catch (Exception error) when (error is InvalidOperationException or JsonException) { throw new WorkDecisionException(503); }
        await RequireDecisionAuthorityAsync(ManualReceiptReversal, FinancePermissionNames.ReverseApprove, snapshot.CompanyId, actor, token);
        if (result is null || !Enum.IsDefined(result.Outcome) || result.Issues is null) throw new WorkDecisionException(503);
        if (result.Outcome != CommandOutcome.Succeeded)
            throw new WorkDecisionException(result.Outcome switch { CommandOutcome.Denied => 403, CommandOutcome.Unavailable => 503,
                CommandOutcome.IdempotencyConflict or CommandOutcome.VersionConflict => 409, _ => 422 },
                result.Issues.FirstOrDefault()?.Code, result.IsReplay, result.OriginalCorrelationId);
        var p = snapshot.ManualReceiptReversalProof!;
        var command = request.Approve ? ReversalProposalService.ApproveCommand : ReversalProposalService.RejectCommand;
        var hash = request.Approve ? ReversalProposalService.ApprovePayloadHash(new(id, request.ExpectedVersion))
            : ReversalProposalService.RejectPayloadHash(new(id, request.ExpectedVersion, request.Reason!));
        var state = request.Approve ? ReversalProposalStatus.Approved : ReversalProposalStatus.Rejected;
        // POST deliberately uses only the original command receipt; it never reads today's human preview or money values.
        if (result.Resource is not { } r || result.DecisionReceipt is not { } receipt || result.Issues.Count != 0
            || r.Id != id || r.OriginalSourceId != p.OriginalSourceId || r.ProposedBy != p.ProposedBy || r.ProposedAtUtc != p.ProposedAtUtc
            || r.Status != state || r.Version != "2" || r.DecidedBy != actor || r.DecidedAtUtc is null
            || r.DecidedAtUtc.Value.Offset != TimeSpan.Zero
            || (request.Approve ? r.DecisionReason is not null || r.ReversalSourceId is null || r.ReversalSourceId == Guid.Empty || r.ReversalSourceId == p.OriginalSourceId
                : r.DecisionReason != request.Reason || r.ReversalSourceId is not null)
            || !snapshot.Pending && p.Status != state.ToString()
            || receipt.CompanyId != snapshot.CompanyId || receipt.ProposalId != id || receipt.OriginalSourceId != p.OriginalSourceId
            || receipt.CommandName != command || receipt.IdempotencyKeyHash != IdempotencyHash.ComputeKey(key)
            || receipt.PayloadHash != hash || receipt.ActorId != actor || receipt.ExpectedVersion != request.ExpectedVersion
            || receipt.ResultVersion != r.Version || receipt.RecordedAtUtc != r.DecidedAtUtc
            || receipt.RecordedFactsDigest != p.RecordedFactsDigest
            || string.IsNullOrWhiteSpace(result.OriginalCorrelationId) || receipt.OriginalCorrelationId != result.OriginalCorrelationId
            || !result.IsReplay && (!snapshot.Pending || snapshot.SourceVersion != request.ExpectedVersion || result.OriginalCorrelationId != correlation.CorrelationId))
            throw new WorkDecisionException(503);
        return new(snapshot.CompanyId, ManualReceiptReversal, id, state.ToString(), result.IsReplay, result.OriginalCorrelationId);
    }
}
