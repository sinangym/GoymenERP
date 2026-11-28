using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

public sealed partial class SalesStore
{
    private async Task<SalesDiscountOriginalReceiptV1?> ReadOriginalDiscountReceiptAsync(SalesDraftWriteEnvelope envelope,
        string hash, SalesOrderDraftResponse response, CancellationToken token)
    {
        const string command = SalesDraftCommands.ApproveDiscountExceptionsV1;
        RequireDiscountApprovalCommand(envelope, hash, command, writes: false);
        var company = RequireResolvedCompany(); var key = IdempotencyKeyHash(envelope.IdempotencyKey);
        await DiscountApprovalFenceAsync(token);
        var attempts = await dbContext.DraftAttempts.AsNoTracking().Where(x => x.CompanyId == company
            && x.CommandName == command && x.IdempotencyKeyHash == key).Take(2).ToArrayAsync(token);
        await DiscountApprovalFenceAsync(token);
        if (attempts.Length != 1 || attempts[0].DraftId is not { } id) return null;
        var entity = id.ToString("D");
        var events = await dbContext.Set<AuditEvent>().AsNoTracking().Where(x => x.CompanyId == company
            && x.EntityType == DraftEntityType && x.EntityId == entity
            && x.EventType == "sales.order-draft.discount-exceptions-approved.v1" && x.IdempotencyKeyHash == key)
            .Take(2).ToArrayAsync(token);
        await DiscountApprovalFenceAsync(token);
        return RestoreOriginalDiscountReceipt(attempts[0], events, envelope, hash, response);
    }

    internal static SalesDiscountOriginalReceiptV1? RestoreOriginalDiscountReceipt(DraftAttemptRow attempt,
        IReadOnlyList<AuditEvent> events, SalesDraftWriteEnvelope envelope, string hash, SalesOrderDraftResponse response)
    {
        if (envelope is null || string.IsNullOrWhiteSpace(envelope.ActorId) || string.IsNullOrWhiteSpace(envelope.IdempotencyKey)) return null;
        return RestoreOriginalDiscountReceiptBound(attempt, events, envelope.ActorId.Trim(), envelope.PermissionCode,
            IdempotencyKeyHash(envelope.IdempotencyKey), hash, response);
    }

    // History reads already own the exact stored selector. No raw idempotency key is invented from a digest.
    private static SalesDiscountOriginalReceiptV1? RestoreOriginalDiscountReceiptBound(DraftAttemptRow attempt,
        IReadOnlyList<AuditEvent> events, string actor, string permission, string keyHash, string hash, SalesOrderDraftResponse response)
    {
        if (attempt is null || events is not { Count: 1 } || events[0] is null || response?.Detail is null
            || attempt.DraftId is not { } id || id == Guid.Empty || attempt.CompanyId == Guid.Empty
            || attempt.CommandName != SalesDraftCommands.ApproveDiscountExceptionsV1 || attempt.Outcome != AttemptOutcomeRecorded
            || attempt.RefusalJson is not null || attempt.ResultJson is null || attempt.RequestHash != hash
            || string.IsNullOrWhiteSpace(actor) || attempt.ActorId != actor
            || attempt.IdempotencyKeyHash != keyHash
            || string.IsNullOrWhiteSpace(attempt.CorrelationId) || response.OriginalCorrelationId != attempt.CorrelationId) return null;
        var audit = events[0];
        if (audit.Id == Guid.Empty || audit.SubjectKind != AuditEvent.CompanySubject || audit.CompanyId != attempt.CompanyId
            || audit.EntityType != DraftEntityType || audit.EntityId != id.ToString("D")
            || audit.EventType != "sales.order-draft.discount-exceptions-approved.v1" || audit.Operation != "ApproveDiscountExceptions"
            || audit.ActorType != AuditActorTypes.User || audit.ActorId != attempt.ActorId
            || audit.PermissionCode != permission || audit.PermissionCode != SalesPermissionManifestV1.OrdersDiscountExceptionApprove
            || audit.Outcome != AuditOutcomes.Succeeded || audit.CorrelationId != attempt.CorrelationId
            || audit.IdempotencyKeyHash != attempt.IdempotencyKeyHash || audit.SchemaVersion != 1
            || audit.IntegrityHash != AuditEventIntegrity.Compute(audit.ToIntegrityEnvelope())
            || audit.EntityVersionBefore is not { } expected || expected < 1 || expected == long.MaxValue
            || audit.EntityVersionAfter != expected + 1) return null;
        try
        {
            // Purpose proof is closed-world even though the unchanged legacy replay codec is deliberately older.
            if (JsonSerializer.Deserialize<SalesDraftReplayResult>(attempt.ResultJson, SalesDraftJson.Options) is not
                { Visible: { Draft: not null, Lines: not null }, Withheld: { Draft: not null, Lines: not null } }) return null;
            var stored = ReplayResult(attempt);
            if (stored is null || !FloorJsonEquals(attempt.ResultJson, JsonSerializer.Serialize(stored, Json))
                || response.Detail.IsOwnDecisionRestricted && !response.Detail.IsOwnDraft
                || !MatchesFloorObservedDetail(response.Detail, stored.Visible) && !MatchesFloorObservedDetail(response.Detail, stored.Withheld)
                || stored.Visible.Draft.Version != (expected + 1).ToString(CultureInfo.InvariantCulture)
                || stored.Visible.Draft.Id != id || stored.Visible.Draft.CompanyId != attempt.CompanyId || stored.Visible.Draft.SubcontractService == true
                || stored.Visible.Lines is not { Count: > 0 and <= SalesOrderDraft.MaximumLines }
                || stored.Visible.Lines.Select(x => x.LineId).Distinct().Count() != stored.Visible.Lines.Count
                || stored.Visible.Lines.Select(x => x.LineNo).Distinct().Count() != stored.Visible.Lines.Count) return null;
            var before = FloorAudit(attempt: audit.BeforeJson); var after = FloorAudit(attempt: audit.AfterJson);
            if (before is null || after is null || before.Revision < 0 || before.Revision != after.Revision || before.State != after.State
                || before.State is not ("Preparing" or "Rejected")
                || stored.Visible.Draft.State != "Draft" || stored.Visible.Draft.ApprovalState != after.State
                || stored.Visible.Draft.Revision != SalesDraftApiV1.RevisionText(after.Revision)
                || stored.Visible.Draft.LineCount != stored.Visible.Lines.Count
                || !FloorJsonEquals(JsonSerializer.Serialize(stored.Visible.Approval, Json), after.Approval.GetRawText())
                || !FloorJsonEquals(before.Approval.GetRawText(), after.Approval.GetRawText())
                || before.Lines.Length != stored.Visible.Lines.Count || after.Lines.Length != before.Lines.Length
                || before.Lines.Select(x => x.LineNo).Distinct().Count() != before.Lines.Length
                || after.Lines.Select(x => x.LineNo).Distinct().Count() != after.Lines.Length) return null;
            var originals = new List<SalesDiscountOriginalLineV1>(); var approved = 0;
            foreach (var old in before.Lines)
            {
                var next = after.Lines.SingleOrDefault(x => x.LineNo == old.LineNo);
                var line = stored.Visible.Lines.SingleOrDefault(x => x.LineNo == old.LineNo);
                if (next is null || line is null || line.LineId == Guid.Empty || old.LineNo < 1) return null;
                var prior = old.Commercial(); var current = next.Commercial();
                if (!SalesDiscountDecisionDigestsV1.MatchesCommercialSnapshot(prior)
                    || !SalesDiscountDecisionDigestsV1.MatchesCommercialSnapshot(current)
                    || line.UnitPrice != current.UnitPrice || line.PriceUnitId != current.PriceUnitId
                    || line.DiscountPercent != current.DiscountPercent
                    || old.NetUnitPrice != SalesLinePriceCalculator.NetUnitPriceText(prior)
                    || next.NetUnitPrice != SalesLinePriceCalculator.NetUnitPriceText(current)
                    || !FloorJsonEquals(old.Tax.GetRawText(), next.Tax.GetRawText())) return null;
                if (SalesOrderDraft.NeedsDiscountExceptionApproval(prior))
                {
                    if (prior.DiscountCheck is not { IsApproved: false } check
                        || prior.Reference is not null && check.CheckedBy == attempt.ActorId
                        || current.DiscountCheck is not { IsApproved: true } decided
                        || decided.ApprovedBy != attempt.ActorId || decided.ApprovedAtUtc != attempt.RecordedAtUtc
                        || string.IsNullOrWhiteSpace(decided.Reason) || decided.Reason != decided.Reason.Trim()
                        || decided.Reason.Length > SalesOrderDraft.MaximumRejectionReasonLength
                        || (decided with { ApprovedBy = null, ApprovedAtUtc = null, Reason = null }) != check
                        || (current with { DiscountCheck = check }) != prior) return null;
                    approved++;
                }
                else if (current != prior) return null;
                originals.Add(new(line.LineId, old.LineNo, prior, current));
            }
            if (approved == 0) return null;
            var receipt = new SalesDiscountOriginalReceiptV1(attempt.CompanyId, id, attempt.CommandName, attempt.ActorId,
                attempt.IdempotencyKeyHash, attempt.RequestHash, expected, expected + 1, attempt.CorrelationId,
                attempt.RecordedAtUtc, audit.Id, audit.IntegrityHash, originals.AsReadOnly(), stored,
                SalesDiscountDecisionDigestsV1.OriginalResultDigest(stored), "");
            return receipt with { OriginalDecisionDigest = SalesDiscountDecisionDigestsV1.OriginalDecisionDigest(receipt),
                CurrentResponseDigest = SalesDiscountDecisionDigestsV1.CurrentResponseDigest(response) };
        }
        catch (Exception failure) when (failure is JsonException or InvalidOperationException or ArgumentException or NotSupportedException
            or FormatException or OverflowException)
        { return null; }
    }

}
