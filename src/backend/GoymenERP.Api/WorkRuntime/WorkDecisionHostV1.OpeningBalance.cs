using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Idempotency.Domain.Executions;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private static bool ValidOpeningBalanceProof(string kind, WorkApprovalDecisionSnapshotV1 snapshot)
    {
        if (kind is not (OpeningBalance or OpeningBalanceReversal)) return snapshot.OpeningBalanceProof is null;
        if (snapshot.OpeningBalanceProof is not { } p || p.CompanyId != snapshot.CompanyId || p.OpeningBalanceId == Guid.Empty
            || p.ProposedBy != snapshot.Item.PreparedBy || string.IsNullOrWhiteSpace(p.ProposedBy) || string.IsNullOrWhiteSpace(p.OpeningProposedBy)
            || p.ProposedAtUtc.Offset != TimeSpan.Zero || p.ProposedAtUtc != snapshot.Item.WaitingSinceUtc || p.Version != snapshot.SourceVersion
            || !OfficialSalesDigest(p.OpeningFactsDigest) || !OfficialSalesDigest(p.PreviewDigest)
            || snapshot.Item.Scope is not null || snapshot.Item.ExcludedDeciders is { Count: > 0 }
            || snapshot.Item.Rules.Count != 1 || snapshot.Item.Rules[0].RuleCode != (kind == OpeningBalance
                ? "finance.opening-balance-approval-required" : "finance.opening-balance-reversal-approval-required")
            || snapshot.Item.Rules[0].Actual is null || snapshot.Item.Rules[0].Threshold is not null
            || snapshot.Item.Rules[0].Deviation is not null || snapshot.Item.Rules[0].Unit is not null) return false;
        var reversal = kind == OpeningBalanceReversal;
        if (reversal ? p.ReversalId != snapshot.Item.ItemId || p.ReversalId == Guid.Empty || !OfficialSalesDigest(p.ReversalFactsDigest)
            : p.ReversalId is not null || p.OpeningBalanceId != snapshot.Item.ItemId || p.ProposedBy != p.OpeningProposedBy || p.ReversalFactsDigest is not null) return false;
        if (snapshot.Pending ? p.Status != "Proposed" || p.Version != "1"
            : p.Status is "Approved" or "Rejected" ? p.Version != "2"
            : reversal || p.Status != "Reversed" || p.Version != "3") return false;
        var refs = snapshot.Item.References;
        return refs.Count(x => (x.Role is "cash-account" or "bank-account" or "company-account") && x.RecordId is { } target && target != Guid.Empty) == 1
            && refs.Count(x => x.Role == "currency" && x.RecordId is { } currency && currency != Guid.Empty) == 1
            && refs.Count(x => x.Role == "opening-balance" && x.RecordId == p.OpeningBalanceId) == (reversal ? 1 : 0)
            && (refs.Any(x => x.Role == "company-account") ? refs.Count(x => x.Role == "party" && x.RecordId is { } party && party != Guid.Empty) == 1 : refs.All(x => x.Role != "party"))
            && refs.Count == (reversal ? 3 : 2) + (refs.Any(x => x.Role == "company-account") ? 1 : 0);
    }

    private static bool ValidOpeningBalanceRequest(string kind, WorkDecisionRequestV1 request) =>
        kind is not (OpeningBalance or OpeningBalanceReversal) || request.ExpectedVersion == "1"
        && (request.Approve ? request.Reason is null : !string.IsNullOrWhiteSpace(request.Reason) && request.Reason.Length <= 500 && request.Reason == request.Reason.Trim())
        && request.ManufacturingPlanReapproval is null && request.InventoryTransferLoss is null && request.InventoryTransferOverage is null
        && request.LogisticsPlanExcess is null && request.SupplierReturnCostException is null && request.LogisticsCustomerReturn is null && request.ProductPackageStandard is null;

    private async Task<OpeningBalanceDecisionDetailV1> OpeningBalanceHumanPreviewAsync(WorkApprovalDecisionSnapshotV1 snapshot,
        string actor, CancellationToken token)
    {
        if (openingBalances is not IFinanceOpeningBalanceDecisionReadV1 owner) throw new WorkDecisionException(503);
        var kind = snapshot.Item.Kind; ReadResult<OpeningBalanceDecisionDetailV1> result;
        try
        {
            var context = new FinanceContext(snapshot.CompanyId, actor, correlation.CorrelationId, correlation.CausationId);
            result = kind == OpeningBalance ? await owner.ReadOpeningDecisionAsync(context, snapshot.Item.ItemId, Fence, token)
                : await owner.ReadReversalDecisionAsync(context, snapshot.Item.ItemId, Fence, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { await Fence(token); throw new WorkDecisionException(503); }
        await Fence(token);
        if (result is null) throw new WorkDecisionException(503);
        if (result.Outcome == ReadOutcome.Denied && result.Resource is null) throw new WorkDecisionException(403);
        var p = snapshot.OpeningBalanceProof!;
        if (result.Outcome != ReadOutcome.Found || result.Resource is not { } detail || detail.OpeningBalance is not { } human || detail.DecisionProof is not { } actual
            || actual != new OpeningBalanceDecisionProofV1(p.CompanyId, p.OpeningBalanceId, p.ReversalId, p.OpeningProposedBy, p.ProposedBy, p.ProposedAtUtc,
                p.Status, p.Version, p.OpeningFactsDigest, p.ReversalFactsDigest, p.PreviewDigest)
            || human.IsOwnProposal != (p.OpeningProposedBy == actor)
            || kind == OpeningBalanceReversal && human.Reversal?.IsOwnProposal != (p.ProposedBy == actor)
            || !FinanceOpeningBalanceService.MatchesDecisionItem(kind, snapshot.Item, detail)) throw new WorkDecisionException(503);
        return detail;
        async Task Fence(CancellationToken ct)
        { await RequireDecisionAuthorityAsync(kind, ErpPermissions.FinanceOpeningBalancesApprove, snapshot.CompanyId, actor, ct); ct.ThrowIfCancellationRequested(); }
    }

    private async Task<WorkDecisionResultV1> DecideOpeningBalanceAsync(string kind, Guid id, WorkDecisionRequestV1 request, string key,
        WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (openingBalances is null) throw new WorkDecisionException(503);
        var reversal = kind == OpeningBalanceReversal; var p = snapshot.OpeningBalanceProof!;
        OpeningBalanceCommandResult result;
        try
        {
            var context = new FinanceContext(snapshot.CompanyId, actor, correlation.CorrelationId, correlation.CausationId);
            var metadata = new CommandMetadata(key);
            if (reversal)
            {
                if (openingBalances is not IFinanceOpeningBalanceReversalDecisionV1 owner) throw new WorkDecisionException(503);
                result = request.Approve ? await owner.ApproveReversalDecisionAsync(context, metadata, id, new(p.OpeningBalanceId, request.ExpectedVersion), token)
                    : await owner.RejectReversalDecisionAsync(context, metadata, id, new(p.OpeningBalanceId, request.ExpectedVersion, request.Reason!), token);
            }
            else result = request.Approve ? await openingBalances.ApproveAsync(context, metadata, new(id, request.ExpectedVersion), token)
                : await openingBalances.RejectAsync(context, metadata, new(id, request.ExpectedVersion, request.Reason!), token);
        }
        catch (Exception error) when (error is InvalidOperationException or JsonException) { throw new WorkDecisionException(503); }
        await RequireDecisionAuthorityAsync(kind, ErpPermissions.FinanceOpeningBalancesApprove, snapshot.CompanyId, actor, token); token.ThrowIfCancellationRequested();
        if (result is null || !Enum.IsDefined(result.Outcome) || result.Issues is null) throw new WorkDecisionException(503);
        if (result.Outcome != CommandOutcome.Succeeded)
            throw new WorkDecisionException(result.Outcome switch { CommandOutcome.Denied => 403, CommandOutcome.Unavailable => 503,
                CommandOutcome.IdempotencyConflict or CommandOutcome.VersionConflict => 409, _ => 422 }, result.Issues.FirstOrDefault()?.Code, result.IsReplay, result.OriginalCorrelationId);
        var command = reversal ? request.Approve ? FinanceOpeningBalanceService.ApproveReversalCommand : FinanceOpeningBalanceService.RejectReversalCommand
            : request.Approve ? FinanceOpeningBalanceService.ApproveCommand : FinanceOpeningBalanceService.RejectCommand;
        var hash = reversal ? request.Approve ? FinanceOpeningBalanceService.ApproveReversalPayloadHash(new(p.OpeningBalanceId, request.ExpectedVersion))
            : FinanceOpeningBalanceService.RejectReversalPayloadHash(new(p.OpeningBalanceId, request.ExpectedVersion, request.Reason!))
            : request.Approve ? FinanceOpeningBalanceService.ApprovePayloadHash(new(id, request.ExpectedVersion)) : FinanceOpeningBalanceService.RejectPayloadHash(new(id, request.ExpectedVersion, request.Reason!));
        var state = request.Approve ? "Approved" : "Rejected";
        if (result.Resource is not { } r || result.DecisionReceipt is not { } receipt || result.Issues.Count != 0
            || r.CompanyId != snapshot.CompanyId || r.Id != p.OpeningBalanceId || r.IsOwnProposal != (p.OpeningProposedBy == actor)
            || receipt.CompanyId != snapshot.CompanyId || receipt.OpeningBalanceId != p.OpeningBalanceId || receipt.ReversalId != p.ReversalId
            || receipt.CommandName != command || receipt.IdempotencyKeyHash != IdempotencyHash.ComputeKey(key) || receipt.PayloadHash != hash
            || receipt.ActorId != actor || receipt.ActorId == p.ProposedBy || receipt.ExpectedVersion != request.ExpectedVersion || receipt.ResultVersion != "2"
            || receipt.OpeningFactsDigest != p.OpeningFactsDigest || receipt.ReversalFactsDigest != p.ReversalFactsDigest
            || FinanceOpeningBalanceService.OpeningFactsDigest(r, p.OpeningProposedBy) != p.OpeningFactsDigest
            || receipt.OriginalReceiptDigest != FinanceOpeningBalanceService.OriginalReceiptDigest(result)
            || string.IsNullOrWhiteSpace(result.OriginalCorrelationId) || receipt.OriginalCorrelationId != result.OriginalCorrelationId
            || !result.IsReplay && (!snapshot.Pending || snapshot.SourceVersion != request.ExpectedVersion || result.OriginalCorrelationId != correlation.CorrelationId)
            || !snapshot.Pending && !(p.Status == state || !reversal && request.Approve && p.Status == "Reversed")) throw new WorkDecisionException(503);
        if (reversal)
        {
            if (r.Reversal is not { } target || target.Id != id || target.IsOwnProposal || target.ProposedAtUtc != p.ProposedAtUtc
                || target.Version != "2" || target.Status.ToString() != state || target.DecidedAtUtc is null || target.DecidedAtUtc.Value.Offset != TimeSpan.Zero
                || receipt.RecordedAtUtc != target.DecidedAtUtc || (request.Approve ? target.RejectionReason is not null : target.RejectionReason != request.Reason)
                || FinanceOpeningBalanceService.ReversalFactsDigest(r.Id, target, p.ProposedBy) != p.ReversalFactsDigest
                || (request.Approve ? r.Status != OpeningBalanceStatus.Reversed || r.Version != "3" : r.Status != OpeningBalanceStatus.Approved || r.Version != "2")) throw new WorkDecisionException(503);
        }
        else if (r.Reversal is not null || r.ProposedAtUtc != p.ProposedAtUtc || r.Version != "2" || r.Status.ToString() != state
            || r.DecidedAtUtc is null || r.DecidedAtUtc.Value.Offset != TimeSpan.Zero || receipt.RecordedAtUtc != r.DecidedAtUtc
            || (request.Approve ? r.RejectionReason is not null : r.RejectionReason != request.Reason)) throw new WorkDecisionException(503);
        return new(snapshot.CompanyId, kind, id, state, result.IsReplay, result.OriginalCorrelationId);
    }
}
