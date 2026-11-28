using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Requests;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;

namespace GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

/// <summary>One owner formatter and the existing native hashes; no historical HTTP-detail reconstruction or second read.</summary>
public static class SalesRequestDecisionBindingV1
{
    public static WorkSalesRequestProofV1 Proof(SalesRequestDecisionDetailV1 detail)
    {
        var h = detail.Detail.Request;
        return new(h.CompanyId, h.Id, h.DocumentNo, h.PartyId, h.CompanyAccountId, h.BusinessDate, h.CreatedAtUtc,
            h.Version, h.State, detail.ApprovalSupported, detail.RejectionSupported, detail.SourceFactsDigest, detail.PreviewDigest);
    }

    public static WorkInboxItemV1? Item(SalesRequestDecisionDetailV1? detail) => ValidDetail(detail) ? Item(Proof(detail!)) : null;

    public static bool MatchesDecisionItem(WorkInboxItemV1? item, SalesRequestDecisionDetailV1? detail) =>
        ValidDetail(detail) && MatchesDecisionProof(item, Proof(detail!));

    public static bool MatchesDecisionProof(WorkInboxItemV1? item, WorkSalesRequestProofV1? proof)
    {
        if (!ValidProof(proof) || item is null) return false;
        var expected = Item(proof!);
        return item.Module == expected.Module && item.Kind == expected.Kind && item.ItemId == expected.ItemId
            && item.DocumentLabel == expected.DocumentLabel && item.WaitingSinceUtc == expected.WaitingSinceUtc && item.Link == expected.Link
            && item.PreparedBy is null && item.Scope is null && item.ExcludedDeciders is null
            && item.Rules is not null && item.Rules.SequenceEqual(expected.Rules)
            && item.References is not null && item.References.SequenceEqual(expected.References);
    }

    public static bool MatchesDecisionSnapshot(WorkApprovalDecisionSnapshotV1? snapshot, SalesRequestDecisionDetailV1? detail) =>
        snapshot is not null && ValidDetail(detail) && snapshot.CompanyId == detail!.Detail.Request.CompanyId
        && snapshot.SourceVersion == detail.Detail.Request.Version && snapshot.Pending == (detail.Detail.Request.State == "Submitted")
        && snapshot.SalesRequestProof == Proof(detail) && MatchesDecisionItem(snapshot.Item, detail);

    public static bool MatchesOriginalDecisionReceipt(WorkApprovalDecisionSnapshotV1? snapshot, SalesRequestResponse? response,
        string commandName, SalesDraftStateCommandRequest request, string key, string actorId)
    {
        if (snapshot?.SalesRequestProof is not { } selected || snapshot.CompanyId != selected.CompanyId
            || snapshot.SourceVersion != selected.Version || snapshot.Pending != (selected.State == "Submitted")
            || !MatchesDecisionProof(snapshot.Item, selected) || response?.OriginalDecisionReceipt is not { } receipt
            || response.Detail?.Request is not { } current || request is null || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(actorId)
            || !TryVersion(request.ExpectedVersion, out var expected) || expected == long.MaxValue
            || !TryVersion(receipt.ResultVersion, out var result) || result != expected + 1
            || !TryVersion(current.Version, out var observed) || observed < result
            || commandName is not (SalesRequestCommands.ApproveV1 or SalesRequestCommands.RejectV1)) return false;
        var approve = commandName == SalesRequestCommands.ApproveV1;
        return receipt.CompanyId == selected.CompanyId && receipt.RequestId == selected.RequestId
            && receipt.DocumentNo == selected.DocumentNo && receipt.PartyId == selected.PartyId
            && receipt.CompanyAccountId == selected.CompanyAccountId && receipt.BusinessDate == selected.BusinessDate
            && current.CompanyId == receipt.CompanyId && current.Id == receipt.RequestId && current.DocumentNo == receipt.DocumentNo
            && current.PartyId == receipt.PartyId && current.CompanyAccountId == receipt.CompanyAccountId && current.BusinessDate == receipt.BusinessDate
            && receipt.CommandName == commandName && receipt.ExpectedVersion == request.ExpectedVersion && receipt.ActorId == actorId
            && receipt.IdempotencyKeyHash == SalesStore.IdempotencyKeyHash(key)
            && receipt.PayloadHash == SalesRequestHash.Compute(commandName, selected.RequestId, request)
            && receipt.OriginalCorrelationId == response.OriginalCorrelationId && !string.IsNullOrWhiteSpace(receipt.OriginalCorrelationId)
            && receipt.Decision is { } decision && decision.DecidedBy == actorId
            && receipt.State == (approve ? "Approved" : "Rejected") && decision.RejectionReason == request.Reason
            && (approve ? request.Reason is null : !string.IsNullOrWhiteSpace(request.Reason) && request.Reason.Length <= SalesRequest.MaximumReasonLength)
            && (observed != result || current.State == receipt.State && response.Detail.Decision == decision)
            && receipt.AuditEventId != Guid.Empty && receipt.AuditIntegrityHash is { Length: 64 } auditHash && auditHash.All(char.IsAsciiHexDigit)
            && receipt.OriginalDecisionDigest == SalesRequestDecisionDigestsV1.OriginalDecisionDigest(receipt)
            && receipt.CurrentResponseDigest == SalesRequestDecisionDigestsV1.CurrentResponseDigest(response);
    }

    public static bool TryVersion(string? value, out long version) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out version) && version > 0
        && version.ToString(CultureInfo.InvariantCulture) == value;

    private static WorkInboxItemV1 Item(WorkSalesRequestProofV1 proof) => SalesRequestWorkInboxSourceV1.Project(proof.CompanyId,
        [new(proof.RequestId, proof.CompanyId, proof.DocumentNo, proof.CreatedAtUtc)], 1).Items.Single();

    private static bool ValidDetail(SalesRequestDecisionDetailV1? detail) => detail?.Detail?.Request is { } h
        && detail.Detail.Lines is { Count: > 0 } lines && lines.Count <= SalesRequest.MaximumLines && lines.Count == h.LineCount
        && lines.All(line => line is not null)
        && (h.State == "Submitted" ? detail.Detail.Decision is null
            : detail.Detail.Decision is { } decision && !string.IsNullOrWhiteSpace(decision.DecidedBy)
                && decision.DecidedBy.Length <= SalesRequest.MaximumActorLength
                && (h.State == "Approved" ? decision.RejectionReason is null
                    : !string.IsNullOrWhiteSpace(decision.RejectionReason) && decision.RejectionReason.Length <= SalesRequest.MaximumReasonLength))
        && ValidProof(Proof(detail)) && detail.SourceFactsDigest == SalesRequestDecisionDigestsV1.SourceFactsDigest(detail.Detail)
        && detail.PreviewDigest == SalesRequestDecisionDigestsV1.PreviewDigest(detail);

    private static bool ValidProof(WorkSalesRequestProofV1? p) => p is not null && p.CompanyId != Guid.Empty && p.RequestId != Guid.Empty
        && p.PartyId != Guid.Empty && p.CompanyAccountId != Guid.Empty && !string.IsNullOrWhiteSpace(p.DocumentNo)
        && p.DocumentNo == p.DocumentNo.Trim() && p.DocumentNo.Length <= SalesRequest.MaximumDocumentNoLength
        && p.DocumentNo.Length <= WorkInboxRulesV1.MaxDocumentLabelLength
        && TryVersion(p.Version, out var version) && p.State is "Submitted" or "Approved" or "Rejected"
        && (p.State == "Submitted" || version >= 2) && p.ApprovalSupported == (p.State == "Submitted")
        && p.RejectionSupported == (p.State is "Submitted" or "Approved") && Digest(p.SourceFactsDigest) && Digest(p.PreviewDigest);

    private static bool Digest(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
