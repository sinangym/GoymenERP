using System.Text.Json;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.ApprovalRequests;
using GoymenERP.Modules.Sales.Domain.Drafts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// WP4 (SAT-D475 / SAT-D476): the approval request half of the Sales store. The attempt rules are the risk exception's (ADR-011
/// §2–§8) in the request's own attempt table: (company, command, key hash) is the identity, the raw key is never stored, the same
/// key, content and actor answers the stored request, another content or actor is one 409, a lost race stores nothing. One save
/// writes the request (or its decision), the attempt and the audit event; the over-delivery approval also appends the order's
/// over-delivery approval fact and the order's version step in that save. The filtered unique indexes keep one pending request per
/// quote revision and per order line. The Logistics instruction and shipment are opaque references.
/// </summary>
public sealed partial class SalesStore : ISalesApprovalRequestStore
{
    private const string ApprovalRequestEntityType = "Sales.ApprovalRequest";

    public async Task<SalesApprovalRequestReplay?> FindApprovalRequestReplayAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        string commandName, CancellationToken cancellationToken = default)
    {
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireApprovalRequestCommand(commandName);
        RequireResolvedCompany();
        var attempt = await FindApprovalRequestAttemptAsync(commandName, IdempotencyKeyHash(envelope.IdempotencyKey), cancellationToken);
        if (attempt is null)
        {
            return null;
        }

        RequireSameApprovalRequestAttempt(attempt, envelope.ActorId.Trim(), requestHash);
        return await ReadApprovalRequestReplayAsync(attempt, cancellationToken);
    }

    public async Task<SalesApprovalRequestStoreResult> StoreQuoteConversionRequestAsync(SalesDraftWriteEnvelope envelope,
        string requestHash, Guid quoteId, Func<int, DateTimeOffset, SalesApprovalRequest> build, SalesDraftStateAudit audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(build);
        RequireApprovalRequestAudit(audit);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        var companyId = RequireResolvedCompany();
        if (quoteId == Guid.Empty)
        {
            throw new ArgumentException("Teklif kimliği boş olamaz.", nameof(quoteId));
        }

        const string commandName = SalesApprovalRequestCommands.RequestQuoteConversionV1;
        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        var actorId = envelope.ActorId.Trim();
        var attempt = await FindApprovalRequestAttemptAsync(commandName, keyHash, cancellationToken);
        if (attempt is not null)
        {
            return await ReplayApprovalRequestAttemptAsync(attempt, actorId, requestHash, cancellationToken);
        }

        var next = await NextApprovalRequestNoAsync(SalesApprovalRequestKind.QuoteExpiredConversion, quoteId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var item = build(next, now) ?? throw new ArgumentException("Onay talebi kurulmadı.", nameof(build));
        RequireCompany(item.CompanyId);
        if (item.Kind != SalesApprovalRequestKind.QuoteExpiredConversion || item.SubjectId != quoteId || item.RequestNo != next
            || item.State != SalesApprovalRequestState.Requested || item.Version != 1
            || !string.Equals(item.RequestedBy, actorId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Dönüşüm talebi bu teklifin sıradaki numarasıyla, yazma zarfının aktörü adına açılır.", nameof(build));
        }

        dbContext.ApprovalRequests.Add(SalesApprovalRequestRows.ToRow(item, envelope.CorrelationId));
        dbContext.ApprovalRequestAttempts.Add(NewApprovalRequestAttempt(companyId, commandName, keyHash, requestHash, actorId, quoteId,
            AttemptOutcomeRecorded, item.Id, null, envelope.CorrelationId, now));
        EnqueueApprovalRequestAudit(envelope, companyId, audit, item, null, "{}");
        await SaveAsync(cancellationToken);
        return new SalesApprovalRequestStoreResult(item, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }

    public async Task RecordApprovalRequestRefusalAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
        Guid subjectId, SalesDraftTerminalRefusal refusal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireApprovalRequestCommand(commandName);
        var companyId = RequireResolvedCompany();
        if (refusal.Status is < 400 or > 599 || string.IsNullOrWhiteSpace(refusal.Code) || refusal.Errors is null
            || refusal.Errors.Any(error => error is null || string.IsNullOrWhiteSpace(error.Field) || string.IsNullOrWhiteSpace(error.Code)))
        {
            throw new ArgumentException("Terminal ret bir durum, bir kod ve tam alan hataları taşır.", nameof(refusal));
        }

        if (subjectId == Guid.Empty)
        {
            throw new ArgumentException("Talebin konusu boş olamaz.", nameof(subjectId));
        }

        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        if (await FindApprovalRequestAttemptAsync(commandName, keyHash, cancellationToken) is not null)
        {
            throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Bu anahtar zaten bir deneme taşıyor; tekrar onu alır.");
        }

        dbContext.ApprovalRequestAttempts.Add(NewApprovalRequestAttempt(companyId, commandName, keyHash, requestHash,
            envelope.ActorId.Trim(), subjectId, AttemptOutcomeRefused, null, JsonSerializer.Serialize(refusal, Json), envelope.CorrelationId,
            timeProvider.GetUtcNow()));
        await SaveAsync(cancellationToken);
    }

    public async Task<SalesApprovalRequestStoreResult?> RejectApprovalRequestAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        string commandName, SalesApprovalRequestKind kind, Guid subjectId, int requestNo,
        Action<SalesApprovalRequest, DateTimeOffset> mutate, SalesDraftStateAudit audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        RequireApprovalRequestAudit(audit);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireApprovalRequestCommand(commandName);
        if (commandName is not (SalesApprovalRequestCommands.RejectQuoteConversionV1 or SalesApprovalRequestCommands.RejectOverDeliveryV1))
        {
            throw new ArgumentException("Yalnız ret komutları bu yöntemi kullanır.", nameof(commandName));
        }

        var companyId = RequireResolvedCompany();
        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        var actorId = envelope.ActorId.Trim();
        var attempt = await FindApprovalRequestAttemptAsync(commandName, keyHash, cancellationToken);
        if (attempt is not null)
        {
            return await ReplayApprovalRequestAttemptAsync(attempt, actorId, requestHash, cancellationToken);
        }

        if (subjectId == Guid.Empty || requestNo < 1)
        {
            return null;
        }

        var row = await dbContext.ApprovalRequests.SingleOrDefaultAsync(candidate => candidate.Kind == (int)kind
            && candidate.SubjectId == subjectId && candidate.RequestNo == requestNo, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var item = SalesApprovalRequestRows.Restore(row);
        var before = ApprovalRequestAuditState(item);
        var versionBefore = item.Version;
        var now = timeProvider.GetUtcNow();
        mutate(item, now);
        if (item.Version != versionBefore + 1 || item.Id != row.Id || item.CompanyId != companyId
            || item.State != SalesApprovalRequestState.Rejected || !string.Equals(item.Decision?.DecidedBy, actorId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Talebin reddi tam olarak bir sürüm ilerletir ve aktörün adına verilir.");
        }

        SalesApprovalRequestRows.ApplyDecision(row, item);
        dbContext.ApprovalRequestAttempts.Add(NewApprovalRequestAttempt(companyId, commandName, keyHash, requestHash, actorId, subjectId,
            AttemptOutcomeRecorded, item.Id, null, envelope.CorrelationId, now));
        EnqueueApprovalRequestAudit(envelope, companyId, audit, item, versionBefore, before);
        await SaveAsync(cancellationToken);
        return new SalesApprovalRequestStoreResult(item, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }

    public async Task<SalesApprovalRequestStoreResult?> ApproveOverDeliveryRequestAsync(SalesDraftWriteEnvelope envelope,
        string requestHash, Guid orderId, int requestNo,
        Func<SalesOrderDraft, SalesApprovalRequest, CancellationToken, Task<OverDeliveryApprovalInput>> beforeWrite,
        Func<SalesOrderDraft, SalesApprovalRequest, OverDeliveryApprovalInput, DateTimeOffset, OverDeliveryApprovalFacts> mutate,
        SalesDraftStateAudit audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(beforeWrite);
        ArgumentNullException.ThrowIfNull(mutate);
        RequireApprovalRequestAudit(audit);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        const string commandName = SalesApprovalRequestCommands.ApproveOverDeliveryV1;
        var companyId = RequireResolvedCompany();
        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        var actorId = envelope.ActorId.Trim();
        var attempt = await FindApprovalRequestAttemptAsync(commandName, keyHash, cancellationToken);
        if (attempt is not null)
        {
            return await ReplayApprovalRequestAttemptAsync(attempt, actorId, requestHash, cancellationToken);
        }

        if (orderId == Guid.Empty || requestNo < 1)
        {
            return null;
        }

        var requestRow = await dbContext.ApprovalRequests.SingleOrDefaultAsync(candidate =>
            candidate.Kind == (int)SalesApprovalRequestKind.OverDelivery && candidate.SubjectId == orderId
            && candidate.RequestNo == requestNo, cancellationToken);
        var draftRow = requestRow is null
            ? null
            : await dbContext.Drafts.SingleOrDefaultAsync(candidate => candidate.Id == orderId, cancellationToken);
        if (requestRow is null || draftRow is null)
        {
            return null;
        }

        var request = SalesApprovalRequestRows.Restore(requestRow);
        var draft = (await RestoreAsync([draftRow], cancellationToken)).Single();
        var input = await beforeWrite(draft, request, cancellationToken)
            ?? throw new InvalidOperationException("Fazla teslim talebinin onayı, yeniden okunan yargı olmadan kaydedilmez.");
        var requestBefore = ApprovalRequestAuditState(request);
        var requestVersionBefore = request.Version;
        var draftVersionBefore = draft.Version;
        var stateBefore = (draft.State, draft.Revision, draft.ApprovalState, draft.Approval);
        var approvalsBefore = draft.OverDeliveryApprovals.Count;
        var quantitiesBefore = draft.Lines.Select(line => (line.Id, line.QuantityText)).ToArray();
        var now = timeProvider.GetUtcNow();
        var facts = mutate(draft, request, input, now);
        if (draft.Version != draftVersionBefore + 1 || draft.Id != draftRow.Id || draft.CompanyId != companyId
            || (draft.State, draft.Revision, draft.ApprovalState, draft.Approval) != stateBefore
            || facts is null || draft.OverDeliveryApprovals.Count != approvalsBefore + 1 || draft.OverDeliveryApprovals[^1] != facts
            || facts.LineId != input.LineId || request.LineId != facts.LineId
            || !quantitiesBefore.SequenceEqual(draft.Lines.Select(line => (line.Id, line.QuantityText)))
            || request.Version != requestVersionBefore + 1 || request.State != SalesApprovalRequestState.Approved
            || !string.Equals(request.Decision?.DecidedBy, actorId, StringComparison.Ordinal)
            || !string.Equals(facts.ApprovedBy, actorId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Fazla teslim talebinin onayı siparişe tek bir onay kaydı ekler, sipariş ve talep sürümünü birer ilerletir; başka bir şeyi değiştirmez.");
        }

        var line = draft.Lines.Single(candidate => candidate.Id == facts.LineId);
        dbContext.DraftOverDeliveryApprovals.Add(new DraftOverDeliveryApprovalRow
        {
            CompanyId = companyId,
            DraftId = draft.Id,
            LineId = facts.LineId,
            LineNo = line.LineNo,
            ApprovalNo = facts.ApprovalNo,
            BasisRevision = facts.BasisRevision,
            Ordered = facts.Ordered,
            ApprovedShipped = facts.ApprovedShipped,
            ApprovedBy = facts.ApprovedBy,
            ApprovedAtUtc = facts.ApprovedAtUtc,
            DraftVersion = draft.Version,
            CorrelationId = envelope.CorrelationId.Trim(),
        });
        draftRow.Version = draft.Version;
        draftRow.UpdatedAtUtc = draft.UpdatedAtUtc;
        SalesApprovalRequestRows.ApplyDecision(requestRow, request);
        dbContext.ApprovalRequestAttempts.Add(NewApprovalRequestAttempt(companyId, commandName, keyHash, requestHash, actorId, orderId,
            AttemptOutcomeRecorded, request.Id, null, envelope.CorrelationId, now));
        EnqueueApprovalRequestAudit(envelope, companyId, audit, request, requestVersionBefore, requestBefore);
        auditEvents.Enqueue(new AuditEventDraft(
            companyId,
            "sales.order-draft.over-delivery-approved.v1",
            DraftEntityType,
            draft.Id.ToString("D"),
            "ApproveOverDeliveryRequest",
            EntityVersionBefore: draftVersionBefore,
            EntityVersionAfter: draft.Version,
            AuditActorTypes.User,
            envelope.ActorId,
            envelope.PermissionCode,
            envelope.RoleCodes,
            JsonSerializer.Serialize(new { version = draftVersionBefore }, Json),
            JsonSerializer.Serialize(new
            {
                version = draft.Version,
                overDeliveryApproval = new
                {
                    facts.LineId, line.LineNo, facts.ApprovalNo, facts.BasisRevision, facts.Ordered, facts.ApprovedShipped,
                    facts.ApprovedBy, facts.ApprovedAtUtc, requestNo = request.RequestNo, shippedBy = input.ShippedBy.Order(StringComparer.Ordinal).ToArray(),
                },
            }, Json),
            "Fazla teslim talebi onaylandı; onay talep edilen sevk toplamını kapsar (SAT-D475, SAT-D213)",
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
        return new SalesApprovalRequestStoreResult(request, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }

    public async Task<(SalesApprovalRequest Request, bool Created)> OpenOverDeliveryRequestAsync(SalesOverDeliveryRequestOpenV1 input,
        SalesDraftStateAudit audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        RequireApprovalRequestAudit(audit);
        RequireEnvelope(input.Envelope);
        var companyId = RequireResolvedCompany();
        if (input.OrderId == Guid.Empty || input.LineId == Guid.Empty)
        {
            throw new ArgumentException("Sipariş ve satır kimliği boş olamaz.", nameof(input));
        }

        var pending = await PendingOverDeliveryRequestAsync(input.OrderId, input.LineId, cancellationToken);
        if (pending is not null)
        {
            return (pending, false);
        }

        var next = await NextApprovalRequestNoAsync(SalesApprovalRequestKind.OverDelivery, input.OrderId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var item = SalesApprovalRequest.RequestOverDelivery(Guid.NewGuid(), companyId, input.OrderId, next, input.LineId,
            input.BasisRevision, input.Ordered, input.Quantity, input.RequestedTotal, input.LoadingInstructionId, input.ShipmentId,
            input.Envelope.ActorId, now);
        dbContext.ApprovalRequests.Add(SalesApprovalRequestRows.ToRow(item, input.Envelope.CorrelationId));
        EnqueueApprovalRequestAudit(input.Envelope, companyId, audit, item, null, "{}");
        try
        {
            await SaveAsync(cancellationToken);
        }
        catch (SalesDraftStoreException exception) when (exception.Code == SalesDraftStoreCodes.ConcurrentChange)
        {
            // A racing attempt of the same line won the unique pending index: its request is the line's pending one.
            var won = await PendingOverDeliveryRequestAsync(input.OrderId, input.LineId, cancellationToken);
            if (won is null)
            {
                throw;
            }

            return (won, false);
        }

        return (item, true);
    }

    public async Task<SalesApprovalRequest?> FindApprovalRequestAsync(SalesApprovalRequestKind kind, Guid subjectId, int requestNo,
        CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        var row = await dbContext.ApprovalRequests.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Kind == (int)kind
            && candidate.SubjectId == subjectId && candidate.RequestNo == requestNo, cancellationToken);
        return row is null ? null : SalesApprovalRequestRows.Restore(row);
    }

    public async Task<IReadOnlyList<SalesApprovalRequest>> ListApprovalRequestsAsync(SalesApprovalRequestKind kind, Guid subjectId,
        int limit, CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        if (limit < 1 || limit > SalesApprovalRequestApiV1.MaximumListed)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        var rows = await dbContext.ApprovalRequests.AsNoTracking()
            .Where(candidate => candidate.Kind == (int)kind && candidate.SubjectId == subjectId)
            .OrderByDescending(candidate => candidate.RequestNo).Take(limit).ToListAsync(cancellationToken);
        return [.. rows.Select(SalesApprovalRequestRows.Restore)];
    }

    private async Task<SalesApprovalRequest?> PendingOverDeliveryRequestAsync(Guid orderId, Guid lineId, CancellationToken cancellationToken)
    {
        var row = await dbContext.ApprovalRequests.AsNoTracking().SingleOrDefaultAsync(candidate =>
            candidate.Kind == (int)SalesApprovalRequestKind.OverDelivery && candidate.SubjectId == orderId
            && candidate.LineId == lineId && candidate.State == (int)SalesApprovalRequestState.Requested, cancellationToken);
        return row is null ? null : SalesApprovalRequestRows.Restore(row);
    }

    private async Task<int> NextApprovalRequestNoAsync(SalesApprovalRequestKind kind, Guid subjectId, CancellationToken cancellationToken)
    {
        // Tenant-filtered: a racing number loses at UX_ApprovalRequests_No (the save's concurrent change).
        var last = await dbContext.ApprovalRequests.AsNoTracking()
            .Where(candidate => candidate.Kind == (int)kind && candidate.SubjectId == subjectId)
            .MaxAsync(candidate => (int?)candidate.RequestNo, cancellationToken) ?? 0;
        return checked(last + 1);
    }

    private Task<ApprovalRequestAttemptRow?> FindApprovalRequestAttemptAsync(string commandName, string keyHash,
        CancellationToken cancellationToken) =>
        dbContext.ApprovalRequestAttempts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == commandName && row.IdempotencyKeyHash == keyHash, cancellationToken);

    private static ApprovalRequestAttemptRow NewApprovalRequestAttempt(Guid companyId, string commandName, string keyHash,
        string requestHash, string actorId, Guid subjectId, int outcome, Guid? requestId, string? refusalJson, string correlationId,
        DateTimeOffset now) => new()
    {
        CompanyId = companyId,
        CommandName = commandName,
        IdempotencyKeyHash = keyHash,
        RequestHash = requestHash,
        ActorId = actorId,
        Outcome = outcome,
        SubjectId = subjectId,
        RequestId = requestId,
        RefusalJson = refusalJson,
        CorrelationId = correlationId.Trim(),
        RecordedAtUtc = now,
    };

    private void EnqueueApprovalRequestAudit(SalesDraftWriteEnvelope envelope, Guid companyId, SalesDraftStateAudit audit,
        SalesApprovalRequest item, long? versionBefore, string before) =>
        auditEvents.Enqueue(new AuditEventDraft(
            companyId,
            audit.EventType,
            ApprovalRequestEntityType,
            item.Id.ToString("D"),
            audit.Operation,
            EntityVersionBefore: versionBefore,
            EntityVersionAfter: item.Version,
            AuditActorTypes.User,
            envelope.ActorId,
            envelope.PermissionCode,
            envelope.RoleCodes,
            before,
            ApprovalRequestAuditState(item),
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

    private static void RequireApprovalRequestCommand(string commandName)
    {
        if (!SalesApprovalRequestCommands.IsApprovalRequestCommand(commandName))
        {
            throw new ArgumentException("Yalnız onay talebi komutları bu deneme kaydını taşır.", nameof(commandName));
        }
    }

    private static void RequireApprovalRequestAudit(SalesDraftStateAudit audit)
    {
        ArgumentNullException.ThrowIfNull(audit);
        if (string.IsNullOrWhiteSpace(audit.EventType) || string.IsNullOrWhiteSpace(audit.Operation) || string.IsNullOrWhiteSpace(audit.Reason))
        {
            throw new ArgumentException("Onay talebinin audit olayı tür, işlem ve gerekçe ister.", nameof(audit));
        }
    }

    private static void RequireSameApprovalRequestAttempt(ApprovalRequestAttemptRow attempt, string actorId, string requestHash)
    {
        if (!string.Equals(attempt.ActorId, actorId, StringComparison.Ordinal)
            || !string.Equals(attempt.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw Refused(SalesDraftStoreCodes.SameKeyPayloadMismatch,
                "Aynı Idempotency-Key başka bir istekle geldi; ilk cevap bu isteğe verilmez.");
        }
    }

    private async Task<SalesApprovalRequestReplay> ReadApprovalRequestReplayAsync(ApprovalRequestAttemptRow attempt,
        CancellationToken cancellationToken)
    {
        if (attempt.Outcome == AttemptOutcomeRecorded && attempt.RequestId is { } requestId)
        {
            var row = await dbContext.ApprovalRequests.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == requestId, cancellationToken)
                ?? throw new InvalidOperationException("Kayıtlı deneme kaydının onay talebi bulunamadı.");
            return new SalesApprovalRequestReplay(SalesApprovalRequestRows.Restore(row), null, attempt.CorrelationId);
        }

        var refusal = attempt.RefusalJson is null ? null : JsonSerializer.Deserialize<SalesDraftTerminalRefusal>(attempt.RefusalJson, Json);
        if (attempt.Outcome != AttemptOutcomeRefused || refusal is null || refusal.Status is < 400 or > 599
            || string.IsNullOrWhiteSpace(refusal.Code) || refusal.Errors is null)
        {
            throw new InvalidOperationException("Onay talebi deneme kaydı okunamadı.");
        }

        return new SalesApprovalRequestReplay(null, refusal, attempt.CorrelationId);
    }

    private async Task<SalesApprovalRequestStoreResult> ReplayApprovalRequestAttemptAsync(ApprovalRequestAttemptRow attempt,
        string actorId, string requestHash, CancellationToken cancellationToken)
    {
        RequireSameApprovalRequestAttempt(attempt, actorId, requestHash);
        var replay = await ReadApprovalRequestReplayAsync(attempt, cancellationToken);
        if (replay.Request is null)
        {
            throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Bu anahtar kayıtlı bir terminal reddi taşıyor; tekrar aynı reddi alır.");
        }

        return new SalesApprovalRequestStoreResult(replay.Request, SalesDraftWriteOutcome.Replayed, attempt.CorrelationId);
    }

    /// <summary>Who asked, who decided, what and when — never an amount (an over-delivery names quantities only).</summary>
    private static string ApprovalRequestAuditState(SalesApprovalRequest item) => JsonSerializer.Serialize(new
    {
        item.Id,
        kind = item.Kind.ToString(),
        item.SubjectId,
        item.RequestNo,
        item.SubjectRevision,
        item.LineId,
        item.Ordered,
        item.Quantity,
        item.RequestedTotal,
        item.SourceInstructionId,
        item.SourceShipmentId,
        item.Note,
        state = item.State.ToString(),
        version = item.Version,
        item.RequestedBy,
        item.RequestedAtUtc,
        decidedBy = item.Decision?.DecidedBy,
        decidedAtUtc = item.Decision?.DecidedAtUtc,
        decisionReason = item.Decision?.Reason,
    }, Json);
}
