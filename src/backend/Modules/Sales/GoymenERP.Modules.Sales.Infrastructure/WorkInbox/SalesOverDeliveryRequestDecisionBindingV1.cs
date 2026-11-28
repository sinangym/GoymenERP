using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.ApprovalRequests;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;

namespace GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

public static class SalesOverDeliveryRequestDecisionBindingV1
{
    public const string KindName = "sales.over-delivery-request.v1";
    public static WorkSalesOverDeliveryRequestProofV1 Proof(SalesOverDeliveryRequestDecisionDetailV1 d) => Proof(d.CompanyId, d.RequestId, d.Request, d.Order.DocumentNo, d.SourceFactsDigest, d.PreviewDigest);
    public static WorkSalesOverDeliveryRequestProofV1 Proof(SalesOverDeliveryRequestOriginalReceiptV1 r) => Proof(r.CompanyId, r.Target.RequestId, r.OriginalRequest, r.OrderDocumentNo, r.SourceFactsDigest, r.OriginalDecisionDigest);
    private static WorkSalesOverDeliveryRequestProofV1 Proof(Guid company, Guid id, SalesApprovalRequestView r, string documentNo, string source, string preview)
    {
        if (!SalesQuoteConversionDecisionBindingV1.TryRevision(r.SubjectRevision, out var revision) || !SalesRequestDecisionBindingV1.TryVersion(r.Version, out var version) || r.LineId is not { } line)
            throw new ArgumentException("Malformed native over-delivery identity.");
        return new(company, id, r.SubjectId, r.RequestNo, revision, line, r.RequestedBy, r.RequestedAtUtc, documentNo, r.Ordered!, r.Quantity!, r.RequestedTotal!, r.Status, version, source, preview);
    }
    public static SalesOverDeliveryRequestTargetV1 Target(WorkSalesOverDeliveryRequestProofV1 p, string selectedVersion) => new(p.RequestId, p.OrderId, p.RequestNo, p.SubjectRevision, p.LineId, selectedVersion);
    public static WorkInboxItemV1? Item(SalesOverDeliveryRequestDecisionDetailV1? d) => ValidDetail(d) ? Item(Proof(d!)) : null;
    public static WorkInboxItemV1? Item(SalesOverDeliveryRequestOriginalReceiptV1? r) => ValidReceipt(r) ? Item(Proof(r!)) : null;
    private static WorkInboxItemV1 Item(WorkSalesOverDeliveryRequestProofV1 p) => SalesOverDeliveryRequestWorkInboxSourceV1.Project(p.CompanyId,
        [new(p.RequestId, p.CompanyId, p.OrderId, p.RequestNo, p.OrderDocumentNo, p.Ordered, p.RequestedTotal, p.RequestedBy, p.RequestedAtUtc)], 1).Items.Single();
    public static bool MatchesDecisionItem(WorkInboxItemV1? item, SalesOverDeliveryRequestDecisionDetailV1? d) => ValidDetail(d) && MatchesDecisionProof(item, Proof(d!));
    public static bool MatchesDecisionItem(WorkInboxItemV1? item, SalesApprovalRequestCommandResult? result, SalesOverDeliveryRequestOriginalReceiptV1? receipt) =>
        ValidReceipt(receipt) && result is not null && result.OriginalDecisionReceipt is null && result.OriginalOverDeliveryReceipt == receipt && result.Request == receipt!.OriginalRequest
        && result.CorrelationId == receipt.OriginalCorrelationId && receipt.CurrentResponseDigest == SalesOverDeliveryRequestDecisionDigestsV1.CurrentResponseDigest(result)
        && MatchesDecisionProof(item, Proof(receipt));
    public static bool MatchesDecisionProof(WorkInboxItemV1? item, WorkSalesOverDeliveryRequestProofV1? p)
    {
        if (item is null || !ValidProof(p)) return false;
        WorkInboxItemV1 expected;
        try { expected = Item(p!); } catch (ArgumentException) { return false; } catch (InvalidOperationException) { return false; }
        return item.Module == expected.Module && item.Kind == expected.Kind && item.ItemId == expected.ItemId && item.DocumentLabel == expected.DocumentLabel
            && item.WaitingSinceUtc == expected.WaitingSinceUtc && item.Link == expected.Link && item.PreparedBy == expected.PreparedBy
            && item.Scope is null && item.ExcludedDeciders is null && item.Rules is not null && item.Rules.SequenceEqual(expected.Rules)
            && item.References is not null && item.References.SequenceEqual(expected.References);
    }
    public static bool MatchesDecisionSnapshot(WorkApprovalDecisionSnapshotV1? s, SalesOverDeliveryRequestDecisionDetailV1? d) => s is not null && ValidDetail(d)
        && s.CompanyId == d!.CompanyId && s.SalesOverDeliveryRequestProof == Proof(d) && s.SourceVersion == d.Request.Version
        && s.Pending == (d.Request.Status == "Pending") && MatchesDecisionItem(s.Item, d);
    public static bool MatchesOriginalDecisionReceipt(WorkApprovalDecisionSnapshotV1? s, SalesApprovalRequestCommandResult? response,
        SalesDraftStateCommandRequest request, string key, string actor, bool approve)
    {
        if (string.IsNullOrWhiteSpace(key) || key != key.Trim() || string.IsNullOrWhiteSpace(actor)
            || s?.SalesOverDeliveryRequestProof is not { } p || !MatchesDecisionProof(s.Item, p) || response?.OriginalOverDeliveryReceipt is not { } r
            || request is null || !ValidReceipt(r) || !SalesRequestDecisionBindingV1.TryVersion(request.ExpectedVersion, out var n)
            || n == long.MaxValue || s.CompanyId != p.CompanyId || s.SourceVersion != request.ExpectedVersion || r.ExpectedRequestVersion != n
            || r.ResultRequestVersion != n + 1 || r.ActorId != actor || r.IdempotencyKeyHash != SalesStore.IdempotencyKeyHash(key)
            || r.CommandName != (approve ? SalesApprovalRequestCommands.ApproveOverDeliveryV1 : SalesApprovalRequestCommands.RejectOverDeliveryV1)
            || r.PayloadHash != SalesApprovalRequestHash.Compute(r.CommandName, p.OrderId, p.RequestNo, request)
            || r.Target != Target(p, request.ExpectedVersion) || !response.IsReplay && (!s.Pending || p.RequestVersion != n)
            || p.CompanyId != r.CompanyId || p.SourceFactsDigest != r.SourceFactsDigest || p.RequestedBy != r.OriginalRequest.RequestedBy
            || p.RequestedAtUtc != r.OriginalRequest.RequestedAtUtc || p.OrderDocumentNo != r.OrderDocumentNo
            || p.Ordered != r.OriginalRequest.Ordered || p.Quantity != r.OriginalRequest.Quantity || p.RequestedTotal != r.OriginalRequest.RequestedTotal
            || approve && response.Request.Status != "Approved" || !approve && (response.Request.Status != "Rejected" || response.Request.DecisionReason != request.Reason)) return false;
        return MatchesDecisionItem(s.Item, response, r);
    }
    private static bool ValidDetail(SalesOverDeliveryRequestDecisionDetailV1? d)
    {
        if (d?.Request is not { } r || d.Order is not { } o || d.CompanyId == Guid.Empty || d.RequestId == Guid.Empty || r.SubjectId != o.Id || o.Version < 1 || o.Revision < 0
            || o.PartyId == Guid.Empty || o.CompanyAccountId == Guid.Empty || o.CurrencyId == Guid.Empty || o.State is not ("Draft" or "Cancelled" or "Closed") || o.ApprovalState is not ("Preparing" or "PendingApproval" or "Approved" or "Rejected" or "Withdrawn")
            || o.Line is { } line && (line.Id != r.LineId || line.LineNo < 1 || line.ProductId == Guid.Empty || line.OrderUnitId == Guid.Empty
                || string.IsNullOrWhiteSpace(line.ProductCode) || string.IsNullOrWhiteSpace(line.ProductName) || string.IsNullOrWhiteSpace(line.OrderUnitCode) || string.IsNullOrWhiteSpace(line.CurrentQuantity))
            || d.ApprovalSupported != (r.Status == "Pending") || d.RejectionSupported != (r.Status == "Pending") || !ValidNativeRequest(d.CompanyId, d.RequestId, r)) return false;
        return ValidProof(Proof(d)) && d.SourceFactsDigest == SalesOverDeliveryRequestDecisionDigestsV1.SourceFactsDigest(d.CompanyId, d.RequestId, r, o.DocumentNo)
            && d.PreviewDigest == SalesOverDeliveryRequestDecisionDigestsV1.PreviewDigest(d);
    }
    private static bool ValidNativeRequest(Guid company, Guid id, SalesApprovalRequestView r)
    {
        if (r.Kind != SalesApprovalRequestApiV1.KindOverDelivery || !SalesRequestDecisionBindingV1.TryVersion(r.Version, out var version)
            || !SalesQuoteConversionDecisionBindingV1.TryRevision(r.SubjectRevision, out var revision) || r.Status is not ("Pending" or "Approved" or "Rejected")) return false;
        try
        {
            var source = SalesApprovalRequest.Rebuild(id, company, SalesApprovalRequestKind.OverDelivery, r.SubjectId, r.RequestNo, revision, r.LineId,
                r.Ordered, r.Quantity, r.RequestedTotal, r.LoadingInstructionId, r.ShipmentId, r.Note, r.RequestedBy, r.RequestedAtUtc);
            var state = r.Status == "Pending" ? SalesApprovalRequestState.Requested : r.Status == "Approved" ? SalesApprovalRequestState.Approved : SalesApprovalRequestState.Rejected;
            if (state == SalesApprovalRequestState.Requested && (r.DecidedBy is not null || r.DecidedAtUtc is not null || r.DecisionReason is not null)
                || state != SalesApprovalRequestState.Requested && (r.DecidedBy is null || r.DecidedAtUtc is null)) return false;
            source.Restore(state, state == SalesApprovalRequestState.Requested ? null : new(r.DecidedBy!, r.DecidedAtUtc!.Value, r.DecisionReason), version, r.DecidedAtUtc ?? r.RequestedAtUtc);
            return true;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or GoymenERP.Modules.Sales.Domain.SalesRuleException) { return false; }
    }
    private static bool ValidReceipt(SalesOverDeliveryRequestOriginalReceiptV1? r) => r is not null && r.Target is not null && r.OriginalRequest is not null && r.CompanyId != Guid.Empty
        && ValidNativeRequest(r.CompanyId, r.Target.RequestId, r.OriginalRequest) && r.ExpectedRequestVersion > 0 && r.ExpectedRequestVersion < long.MaxValue
        && r.ResultRequestVersion == r.ExpectedRequestVersion + 1 && r.OriginalRequest.Version == r.ResultRequestVersion.ToString(CultureInfo.InvariantCulture)
        && r.Target.OrderId == r.OriginalRequest.SubjectId && r.Target.RequestNo == r.OriginalRequest.RequestNo && r.Target.LineId == r.OriginalRequest.LineId
        && r.Target.RequestVersion == r.ExpectedRequestVersion.ToString(CultureInfo.InvariantCulture)
        && r.OriginalRequest.SubjectRevision == SalesDraftApiV1.RevisionText(r.Target.SubjectRevision) && r.Target.RequestId != Guid.Empty
        && r.SourceFactsDigest == SalesOverDeliveryRequestDecisionDigestsV1.SourceFactsDigest(r.CompanyId, r.Target.RequestId, r.OriginalRequest, r.OrderDocumentNo)
        && r.OriginalResultDigest == SalesOverDeliveryRequestDecisionDigestsV1.CurrentResponseDigest(new(r.OriginalRequest, false, r.OriginalCorrelationId))
        && r.OriginalDecisionDigest == SalesOverDeliveryRequestDecisionDigestsV1.OriginalDecisionDigest(r) && r.RequestAuditEventId != Guid.Empty && NativeDigest(r.RequestAuditIntegrityHash)
        && r.DecidedAtUtc == r.OriginalRequest.DecidedAtUtc && r.ActorId == r.OriginalRequest.DecidedBy
        && (r.CommandName == SalesApprovalRequestCommands.ApproveOverDeliveryV1 ? r.OriginalRequest.Status == "Approved" && r.OrderAuditEventId is not null && NativeDigest(r.OrderAuditIntegrityHash)
            && r.OriginalOrderVersionBefore is > 0 and < long.MaxValue && r.OriginalOrderVersionAfter == r.OriginalOrderVersionBefore + 1
            : r.CommandName == SalesApprovalRequestCommands.RejectOverDeliveryV1 && r.OriginalRequest.Status == "Rejected" && r.OrderAuditEventId is null && r.OrderAuditIntegrityHash is null && r.OriginalOrderVersionBefore is null && r.OriginalOrderVersionAfter is null);
    private static bool ValidProof(WorkSalesOverDeliveryRequestProofV1? p) => p is not null && p.CompanyId != Guid.Empty && p.RequestId != Guid.Empty && p.OrderId != Guid.Empty
        && p.RequestNo > 0 && p.SubjectRevision >= 0 && p.LineId != Guid.Empty && !string.IsNullOrWhiteSpace(p.RequestedBy) && p.RequestedBy.Length <= SalesApprovalRequest.MaximumActorLength
        && p.RequestedAtUtc.Offset == TimeSpan.Zero && p.RequestVersion > 0 && p.RequestStatus is "Pending" or "Approved" or "Rejected"
        && decimal.TryParse(p.Quantity, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var quantity) && quantity > 0
        && !string.IsNullOrWhiteSpace(p.OrderDocumentNo) && p.OrderDocumentNo.Trim() == p.OrderDocumentNo && Digest(p.SourceFactsDigest) && Digest(p.PreviewDigest);
    private static bool NativeDigest(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
    private static bool Digest(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
