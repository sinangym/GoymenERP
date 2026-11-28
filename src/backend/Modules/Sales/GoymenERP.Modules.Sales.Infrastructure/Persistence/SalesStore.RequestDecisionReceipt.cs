using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Requests;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

public sealed partial class SalesStore
{
    private async Task<SalesRequestOriginalDecisionReceiptV1?> ReadOriginalRequestDecisionReceiptAsync(SalesDraftWriteEnvelope envelope,
        string requestHash, string command, SalesRequestResponse currentResponse, CancellationToken token)
    {
        RequireRequestApprovalCommand(envelope, requestHash, command, writes: false);
        var company = RequireResolvedCompany(); var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        var attempts = await RequestApprovalReadAsync(() => dbContext.RequestAttempts.AsNoTracking()
            .Where(x => x.CompanyId == company && x.CommandName == command && x.IdempotencyKeyHash == keyHash)
            .Take(2).ToArrayAsync(token), token);
        if (attempts.Length != 1 || attempts[0].RequestId is not { } id || id == Guid.Empty) return null;
        var entityId = id.ToString("D");
        var eventType = command == SalesRequestCommands.ApproveV1 ? "sales.request.approved.v1" : "sales.request.rejected.v1";
        // No actor/correlation/permission filter that could hide a contradictory duplicate: validate the complete candidates.
        var events = await RequestApprovalReadAsync(() => dbContext.Set<AuditEvent>().AsNoTracking()
            .Where(x => x.CompanyId == company && x.EntityType == RequestEntityType && x.EntityId == entityId
                && x.EventType == eventType && x.IdempotencyKeyHash == keyHash)
            .Take(2).ToArrayAsync(token), token);
        return RestoreOriginalRequestDecisionReceipt(attempts[0], events, envelope, requestHash, command, currentResponse);
    }

    internal static SalesRequestOriginalDecisionReceiptV1? RestoreOriginalRequestDecisionReceipt(RequestAttemptRow attempt,
        IReadOnlyList<AuditEvent> events, SalesDraftWriteEnvelope envelope, string requestHash, string command, SalesRequestResponse currentResponse)
    {
        if (attempt is null || events is null || envelope is null || currentResponse?.Detail?.Request is not { } current
            || events.Count != 1 || events[0] is null || string.IsNullOrWhiteSpace(envelope.ActorId) || string.IsNullOrWhiteSpace(envelope.IdempotencyKey)
            || command is not (SalesRequestCommands.ApproveV1 or SalesRequestCommands.RejectV1)
            || attempt.Outcome != AttemptOutcomeRecorded || attempt.ResultJson is not null || attempt.RefusalJson is not null
            || attempt.RequestId is not { } id || id == Guid.Empty || attempt.CompanyId == Guid.Empty
            || attempt.CommandName != command || attempt.IdempotencyKeyHash != IdempotencyKeyHash(envelope.IdempotencyKey)
            || attempt.RequestHash != requestHash || attempt.ActorId != envelope.ActorId.Trim()
            || string.IsNullOrWhiteSpace(attempt.CorrelationId) || attempt.CorrelationId != currentResponse.OriginalCorrelationId
            || currentResponse.Detail.Request.Id != id || currentResponse.Detail.Request.CompanyId != attempt.CompanyId) return null;
        var audit = events[0]; var approve = command == SalesRequestCommands.ApproveV1;
        if (audit.Id == Guid.Empty || audit.SubjectKind != AuditEvent.CompanySubject || audit.CompanyId != attempt.CompanyId || audit.EntityType != RequestEntityType
            || audit.EntityId != id.ToString("D") || audit.EventType != (approve ? "sales.request.approved.v1" : "sales.request.rejected.v1")
            || audit.Operation != (approve ? "Approve" : "Reject") || audit.ActorType != AuditActorTypes.User
            || audit.ActorId != attempt.ActorId || audit.PermissionCode != envelope.PermissionCode
            || audit.PermissionCode != SalesPermissionManifestV1.RequestsApprove || audit.Outcome != AuditOutcomes.Succeeded
            || audit.CorrelationId != attempt.CorrelationId || audit.IdempotencyKeyHash != attempt.IdempotencyKeyHash
            || audit.SchemaVersion != 1 || audit.IntegrityHash != AuditEventIntegrity.Compute(audit.ToIntegrityEnvelope())
            || !TryRequestDecisionAudit(audit.BeforeJson, out var before) || !TryRequestDecisionAudit(audit.AfterJson, out var after)) return null;
        if (before!.Id != id || after!.Id != id || before.Version == long.MaxValue || after.Version != before.Version + 1
            || audit.EntityVersionBefore != before.Version || audit.EntityVersionAfter != after.Version
            || before.DocumentNo != after.DocumentNo || before.PartyId != after.PartyId
            || before.CompanyAccountId != after.CompanyAccountId || before.BusinessDate != after.BusinessDate
            || approve && (before.State != "Submitted" || after.State != "Approved")
            || !approve && (before.State is not ("Submitted" or "Approved") || after.State != "Rejected")
            || after.Decision is not { } decision || decision.DecidedBy != attempt.ActorId || decision.DecidedAtUtc != attempt.RecordedAtUtc
            || approve && decision.RejectionReason is not null
            || !approve && (string.IsNullOrWhiteSpace(decision.RejectionReason) || decision.RejectionReason.Length > SalesRequest.MaximumReasonLength)) return null;
        var nativePayload = new SalesDraftStateCommandRequest(before.Version.ToString(CultureInfo.InvariantCulture), decision.RejectionReason);
        if (SalesRequestHash.Compute(command, id, nativePayload) != requestHash) return null;
        // These immutable identities may confirm the current response; current state/version/lines never supply the original decision.
        if (current.DocumentNo != after.DocumentNo || current.PartyId != after.PartyId
            || current.CompanyAccountId != after.CompanyAccountId || current.BusinessDate != after.BusinessDate) return null;
        var proof = new SalesRequestOriginalDecisionReceiptV1(attempt.CompanyId, id, after.DocumentNo, after.PartyId,
            after.CompanyAccountId, after.BusinessDate, command, attempt.ActorId, attempt.IdempotencyKeyHash, requestHash,
            nativePayload.ExpectedVersion, after.Version.ToString(CultureInfo.InvariantCulture), after.State, decision,
            attempt.CorrelationId, audit.Id, audit.IntegrityHash, "", SalesRequestDecisionDigestsV1.CurrentResponseDigest(currentResponse));
        return proof with { OriginalDecisionDigest = SalesRequestDecisionDigestsV1.OriginalDecisionDigest(proof) };
    }

    private static bool TryRequestDecisionAudit(string json, out RequestDecisionAuditState? state)
    {
        state = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!ExactRequestAudit(root, ["id", "documentNo", "state", "version", "decision", "partyId", "companyAccountId", "businessDate", "lines"])
                || !root.TryGetProperty("lines", out var lines) || lines.ValueKind != JsonValueKind.Null
                || !root.TryGetProperty("decision", out var decision)
                || decision.ValueKind != JsonValueKind.Null && !ExactRequestAudit(decision, ["decidedBy", "decidedAtUtc", "rejectionReason"])) return false;
            state = JsonSerializer.Deserialize<RequestDecisionAuditState>(json, Json);
            return state is { Id: var id, PartyId: var party, CompanyAccountId: var account, Version: > 0 }
                && id != Guid.Empty && party != Guid.Empty && account != Guid.Empty
                && !string.IsNullOrWhiteSpace(state.DocumentNo) && state.DocumentNo.Length <= SalesRequest.MaximumDocumentNoLength
                && (state.State switch
                {
                    "Submitted" => state.Decision is null,
                    "Approved" => state.Version >= 2 && ValidDecision(state.Decision) && state.Decision!.RejectionReason is null,
                    "Rejected" => state.Version >= 2 && ValidDecision(state.Decision) && !string.IsNullOrWhiteSpace(state.Decision!.RejectionReason)
                        && state.Decision.RejectionReason.Length <= SalesRequest.MaximumReasonLength,
                    _ => false,
                });
        }
        catch (JsonException) { return false; }
        catch (NotSupportedException) { return false; }

        static bool ValidDecision(SalesRequestDecisionV1? value) => value is not null
            && !string.IsNullOrWhiteSpace(value.DecidedBy) && value.DecidedBy.Length <= SalesRequest.MaximumActorLength;
    }
    private static bool ExactRequestAudit(JsonElement value, string[] keys) => value.ValueKind == JsonValueKind.Object
        && value.EnumerateObject().Count() == keys.Length && keys.All(key => value.TryGetProperty(key, out _));
    private sealed record RequestDecisionAuditState(Guid Id, string DocumentNo, string State, long Version,
        SalesRequestDecisionV1? Decision, Guid PartyId, Guid CompanyAccountId, DateOnly BusinessDate, JsonElement Lines);
}
