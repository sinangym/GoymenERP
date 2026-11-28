using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.ApprovalRequests;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

public sealed partial class SalesStore
{
    private void RequireSelectedOverDeliveryTarget(SalesApprovalRequest request)
    {
        if (ownedOverDeliveryRequestApproval?.Decision is not { } decision) return;
        var t = decision.Target;
        if (!ValidOverTarget(t) || request.CompanyId != ownedOverDeliveryRequestApproval.CompanyId || request.Id != t.RequestId
            || request.Kind != SalesApprovalRequestKind.OverDelivery || request.SubjectId != t.OrderId || request.RequestNo != t.RequestNo
            || request.SubjectRevision != t.SubjectRevision || request.LineId != t.LineId || request.Version.ToString(CultureInfo.InvariantCulture) != t.RequestVersion)
            throw new SalesDraftServiceException(503, "service_unavailable");
    }
    private async Task<SalesApprovalRequestCommandResult> AttachOverDeliveryRequestReceiptAsync(SalesApprovalRequestCommandResult result,
        OwnedOverDeliveryRequestApproval state, CancellationToken token)
    {
        var key = IdempotencyKeyHash(state.Envelope.IdempotencyKey);
        var attempts = await OverDeliveryRequestApprovalReadAsync(() => dbContext.ApprovalRequestAttempts.AsNoTracking().Where(x =>
            x.CompanyId == state.CompanyId && x.CommandName == state.Command && x.IdempotencyKeyHash == key).Take(2).ToArrayAsync(token), token);
        if (attempts.Length != 1 || attempts[0].RequestId is not { } id) return result;
        var attempt = attempts[0];
        var audits = await OverDeliveryRequestApprovalReadAsync(() => dbContext.Set<AuditEvent>().AsNoTracking().Where(x =>
            x.CompanyId == state.CompanyId && x.EntityType == ApprovalRequestEntityType && x.EntityId == id.ToString("D")
            && x.IdempotencyKeyHash == key && (x.EventType == "sales.order.over-delivery-request-approved.v1" || x.EventType == "sales.order.over-delivery-request-rejected.v1"))
            .Take(2).ToArrayAsync(token), token);
        var documentNo = await OverDeliveryRequestApprovalReadAsync(() => dbContext.Drafts.AsNoTracking().Where(x =>
            x.CompanyId == state.CompanyId && x.Id == attempt.SubjectId).Select(x => x.DocumentNo).SingleOrDefaultAsync(token), token);
        AuditEvent[] orderAudits = [];
        if (state.Command == SalesApprovalRequestCommands.ApproveOverDeliveryV1)
            orderAudits = await OverDeliveryRequestApprovalReadAsync(() => dbContext.Set<AuditEvent>().AsNoTracking().Where(x =>
                x.CompanyId == state.CompanyId && x.EntityType == DraftEntityType && x.EntityId == attempt.SubjectId.ToString("D")
                && x.EventType == "sales.order-draft.over-delivery-approved.v1" && x.IdempotencyKeyHash == key).Take(2).ToArrayAsync(token), token);
        var receipt = RestoreOverDeliveryRequestReceipt(attempt, audits, orderAudits, documentNo, state.Envelope.ActorId, state.NativeRequest, state.Decision, result);
        return result with { OriginalOverDeliveryReceipt = receipt };
    }

    internal static SalesOverDeliveryRequestOriginalReceiptV1? RestoreOverDeliveryRequestReceipt(ApprovalRequestAttemptRow? attempt,
        IReadOnlyList<AuditEvent> events, IReadOnlyList<AuditEvent> orderEvents, string? documentNo, string actor,
        SalesDraftStateCommandRequest? nativeRequest = null, SalesOverDeliveryRequestDecisionCommandV1? decision = null,
        SalesApprovalRequestCommandResult? observed = null)
    {
        if (attempt is not { Outcome: AttemptOutcomeRecorded, RefusalJson: null, RequestId: { } requestId } || attempt.CompanyId == Guid.Empty
            || attempt.ActorId != actor || string.IsNullOrWhiteSpace(actor) || string.IsNullOrWhiteSpace(documentNo)
            || attempt.SubjectId == Guid.Empty || string.IsNullOrWhiteSpace(attempt.CorrelationId) || !LowerOrUpperHash(attempt.RequestHash)
            || !LowerOrUpperHash(attempt.IdempotencyKeyHash) || events is not { Count: 1 } || events[0] is not { } audit) return null;
        var approve = attempt.CommandName == SalesApprovalRequestCommands.ApproveOverDeliveryV1;
        if (!approve && attempt.CommandName != SalesApprovalRequestCommands.RejectOverDeliveryV1) return null;
        if (!ValidOverAudit(audit, attempt, ApprovalRequestEntityType, requestId.ToString("D"), approve
            ? "sales.order.over-delivery-request-approved.v1" : "sales.order.over-delivery-request-rejected.v1", approve ? "ApproveOverDeliveryRequest" : "RejectOverDeliveryRequest")) return null;
        try
        {
            var before = ReadOverAuditState(audit.BeforeJson); var after = ReadOverAuditState(audit.AfterJson);
            if (before is null || after is null || before.Id != requestId || before.SubjectId != attempt.SubjectId || before.Kind != "OverDelivery"
                || before.State != "Requested" || before.DecidedBy is not null || before.DecidedAtUtc is not null || before.DecisionReason is not null
                || before.Version < 1 || before.Version == long.MaxValue || after.Version != before.Version + 1
                || audit.EntityVersionBefore != before.Version || audit.EntityVersionAfter != after.Version
                || after.State != (approve ? "Approved" : "Rejected") || after.DecidedBy != actor || after.DecidedBy == before.RequestedBy
                || after.DecidedAtUtc != attempt.RecordedAtUtc || before.RequestedAtUtc.Offset != TimeSpan.Zero
                || (before with { State = after.State, Version = after.Version, DecidedBy = after.DecidedBy, DecidedAtUtc = after.DecidedAtUtc, DecisionReason = after.DecisionReason }) != after
                || approve && after.DecisionReason is not null || !approve && (string.IsNullOrWhiteSpace(after.DecisionReason) || after.DecisionReason.Length > SalesApprovalRequest.MaximumTextLength)) return null;
            // Native rebuild validates the retained quantities, actor, opaque shipment/instruction and once-only decision state.
            var restored = SalesApprovalRequest.Rebuild(before.Id, attempt.CompanyId, SalesApprovalRequestKind.OverDelivery, before.SubjectId,
                before.RequestNo, before.SubjectRevision, before.LineId, before.Ordered, before.Quantity, before.RequestedTotal,
                before.SourceInstructionId, before.SourceShipmentId, before.Note, before.RequestedBy, before.RequestedAtUtc);
            if (restored.RequestedBy != before.RequestedBy) return null;
            restored.Restore(approve ? SalesApprovalRequestState.Approved : SalesApprovalRequestState.Rejected,
                new(actor, after.DecidedAtUtc!.Value, after.DecisionReason), after.Version, after.DecidedAtUtc.Value);
            var target = new SalesOverDeliveryRequestTargetV1(requestId, before.SubjectId, before.RequestNo, before.SubjectRevision,
                before.LineId!.Value, before.Version.ToString(CultureInfo.InvariantCulture));
            if (decision is not null && (decision.Target != target || decision.ExpectedVersion != nativeRequest?.ExpectedVersion || decision.Reason != nativeRequest?.Reason)) return null;
            if (nativeRequest is not null && (!long.TryParse(nativeRequest.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var expected)
                || expected != before.Version || !approve && nativeRequest.Reason != after.DecisionReason
                || SalesApprovalRequestHash.Compute(attempt.CommandName, target.OrderId, target.RequestNo, nativeRequest) != attempt.RequestHash)) return null;
            long? orderBefore = null, orderAfter = null; AuditEvent? orderAudit = null;
            if (approve)
            {
                if (orderEvents is not { Count: 1 } || orderEvents[0] is not { } oa
                    || !ValidOverAudit(oa, attempt, DraftEntityType, target.OrderId.ToString("D"), "sales.order-draft.over-delivery-approved.v1", "ApproveOverDeliveryRequest")) return null;
                using var oldOrder = JsonDocument.Parse(oa.BeforeJson); using var newOrder = JsonDocument.Parse(oa.AfterJson);
                if (!ExactQuoteAudit(oldOrder.RootElement, ["version"]) || !DecisionAuditVersion(oldOrder.RootElement, out var oldVersion)
                    || !ExactQuoteAudit(newOrder.RootElement, ["version", "overDeliveryApproval"]) || !DecisionAuditVersion(newOrder.RootElement, out var newVersion)
                    || oldVersion == long.MaxValue || newVersion != oldVersion + 1 || oa.EntityVersionBefore != oldVersion || oa.EntityVersionAfter != newVersion
                    || !newOrder.RootElement.TryGetProperty("overDeliveryApproval", out var fact)
                    || !ExactQuoteAudit(fact, ["lineId", "lineNo", "approvalNo", "basisRevision", "ordered", "approvedShipped", "approvedBy", "approvedAtUtc", "requestNo", "shippedBy"])
                    || !JsonGuid(fact, "lineId", target.LineId) || !JsonInt(fact, "requestNo", target.RequestNo)
                    || !fact.TryGetProperty("basisRevision", out var br) || !br.TryGetInt32(out var basisRevision) || basisRevision < 0
                    || !JsonText(fact, "ordered", before.Ordered!) || !JsonText(fact, "approvedShipped", before.RequestedTotal!) || !JsonText(fact, "approvedBy", actor)
                    || !fact.TryGetProperty("lineNo", out var ln) || !ln.TryGetInt32(out var lineNo) || lineNo < 1
                    || !fact.TryGetProperty("approvalNo", out var an) || !an.TryGetInt32(out var approvalNo) || approvalNo < 1
                    || !fact.TryGetProperty("approvedAtUtc", out var at) || !at.TryGetDateTimeOffset(out var time) || time != attempt.RecordedAtUtc
                    || !fact.TryGetProperty("shippedBy", out var shippers) || shippers.ValueKind != JsonValueKind.Array
                    || shippers.GetArrayLength() != 1 || shippers[0].GetString() != before.RequestedBy) return null;
                orderBefore = oldVersion; orderAfter = newVersion; orderAudit = oa;
            }
            else if (orderEvents.Count != 0) return null;
            var original = SalesDraftService.ProjectOverDeliveryRequest(restored);
            var response = new SalesApprovalRequestCommandResult(original, false, attempt.CorrelationId);
            if (observed is not null && (observed.OriginalDecisionReceipt is not null || observed.CorrelationId != attempt.CorrelationId || observed.Request != original)) return null;
            var receipt = new SalesOverDeliveryRequestOriginalReceiptV1(attempt.CompanyId, target, documentNo!, attempt.CommandName, actor,
                attempt.IdempotencyKeyHash, attempt.RequestHash, before.Version, after.Version, orderBefore, orderAfter, attempt.CorrelationId,
                attempt.RecordedAtUtc, original, audit.Id, audit.IntegrityHash, orderAudit?.Id, orderAudit?.IntegrityHash,
                SalesOverDeliveryRequestDecisionDigestsV1.SourceFactsDigest(attempt.CompanyId, requestId, original, documentNo!),
                SalesOverDeliveryRequestDecisionDigestsV1.CurrentResponseDigest(response), "")
            { CurrentResponseDigest = observed is null ? "" : SalesOverDeliveryRequestDecisionDigestsV1.CurrentResponseDigest(observed) };
            return receipt with { OriginalDecisionDigest = SalesOverDeliveryRequestDecisionDigestsV1.OriginalDecisionDigest(receipt) };
        }
        catch (JsonException) { return null; }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
    }
    private static bool ValidOverAudit(AuditEvent audit, ApprovalRequestAttemptRow attempt, string entityType, string entityId, string eventType, string operation) =>
        audit.Id != Guid.Empty && audit.SubjectKind == AuditEvent.CompanySubject && audit.CompanyId == attempt.CompanyId && audit.EntityType == entityType
        && audit.EntityId == entityId && audit.EventType == eventType && audit.Operation == operation && audit.ActorType == AuditActorTypes.User
        && audit.ActorId == attempt.ActorId && audit.PermissionCode == SalesPermissionManifestV1.OrdersOverDeliveryApprove
        && audit.Outcome == AuditOutcomes.Succeeded && audit.CorrelationId == attempt.CorrelationId && audit.IdempotencyKeyHash == attempt.IdempotencyKeyHash
        && audit.SchemaVersion == 1 && audit.IntegrityHash == AuditEventIntegrity.Compute(audit.ToIntegrityEnvelope());
    private static bool ValidOverTarget(SalesOverDeliveryRequestTargetV1? t) => t is { RequestNo: > 0, SubjectRevision: >= 0 }
        && t.RequestId != Guid.Empty && t.OrderId != Guid.Empty && t.LineId != Guid.Empty
        && long.TryParse(t.RequestVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0 && t.RequestVersion == n.ToString(CultureInfo.InvariantCulture);
    private static bool LowerOrUpperHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static OverRequestAuditState? ReadOverAuditState(string json)
    {
        using var doc = JsonDocument.Parse(json); var value = doc.RootElement;
        string[] keys = ["id", "kind", "subjectId", "requestNo", "subjectRevision", "lineId", "ordered", "quantity", "requestedTotal", "sourceInstructionId", "sourceShipmentId", "note", "state", "version", "requestedBy", "requestedAtUtc", "decidedBy", "decidedAtUtc", "decisionReason"];
        if (!ExactQuoteAudit(value, keys)) return null;
        var parsed = value.Deserialize<OverRequestAuditState>(Json);
        return parsed is not null && JsonElement.DeepEquals(value, JsonSerializer.SerializeToElement(parsed, Json)) ? parsed : null;
    }
    private sealed record OverRequestAuditState(Guid Id, string Kind, Guid SubjectId, int RequestNo, int SubjectRevision, Guid? LineId,
        string? Ordered, string? Quantity, string? RequestedTotal, Guid? SourceInstructionId, Guid? SourceShipmentId, string? Note,
        string State, long Version, string RequestedBy, DateTimeOffset RequestedAtUtc, string? DecidedBy, DateTimeOffset? DecidedAtUtc, string? DecisionReason);
}
