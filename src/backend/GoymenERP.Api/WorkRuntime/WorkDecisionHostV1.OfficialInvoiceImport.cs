using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Idempotency.Domain.Executions;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private static FinanceOfficialInvoiceImportBatchViewV1 ImportHeader(WorkApprovalDecisionSnapshotV1 s)
    {
        var p = s.OfficialInvoiceImportProof!;
        return new(p.BatchId, p.CompanyId, p.BatchVersion, p.BatchChecksum, p.TemplateVersion,
            s.Pending ? FinanceOfficialInvoiceImportApprovalStatusV1.Pending : FinanceOfficialInvoiceImportApprovalStatusV1.Approved,
            s.SourceVersion, p.RowCount, p.UploadedBy, p.UploadedAtUtc, p.ApprovedBy, p.ApprovedAtUtc);
    }

    private static bool ValidOfficialImportProof(string kind, WorkApprovalDecisionSnapshotV1 snapshot)
    {
        if (kind != OfficialInvoiceImport) return snapshot.OfficialInvoiceImportProof is null;
        return snapshot.OfficialInvoiceImportProof is { } proof && proof.CompanyId == snapshot.CompanyId
            && proof.BatchId == snapshot.Item.ItemId && proof.UploadedBy == snapshot.Item.PreparedBy
            && proof.PreviewDigest is { Length: 64 } && proof.PreviewDigest.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f')
            && proof.UploadedAtUtc == snapshot.Item.WaitingSinceUtc && snapshot.Item.Scope is null
            && snapshot.Item.ExcludedDeciders is not { Count: > 0 } && snapshot.Item.References.Count == 0
            && snapshot.Item.Rules.Count == 1
            && snapshot.Item.Rules[0] == new WorkApprovalRuleResultV1(NativeApprovalWorkInboxSourceV1.OfficialInvoiceImportRule, null, null, null, null)
            && FinanceOfficialInvoiceImportPendingApprovalReadServiceV1.ValidDecisionHeader(snapshot.CompanyId, proof.BatchId, ImportHeader(snapshot));
    }

    private async Task<FinanceOfficialInvoiceImportPendingBatchDetailV1> OfficialImportHumanPreviewAsync(
        WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (officialInvoiceImportReads is not IFinanceOfficialInvoiceImportDecisionReadServiceV1 owner) throw new WorkDecisionException(503);
        ReadResult<FinanceOfficialInvoiceImportPendingBatchDetailV1> result;
        var context = new FinanceContext(snapshot.CompanyId, actor, correlation.CorrelationId, correlation.CausationId);
        try { result = await owner.GetDecisionAsync(context, snapshot.Item.ItemId, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { await RequireDecisionAuthorityAsync(OfficialInvoiceImport, FinanceOfficialInvoiceImportPermissionNamesV1.Approve, snapshot.CompanyId, actor, token); throw new WorkDecisionException(503); }
        await RequireDecisionAuthorityAsync(OfficialInvoiceImport, FinanceOfficialInvoiceImportPermissionNamesV1.Approve, snapshot.CompanyId, actor, token);
        if (result.Outcome == ReadOutcome.Denied && result.Resource is null) throw new WorkDecisionException(403);
        if (result.Outcome != ReadOutcome.Found || result.Resource?.Batch != ImportHeader(snapshot)
            || !FinanceOfficialInvoiceImportPendingApprovalReadServiceV1.ValidDecisionDetail(context, snapshot.Item.ItemId, result.Resource)
            || FinanceOfficialInvoiceImportPendingApprovalReadServiceV1.DecisionPreviewDigest(result.Resource) != snapshot.OfficialInvoiceImportProof!.PreviewDigest)
            throw new WorkDecisionException(503);
        return result.Resource!;
    }

    private async Task<WorkDecisionResultV1> DecideOfficialImportAsync(Guid id, WorkDecisionRequestV1 request,
        string key, WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (officialInvoiceImports is null) throw new WorkDecisionException(503);
        var context = new FinanceContext(snapshot.CompanyId, actor, correlation.CorrelationId, correlation.CausationId);
        var native = new FinanceOfficialInvoiceImportApproveRequestV1(id, request.ExpectedVersion);
        FinanceOfficialInvoiceImportApprovalResultV1 result;
        try { result = await officialInvoiceImports.ApproveAsync(context, new(key), native, token); }
        catch (Exception error) when (error is InvalidOperationException or JsonException) { throw new WorkDecisionException(503); }
        await RequireDecisionAuthorityAsync(OfficialInvoiceImport, FinanceOfficialInvoiceImportPermissionNamesV1.Approve, snapshot.CompanyId, actor, token);
        if (result is null || !Enum.IsDefined(result.Outcome) || result.Issues is null) throw new WorkDecisionException(503);
        if (result.Outcome != CommandOutcome.Succeeded)
            throw new WorkDecisionException(result.Outcome switch { CommandOutcome.Denied => 403, CommandOutcome.Unavailable => 503,
                CommandOutcome.IdempotencyConflict or CommandOutcome.VersionConflict => 409, _ => 422 },
                result.Issues.FirstOrDefault()?.Code, result.IsReplay, result.OriginalCorrelationId);
        var p = snapshot.OfficialInvoiceImportProof!;
        if (result.Resource is not { } r || result.DecisionReceipt is not { } receipt || result.Issues.Count != 0
            || !FinanceOfficialInvoiceImportPendingApprovalReadServiceV1.ValidDecisionHeader(snapshot.CompanyId, id, r)
            || r.Status != FinanceOfficialInvoiceImportApprovalStatusV1.Approved || r.Version != "2"
            || r.BatchVersion != p.BatchVersion || r.BatchChecksum != p.BatchChecksum || r.TemplateVersion != p.TemplateVersion
            || r.RowCount != p.RowCount || r.UploadedBy != p.UploadedBy || r.UploadedAtUtc != p.UploadedAtUtc
            || r.ApprovedBy != actor || r.ApprovedAtUtc is not { } decided
            || !snapshot.Pending && (r.ApprovedBy != p.ApprovedBy || r.ApprovedAtUtc != p.ApprovedAtUtc)
            || receipt.CompanyId != snapshot.CompanyId || receipt.BatchId != id || receipt.ApprovalId == Guid.Empty
            || receipt.CommandName != FinanceOfficialInvoiceImportApprovalServiceV1.ApproveCommand
            || receipt.IdempotencyKeyHash != IdempotencyHash.ComputeKey(key)
            || receipt.PayloadHash != FinanceOfficialInvoiceImportApprovalServiceV1.ApprovalPayloadHash(native)
            || receipt.ActorId != actor || receipt.ExpectedVersion != request.ExpectedVersion || receipt.ResultVersion != r.Version
            || receipt.RecordedAtUtc != decided || receipt.RecordedAtUtc.Offset != TimeSpan.Zero
            || string.IsNullOrWhiteSpace(result.OriginalCorrelationId) || receipt.OriginalCorrelationId != result.OriginalCorrelationId
            || !result.IsReplay && (!snapshot.Pending || snapshot.SourceVersion != request.ExpectedVersion
                || result.OriginalCorrelationId != correlation.CorrelationId)) throw new WorkDecisionException(503);
        // No current batch or approval read can substitute for the original native command receipt.
        return new(snapshot.CompanyId, OfficialInvoiceImport, id, "Approved", result.IsReplay, result.OriginalCorrelationId);
    }
}
