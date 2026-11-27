using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Idempotency.Domain.Executions;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private static bool ValidCashExchangeBookValueRequest(WorkDecisionRequestV1 request) => request.ExpectedVersion == "1"
        && (request.Approve ? request.Reason is null : !string.IsNullOrWhiteSpace(request.Reason) && request.Reason.Length <= 1000
            && request.Reason == request.Reason.Trim() && !request.Reason.Any(char.IsControl))
        && request.ManufacturingPlanReapproval is null && request.InventoryTransferLoss is null && request.InventoryTransferOverage is null
        && request.LogisticsPlanExcess is null && request.SupplierReturnCostException is null && request.LogisticsCustomerReturn is null
        && request.ProductPackageStandard is null;

    private static bool ValidCashExchangeBookValueProof(string kind, WorkApprovalDecisionSnapshotV1 snapshot)
    {
        if (kind != CashExchangeBookValue) return snapshot.CashExchangeBookValueProof is null;
        if (snapshot.CashExchangeBookValueProof is not { } p || p.CompanyId != snapshot.CompanyId || p.ProposalId != snapshot.Item.ItemId
            || p.ProposalId == Guid.Empty || p.TransferId == Guid.Empty || p.PreparedBy != snapshot.Item.PreparedBy
            || string.IsNullOrWhiteSpace(p.PreparedBy) || p.PreparedBy.Length > 128 || p.PreparedAtUtc.Offset != TimeSpan.Zero
            || p.PreparedAtUtc != snapshot.Item.WaitingSinceUtc || p.Version != snapshot.SourceVersion
            || !OfficialSalesDigest(p.TransferSourceHash) || !OfficialSalesDigest(p.ProposalFactsDigest) || !OfficialSalesDigest(p.PreviewDigest)
            || (snapshot.Pending ? p.Version != "1" || p.Status != "Pending" : p.Version != "2" || p.Status is not ("Approved" or "Rejected"))
            || snapshot.Item.Scope is not null || snapshot.Item.ExcludedDeciders is { Count: > 0 }
            || snapshot.Item.Rules.Count != 2 || snapshot.Item.References.Count != 2) return false;
        var rules = snapshot.Item.Rules;
        return rules[0].RuleCode == "finance.cash-exchange-source-book-value" && rules[0].Actual is not null
            && rules[0].Threshold is null && rules[0].Deviation is null && rules[0].Unit is null
            && (rules[1].RuleCode == "finance.cash-exchange-target-functional-value" ? rules[1].Actual is not null
                : rules[1].RuleCode == "finance.cash-exchange-target-functional-value-missing" && rules[1].Actual is null)
            && rules[1].Threshold is null && rules[1].Deviation is null && rules[1].Unit is null
            && snapshot.Item.References.Count(x => x.Role == "cash-transfer" && x.RecordId == p.TransferId && x.Link is null) == 1
            && snapshot.Item.References.Count(x => x.Role == "evidence-document" && x.RecordId is { } id && id != Guid.Empty && x.Link is null) == 1;
    }

    private async Task<CashExchangeBookValueDecisionDetailV1> CashExchangeBookValueHumanPreviewAsync(WorkApprovalDecisionSnapshotV1 snapshot,
        string actor, CancellationToken token)
    {
        if (cashExchangeBookValues is not ICashExchangeBookValueDecisionReadV1 owner) throw new WorkDecisionException(503);
        ReadResult<CashExchangeBookValueDecisionDetailV1> result;
        try { result = await owner.ReadDecisionAsync(new(snapshot.CompanyId, actor, correlation.CorrelationId, correlation.CausationId), snapshot.Item.ItemId, Fence, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { await Fence(token); throw new WorkDecisionException(503); }
        await Fence(token);
        if (result is null) throw new WorkDecisionException(503);
        if (result.Outcome == ReadOutcome.Denied && result.Resource is null) throw new WorkDecisionException(403);
        var p = snapshot.CashExchangeBookValueProof!;
        if (result.Outcome != ReadOutcome.Found || result.Resource is not { } detail || detail.DecisionProof is not { } actual
            || actual != new CashExchangeBookValueDecisionProofV1(p.CompanyId, p.ProposalId, p.TransferId, p.PreparedBy, p.PreparedAtUtc,
                p.Status, p.Version, p.TransferSourceHash, p.ProposalFactsDigest, p.PreviewDigest)
            || !CashExchangeBookValueServiceV1.MatchesDecisionItem(snapshot.Item, detail)) throw new WorkDecisionException(503);
        return detail;
        async Task Fence(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            await RequireDecisionAuthorityAsync(CashExchangeBookValue, CashExchangeBookValuePermissionsV1.Approve, snapshot.CompanyId, actor, ct);
            ct.ThrowIfCancellationRequested();
        }
    }

    private async Task<WorkDecisionResultV1> DecideCashExchangeBookValueAsync(Guid id, WorkDecisionRequestV1 request, string key,
        WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (cashExchangeBookValues is null) throw new WorkDecisionException(503);
        var nativeRequest = new DecideCashExchangeBookValueV1(id, request.ExpectedVersion, request.Approve, request.Reason);
        CashExchangeBookValueCommandResultV1 result;
        try { result = await cashExchangeBookValues.DecideAsync(new(snapshot.CompanyId, actor, correlation.CorrelationId, correlation.CausationId), new(key), nativeRequest, token); }
        catch (Exception error) when (error is InvalidOperationException or JsonException) { throw new WorkDecisionException(503); }
        token.ThrowIfCancellationRequested();
        await RequireDecisionAuthorityAsync(CashExchangeBookValue, CashExchangeBookValuePermissionsV1.Approve, snapshot.CompanyId, actor, token);
        token.ThrowIfCancellationRequested();
        if (result is null || !Enum.IsDefined(result.Outcome)) throw new WorkDecisionException(503);
        if (result.Outcome != CommandOutcome.Succeeded)
            throw new WorkDecisionException(result.Outcome switch { CommandOutcome.Denied => 403, CommandOutcome.Unavailable => 503,
                CommandOutcome.IdempotencyConflict or CommandOutcome.VersionConflict => 409, _ => 422 }, result.Code, result.IsReplay, result.OriginalCorrelationId);
        var p = snapshot.CashExchangeBookValueProof!;
        var state = request.Approve ? CashExchangeBookValueStatusV1.Approved : CashExchangeBookValueStatusV1.Rejected;
        // The immutable native proposal and actual command receipt are sufficient on POST; no second human read.
        if (request.ExpectedVersion != "1" || result.Resource is not { } r || result.DecisionReceipt is not { } receipt || result.Code is not null
            || r.Id != id || r.CompanyId != snapshot.CompanyId || r.TransferId != p.TransferId || r.Version != "2" || r.Status != state
            || r.PreparedBy != p.PreparedBy || r.PreparedBy == actor || r.PreparedAtUtc != p.PreparedAtUtc || r.TransferSourceHash != p.TransferSourceHash
            || r.DecidedBy != actor || r.DecidedAtUtc is not { } decided || decided.Offset != TimeSpan.Zero
            || (request.Approve ? r.DecisionReason is not null || r.TargetFunctionalValueTl is null : r.DecisionReason != request.Reason)
            || CashExchangeBookValueServiceV1.ProposalFactsDigest(r) != p.ProposalFactsDigest
            || !snapshot.Pending && p.Status != state.ToString()
            || receipt.CompanyId != snapshot.CompanyId || receipt.ProposalId != id || receipt.TransferId != p.TransferId
            || receipt.CommandName != CashExchangeBookValueServiceV1.DecideCommand || receipt.IdempotencyKeyHash != IdempotencyHash.ComputeKey(key)
            || receipt.PayloadHash != CashExchangeBookValueServiceV1.DecidePayloadHash(nativeRequest) || receipt.ActorId != actor
            || receipt.ExpectedVersion != request.ExpectedVersion || receipt.ResultVersion != "2" || receipt.RecordedAtUtc != decided
            || receipt.TransferSource is not { } transfer || transfer.CompanyId != snapshot.CompanyId || transfer.Id != p.TransferId
            || !CashExchangeBookValueServiceV1.MatchesDecisionItem(snapshot.Item, new(transfer, r))
            || receipt.ProposalFactsDigest != p.ProposalFactsDigest || receipt.OriginalReceiptDigest != CashExchangeBookValueServiceV1.OriginalReceiptDigest(result)
            || string.IsNullOrWhiteSpace(result.OriginalCorrelationId) || receipt.OriginalCorrelationId != result.OriginalCorrelationId
            || !result.IsReplay && (!snapshot.Pending || snapshot.SourceVersion != request.ExpectedVersion || result.OriginalCorrelationId != correlation.CorrelationId))
            throw new WorkDecisionException(503);
        return new(snapshot.CompanyId, CashExchangeBookValue, id, state.ToString(), result.IsReplay, result.OriginalCorrelationId);
    }
}
