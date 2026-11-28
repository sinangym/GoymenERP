using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.ApprovalRequests;
using GoymenERP.Modules.Sales.Domain.Quotes;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;

namespace GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

/// <summary>Pure owner bindings. Reuses the queue formatter and native hashes; no historical full HTTP snapshot or second read.</summary>
public static class SalesQuoteConversionDecisionBindingV1
{
    public static WorkSalesQuoteConversionProofV1 Proof(SalesQuoteConversionDecisionDetailV1 detail)
    {
        var r = detail.Request; var q = detail.Quote.Quote;
        if (!TryRevision(r.SubjectRevision, out var selectedRevision) || !TryRevision(q.Revision, out var quoteRevision))
            throw new ArgumentException("Native revision is malformed.", nameof(detail));
        return new(q.CompanyId, detail.RequestId, q.Id, r.RequestNo, selectedRevision, r.Version, q.Version, quoteRevision,
            q.DocumentNo, r.RequestedBy, r.RequestedAtUtc, r.Status, q.State, detail.ApprovalSupported, detail.RejectionSupported,
            detail.SourceFactsDigest, detail.PreviewDigest);
    }

    public static SalesQuoteConversionTargetV1 Target(WorkSalesQuoteConversionProofV1 proof) =>
        new(proof.RequestId, proof.QuoteId, proof.RequestNo, proof.SubjectRevision, proof.RequestVersion);

    public static WorkInboxItemV1? Item(SalesQuoteConversionDecisionDetailV1? detail) => ValidDetail(detail) ? Item(Proof(detail!)) : null;
    public static bool MatchesDecisionItem(WorkInboxItemV1? item, SalesQuoteConversionDecisionDetailV1? detail) =>
        ValidDetail(detail) && MatchesDecisionProof(item, Proof(detail!));

    public static bool MatchesDecisionProof(WorkInboxItemV1? item, WorkSalesQuoteConversionProofV1? p)
    {
        if (!ValidProof(p) || item is null) return false;
        var expected = Item(p!);
        return item.Module == expected.Module && item.Kind == expected.Kind && item.ItemId == expected.ItemId
            && item.DocumentLabel == expected.DocumentLabel && item.WaitingSinceUtc == expected.WaitingSinceUtc && item.Link == expected.Link
            && item.PreparedBy is null && item.Scope is null && item.ExcludedDeciders is null
            && item.Rules is not null && item.Rules.SequenceEqual(expected.Rules)
            && item.References is not null && item.References.SequenceEqual(expected.References);
    }

    public static bool MatchesDecisionSnapshot(WorkApprovalDecisionSnapshotV1? snapshot, SalesQuoteConversionDecisionDetailV1? detail) =>
        ValidSnapshot(snapshot) && ValidDetail(detail) && snapshot!.SalesQuoteConversionProof == Proof(detail!);

    public static bool MatchesOriginalDecisionReceipt(WorkApprovalDecisionSnapshotV1? snapshot, SalesQuoteConversionTargetV1 target,
        SalesQuoteResponse? response, SalesQuoteExpiryApprovalRequest request, string key, string actorId)
    {
        if (!ValidSnapshot(snapshot) || response?.OriginalDecisionReceipt is not { } receipt || response.Detail?.Quote is not { } current
            || request is null || request.Note is not null || !BoundReceipt(snapshot!, target, receipt, response.IsReplay, key, actorId,
                request.ExpectedVersion, SalesQuoteCommands.ApproveExpiredConversionV1, out var expected, out var result)
            || !TryVersion(current.Version, out var observed) || observed < result
            || !TryVersion(snapshot!.SalesQuoteConversionProof!.QuoteVersion, out var selectedVersion)
            || observed < selectedVersion || !response.IsReplay && (selectedVersion != expected || !snapshot!.SalesQuoteConversionProof!.ApprovalSupported)) return false;
        var p = snapshot!.SalesQuoteConversionProof!;
        return current.Id == p.QuoteId && current.CompanyId == p.CompanyId && current.DocumentNo == p.QuoteDocumentNo
            && receipt.PayloadHash == SalesQuoteHash.Compute(p.QuoteId, request) && receipt.NoteOrReason is null
            && receipt.OriginalState == "Approved" && receipt.OriginalCorrelationId == response.OriginalCorrelationId
            && receipt.CurrentResponseDigest == SalesQuoteDecisionDigestsV1.CurrentResponseDigest(response);
    }

    public static bool MatchesOriginalDecisionReceipt(WorkApprovalDecisionSnapshotV1? snapshot, SalesQuoteConversionTargetV1 target,
        SalesApprovalRequestCommandResult? response, SalesDraftStateCommandRequest request, string key, string actorId)
    {
        if (!ValidSnapshot(snapshot) || response?.OriginalDecisionReceipt is not { } receipt || response.Request is not { } current
            || request is null || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > SalesApprovalRequest.MaximumTextLength
            || !BoundReceipt(snapshot!, target, receipt, response.IsReplay, key, actorId, request.ExpectedVersion,
                SalesApprovalRequestCommands.RejectQuoteConversionV1, out var expected, out var result)
            || !TryVersion(current.Version, out var observed) || observed < result
            || !TryVersion(snapshot!.SalesQuoteConversionProof!.RequestVersion, out var selectedVersion)
            || observed < selectedVersion || !response.IsReplay && (selectedVersion != expected || !snapshot!.SalesQuoteConversionProof!.RejectionSupported)) return false;
        var p = snapshot!.SalesQuoteConversionProof!;
        return current.Kind == SalesApprovalRequestApiV1.KindQuoteExpiredConversion && current.SubjectId == p.QuoteId && current.RequestNo == p.RequestNo
            && current.SubjectRevision == SalesDraftApiV1.RevisionText(p.SubjectRevision)
            && receipt.PayloadHash == SalesApprovalRequestHash.Compute(SalesApprovalRequestCommands.RejectQuoteConversionV1, p.QuoteId, p.RequestNo, request)
            && receipt.NoteOrReason == request.Reason && receipt.OriginalState == "Rejected"
            && receipt.OriginalCorrelationId == response.CorrelationId
            && (observed != result || current.Status == "Rejected" && current.DecidedBy == actorId
                && current.DecidedAtUtc == receipt.DecidedAtUtc && current.DecisionReason == request.Reason)
            && receipt.CurrentResponseDigest == SalesQuoteDecisionDigestsV1.CurrentResponseDigest(response);
    }

    private static bool BoundReceipt(WorkApprovalDecisionSnapshotV1 snapshot, SalesQuoteConversionTargetV1? target,
        SalesQuoteOriginalDecisionReceiptV1 receipt, bool replay, string key, string actor, string expectedVersion, string command,
        out long expected, out long result)
    {
        expected = result = 0; var p = snapshot.SalesQuoteConversionProof!;
        if (target is null || target.RequestId != p.RequestId || target.QuoteId != p.QuoteId || target.RequestNo != p.RequestNo
            || target.SubjectRevision != p.SubjectRevision || !TryVersion(target.RequestVersion, out var originalRequestVersion)
            || !TryVersion(p.RequestVersion, out var currentRequestVersion) || originalRequestVersion > currentRequestVersion
            || !replay && target != Target(p) || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(actor)
            || !TryVersion(expectedVersion, out expected) || expected == long.MaxValue || !TryVersion(receipt.ResultVersion, out result)
            || result != expected + 1) return false;
        return receipt.CompanyId == p.CompanyId && receipt.Target == target && receipt.CommandName == command
            && receipt.ActorId == actor && receipt.ExpectedVersion == expectedVersion && receipt.IdempotencyKeyHash == SalesStore.IdempotencyKeyHash(key)
            && !string.IsNullOrWhiteSpace(receipt.OriginalCorrelationId) && receipt.AuditEventId != Guid.Empty
            && receipt.AuditIntegrityHash is { Length: 64 } auditHash && auditHash.All(char.IsAsciiHexDigit)
            && Digest(receipt.OriginalDecisionDigest) && receipt.OriginalDecisionDigest == SalesQuoteDecisionDigestsV1.OriginalDecisionDigest(receipt);
    }

    private static bool ValidSnapshot(WorkApprovalDecisionSnapshotV1? snapshot) => snapshot?.SalesQuoteConversionProof is { } p
        && snapshot.CompanyId == p.CompanyId && snapshot.SourceVersion == p.QuoteVersion
        && snapshot.Pending == (p.RequestStatus == "Pending") && MatchesDecisionProof(snapshot.Item, p);

    private static WorkInboxItemV1 Item(WorkSalesQuoteConversionProofV1 proof) => SalesQuoteConversionRequestWorkInboxSourceV1.Project(proof.CompanyId,
        [new(proof.RequestId, proof.CompanyId, proof.QuoteId, proof.RequestNo, proof.QuoteDocumentNo, proof.RequestedBy, proof.RequestedAtUtc)], 1).Items.Single();

    private static bool ValidDetail(SalesQuoteConversionDecisionDetailV1? d)
    {
        if (d?.Request is not { } r || d.Quote?.Quote is not { } q || d.RequestId == Guid.Empty || r.SubjectId != q.Id
            || r.Kind != SalesApprovalRequestApiV1.KindQuoteExpiredConversion || r.LineId is not null || r.Ordered is not null || r.Quantity is not null
            || r.RequestedTotal is not null || r.LoadingInstructionId is not null || r.ShipmentId is not null
            || !TryRevision(r.SubjectRevision, out var selectedRevision) || !TryRevision(q.Revision, out var quoteRevision)
            || selectedRevision > quoteRevision || d.Quote.Lines is not { Count: > 0 } lines || lines.Count != q.LineCount || lines.Count > SalesQuote.MaximumLines
            || lines.Any(line => line is null || line.LineId == Guid.Empty || line.LineNo < 1 || line.ProductId == Guid.Empty
                || !TryVersion(line.ProductVersion, out _) || line.OrderUnitId == Guid.Empty || string.IsNullOrWhiteSpace(line.Quantity)
                || !Enum.IsDefined(line.PriceDisclosure) || line.PriceDisclosure == SalesPriceDisclosure.Withheld
                    && (line.UnitPrice is not null || line.PriceUnitId is not null || line.DiscountPercent is not null))
            || lines.Select(line => line.LineId).Distinct().Count() != lines.Count || lines.Select(line => line.LineNo).Distinct().Count() != lines.Count
            || d.Quote.RequestIds is null || d.Quote.RequestIds.Count > SalesQuote.MaximumRequests || d.Quote.RequestIds.Any(id => id == Guid.Empty)
            || d.Quote.RequestIds.Distinct().Count() != d.Quote.RequestIds.Count || d.Quote.Revisions is not { Count: > 0 } revisions
            || revisions.Any(v => v is null || !TryRevision(v.Revision, out _) || string.IsNullOrWhiteSpace(v.RevisedBy))
            || !revisions.Any(v => v.Revision == q.Revision) || d.Quote.ExpiryApprovals is not { } approvals || d.Quote.Expiry is not { } expiry
            || approvals.Any(a => a is null || !TryRevision(a.Revision, out _) || string.IsNullOrWhiteSpace(a.ApprovedBy))
            || expiry.IsApproved != approvals.Any(a => a.Revision == q.Revision)
            || d.Quote.DueTermsDisclosure is not ("Visible" or "Withheld") || d.Quote.DueTermsDisclosure == "Withheld" && d.Quote.DueTerms is not null
            || lines.Any(line => line.PriceDisclosure.ToString() != d.Quote.DueTermsDisclosure)
            || string.IsNullOrWhiteSpace(d.Quote.CreatedBy) || q.PartyId == Guid.Empty || q.CompanyAccountId == Guid.Empty || q.CurrencyId == Guid.Empty
            || r.Status == "Rejected" && (string.IsNullOrWhiteSpace(r.DecidedBy) || r.DecidedAtUtc is null || string.IsNullOrWhiteSpace(r.DecisionReason)
                || r.DecisionReason.Length > SalesApprovalRequest.MaximumTextLength)
            || r.Status != "Rejected" && (r.DecidedBy is not null || r.DecidedAtUtc is not null || r.DecisionReason is not null)) return false;
        var hasApproval = approvals.Any(a => a.Revision == r.SubjectRevision);
        var status = r.Status == "Rejected" ? "Rejected" : hasApproval ? "Approved" : q.State == "Open" && selectedRevision == quoteRevision ? "Pending" : "Stale";
        return r.Status == status && ValidProof(Proof(d))
            && d.SourceFactsDigest == SalesQuoteDecisionDigestsV1.SourceFactsDigest(r, d.Quote)
            && d.PreviewDigest == SalesQuoteDecisionDigestsV1.PreviewDigest(d);
    }

    private static bool ValidProof(WorkSalesQuoteConversionProofV1? p) => p is not null && p.CompanyId != Guid.Empty && p.RequestId != Guid.Empty
        && p.QuoteId != Guid.Empty && p.RequestNo > 0 && p.SubjectRevision >= 0 && p.QuoteRevision >= p.SubjectRevision
        && TryVersion(p.RequestVersion, out _) && TryVersion(p.QuoteVersion, out _) && p.QuoteState is "Open" or "Lost" or "Converted"
        && p.RequestStatus is "Pending" or "Approved" or "Rejected" or "Stale" && p.ApprovalSupported == (p.RequestStatus == "Pending")
        && p.RejectionSupported == (p.RequestStatus != "Rejected") && (p.RequestStatus != "Pending" || p.QuoteState == "Open" && p.SubjectRevision == p.QuoteRevision)
        && !string.IsNullOrWhiteSpace(p.RequestedBy) && p.RequestedBy.Length <= SalesApprovalRequest.MaximumActorLength
        && !string.IsNullOrWhiteSpace(p.QuoteDocumentNo) && p.QuoteDocumentNo.Trim() == p.QuoteDocumentNo
        && p.QuoteDocumentNo.Length <= SalesQuote.MaximumDocumentNoLength
        && (p.QuoteDocumentNo + " #" + p.RequestNo.ToString(CultureInfo.InvariantCulture)).Length <= WorkInboxRulesV1.MaxDocumentLabelLength
        && Digest(p.SourceFactsDigest) && Digest(p.PreviewDigest);

    public static bool TryVersion(string? text, out long version) => SalesRequestDecisionBindingV1.TryVersion(text, out version);
    public static bool TryRevision(string? text, out int revision)
    {
        revision = 0;
        return text is { Length: > 4 } && text.StartsWith("Rev.", StringComparison.Ordinal)
            && int.TryParse(text.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out revision) && revision >= 0
            && text == SalesDraftApiV1.RevisionText(revision);
    }
    private static bool Digest(string? text) => text is { Length: 64 } && text.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
