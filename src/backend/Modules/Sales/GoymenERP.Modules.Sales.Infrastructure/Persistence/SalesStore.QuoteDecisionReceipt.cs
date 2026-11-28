using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.ApprovalRequests;
using GoymenERP.Modules.Sales.Domain.Quotes;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

public sealed partial class SalesStore
{
    private async Task RequireSelectedQuoteTargetAsync(Guid quoteId, int? requestNo, int? revision, CancellationToken token)
    {
        if (ownedQuoteApproval?.Decision is not { } decision) return;
        var target = decision.Target;
        if (!ValidQuoteTarget(target) || target.QuoteId != quoteId || requestNo is { } no && target.RequestNo != no
            || revision is { } rev && target.SubjectRevision != rev) throw SelectedQuoteUnavailable();
        var company = RequireResolvedCompany();
        var row = await QuoteApprovalReadAsync(() => dbContext.ApprovalRequests.AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == company && x.Id == target.RequestId && x.Kind == (int)SalesApprovalRequestKind.QuoteExpiredConversion, token), token);
        if (row is null || row.SubjectId != quoteId || row.RequestNo != target.RequestNo || row.SubjectRevision != target.SubjectRevision
            || row.Version.ToString(CultureInfo.InvariantCulture) != target.RequestVersion
            || row.State != (int)SalesApprovalRequestState.Requested) throw SelectedQuoteUnavailable();
        // Reject's ExpectedVersion belongs to the request, approval's belongs to the quote.
        if (ownedQuoteApproval.Command == SalesApprovalRequestCommands.RejectQuoteConversionV1
            && (!long.TryParse(decision.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var expected) || expected != row.Version))
            throw SelectedQuoteUnavailable();
    }

    private string? SelectedQuoteApprovalContext() => ownedQuoteApproval?.Decision is { } decision
        ? JsonSerializer.Serialize(decision.Target, Json) : null;

    private async Task<T> AttachQuoteDecisionReceiptAsync<T>(T result, OwnedQuoteApproval state, CancellationToken token) where T : class
    {
        var decision = state.Decision!; var target = decision.Target;
        if (!ValidQuoteTarget(target)) throw SelectedQuoteUnavailable();
        var approve = state.Command == SalesQuoteCommands.ApproveExpiredConversionV1;
        var recomputed = approve
            ? SalesQuoteHash.Compute(target.QuoteId, new SalesQuoteExpiryApprovalRequest(decision.ExpectedVersion, decision.NoteOrReason))
            : SalesApprovalRequestHash.Compute(state.Command, target.QuoteId, target.RequestNo, new(decision.ExpectedVersion, decision.NoteOrReason));
        if (recomputed != state.Hash) throw SelectedQuoteUnavailable();
        var key = IdempotencyKeyHash(state.Envelope.IdempotencyKey);
        var company = state.CompanyId;
        QuoteAttemptRow? quoteAttempt = null; ApprovalRequestAttemptRow? requestAttempt = null;
        if (approve)
        {
            var attempts = await QuoteApprovalReadAsync(() => dbContext.QuoteAttempts.AsNoTracking().Where(x => x.CompanyId == company
                && x.CommandName == state.Command && x.IdempotencyKeyHash == key).Take(2).ToArrayAsync(token), token);
            if (attempts.Length == 1) quoteAttempt = attempts[0];
        }
        else
        {
            var attempts = await QuoteApprovalReadAsync(() => dbContext.ApprovalRequestAttempts.AsNoTracking().Where(x => x.CompanyId == company
                && x.CommandName == state.Command && x.IdempotencyKeyHash == key).Take(2).ToArrayAsync(token), token);
            if (attempts.Length == 1) requestAttempt = attempts[0];
        }
        var entityId = (approve ? target.QuoteId : target.RequestId).ToString("D");
        var entityType = approve ? QuoteEntityType : ApprovalRequestEntityType;
        var eventType = approve ? "sales.quote.expired-conversion-approved.v1" : "sales.quote.expired-conversion-request-rejected.v1";
        // Validate all candidates, rather than concealing contradictory actor/correlation duplicates with query filters.
        var events = await QuoteApprovalReadAsync(() => dbContext.Set<AuditEvent>().AsNoTracking().Where(x => x.CompanyId == company
            && x.EntityType == entityType && x.EntityId == entityId && x.EventType == eventType && x.IdempotencyKeyHash == key)
            .Take(2).ToArrayAsync(token), token);
        var receipt = RestoreQuoteDecisionReceipt(decision, state.Envelope, state.Hash, quoteAttempt, requestAttempt, events, result);
        if (result is SalesQuoteResponse quote) return (T)(object)(quote with { OriginalDecisionReceipt = receipt });
        if (result is SalesApprovalRequestCommandResult request) return (T)(object)(request with { OriginalDecisionReceipt = receipt });
        throw SelectedQuoteUnavailable();
    }

    internal static SalesQuoteOriginalDecisionReceiptV1? RestoreQuoteDecisionReceipt<T>(SalesQuoteDecisionCommandV1 decision,
        SalesDraftWriteEnvelope envelope, string hash, QuoteAttemptRow? quoteAttempt, ApprovalRequestAttemptRow? requestAttempt,
        IReadOnlyList<AuditEvent> events, T result) where T : class
    {
        if (decision is null || !ValidQuoteTarget(decision.Target) || envelope is null || events is null || events.Count != 1
            || events[0] is not { } audit || string.IsNullOrWhiteSpace(envelope.ActorId) || string.IsNullOrWhiteSpace(envelope.IdempotencyKey)
            || envelope.PermissionCode != SalesPermissionManifestV1.QuotesExpiredConversionApprove) return null;
        var target = decision.Target; var approve = result is SalesQuoteResponse;
        var command = approve ? SalesQuoteCommands.ApproveExpiredConversionV1 : SalesApprovalRequestCommands.RejectQuoteConversionV1;
        var company = approve ? quoteAttempt?.CompanyId : requestAttempt?.CompanyId;
        var actor = approve ? quoteAttempt?.ActorId : requestAttempt?.ActorId;
        var correlation = approve ? quoteAttempt?.CorrelationId : requestAttempt?.CorrelationId;
        var recordedAt = approve ? quoteAttempt?.RecordedAtUtc : requestAttempt?.RecordedAtUtc;
        var key = IdempotencyKeyHash(envelope.IdempotencyKey);
        if (company is null || company == Guid.Empty || actor != envelope.ActorId.Trim() || string.IsNullOrWhiteSpace(correlation)) return null;
        if (approve)
        {
            if (quoteAttempt is not { Outcome: AttemptOutcomeRecorded, RefusalJson: null, ResultJson: null }
                || quoteAttempt.CommandName != command || quoteAttempt.QuoteId != target.QuoteId || quoteAttempt.RequestHash != hash
                || quoteAttempt.IdempotencyKeyHash != key || result is not SalesQuoteResponse response
                || response.Detail?.Quote is not { } current || current.Id != target.QuoteId || current.CompanyId != company
                || response.OriginalCorrelationId != correlation
                || SalesQuoteHash.Compute(target.QuoteId, new SalesQuoteExpiryApprovalRequest(decision.ExpectedVersion, decision.NoteOrReason)) != hash) return null;
        }
        else
        {
            if (requestAttempt is not { Outcome: AttemptOutcomeRecorded, RefusalJson: null }
                || requestAttempt.CommandName != command || requestAttempt.SubjectId != target.QuoteId || requestAttempt.RequestId != target.RequestId
                || requestAttempt.RequestHash != hash || requestAttempt.IdempotencyKeyHash != key
                || result is not SalesApprovalRequestCommandResult response || response.Request is not { } current
                || current.Kind != SalesApprovalRequestApiV1.KindQuoteExpiredConversion || current.SubjectId != target.QuoteId || current.RequestNo != target.RequestNo
                || current.SubjectRevision != SalesDraftApiV1.RevisionText(target.SubjectRevision) || response.CorrelationId != correlation
                || SalesApprovalRequestHash.Compute(command, target.QuoteId, target.RequestNo, new(decision.ExpectedVersion, decision.NoteOrReason)) != hash) return null;
        }
        if (audit.Id == Guid.Empty || audit.SubjectKind != AuditEvent.CompanySubject || audit.CompanyId != company
            || audit.EntityType != (approve ? QuoteEntityType : ApprovalRequestEntityType)
            || audit.EntityId != (approve ? target.QuoteId : target.RequestId).ToString("D")
            || audit.EventType != (approve ? "sales.quote.expired-conversion-approved.v1" : "sales.quote.expired-conversion-request-rejected.v1")
            || audit.Operation != (approve ? "ApproveExpiredConversion" : "RejectQuoteConversionRequest")
            || audit.ActorType != AuditActorTypes.User || audit.ActorId != actor || audit.PermissionCode != envelope.PermissionCode
            || audit.Outcome != AuditOutcomes.Succeeded || audit.CorrelationId != correlation || audit.IdempotencyKeyHash != key
            || audit.SchemaVersion != 1 || audit.IntegrityHash != AuditEventIntegrity.Compute(audit.ToIntegrityEnvelope())) return null;
        try
        {
            using var beforeDoc = JsonDocument.Parse(audit.BeforeJson); using var afterDoc = JsonDocument.Parse(audit.AfterJson);
            var before = beforeDoc.RootElement; var after = afterDoc.RootElement;
            if (!DecisionAuditVersion(before, out var beforeVersion) || !DecisionAuditVersion(after, out var afterVersion)
                || beforeVersion == long.MaxValue || afterVersion != beforeVersion + 1
                || !long.TryParse(decision.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var expected) || expected != beforeVersion
                || audit.EntityVersionBefore != beforeVersion || audit.EntityVersionAfter != afterVersion) return null;
            string originalState; string? note; DateTimeOffset time;
            if (approve)
            {
                string[] keys = ["id","documentNo","state","revision","version","partyId","companyAccountId","currencyId","businessDate","requests","terms","loss","conversion","expiryApprovals","lines"];
                if (!ExactQuoteAudit(before, keys) || !ExactQuoteAudit(after, keys) || !JsonGuid(before,"id",target.QuoteId) || !JsonGuid(after,"id",target.QuoteId)
                    || !JsonInt(before,"revision",target.SubjectRevision) || !JsonInt(after,"revision",target.SubjectRevision)
                    || !JsonText(before,"state","Open") || !JsonText(after,"state","Open")
                    || !before.TryGetProperty("lines",out var bl) || bl.ValueKind != JsonValueKind.Null
                    || !after.TryGetProperty("lines",out var al) || al.ValueKind != JsonValueKind.Null
                    || !SameImmutableQuoteFacts(before,after) || !TrySelectedTrace(audit.ApprovalContextJson,target)
                    || !before.TryGetProperty("expiryApprovals",out var oldApprovals) || !after.TryGetProperty("expiryApprovals",out var newApprovals)) return null;
                var previous = oldApprovals.Deserialize<SalesQuoteExpiryApproval[]>(Json);
                var approvals = newApprovals.Deserialize<SalesQuoteExpiryApproval[]>(Json);
                if (previous is null || approvals is null || previous.Any(x=>x is null) || approvals.Any(x=>x is null)
                    || approvals.Length != previous.Length+1 || !previous.SequenceEqual(approvals.Take(previous.Length))) return null;
                var added = approvals[^1];
                if (added.Revision != target.SubjectRevision || added.ApprovedBy != actor || added.ApprovedAtUtc != recordedAt
                    || added.Note != NormalizeQuoteDecisionText(decision.NoteOrReason)) return null;
                originalState = "Approved"; note = added.Note; time = added.ApprovedAtUtc;
            }
            else
            {
                string[] keys = ["id","kind","subjectId","requestNo","subjectRevision","lineId","ordered","quantity","requestedTotal","sourceInstructionId","sourceShipmentId","note","state","version","requestedBy","requestedAtUtc","decidedBy","decidedAtUtc","decisionReason"];
                if (!ExactQuoteAudit(before,keys) || !ExactQuoteAudit(after,keys) || !JsonGuid(before,"id",target.RequestId) || !JsonGuid(after,"id",target.RequestId)
                    || !JsonGuid(before,"subjectId",target.QuoteId) || !JsonGuid(after,"subjectId",target.QuoteId)
                    || !JsonInt(before,"requestNo",target.RequestNo) || !JsonInt(after,"requestNo",target.RequestNo)
                    || !JsonInt(before,"subjectRevision",target.SubjectRevision) || !JsonInt(after,"subjectRevision",target.SubjectRevision)
                    || beforeVersion.ToString(CultureInfo.InvariantCulture) != target.RequestVersion
                    || !JsonText(before,"kind","QuoteExpiredConversion") || !JsonText(after,"kind","QuoteExpiredConversion")
                    || !JsonText(before,"state","Requested") || !JsonText(after,"state","Rejected") || !JsonText(after,"decidedBy",actor!)
                    || !after.TryGetProperty("decidedAtUtc",out var decided) || !decided.TryGetDateTimeOffset(out time) || time != recordedAt
                    || !after.TryGetProperty("decisionReason",out var reason) || reason.ValueKind != JsonValueKind.String) return null;
                foreach (var name in keys.Except(["version","state","decidedBy","decidedAtUtc","decisionReason"]))
                    if (!before.TryGetProperty(name,out var b) || !after.TryGetProperty(name,out var a) || b.GetRawText()!=a.GetRawText()) return null;
                note = reason.GetString(); if (string.IsNullOrWhiteSpace(note) || note.Length>SalesApprovalRequest.MaximumTextLength || note != NormalizeQuoteDecisionText(decision.NoteOrReason)) return null;
                originalState = "Rejected";
            }
            var currentDigest = result is SalesQuoteResponse quote ? SalesQuoteDecisionDigestsV1.CurrentResponseDigest(quote)
                : SalesQuoteDecisionDigestsV1.CurrentResponseDigest((SalesApprovalRequestCommandResult)(object)result);
            var receipt = new SalesQuoteOriginalDecisionReceiptV1(company.Value,target,command,actor!,key,hash,decision.ExpectedVersion,
                afterVersion.ToString(CultureInfo.InvariantCulture),originalState,time,note,correlation!,audit.Id,audit.IntegrityHash,"",currentDigest);
            return receipt with { OriginalDecisionDigest = SalesQuoteDecisionDigestsV1.OriginalDecisionDigest(receipt) };
        }
        catch (JsonException) { return null; }
        catch (NotSupportedException) { return null; }
    }

    private static bool TrySelectedTrace(string? json, SalesQuoteConversionTargetV1 target)
    {
        if (json is null) return false;
        using var doc = JsonDocument.Parse(json);
        return ExactQuoteAudit(doc.RootElement,["requestId","quoteId","requestNo","subjectRevision","requestVersion"])
            && doc.RootElement.Deserialize<SalesQuoteConversionTargetV1>(Json) == target;
    }
    private static bool SameImmutableQuoteFacts(JsonElement before, JsonElement after)
    {
        foreach (var name in new[]{"documentNo","partyId","companyAccountId","currencyId","businessDate","requests","terms","loss","conversion"})
            if (!before.TryGetProperty(name,out var b) || !after.TryGetProperty(name,out var a) || b.GetRawText()!=a.GetRawText()) return false;
        return true;
    }
    private static bool ValidQuoteTarget(SalesQuoteConversionTargetV1? target) => target is { RequestId: var id, QuoteId: var quote, RequestNo: >0, SubjectRevision: >=0 }
        && id != Guid.Empty && quote != Guid.Empty && long.TryParse(target.RequestVersion,NumberStyles.None,CultureInfo.InvariantCulture,out var version)
        && version>0 && target.RequestVersion==version.ToString(CultureInfo.InvariantCulture);
    private static bool ExactQuoteAudit(JsonElement value,string[] keys) => value.ValueKind==JsonValueKind.Object
        && value.EnumerateObject().Count()==keys.Length && keys.All(key=>value.TryGetProperty(key,out _));
    private static bool DecisionAuditVersion(JsonElement root,out long version)
    { version=0; return root.ValueKind==JsonValueKind.Object && root.TryGetProperty("version",out var value) && value.ValueKind==JsonValueKind.Number && value.TryGetInt64(out version) && version>0; }
    private static bool JsonGuid(JsonElement root,string key,Guid expected) => root.TryGetProperty(key,out var v) && v.ValueKind==JsonValueKind.String && v.TryGetGuid(out var id) && id==expected;
    private static bool JsonInt(JsonElement root,string key,int expected) => root.TryGetProperty(key,out var v) && v.ValueKind==JsonValueKind.Number && v.TryGetInt32(out var n) && n==expected;
    private static bool JsonText(JsonElement root,string key,string expected) => root.TryGetProperty(key,out var v) && v.ValueKind==JsonValueKind.String && v.GetString()==expected;
    private static string? NormalizeQuoteDecisionText(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static SalesDraftServiceException SelectedQuoteUnavailable() => new(503,"service_unavailable");
}
