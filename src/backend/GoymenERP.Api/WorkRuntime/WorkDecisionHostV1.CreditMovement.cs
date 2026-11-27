using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Idempotency.Domain.Executions;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private static bool ValidCreditMovementRequest(WorkDecisionRequestV1 request) =>
        request.ExpectedVersion == "1" && (request.Approve ? request.Reason is null
            : !string.IsNullOrWhiteSpace(request.Reason) && request.Reason.Length <= 1000 && request.Reason == request.Reason.Trim());

    private static bool ValidCreditMovementProof(string kind, WorkApprovalDecisionSnapshotV1 snapshot)
    {
        if (kind != CreditMovement) return snapshot.CreditMovementProof is null;
        return snapshot.CreditMovementProof is { } p && p.CompanyId == snapshot.CompanyId && p.MovementId == snapshot.Item.ItemId
            && p.MovementId != Guid.Empty && p.ProposedBy == snapshot.Item.PreparedBy && !string.IsNullOrWhiteSpace(p.ProposedBy) && p.ProposedBy.Length <= 128
            && p.ProposedAtUtc.Offset == TimeSpan.Zero && p.ProposedAtUtc == snapshot.Item.WaitingSinceUtc
            && OfficialSalesDigest(p.SourceFactsDigest) && OfficialSalesDigest(p.PreviewDigest)
            && (snapshot.Pending ? snapshot.SourceVersion == "1" && p.Status == "Proposed"
                : (p.Status is "Approved" or "Rejected") && snapshot.SourceVersion == "2"
                    || p.Status == "Approved" && snapshot.SourceVersion == "3" && snapshot.Item.Rules.Count == 1
                        && snapshot.Item.Rules[0].RuleCode == "finance.credit-movement-opening-balance")
            && snapshot.Item.Scope is null && snapshot.Item.ExcludedDeciders is not { Count: > 0 }
            && snapshot.Item.Rules.Count == 1
            && (snapshot.Item.Rules[0].RuleCode is "finance.credit-movement-drawdown" or "finance.credit-movement-repayment"
                or "finance.credit-movement-interest-accrual" or "finance.credit-movement-opening-balance")
            && snapshot.Item.Rules[0].Threshold is null && snapshot.Item.Rules[0].Deviation is null && snapshot.Item.Rules[0].Unit is null
            && snapshot.Item.References.Count is >= 2 and <= 4
            && new[] { "credit-account", "currency" }.All(role =>
                snapshot.Item.References.Count(x => x.Role == role && x.RecordId is { } id && id != Guid.Empty) == 1)
            && snapshot.Item.References.All(x => x.RecordId is { } id && id != Guid.Empty
                && x.Role is "credit-account" or "currency" or "bank-account" or "replaced-opening-movement")
            && snapshot.Item.References.Select(x => x.Role).Distinct(StringComparer.Ordinal).Count() == snapshot.Item.References.Count;
    }

    private async Task<FinanceCreditMovementDecisionDetailV1> CreditMovementHumanPreviewAsync(WorkApprovalDecisionSnapshotV1 snapshot,
        string actor, CancellationToken token)
    {
        if (creditMovements is not IFinanceCreditMovementDecisionReadV1 owner) throw new WorkDecisionException(503);
        ReadResult<FinanceCreditMovementDecisionDetailV1> result;
        try { result = await owner.ReadDecisionAsync(new(snapshot.CompanyId, actor, correlation.CorrelationId, correlation.CausationId), snapshot.Item.ItemId, Fence, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { await Fence(token); throw new WorkDecisionException(503); }
        await Fence(token);
        if (result is null) throw new WorkDecisionException(503);
        if (result.Outcome == ReadOutcome.Denied && result.Resource is null) throw new WorkDecisionException(403);
        var p = snapshot.CreditMovementProof!;
        if (result.Outcome != ReadOutcome.Found || result.Resource is not { } detail || detail.Movement is not { } view
            || view.CompanyId != snapshot.CompanyId || view.Id != p.MovementId || view.Version != snapshot.SourceVersion
            || detail.ProposedBy != p.ProposedBy || view.ProposedAtUtc != p.ProposedAtUtc || view.Status.ToString() != p.Status
            || detail.SourceFactsDigest != p.SourceFactsDigest || FinanceCreditMovementServiceV1.SourceFactsDigest(view, detail.ProposedBy) != p.SourceFactsDigest
            || FinanceCreditMovementServiceV1.DecisionPreviewDigest(detail) != p.PreviewDigest
            || !FinanceCreditMovementServiceV1.MatchesDecisionItem(snapshot.Item, detail)) throw new WorkDecisionException(503);
        return detail;
        async Task Fence(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            await RequireDecisionAuthorityAsync(CreditMovement, ErpPermissions.FinanceCreditsApprove, snapshot.CompanyId, actor, ct);
            ct.ThrowIfCancellationRequested();
        }
    }

    private async Task<WorkDecisionResultV1> DecideCreditMovementAsync(Guid id, WorkDecisionRequestV1 request, string key,
        WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (creditMovements is null) throw new WorkDecisionException(503);
        var nativeRequest = new CreditMovementDecisionV1(id, request.ExpectedVersion, request.Approve, request.Reason);
        CreditMovementResultV1 result;
        try
        {
            result = await creditMovements.DecideAsync(new(snapshot.CompanyId, actor, correlation.CorrelationId, correlation.CausationId),
                new(key), nativeRequest, token);
        }
        catch (Exception error) when (error is InvalidOperationException or JsonException) { throw new WorkDecisionException(503); }
        token.ThrowIfCancellationRequested();
        await RequireDecisionAuthorityAsync(CreditMovement, ErpPermissions.FinanceCreditsApprove, snapshot.CompanyId, actor, token);
        token.ThrowIfCancellationRequested();
        if (result is null || !Enum.IsDefined(result.Outcome) || result.Issues is null) throw new WorkDecisionException(503);
        if (result.Outcome != CommandOutcome.Succeeded)
            throw new WorkDecisionException(result.Outcome switch { CommandOutcome.Denied => 403, CommandOutcome.Unavailable => 503,
                CommandOutcome.IdempotencyConflict or CommandOutcome.VersionConflict => 409, _ => 422 },
                result.Issues.FirstOrDefault()?.Code, result.IsReplay, result.OriginalCorrelationId);
        var p = snapshot.CreditMovementProof!;
        var state = request.Approve ? CreditMovementStatusV1.Approved : CreditMovementStatusV1.Rejected;
        // No second human POST read. A later opening reversal/version 3 is not the immutable approved version 2 receipt.
        if (request.ExpectedVersion != "1" || result.Resource is not { } r || result.DecisionReceipt is not { } receipt || result.Issues.Count != 0
            || r.Id != id || r.CompanyId != snapshot.CompanyId || r.Version != "2" || r.Status != state || r.IsOwnProposal || r.ReversedByMovementId is not null
            || r.ProposedAtUtc != p.ProposedAtUtc || p.ProposedBy == actor || r.DecidedAtUtc is not { } decided || decided.Offset != TimeSpan.Zero
            || (request.Approve ? r.RejectionReason is not null : r.RejectionReason != request.Reason)
            || !snapshot.Pending && p.Status != state.ToString()
            || receipt.CompanyId != snapshot.CompanyId || receipt.MovementId != id || receipt.CommandName != FinanceCreditMovementServiceV1.DecideCommand
            || receipt.IdempotencyKeyHash != IdempotencyHash.ComputeKey(key) || receipt.PayloadHash != FinanceCreditMovementServiceV1.DecisionPayloadHash(nativeRequest)
            || receipt.ActorId != actor || receipt.ExpectedVersion != request.ExpectedVersion || receipt.ResultVersion != "2"
            || receipt.DecidedAtUtc != decided || receipt.SourceFactsDigest != p.SourceFactsDigest
            || FinanceCreditMovementServiceV1.SourceFactsDigest(r, p.ProposedBy) != p.SourceFactsDigest
            || !FinanceCreditMovementServiceV1.MatchesDecisionItem(snapshot.Item, new(r, p.ProposedBy, receipt.BankDisplayName, receipt.SourceFactsDigest))
            || receipt.OriginalReceiptDigest != FinanceCreditMovementServiceV1.OriginalReceiptDigest(result)
            || string.IsNullOrWhiteSpace(result.OriginalCorrelationId) || receipt.OriginalCorrelationId != result.OriginalCorrelationId
            || !result.IsReplay && (!snapshot.Pending || snapshot.SourceVersion != request.ExpectedVersion || result.OriginalCorrelationId != correlation.CorrelationId))
            throw new WorkDecisionException(503);
        return new(snapshot.CompanyId, CreditMovement, id, state.ToString(), result.IsReplay, result.OriginalCorrelationId);
    }
}
