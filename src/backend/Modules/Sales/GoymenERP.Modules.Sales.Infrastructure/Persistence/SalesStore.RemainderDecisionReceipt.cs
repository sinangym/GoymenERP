using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

public sealed partial class SalesStore
{
    internal static SalesRemainderOriginalReceiptV1? RestoreOriginalRemainderReceipt(DraftAttemptRow attempt, AuditEvent audit,
        DraftRemainderDecisionRow decision, SalesOrderDraft order, long expectedVersion, int changeNo, string actorId,
        SalesDraftRemainderDecisionRequest request, SalesOrderDraftResponse observed) =>
        BindRemainderReceipt(new(attempt, audit, decision), order, expectedVersion, changeNo, actorId, request, observed);
    public async Task<SalesOrderDraftResponse> AttachRemainderOriginalReceiptAsync(SalesOrderDraftResponse response, Guid draftId, int changeNo,
        SalesDraftRemainderDecisionRequest request, CancellationToken cancellationToken = default)
    {
        if (ownedRemainderDecision is not { } state) throw new InvalidOperationException("Satır kalanı makbuzu yalnız sahip karar işleminden okunabilir.");
        var expected = long.TryParse(request.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
        if (expected < 1 || expected == long.MaxValue || response.Detail.Draft.Id != draftId || response.OriginalCorrelationId.Length == 0) return response;
        var key = IdempotencyKeyHash(state.Envelope.IdempotencyKey);
        var rows = await RemainderReadAsync(() => (from attempt in dbContext.DraftAttempts.AsNoTracking()
            join audit in dbContext.Set<AuditEvent>().AsNoTracking() on new { CompanyId = (Guid?)attempt.CompanyId, attempt.IdempotencyKeyHash, attempt.CorrelationId } equals new { audit.CompanyId, audit.IdempotencyKeyHash, audit.CorrelationId }
            join decision in dbContext.DraftRemainderDecisions.AsNoTracking() on new { attempt.CompanyId, Id = attempt.DraftId } equals new { decision.CompanyId, Id = (Guid?)decision.DraftId }
            where attempt.CompanyId == state.CompanyId && attempt.DraftId == draftId && attempt.CommandName == state.Command && attempt.IdempotencyKeyHash == key
                && attempt.ActorId == state.Envelope.ActorId && audit.ActorId == state.Envelope.ActorId && audit.PermissionCode == SalesPermissionManifestV1.OrdersApprove
                && audit.EntityType == DraftEntityType && audit.EntityId == draftId.ToString()
                && audit.EntityVersionBefore == expected && decision.DraftVersion == expected + 1 && decision.ChangeNo == changeNo
                && (attempt.CommandName == SalesDraftCommands.ApproveRemainderChangeV1 && audit.EventType == "sales.order-draft.remainder-change-approved.v1"
                    || attempt.CommandName == SalesDraftCommands.RejectRemainderChangeV1 && audit.EventType == "sales.order-draft.remainder-change-rejected.v1")
            select new RemainderArtifact(attempt, audit, decision)).Take(2).ToArrayAsync(cancellationToken), cancellationToken);
        if (rows.Length != 1 || rows[0].Attempt.RequestHash != state.Hash) return response;
        var order = await RemainderReadAsync(() => FindDraftAsync(draftId, cancellationToken), cancellationToken);
        if (order is null) return response;
        var receipt = BindRemainderReceipt(rows[0], order, expected, changeNo, state.Envelope.ActorId, request, response);
        return receipt is null ? response : response with { OriginalRemainderDecisionReceipt = receipt };
    }

    public async Task<SalesRemainderOriginalVersionReadResultV1> ReadRemainderOriginalVersionAsync(Guid draftId, long expectedVersion,
        string actorId, Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshFence); cancellationToken.ThrowIfCancellationRequested();
        if (expectedVersion < 1 || expectedVersion == long.MaxValue) throw new ArgumentOutOfRangeException(nameof(expectedVersion));
        EnterRemainderRead(freshFence);
        try
        {
            var company = RequireResolvedCompany();
            var candidates = await RemainderReadAsync(() => (from attempt in dbContext.DraftAttempts.AsNoTracking()
                join audit in dbContext.Set<AuditEvent>().AsNoTracking() on new { CompanyId = (Guid?)attempt.CompanyId, attempt.IdempotencyKeyHash, attempt.CorrelationId } equals new { audit.CompanyId, audit.IdempotencyKeyHash, audit.CorrelationId }
                join decision in dbContext.DraftRemainderDecisions.AsNoTracking() on new { attempt.CompanyId, Id = attempt.DraftId } equals new { decision.CompanyId, Id = (Guid?)decision.DraftId }
                where attempt.CompanyId == company && attempt.DraftId == draftId && attempt.ActorId == actorId && audit.ActorId == actorId
                    && audit.PermissionCode == SalesPermissionManifestV1.OrdersApprove
                    && (attempt.CommandName == SalesDraftCommands.ApproveRemainderChangeV1 || attempt.CommandName == SalesDraftCommands.RejectRemainderChangeV1)
                    && audit.EntityType == DraftEntityType && audit.EntityId == draftId.ToString() && audit.EntityVersionBefore == expectedVersion && decision.DraftVersion == expectedVersion + 1
                    && (attempt.CommandName == SalesDraftCommands.ApproveRemainderChangeV1 && audit.EventType == "sales.order-draft.remainder-change-approved.v1"
                        || attempt.CommandName == SalesDraftCommands.RejectRemainderChangeV1 && audit.EventType == "sales.order-draft.remainder-change-rejected.v1")
                select new RemainderArtifact(attempt, audit, decision)).Take(2).ToArrayAsync(cancellationToken), cancellationToken);
            var order = await RemainderReadAsync(() => FindDraftAsync(draftId, cancellationToken), cancellationToken);
            if (order is null) return new(SalesRemainderDecisionReadStateV1.NotFound, null);
            if (candidates.Length == 0) return new(SalesRemainderDecisionReadStateV1.NotFound, null) { CurrentVersion = order.Version };
            if (candidates.Length != 1) return new(SalesRemainderDecisionReadStateV1.Unavailable, null);
            var receipt = BindRemainderReceipt(candidates[0], order, expectedVersion, null, actorId, null, null);
            return receipt is null ? new(SalesRemainderDecisionReadStateV1.Unavailable, null) : new(SalesRemainderDecisionReadStateV1.Found, receipt) { CurrentVersion = order.Version };
        }
        finally { remainderDecisionReadFence = null; }
    }

    private static SalesRemainderOriginalReceiptV1? BindRemainderReceipt(RemainderArtifact artifact, SalesOrderDraft order, long expectedVersion,
        int? selectedChangeNo, string actorId, SalesDraftRemainderDecisionRequest? nativeRequest, SalesOrderDraftResponse? observed)
    {
        var a = artifact.Attempt; var audit = artifact.Audit; var approve = a.CommandName == SalesDraftCommands.ApproveRemainderChangeV1;
        if (!approve && a.CommandName != SalesDraftCommands.RejectRemainderChangeV1 || a.CompanyId != order.CompanyId || a.DraftId != order.Id
            || a.Outcome != AttemptOutcomeRecorded || a.RefusalJson is not null || a.ResultJson is not null || a.ActorId != actorId || string.IsNullOrWhiteSpace(actorId)
            || !LowerOrUpperHash(a.IdempotencyKeyHash) || !LowerOrUpperHash(a.RequestHash) || string.IsNullOrWhiteSpace(a.CorrelationId)
            || expectedVersion < 1 || expectedVersion == long.MaxValue || audit.Id == Guid.Empty || audit.SubjectKind != AuditEvent.CompanySubject
            || audit.CompanyId != order.CompanyId || audit.EntityType != DraftEntityType || audit.EntityId != order.Id.ToString("D")
            || audit.ActorType != AuditActorTypes.User || audit.ActorId != actorId || audit.PermissionCode != SalesPermissionManifestV1.OrdersApprove
            || audit.EventType != (approve ? "sales.order-draft.remainder-change-approved.v1" : "sales.order-draft.remainder-change-rejected.v1")
            || audit.Operation != (approve ? "ApproveRemainderChange" : "RejectRemainderChange") || audit.Outcome != AuditOutcomes.Succeeded
            || audit.CorrelationId != a.CorrelationId || audit.IdempotencyKeyHash != a.IdempotencyKeyHash || audit.EntityVersionBefore != expectedVersion
            || audit.EntityVersionAfter != expectedVersion + 1 || audit.SchemaVersion != 1 || audit.IntegrityHash != AuditEventIntegrity.Compute(audit.ToIntegrityEnvelope())) return null;
        try
        {
            using var beforeDoc = JsonDocument.Parse(audit.BeforeJson); using var afterDoc = JsonDocument.Parse(audit.AfterJson);
            var before = beforeDoc.RootElement; var after = afterDoc.RootElement;
            if (!ExactQuoteAudit(before, ["version", "lines"]) || !ExactQuoteAudit(after, ["remainder", "change"])
                || !after.TryGetProperty("remainder", out var remainder) || !ExactQuoteAudit(remainder, ["version", "lines"])
                || !before.TryGetProperty("version", out var beforeVersion) || !beforeVersion.TryGetInt64(out var n) || n != expectedVersion
                || !remainder.TryGetProperty("version", out var afterVersion) || !afterVersion.TryGetInt64(out var resultVersion) || resultVersion != n + 1
                || !after.TryGetProperty("change", out var changeJson) || !changeJson.TryGetProperty("changeNo", out var cn) || !cn.TryGetInt32(out var changeNo)
                || selectedChangeNo is not null && selectedChangeNo != changeNo) return null;
            var facts = order.RemainderChanges.SingleOrDefault(x => x.Request.ChangeNo == changeNo);
            var recordedDecision = artifact.Decision;
            if (facts?.Decision is not { } decision || decision.Outcome != (approve ? RemainderChangeState.Approved : RemainderChangeState.Rejected)
                || decision.DecidedBy != actorId || decision.DecidedAtUtc != a.RecordedAtUtc
                || recordedDecision.CompanyId != a.CompanyId || recordedDecision.DraftId != order.Id || recordedDecision.ChangeNo != changeNo
                || recordedDecision.DraftVersion != resultVersion || recordedDecision.CorrelationId != a.CorrelationId || recordedDecision.DecidedBy != actorId
                || recordedDecision.DecidedAtUtc != a.RecordedAtUtc
                || !JsonElement.DeepEquals(changeJson, JsonSerializer.SerializeToElement(RemainderAudit(facts), Json))) return null;
            if (!before.TryGetProperty("lines", out var oldLines) || !remainder.TryGetProperty("lines", out var newLines)
                || oldLines.ValueKind != JsonValueKind.Array || newLines.ValueKind != JsonValueKind.Array || oldLines.GetArrayLength() != newLines.GetArrayLength()
                || oldLines.GetArrayLength() == 0) return null;
            var ids = new HashSet<Guid>(); var numbers = new HashSet<int>(); var found = false;
            for (var i = 0; i < oldLines.GetArrayLength(); i++)
            {
                var oldLine = oldLines[i]; var newLine = newLines[i];
                if (!ExactQuoteAudit(oldLine, ["lineNo", "id", "quantity", "remainder"]) || !ExactQuoteAudit(newLine, ["lineNo", "id", "quantity", "remainder"])
                    || !oldLine.GetProperty("id").TryGetGuid(out var id) || id == Guid.Empty || !ids.Add(id)
                    || !oldLine.GetProperty("lineNo").TryGetInt32(out var lineNo) || lineNo < 1 || !numbers.Add(lineNo)
                    || !JsonGuid(newLine, "id", id) || !JsonInt(newLine, "lineNo", lineNo)
                    || !RemainderAuditLineShape(oldLine) || !RemainderAuditLineShape(newLine)
                    || id == facts.Request.LineId && lineNo != facts.Request.LineNo) return null;
                if (id != facts.Request.LineId || !approve) { if (!JsonElement.DeepEquals(oldLine, newLine)) return null; }
                else if (lineNo != facts.Request.LineNo || !JsonText(newLine, "quantity", decision.ResultQuantityText!)
                    || !JsonText(newLine, "remainder", facts.Request.Kind == RemainderChangeKind.Cancel ? "Cancelled" : "Revised")) return null;
                found |= id == facts.Request.LineId;
            }
            if (!found) return null;
            var original = SalesDraftService.ProjectRemainderChange(order, facts);
            if (nativeRequest is not null && (!long.TryParse(nativeRequest.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var rawN) || rawN != n
                || SalesDraftRequestHash.Compute(a.CommandName, order.Id, changeNo, nativeRequest) != a.RequestHash)) return null;
            if (observed is not null && (observed.Detail.Draft.Id != order.Id || observed.OriginalCorrelationId != a.CorrelationId
                || observed.OriginalFloorApprovalReceipt is not null || observed.OriginalDiscountApprovalReceipt is not null || observed.OriginalRemainderDecisionReceipt is not null)) return null;
            var receipt = new SalesRemainderOriginalReceiptV1(order.CompanyId, order.Id, order.DocumentNo, changeNo, facts.Request.LineId, a.CommandName, actorId,
                a.IdempotencyKeyHash, a.RequestHash, n, resultVersion, a.CorrelationId, a.RecordedAtUtc, original, audit.Id, audit.IntegrityHash,
                SalesRemainderDecisionDigestsV1.SourceFactsDigest(order.CompanyId, order.Id, order.DocumentNo, original), "")
            { CurrentResponseDigest = observed is null ? "" : SalesRemainderDecisionDigestsV1.CurrentResponseDigest(observed), DraftCreatedBy = order.CreatedBy, CreatorProofEstablished = true };
            return receipt with { OriginalDecisionDigest = SalesRemainderDecisionDigestsV1.OriginalDecisionDigest(receipt) };
        }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (ArgumentException) { return null; }
        catch (FormatException) { return null; }
    }
    private sealed record RemainderArtifact(DraftAttemptRow Attempt, AuditEvent Audit, DraftRemainderDecisionRow Decision);
    private static bool RemainderAuditLineShape(JsonElement line) => line.GetProperty("quantity").ValueKind == JsonValueKind.String
        && decimal.TryParse(line.GetProperty("quantity").GetString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var quantity) && quantity > 0
        && line.GetProperty("remainder").ValueKind == JsonValueKind.String && line.GetProperty("remainder").GetString() is "Open" or "Revised" or "Cancelled";
}
