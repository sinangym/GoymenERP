using System.Text.Json;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

public sealed partial class SalesStore
{
    /// <summary>
    /// SAT-D32: the user closure of a "kapanmaya hazır" order, one save — the force-closure's key rules and shape. The header is
    /// tracked; <paramref name="beforeWrite"/> (the caller's fences, the domain's preconditions and the readiness recomputed from
    /// the owners) runs on the draft as read; the domain decides (version, state) and returns the facts. The user-closure row is
    /// APPENDED beside the header's lifecycle state and version, the attempt record and the audit event — or nothing at all. No
    /// line, revision, schedule or approval column changes (checked); no stock or money event and no other module's record is
    /// written.
    /// </summary>
    public async Task<SalesDraftStateResult?> CloseDraftAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
        Guid draftId, Func<SalesOrderDraft, DateTimeOffset, UserClosureFacts> mutate, SalesDraftStateAudit audit,
        Func<SalesOrderDraft, CancellationToken, Task> beforeWrite, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(beforeWrite);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireCommand(commandName);
        if (commandName != SalesDraftCommands.CloseV1)
        {
            throw new ArgumentException("Yalnız kullanıcı kapatma komutu siparişi bu yoldan kapatır.", nameof(commandName));
        }

        if (string.IsNullOrWhiteSpace(audit.EventType) || string.IsNullOrWhiteSpace(audit.Operation) || string.IsNullOrWhiteSpace(audit.Reason))
        {
            throw new ArgumentException("Kapatma komutunun audit olayı tür, işlem ve gerekçe ister.", nameof(audit));
        }

        var companyId = RequireResolvedCompany();
        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        var actorId = envelope.ActorId.Trim();
        var attempt = await dbContext.DraftAttempts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == commandName && row.IdempotencyKeyHash == keyHash, cancellationToken);
        if (attempt is not null)
        {
            if (!string.Equals(attempt.ActorId, actorId, StringComparison.Ordinal)
                || !string.Equals(attempt.RequestHash, requestHash, StringComparison.Ordinal))
            {
                throw Refused(SalesDraftStoreCodes.SameKeyPayloadMismatch,
                    "Aynı Idempotency-Key başka bir istekle geldi; ilk cevap bu isteğe verilmez.");
            }

            if (attempt.Outcome != AttemptOutcomeRecorded || attempt.DraftId is not { } recordedId)
            {
                throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Bu anahtar kayıtlı bir terminal reddi taşıyor; tekrar aynı reddi alır.");
            }

            var stored = await FindDraftAsync(recordedId, cancellationToken)
                ?? throw new InvalidOperationException("Kayıtlı deneme kaydının taslağı bulunamadı.");
            return new SalesDraftStateResult(stored, SalesDraftWriteOutcome.Replayed, attempt.CorrelationId);
        }

        if (draftId == Guid.Empty)
        {
            return null;
        }

        var row = await dbContext.Drafts.SingleOrDefaultAsync(candidate => candidate.Id == draftId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var draft = (await RestoreAsync([row], cancellationToken)).Single();
        await beforeWrite(draft, cancellationToken);
        var before = new { state = draft.State.ToString(), approvalState = draft.ApprovalState.ToString(), revision = draft.Revision, version = draft.Version };
        var versionBefore = draft.Version;
        var revisionBefore = draft.Revision;
        var approvalBefore = (draft.ApprovalState, draft.Approval);
        var quantitiesBefore = draft.Lines.Select(line => (line.Id, line.QuantityText)).ToArray();
        var now = timeProvider.GetUtcNow();
        var facts = mutate(draft, now);
        if (draft.Version != versionBefore + 1 || draft.Id != row.Id || draft.CompanyId != companyId
            || draft.Revision != revisionBefore || (draft.ApprovalState, draft.Approval) != approvalBefore
            || draft.State != SalesDraftState.Closed || facts is null || draft.UserClosure != facts || draft.ForceClosure is not null
            || !quantitiesBefore.SequenceEqual(draft.Lines.Select(line => (line.Id, line.QuantityText))))
        {
            throw new InvalidOperationException("Kullanıcı kapatması tam olarak bir sürüm ilerletir; revizyonu, onay eksenini ve satır miktarlarını değiştirmez.");
        }

        dbContext.DraftUserClosures.Add(new DraftUserClosureRow
        {
            CompanyId = companyId,
            DraftId = draft.Id,
            Revision = facts.Revision,
            ClosedBy = facts.ClosedBy,
            ClosedAtUtc = facts.ClosedAtUtc,
            DraftVersion = draft.Version,
            CorrelationId = envelope.CorrelationId.Trim(),
        });
        row.State = (int)draft.State;
        row.Version = draft.Version;
        row.UpdatedAtUtc = draft.UpdatedAtUtc;
        dbContext.DraftAttempts.Add(new DraftAttemptRow
        {
            CompanyId = companyId,
            CommandName = commandName,
            IdempotencyKeyHash = keyHash,
            RequestHash = requestHash,
            ActorId = actorId,
            Outcome = AttemptOutcomeRecorded,
            DraftId = draft.Id,
            CorrelationId = envelope.CorrelationId.Trim(),
            RecordedAtUtc = now,
        });
        auditEvents.Enqueue(new AuditEventDraft(
            companyId,
            audit.EventType,
            DraftEntityType,
            draft.Id.ToString("D"),
            audit.Operation,
            EntityVersionBefore: versionBefore,
            EntityVersionAfter: draft.Version,
            AuditActorTypes.User,
            envelope.ActorId,
            envelope.PermissionCode,
            envelope.RoleCodes,
            JsonSerializer.Serialize(before, Json),
            JsonSerializer.Serialize(new
            {
                state = draft.State.ToString(),
                approvalState = draft.ApprovalState.ToString(),
                revision = draft.Revision,
                version = draft.Version,
                userClosure = new { facts.ClosedBy, facts.ClosedAtUtc },
            }, Json),
            audit.Reason,
            ApprovalContextJson: null,
            AuditAiInvolvement.None,
            AiContextJson: null,
            envelope.SourceChannel,
            envelope.ClientIpAddress,
            envelope.UserAgent,
            envelope.CorrelationId,
            envelope.CausationId,
            envelope.IdempotencyKey));
        await SaveAsync(cancellationToken);
        return new SalesDraftStateResult(draft, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }
}
