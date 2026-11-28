using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Approvals;
using GoymenERP.Modules.Identity.Infrastructure.WorkInbox;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private static bool IdentityImpactDigest(string? text) => text is { Length: 64 }
        && text.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
    private static bool ValidIdentityAccessGrantRequest(string kind, WorkDecisionRequestV1 r) => kind != IdentityAccessGrant
        ? r.IdentityAccessGrant is null
        : IdentityAccessGrantDecisionBindingV1.TryVersion(r.ExpectedVersion, out var version) && version < long.MaxValue
            && r.IdentityAccessGrant is { } selection && IdentityImpactDigest(selection.ExpectedImpactDigest)
            && (r.Approve ? r.Reason is null : AccessGrantRequest.IsCanonicalReasonCode(r.Reason))
            && r.InventoryCostCancellation is null && r.PostingAccountMappingSelection is null && r.ApprovedSourceCatalogSelection is null
            && r.AccountingPeriodReopening is null && r.NormalJournalDraft is null && r.ProductPackageStandard is null
            && r.ManufacturingPlanReapproval is null && r.InventoryTransferLoss is null && r.InventoryTransferOverage is null
            && r.LogisticsPlanExcess is null && r.SupplierReturnCostException is null && r.LogisticsCustomerReturn is null;
    private static bool ValidIdentityAccessGrantProof(string kind, WorkApprovalDecisionSnapshotV1 s) => kind != IdentityAccessGrant
        ? s.IdentityAccessGrantProof is null
        : s.IdentityAccessGrantProof is { } p && s.CompanyId == p.CompanyId && s.SourceVersion == p.Version
            && s.Pending == (p.Status == "Pending") && IdentityAccessGrantDecisionBindingV1.MatchesDecisionProof(s.Item, p);
    private async Task IdentityAccessGrantFenceAsync(WorkApprovalDecisionSnapshotV1 s, string actor, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await RequireDecisionAuthorityAsync(IdentityAccessGrant, IdentityPermissionManifestV1.AccessApprove, s.CompanyId, actor, token);
        token.ThrowIfCancellationRequested();
    }
    private async Task<WorkIdentityAccessGrantPreviewV1> IdentityAccessGrantHumanPreviewAsync(WorkApprovalDecisionSnapshotV1 s,
        string actor, CancellationToken token)
    {
        if (identityAccessDecisions is null) throw new WorkDecisionException(503);
        IdentityAccessGrantDecisionDetailV1? detail;
        try { detail = await identityAccessDecisions.ReadDecisionAsync(s.Item.ItemId,
            new IdentityAccessGrantDecisionReadContextV1(s.CompanyId, actor, correlation.CorrelationId, correlation.CausationId), Fence, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (UnauthorizedAccessException) { throw new WorkDecisionException(403); }
        catch { await Fence(token); throw new WorkDecisionException(503); }
        await Fence(token);
        if (!IdentityAccessGrantDecisionBindingV1.MatchesDecisionSnapshot(s, detail)) throw new WorkDecisionException(503);
        return WorkIdentityAccessGrantPreviewV1.FromNative(detail!);
        Task Fence(CancellationToken ct) => IdentityAccessGrantFenceAsync(s, actor, ct);
    }
    private async Task<WorkDecisionResultV1> DecideIdentityAccessGrantAsync(Guid id, WorkDecisionRequestV1 request,
        string key, WorkApprovalDecisionSnapshotV1 s, string actor, CancellationToken token)
    {
        if (identityAccessDecisions is null || request.IdentityAccessGrant is not { } selection) throw new WorkDecisionException(503);
        var metadata = new IdentityAccessCommandMetadataV1(s.CompanyId, actor, correlation.CorrelationId, correlation.CausationId,
            correlation.SourceChannel, correlation.ClientIpAddress, correlation.UserAgent, key);
        IdentityAccessCommandResultV1 answer;
        // The client's frozen human-view token goes to the native comparison inside its owned transaction.
        // The POST source snapshot's current role permissions never replace it, including an original-key replay.
        try { answer = await identityAccessDecisions.DecidePurposeAsync(id, request.Approve, request.ExpectedVersion,
            request.Reason, selection.ExpectedImpactDigest, metadata, Fence, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (UnauthorizedAccessException) { throw new WorkDecisionException(403); }
        catch (ArgumentException) { await Fence(token); throw new WorkDecisionException(400, "work_decision_invalid"); }
        catch (Exception error) when (error is InvalidOperationException or JsonException) { await Fence(token); throw new WorkDecisionException(503); }
        await Fence(token);
        if (answer?.Answer is not { } response || string.IsNullOrWhiteSpace(answer.OriginalCorrelationId)
            || !answer.IsReplay && answer.OriginalCorrelationId != correlation.CorrelationId) throw new WorkDecisionException(503);
        if (response.StatusCode != 200)
        {
            if (response.StatusCode is not (400 or 401 or 403 or 404 or 409 or 422 or 503)) throw new WorkDecisionException(503);
            throw new WorkDecisionException(response.StatusCode, response.Code, answer.IsReplay, answer.OriginalCorrelationId);
        }
        IdentityAccessGrantDecisionReceiptV1? receipt;
        // One genuine original-command receipt read; no second human GET and no historical impact inferred from current roles.
        try { receipt = await identityAccessDecisions.ReadDecisionReceiptAsync(id, request.Approve, request.ExpectedVersion,
            request.Reason, metadata, Fence, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (UnauthorizedAccessException) { throw new WorkDecisionException(403); }
        catch { await Fence(token); throw new WorkDecisionException(503); }
        await Fence(token);
        // A missing historical approval effect is unknown, never an empty permission set or a rollback assertion.
        if (receipt is null || !IdentityAccessGrantDecisionBindingV1.MatchesOriginalDecisionReceipt(s, receipt, answer, request.Approve,
                request.ExpectedVersion, request.Reason, key, actor)
            || request.Approve && (receipt.AppliedImpactDigest != selection.ExpectedImpactDigest || receipt.AppliedPermissionCount is not >= 0)
            || !answer.IsReplay && (!s.Pending || s.SourceVersion != request.ExpectedVersion)) throw new WorkDecisionException(503);
        return new(s.CompanyId, IdentityAccessGrant, id, request.Approve ? "Approved" : "Rejected", answer.IsReplay, receipt.OriginalCorrelationId);
        Task Fence(CancellationToken ct) => IdentityAccessGrantFenceAsync(s, actor, ct);
    }
}
