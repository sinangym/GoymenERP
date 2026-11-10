using System.Text.Json;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

public sealed partial class SalesStore
{
    /// <summary>
    /// SAT-D252..SAT-D255 / API-11: the source plan save, one save — the line closure's key rules and shape. The header is
    /// tracked; <paramref name="beforeWrite"/> (the caller's fences, the domain's preconditions and the delivery, allocation and
    /// linked-supply owners read afresh) runs on the draft as read and answers the plan; the domain decides (version, the ONE
    /// equality per line) and returns the facts. One plan row per listed line is APPENDED at the new version beside the header's
    /// version, the attempt record and the audit event — or nothing at all. No line, revision, schedule, lifecycle or approval
    /// column changes (checked); no stock, reservation or other module's record is written (SAT-D255: the plan reserves nothing).
    /// </summary>
    public async Task<SalesDraftStateResult?> RecordSourcePlanAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        string commandName, Guid draftId, Func<SalesOrderDraft, SourcePlanInput, DateTimeOffset, SourcePlanFacts> mutate,
        SalesDraftStateAudit audit, Func<SalesOrderDraft, CancellationToken, Task<SalesSourcePlanWriteInput>> beforeWrite,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(beforeWrite);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireCommand(commandName);
        if (commandName != SalesDraftCommands.RecordSourcePlanV1)
        {
            throw new ArgumentException("Yalnız kaynak planı komutu kaynak planı kaydeder.", nameof(commandName));
        }

        if (string.IsNullOrWhiteSpace(audit.EventType) || string.IsNullOrWhiteSpace(audit.Operation) || string.IsNullOrWhiteSpace(audit.Reason))
        {
            throw new ArgumentException("Kaynak planı komutunun audit olayı tür, işlem ve gerekçe ister.", nameof(audit));
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
        var input = await beforeWrite(draft, cancellationToken)
            ?? throw new InvalidOperationException("Kaynak planı, sahiplerinden yeniden okunan teslim ve tahsis olmadan kaydedilmez.");
        if (input.Plan is null || input.LinkedSupply is null || input.LinkedSupply.Any(item => item is null))
        {
            throw new InvalidOperationException("Kaynak planı yazma girdisi plan ve bağlı tedarik listesi taşır.");
        }

        var versionBefore = draft.Version;
        var stateBefore = (draft.State, draft.Revision, draft.ApprovalState, draft.Approval);
        var closuresBefore = draft.LineClosures.Count;
        var linesBefore = draft.Lines.Select(line => (line.Id, line.LineNo, line.QuantityText)).ToArray();
        var before = new { state = draft.State.ToString(), approvalState = draft.ApprovalState.ToString(), revision = draft.Revision, version = draft.Version };
        var now = timeProvider.GetUtcNow();
        var facts = mutate(draft, input.Plan, now);
        if (draft.Version != versionBefore + 1 || draft.Id != row.Id || draft.CompanyId != companyId
            || (draft.State, draft.Revision, draft.ApprovalState, draft.Approval) != stateBefore
            || draft.LineClosures.Count != closuresBefore
            || facts?.Lines is null || facts.Lines.Count != input.Plan.Lines.Count
            || !facts.Lines.Select(line => line.LineId).SequenceEqual(input.Plan.Lines.Select(line => line.LineId))
            || facts.Lines.Any(line => line.DraftVersion != draft.Version)
            || !linesBefore.SequenceEqual(draft.Lines.Select(line => (line.Id, line.LineNo, line.QuantityText))))
        {
            throw new InvalidOperationException("Kaynak planı tam olarak bir sürüm ilerletir ve yalnız listelenen satırların plan kayıtlarını ekler; yaşam döngüsünü, onay eksenini ve satırları değiştirmez.");
        }

        var correlationId = envelope.CorrelationId.Trim();
        foreach (var line in facts.Lines)
        {
            dbContext.DraftSourcePlanLines.Add(new DraftSourcePlanLineRow
            {
                CompanyId = companyId,
                DraftId = draft.Id,
                DraftVersion = draft.Version,
                LineId = line.LineId,
                LineNo = line.LineNo,
                LineQuantity = line.LineQuantity,
                LockedQuantity = line.Locked,
                StockQuantity = line.Stock,
                ProductionQuantity = line.Production,
                PurchasingQuantity = line.Purchasing,
                SubcontractQuantity = line.Subcontract,
                LinkedSupplyConfirmed = line.LinkedSupplyConfirmed,
                SavedBy = line.SavedBy,
                SavedAtUtc = line.SavedAtUtc,
                CorrelationId = correlationId,
            });
        }

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
            CorrelationId = correlationId,
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
                sourcePlan = new
                {
                    linkedSupplyConfirmed = input.Plan.LinkedSupplyConfirmed,
                    linkedSupply = input.LinkedSupply
                        .OrderBy(item => item.Kind, StringComparer.Ordinal)
                        .ThenBy(item => item.SupplyId)
                        .ThenBy(item => item.SupplyLineId)
                        .Select(item => new { item.Kind, item.DocumentType, item.SupplyId, item.SupplyLineId, item.OrderLineId })
                        .ToArray(),
                    lines = facts.Lines.Select(line => new
                    {
                        line.LineId, line.LineNo, line.LineQuantity, line.Locked, line.Stock, line.Production, line.Purchasing,
                        line.Subcontract,
                    }).ToArray(),
                },
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
        return new SalesDraftStateResult(draft, SalesDraftWriteOutcome.Recorded, correlationId);
    }

    /// <summary>
    /// API-11: the current recorded plan of each line of the draft — its row with the highest draft version — in line order;
    /// empty when none is recorded. Company-filtered (the query filter) like every read.
    /// </summary>
    public async Task<IReadOnlyList<SourcePlanLineFacts>> FindSourcePlanAsync(Guid draftId, CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        if (draftId == Guid.Empty)
        {
            return [];
        }

        var rows = await dbContext.DraftSourcePlanLines.AsNoTracking()
            .Where(plan => plan.DraftId == draftId)
            .ToListAsync(cancellationToken);
        return [.. rows
            .GroupBy(plan => plan.LineId)
            .Select(group => group.OrderByDescending(plan => plan.DraftVersion).First())
            .OrderBy(plan => plan.LineNo)
            .ThenBy(plan => plan.LineId)
            .Select(plan => new SourcePlanLineFacts(plan.LineId, plan.LineNo, plan.LineQuantity, plan.LockedQuantity,
                plan.StockQuantity, plan.ProductionQuantity, plan.PurchasingQuantity, plan.SubcontractQuantity,
                plan.LinkedSupplyConfirmed, plan.SavedBy, plan.SavedAtUtc, plan.DraftVersion))];
    }
}
