using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Idempotency.Domain.Executions;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private static bool ValidOfficialSalesRequest(WorkDecisionRequestV1 request) => request.ExpectedVersion == "1"
        && (request.Approve ? request.Reason is null : !string.IsNullOrWhiteSpace(request.Reason)
            && request.Reason == request.Reason.Trim() && request.Reason.Length <= 1000);
    private static bool OfficialSalesDigest(string? digest) => digest is { Length: 64 }
        && digest.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');
    private static bool ValidOfficialSalesProof(string kind, WorkApprovalDecisionSnapshotV1 snapshot)
    {
        if (kind != OfficialSalesInvoice) return snapshot.OfficialSalesInvoiceProof is null;
        return snapshot.OfficialSalesInvoiceProof is { } p && p.CompanyId == snapshot.CompanyId && p.InvoiceId == snapshot.Item.ItemId
            && p.CustomerPartyId != Guid.Empty && p.CurrencyId != Guid.Empty && p.PdfDocumentId != Guid.Empty
            && !string.IsNullOrWhiteSpace(p.OfficialNo) && p.OfficialNo == snapshot.Item.DocumentLabel
            && !string.IsNullOrWhiteSpace(p.RecordedBy) && p.RecordedBy == snapshot.Item.PreparedBy
            && p.RecordedAtUtc.Offset == TimeSpan.Zero && p.RecordedAtUtc == snapshot.Item.WaitingSinceUtc
            && OfficialSalesDigest(p.RecordedFactsDigest) && OfficialSalesDigest(p.PreviewDigest)
            && (snapshot.Pending ? p.Status == "PendingApproval" && snapshot.SourceVersion == "1"
                : (p.Status is "Approved" or "Rejected" or "Withdrawn") && snapshot.SourceVersion == "2")
            && snapshot.Item.Scope is null && snapshot.Item.ExcludedDeciders is not { Count: > 0 }
            && snapshot.Item.Rules.Count == 1 && snapshot.Item.Rules[0].RuleCode == "finance.official-sales-invoice-approval-required"
            && snapshot.Item.References.Count == 3
            && snapshot.Item.References.Count(x => x.Role == "customer" && x.RecordId == p.CustomerPartyId) == 1
            && snapshot.Item.References.Count(x => x.Role == "currency" && x.RecordId == p.CurrencyId) == 1
            && snapshot.Item.References.Count(x => x.Role == "pdf-document" && x.RecordId == p.PdfDocumentId) == 1;
    }

    private async Task<FinanceOfficialSalesInvoiceViewV1> OfficialSalesHumanPreviewAsync(WorkApprovalDecisionSnapshotV1 snapshot,
        string actor, CancellationToken token)
    {
        if (officialSalesInvoices is not IFinanceOfficialSalesInvoiceDecisionReadV1 owner) throw new WorkDecisionException(503);
        var context = new FinanceContext(snapshot.CompanyId, actor, correlation.CorrelationId, correlation.CausationId);
        ReadResult<FinanceOfficialSalesInvoiceViewV1> result;
        try { result = await owner.ReadDecisionAsync(context, snapshot.Item.ItemId, Fence, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { await Fence(token); throw new WorkDecisionException(503); }
        await Fence(token);
        if (result.Outcome == ReadOutcome.Denied && result.Resource is null) throw new WorkDecisionException(403);
        if (result.Outcome != ReadOutcome.Found || !FinanceOfficialSalesInvoiceServiceV1.ValidDecisionView(snapshot.CompanyId, snapshot.Item.ItemId, result.Resource)
            || !OfficialSalesRecordedMatches(snapshot, result.Resource!) || result.Resource!.Version != snapshot.SourceVersion
            || result.Resource.Status.ToString() != snapshot.OfficialSalesInvoiceProof!.Status
            || FinanceOfficialSalesInvoiceServiceV1.DecisionPreviewDigest(result.Resource) != snapshot.OfficialSalesInvoiceProof.PreviewDigest)
            throw new WorkDecisionException(503);
        return result.Resource!;
        Task Fence(CancellationToken ct) => RequireDecisionAuthorityAsync(OfficialSalesInvoice, FinanceOfficialSalesInvoicePermissionNamesV1.Approve, snapshot.CompanyId, actor, ct);
    }

    private static bool OfficialSalesRecordedMatches(WorkApprovalDecisionSnapshotV1 snapshot, FinanceOfficialSalesInvoiceViewV1 view)
    {
        var p = snapshot.OfficialSalesInvoiceProof!;
        return view.CompanyId == p.CompanyId && view.InvoiceId == p.InvoiceId && view.OfficialNo == p.OfficialNo
            && view.CustomerPartyId == p.CustomerPartyId && view.CurrencyId == p.CurrencyId && view.PdfDocumentId == p.PdfDocumentId
            && view.RecordedBy == p.RecordedBy && view.RecordedAtUtc == p.RecordedAtUtc
            && FinanceOfficialSalesInvoiceServiceV1.RecordedFactsDigest(view) == p.RecordedFactsDigest;
    }

    private async Task<WorkDecisionResultV1> DecideOfficialSalesAsync(Guid id, WorkDecisionRequestV1 request, string key,
        WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (officialSalesInvoices is null) throw new WorkDecisionException(503);
        var context = new FinanceContext(snapshot.CompanyId, actor, correlation.CorrelationId, correlation.CausationId);
        var metadata = new CommandMetadata(key);
        FinanceOfficialSalesInvoiceResultV1 result;
        try { result = request.Approve ? await officialSalesInvoices.ApproveAsync(context, metadata, new(id, request.ExpectedVersion), token)
            : await officialSalesInvoices.RejectAsync(context, metadata, new(id, request.ExpectedVersion, request.Reason!), token); }
        catch (Exception error) when (error is InvalidOperationException or JsonException) { throw new WorkDecisionException(503); }
        await RequireDecisionAuthorityAsync(OfficialSalesInvoice, FinanceOfficialSalesInvoicePermissionNamesV1.Approve, snapshot.CompanyId, actor, token);
        if (result is null || !Enum.IsDefined(result.Outcome) || result.Issues is null) throw new WorkDecisionException(503);
        if (result.Outcome != CommandOutcome.Succeeded)
            throw new WorkDecisionException(result.Outcome switch { CommandOutcome.Denied => 403, CommandOutcome.Unavailable => 503,
                CommandOutcome.IdempotencyConflict or CommandOutcome.VersionConflict => 409, _ => 422 },
                result.Issues.FirstOrDefault()?.Code, result.IsReplay, result.OriginalCorrelationId);
        var command = request.Approve ? FinanceOfficialSalesInvoiceServiceV1.ApproveCommand : FinanceOfficialSalesInvoiceServiceV1.RejectCommand;
        var hash = request.Approve ? FinanceOfficialSalesInvoiceServiceV1.ApprovePayloadHash(new(id, request.ExpectedVersion))
            : FinanceOfficialSalesInvoiceServiceV1.RejectPayloadHash(new(id, request.ExpectedVersion, request.Reason!));
        var state = request.Approve ? FinanceOfficialSalesInvoiceStatusV1.Approved : FinanceOfficialSalesInvoiceStatusV1.Rejected;
        if (result.Resource is not { } r || result.DecisionReceipt is not { } receipt || result.Issues.Count != 0 || result.ExistingOfficialNo is not null
            || !FinanceOfficialSalesInvoiceServiceV1.ValidDecisionView(snapshot.CompanyId, id, r) || !OfficialSalesRecordedMatches(snapshot, r)
            || r.Status != state || r.Version != "2" || (request.Approve ? r.ApprovedBy : r.RejectedBy) != actor
            || !request.Approve && r.RejectionReason != request.Reason
            || !snapshot.Pending && snapshot.OfficialSalesInvoiceProof!.Status != state.ToString()
            || receipt.CompanyId != snapshot.CompanyId || receipt.InvoiceId != id || receipt.Command != command
            || receipt.KeyHash != IdempotencyHash.ComputeKey(key) || receipt.PayloadHash != hash || receipt.ActorId != actor
            || receipt.ExpectedVersion != request.ExpectedVersion || receipt.ResultVersion != r.Version
            || receipt.RecordedAtUtc != (request.Approve ? r.ApprovedAtUtc : r.RejectedAtUtc) || receipt.RecordedAtUtc.Offset != TimeSpan.Zero
            || string.IsNullOrWhiteSpace(result.OriginalCorrelationId) || receipt.OriginalCorrelationId != result.OriginalCorrelationId
            || !result.IsReplay && (!snapshot.Pending || snapshot.SourceVersion != request.ExpectedVersion || result.OriginalCorrelationId != correlation.CorrelationId))
            throw new WorkDecisionException(503);
        // Dynamic import differences/tax display are never substituted for the native original command result.
        return new(snapshot.CompanyId, OfficialSalesInvoice, id, state.ToString(), result.IsReplay, result.OriginalCorrelationId);
    }
}
