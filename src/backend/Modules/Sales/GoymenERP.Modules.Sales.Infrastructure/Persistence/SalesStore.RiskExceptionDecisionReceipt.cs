using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

public sealed partial class SalesStore
{
    public async Task<SalesRiskExceptionOriginalDecisionReceiptV1?> ReadRiskExceptionDecisionReceiptAsync(SalesDraftWriteEnvelope envelope,
        string hash, string command, SalesRiskExceptionCommandResult response, Func<CancellationToken, Task> freshFence, CancellationToken token)
    {
        RequireRiskExceptionDecisionCommand(envelope, hash, command, writes: false); var company = RequireResolvedCompany();
        var key = IdempotencyKeyHash(envelope.IdempotencyKey); await Fence();
        var attempts = await dbContext.ShipmentRiskExceptionAttempts.AsNoTracking().Where(x => x.CompanyId == company
            && x.CommandName == command && x.IdempotencyKeyHash == key).Take(2).ToArrayAsync(token);
        await Fence();
        if (attempts.Length != 1 || attempts[0].ExceptionId is not { } id) return null;
        var events = await RiskDecisionAuditCandidatesAsync(company, id, command, key, token); await Fence();
        var current = await ReadRiskExceptionDecisionSourceAsync(id, freshFence, token); await Fence();
        return RestoreOriginalRiskDecisionReceipt(attempts[0], events, envelope.ActorId.Trim(), key, hash, command, response, current);
        async Task Fence() { token.ThrowIfCancellationRequested(); RequireCompany(company); await freshFence(token); token.ThrowIfCancellationRequested(); RequireCompany(company); }
    }
    private Task<AuditEvent[]> RiskDecisionAuditCandidatesAsync(Guid company, Guid id, string command, string key, CancellationToken token)
    {
        var entity = id.ToString("D"); var eventType = command == SalesRiskExceptionCommands.ApproveV1 ? "sales.order.risk-exception.approved.v1" : "sales.order.risk-exception.rejected.v1";
        return dbContext.Set<AuditEvent>().AsNoTracking().Where(x => x.CompanyId == company && x.EntityType == RiskExceptionEntityType
            && x.EntityId == entity && x.EventType == eventType && x.IdempotencyKeyHash == key).Take(2).ToArrayAsync(token);
    }
    internal static SalesRiskExceptionOriginalDecisionReceiptV1? RestoreOriginalRiskDecisionReceipt(ShipmentRiskExceptionAttemptRow attempt,
        IReadOnlyList<AuditEvent> events, string actor, string keyHash, string hash, string command,
        SalesRiskExceptionCommandResult response, SalesRiskExceptionDecisionSourceV1? current)
    {
        if (attempt is null || events is not { Count: 1 } || events[0] is null || current?.Recorded is not { } recorded || response?.Exception is null
            || command is not (SalesRiskExceptionCommands.ApproveV1 or SalesRiskExceptionCommands.RejectV1)
            || attempt.CompanyId == Guid.Empty || attempt.ExceptionId is not { } id || id == Guid.Empty
            || attempt.Outcome != AttemptOutcomeRecorded || attempt.RefusalJson is not null || attempt.CommandName != command
            || attempt.ActorId != actor || string.IsNullOrWhiteSpace(actor) || attempt.RequestHash != hash || attempt.IdempotencyKeyHash != keyHash
            || string.IsNullOrWhiteSpace(attempt.CorrelationId) || response.OriginalCorrelationId != attempt.CorrelationId
            || current.CompanyId != attempt.CompanyId || recorded.Id != id || recorded.OrderId != attempt.OrderId
            || !SalesRiskExceptionDecisionDigestsV1.MatchesCurrent(recorded, response.Exception)) return null;
        var audit = events[0]; var approve = command == SalesRiskExceptionCommands.ApproveV1;
        if (audit.Id == Guid.Empty || audit.SubjectKind != AuditEvent.CompanySubject || audit.CompanyId != attempt.CompanyId
            || audit.EntityType != RiskExceptionEntityType || audit.EntityId != id.ToString("D")
            || audit.EventType != (approve ? "sales.order.risk-exception.approved.v1" : "sales.order.risk-exception.rejected.v1")
            || audit.Operation != (approve ? "ApproveRiskException" : "RejectRiskException") || audit.ActorType != AuditActorTypes.User
            || audit.ActorId != actor || audit.PermissionCode != SalesPermissionManifestV1.OrdersRiskExceptionApprove
            || audit.Outcome != AuditOutcomes.Succeeded || audit.CorrelationId != attempt.CorrelationId || audit.IdempotencyKeyHash != keyHash
            || audit.SchemaVersion != 1 || audit.IntegrityHash != AuditEventIntegrity.Compute(audit.ToIntegrityEnvelope())
            || !TryRiskAudit(audit.BeforeJson, out var before) || !TryRiskAudit(audit.AfterJson, out var after)) return null;
        try
        {
            if (before!.Id != id || before.OrderId != attempt.OrderId || before.Version < 1 || before.Version == long.MaxValue
                || audit.EntityVersionBefore != before.Version || audit.EntityVersionAfter != before.Version + 1
                || after != recorded || before.State != "Requested" || before.DecidedBy is not null || before.DecidedAtUtc is not null
                || before.RejectionReason is not null || before.ValidOn is not null || after!.DecidedBy != actor
                || after.DecidedAtUtc != attempt.RecordedAtUtc || after.Version != before.Version + 1) return null;
            var native = SalesRiskExceptionDecisionDigestsV1.Restore(attempt.CompanyId, before);
            if (approve) native.Approve(before.Version, actor, attempt.RecordedAtUtc, SalesBusinessDay.DateOf(attempt.RecordedAtUtc));
            else native.Reject(before.Version, actor, after.RejectionReason, attempt.RecordedAtUtc);
            if (!FloorJsonEquals(audit.AfterJson, RiskExceptionAuditState(native))) return null;
            var expected = before.Version.ToString(CultureInfo.InvariantCulture);
            if (SalesRiskExceptionHash.Compute(command, attempt.OrderId, before.ExceptionNo, new(expected, after.RejectionReason)) != hash) return null;
            var proof = new SalesRiskExceptionOriginalDecisionReceiptV1(attempt.CompanyId, id, command, actor, keyHash, hash,
                expected, after.Version.ToString(CultureInfo.InvariantCulture), attempt.CorrelationId, attempt.RecordedAtUtc,
                audit.Id, audit.IntegrityHash, before, after, "") { CurrentSource = current,
                    CurrentResponseDigest = SalesRiskExceptionDecisionDigestsV1.CurrentResponseDigest(response) };
            return proof with { OriginalDecisionDigest = SalesRiskExceptionDecisionDigestsV1.OriginalDecisionDigest(proof) };
        }
        catch (Exception failure) when (failure is InvalidOperationException or ArgumentException or FormatException or OverflowException or JsonException)
        { return null; }
    }
    private static bool TryRiskAudit(string json, out SalesRiskExceptionRecordedStateV1? state)
    {
        state = null;
        try
        {
            using var document = JsonDocument.Parse(json); var root = document.RootElement;
            string[] keys = ["id","orderId","exceptionNo","loadingInstructionId","state","version","reason","requestedBy","requestedAtUtc","decidedBy","decidedAtUtc","rejectionReason","validOn"];
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != keys.Length || !keys.All(k => root.TryGetProperty(k, out _))) return false;
            state = JsonSerializer.Deserialize<SalesRiskExceptionRecordedStateV1>(json, SalesDraftJson.Options); return state is not null;
        }
        catch (JsonException) { return false; }
        catch (NotSupportedException) { return false; }
    }
    public async Task<SalesRiskExceptionOriginalVersionReadResultV1> ReadRiskExceptionOriginalVersionAsync(Guid exceptionId, long expectedVersion,
        string actorId, Func<CancellationToken, Task> freshFence, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(freshFence); var company = RequireResolvedCompany(); var entity = exceptionId.ToString("D"); await Fence();
        var candidates = await (from attempt in dbContext.ShipmentRiskExceptionAttempts.AsNoTracking()
                                join audit in dbContext.Set<AuditEvent>().AsNoTracking()
                                    on new { CompanyId = (Guid?)attempt.CompanyId, Key = attempt.IdempotencyKeyHash } equals new { audit.CompanyId, Key = audit.IdempotencyKeyHash }
                                where attempt.CompanyId == company && attempt.ExceptionId == exceptionId && attempt.ActorId == actorId
                                    && attempt.Outcome == AttemptOutcomeRecorded && attempt.RefusalJson == null
                                    && (attempt.CommandName == SalesRiskExceptionCommands.ApproveV1 || attempt.CommandName == SalesRiskExceptionCommands.RejectV1)
                                    && audit.CompanyId == company && audit.EntityType == RiskExceptionEntityType && audit.EntityId == entity
                                    && audit.ActorId == actorId && audit.EntityVersionBefore == expectedVersion
                                    && (audit.EventType == "sales.order.risk-exception.approved.v1" || audit.EventType == "sales.order.risk-exception.rejected.v1")
                                select attempt).Take(2).ToArrayAsync(token);
        await Fence();
        var source = await ReadRiskExceptionDecisionSourceAsync(exceptionId, freshFence, token); await Fence();
        if (candidates.Length == 0) return new(SalesRiskExceptionDecisionReadStateV1.NotFound, null) { CurrentVersion = source?.Recorded.Version };
        if (candidates.Length != 1 || source is null) return new(SalesRiskExceptionDecisionReadStateV1.Unavailable, null);
        var a = candidates[0]; var events = await RiskDecisionAuditCandidatesAsync(company, exceptionId, a.CommandName, a.IdempotencyKeyHash, token); await Fence();
        var response = new SalesRiskExceptionCommandResult(GoymenERP.Modules.Sales.Application.SalesRiskExceptionService.ProjectRiskDecision(source, SalesBusinessDay.DateOf(timeProvider.GetUtcNow())).Exception, true, a.CorrelationId);
        var receipt = RestoreOriginalRiskDecisionReceipt(a, events, actorId, a.IdempotencyKeyHash, a.RequestHash, a.CommandName, response, source);
        return receipt?.ExpectedVersion == expectedVersion.ToString(CultureInfo.InvariantCulture)
            ? new(SalesRiskExceptionDecisionReadStateV1.Found, receipt) : new(SalesRiskExceptionDecisionReadStateV1.Unavailable, null);
        async Task Fence() { token.ThrowIfCancellationRequested(); RequireCompany(company); await freshFence(token); token.ThrowIfCancellationRequested(); RequireCompany(company); }
    }
}
