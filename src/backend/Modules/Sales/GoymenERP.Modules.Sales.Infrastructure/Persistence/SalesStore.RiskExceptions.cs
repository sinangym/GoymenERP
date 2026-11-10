using System.Text.Json;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.RiskExceptions;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// API-07 (SAT-D153…D162): the single-shipment risk exception half of the Sales store. The attempt rules are the invoice's
/// (ADR-011 §2–§8) in the exception's own attempt table: (company, command, key hash) is the identity, the raw key is never
/// stored, the same key, content and actor answers the stored exception, another content or actor is one 409, a lost race
/// stores nothing. One save writes the exception (or its decision), the attempt and the audit event. The filtered unique index
/// UX_ShipmentRiskExceptions_PendingShipment makes a second pending exception of one shipment the save's concurrent change, and
/// UX_ShipmentRiskExceptions_No a racing per-order number. Sales writes only its own tables (SAT-D209): the loading instruction
/// id is an opaque reference.
/// </summary>
public sealed partial class SalesStore : ISalesRiskExceptionStore
{
    private const string RiskExceptionEntityType = "Sales.ShipmentRiskException";

    public async Task<SalesRiskExceptionReplay?> FindRiskExceptionReplayAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        string commandName, CancellationToken cancellationToken = default)
    {
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireRiskExceptionCommand(commandName);
        RequireResolvedCompany();
        var attempt = await FindRiskExceptionAttemptAsync(commandName, IdempotencyKeyHash(envelope.IdempotencyKey), cancellationToken);
        if (attempt is null)
        {
            return null;
        }

        RequireSameRiskExceptionRequest(attempt, envelope.ActorId.Trim(), requestHash);
        return await ReadRiskExceptionReplayAsync(attempt, cancellationToken);
    }

    public async Task<SalesRiskExceptionStoreResult> StoreRiskExceptionAsync(SalesDraftWriteEnvelope envelope, string requestHash, Guid orderId,
        Func<int, DateTimeOffset, SalesShipmentRiskException> build, SalesDraftStateAudit audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(build);
        RequireRiskExceptionAudit(audit);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        var companyId = RequireResolvedCompany();
        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("Sipariş kimliği boş olamaz.", nameof(orderId));
        }

        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        var actorId = envelope.ActorId.Trim();
        var attempt = await FindRiskExceptionAttemptAsync(SalesRiskExceptionCommands.RequestV1, keyHash, cancellationToken);
        if (attempt is not null)
        {
            return await ReplayRiskExceptionAttemptAsync(attempt, actorId, requestHash, cancellationToken);
        }

        // Tenant-filtered: the next number of the company's order; a racing number loses at UX_ShipmentRiskExceptions_No.
        var last = await dbContext.ShipmentRiskExceptions.AsNoTracking().Where(row => row.OrderId == orderId)
            .MaxAsync(row => (int?)row.ExceptionNo, cancellationToken) ?? 0;
        var now = timeProvider.GetUtcNow();
        var item = build(checked(last + 1), now) ?? throw new ArgumentException("Risk istisnası kurulmadı.", nameof(build));
        RequireCompany(item.CompanyId);
        if (item.OrderId != orderId || item.ExceptionNo != last + 1 || item.State != SalesShipmentRiskExceptionState.Requested
            || item.Version != 1 || !string.Equals(item.RequestedBy, actorId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Risk istisnası bu siparişin sıradaki numarasıyla, yazma zarfının aktörü adına istenir.", nameof(build));
        }

        dbContext.ShipmentRiskExceptions.Add(SalesRiskExceptionRows.ToRow(item, envelope.CorrelationId));
        dbContext.ShipmentRiskExceptionAttempts.Add(NewRiskExceptionAttempt(companyId, SalesRiskExceptionCommands.RequestV1, keyHash,
            requestHash, actorId, orderId, AttemptOutcomeRecorded, item.Id, null, envelope.CorrelationId, now));
        EnqueueRiskExceptionAudit(envelope, companyId, audit, item, null, "{}");
        await SaveAsync(cancellationToken);
        return new SalesRiskExceptionStoreResult(item, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }

    public async Task RecordRiskExceptionRefusalAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName, Guid orderId,
        SalesDraftTerminalRefusal refusal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireRiskExceptionCommand(commandName);
        var companyId = RequireResolvedCompany();
        if (refusal.Status is < 400 or > 599 || string.IsNullOrWhiteSpace(refusal.Code) || refusal.Errors is null
            || refusal.Errors.Any(error => error is null || string.IsNullOrWhiteSpace(error.Field) || string.IsNullOrWhiteSpace(error.Code)))
        {
            throw new ArgumentException("Terminal ret bir durum, bir kod ve tam alan hataları taşır.", nameof(refusal));
        }

        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("Sipariş kimliği boş olamaz.", nameof(orderId));
        }

        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        if (await FindRiskExceptionAttemptAsync(commandName, keyHash, cancellationToken) is not null)
        {
            throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Bu anahtar zaten bir deneme taşıyor; tekrar onu alır.");
        }

        dbContext.ShipmentRiskExceptionAttempts.Add(NewRiskExceptionAttempt(companyId, commandName, keyHash, requestHash,
            envelope.ActorId.Trim(), orderId, AttemptOutcomeRefused, null, JsonSerializer.Serialize(refusal, Json), envelope.CorrelationId,
            timeProvider.GetUtcNow()));
        await SaveAsync(cancellationToken);
    }

    public async Task<SalesRiskExceptionStoreResult?> DecideRiskExceptionAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        string commandName, Guid orderId, int exceptionNo, Action<SalesShipmentRiskException, DateTimeOffset> mutate,
        SalesDraftStateAudit audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        RequireRiskExceptionAudit(audit);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireRiskExceptionCommand(commandName);
        if (commandName == SalesRiskExceptionCommands.RequestV1)
        {
            throw new ArgumentException("İstek kendi mağaza yöntemini kullanır.", nameof(commandName));
        }

        var companyId = RequireResolvedCompany();
        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        var actorId = envelope.ActorId.Trim();
        var attempt = await FindRiskExceptionAttemptAsync(commandName, keyHash, cancellationToken);
        if (attempt is not null)
        {
            return await ReplayRiskExceptionAttemptAsync(attempt, actorId, requestHash, cancellationToken);
        }

        if (orderId == Guid.Empty || exceptionNo < 1)
        {
            return null;
        }

        var row = await dbContext.ShipmentRiskExceptions
            .SingleOrDefaultAsync(candidate => candidate.OrderId == orderId && candidate.ExceptionNo == exceptionNo, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var item = SalesRiskExceptionRows.Restore(row);
        var before = RiskExceptionAuditState(item);
        var versionBefore = item.Version;
        var now = timeProvider.GetUtcNow();
        mutate(item, now);
        // A decision names its decider; a withdrawal (EK56 SAT-D286) has no decision and is the requester's own.
        var actedByActor = item.State == SalesShipmentRiskExceptionState.Withdrawn
            ? item.Decision is null && string.Equals(item.RequestedBy, actorId, StringComparison.Ordinal)
            : string.Equals(item.Decision?.DecidedBy, actorId, StringComparison.Ordinal);
        if (item.Version != versionBefore + 1 || item.Id != row.Id || item.CompanyId != companyId
            || item.State == SalesShipmentRiskExceptionState.Requested || !actedByActor)
        {
            throw new InvalidOperationException("Risk istisnası kararı tam olarak bir sürüm ilerletir, aktörün adına verilir ve kaydı değiştirmez.");
        }

        SalesRiskExceptionRows.ApplyDecision(row, item);
        dbContext.ShipmentRiskExceptionAttempts.Add(NewRiskExceptionAttempt(companyId, commandName, keyHash, requestHash, actorId, orderId,
            AttemptOutcomeRecorded, item.Id, null, envelope.CorrelationId, now));
        EnqueueRiskExceptionAudit(envelope, companyId, audit, item, versionBefore, before);
        await SaveAsync(cancellationToken);
        return new SalesRiskExceptionStoreResult(item, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }

    public async Task<SalesShipmentRiskException?> FindRiskExceptionAsync(Guid orderId, int exceptionNo,
        CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        var row = await dbContext.ShipmentRiskExceptions.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.OrderId == orderId && candidate.ExceptionNo == exceptionNo, cancellationToken);
        return row is null ? null : SalesRiskExceptionRows.Restore(row);
    }

    public async Task<IReadOnlyList<SalesShipmentRiskException>> ListRiskExceptionsAsync(Guid orderId, int limit,
        CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        if (limit < 1 || limit > SalesRiskExceptionApiV1.MaximumListed)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        var rows = await dbContext.ShipmentRiskExceptions.AsNoTracking().Where(row => row.OrderId == orderId)
            .OrderByDescending(row => row.ExceptionNo).Take(limit).ToListAsync(cancellationToken);
        return [.. rows.Select(SalesRiskExceptionRows.Restore)];
    }

    public async Task<IReadOnlyList<SalesShipmentRiskException>> ListShipmentRiskExceptionsAsync(Guid orderId, Guid loadingInstructionId,
        CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        var rows = await dbContext.ShipmentRiskExceptions.AsNoTracking()
            .Where(row => row.OrderId == orderId && row.LoadingInstructionId == loadingInstructionId)
            .OrderByDescending(row => row.ExceptionNo).ToListAsync(cancellationToken);
        return [.. rows.Select(SalesRiskExceptionRows.Restore)];
    }

    private Task<ShipmentRiskExceptionAttemptRow?> FindRiskExceptionAttemptAsync(string commandName, string keyHash,
        CancellationToken cancellationToken) =>
        dbContext.ShipmentRiskExceptionAttempts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == commandName && row.IdempotencyKeyHash == keyHash, cancellationToken);

    private static ShipmentRiskExceptionAttemptRow NewRiskExceptionAttempt(Guid companyId, string commandName, string keyHash,
        string requestHash, string actorId, Guid orderId, int outcome, Guid? exceptionId, string? refusalJson, string correlationId,
        DateTimeOffset now) => new()
    {
        CompanyId = companyId,
        CommandName = commandName,
        IdempotencyKeyHash = keyHash,
        RequestHash = requestHash,
        ActorId = actorId,
        Outcome = outcome,
        OrderId = orderId,
        ExceptionId = exceptionId,
        RefusalJson = refusalJson,
        CorrelationId = correlationId.Trim(),
        RecordedAtUtc = now,
    };

    private void EnqueueRiskExceptionAudit(SalesDraftWriteEnvelope envelope, Guid companyId, SalesDraftStateAudit audit,
        SalesShipmentRiskException item, long? versionBefore, string before) =>
        auditEvents.Enqueue(new AuditEventDraft(
            companyId,
            audit.EventType,
            RiskExceptionEntityType,
            item.Id.ToString("D"),
            audit.Operation,
            EntityVersionBefore: versionBefore,
            EntityVersionAfter: item.Version,
            AuditActorTypes.User,
            envelope.ActorId,
            envelope.PermissionCode,
            envelope.RoleCodes,
            before,
            RiskExceptionAuditState(item),
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

    private static void RequireRiskExceptionCommand(string commandName)
    {
        if (!SalesRiskExceptionCommands.IsRiskExceptionCommand(commandName))
        {
            throw new ArgumentException("Yalnız risk istisnası istek, onay ve ret komutları bu deneme kaydını taşır.", nameof(commandName));
        }
    }

    private static void RequireRiskExceptionAudit(SalesDraftStateAudit audit)
    {
        ArgumentNullException.ThrowIfNull(audit);
        if (string.IsNullOrWhiteSpace(audit.EventType) || string.IsNullOrWhiteSpace(audit.Operation) || string.IsNullOrWhiteSpace(audit.Reason))
        {
            throw new ArgumentException("Risk istisnasının audit olayı tür, işlem ve gerekçe ister.", nameof(audit));
        }
    }

    private static void RequireSameRiskExceptionRequest(ShipmentRiskExceptionAttemptRow attempt, string actorId, string requestHash)
    {
        if (!string.Equals(attempt.ActorId, actorId, StringComparison.Ordinal)
            || !string.Equals(attempt.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw Refused(SalesDraftStoreCodes.SameKeyPayloadMismatch,
                "Aynı Idempotency-Key başka bir istekle geldi; ilk cevap bu isteğe verilmez.");
        }
    }

    private async Task<SalesRiskExceptionReplay> ReadRiskExceptionReplayAsync(ShipmentRiskExceptionAttemptRow attempt,
        CancellationToken cancellationToken)
    {
        if (attempt.Outcome == AttemptOutcomeRecorded && attempt.ExceptionId is { } exceptionId)
        {
            var row = await dbContext.ShipmentRiskExceptions.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == exceptionId, cancellationToken)
                ?? throw new InvalidOperationException("Kayıtlı deneme kaydının risk istisnası bulunamadı.");
            return new SalesRiskExceptionReplay(SalesRiskExceptionRows.Restore(row), null, attempt.CorrelationId);
        }

        var refusal = attempt.RefusalJson is null ? null : JsonSerializer.Deserialize<SalesDraftTerminalRefusal>(attempt.RefusalJson, Json);
        if (attempt.Outcome != AttemptOutcomeRefused || refusal is null || refusal.Status is < 400 or > 599
            || string.IsNullOrWhiteSpace(refusal.Code) || refusal.Errors is null)
        {
            throw new InvalidOperationException("Risk istisnası deneme kaydı okunamadı.");
        }

        return new SalesRiskExceptionReplay(null, refusal, attempt.CorrelationId);
    }

    private async Task<SalesRiskExceptionStoreResult> ReplayRiskExceptionAttemptAsync(ShipmentRiskExceptionAttemptRow attempt, string actorId,
        string requestHash, CancellationToken cancellationToken)
    {
        RequireSameRiskExceptionRequest(attempt, actorId, requestHash);
        var replay = await ReadRiskExceptionReplayAsync(attempt, cancellationToken);
        if (replay.Exception is null)
        {
            // ADR-011 §7: the key holds a recorded terminal refusal; the caller replays it through FindRiskExceptionReplayAsync.
            throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Bu anahtar kayıtlı bir terminal reddi taşıyor; tekrar aynı reddi alır.");
        }

        return new SalesRiskExceptionStoreResult(replay.Exception, SalesDraftWriteOutcome.Replayed, attempt.CorrelationId);
    }

    /// <summary>SAT-D154: who requested, who decided, which shipment, the reason and the times — never an amount.</summary>
    private static string RiskExceptionAuditState(SalesShipmentRiskException item) => JsonSerializer.Serialize(new
    {
        item.Id,
        item.OrderId,
        item.ExceptionNo,
        item.LoadingInstructionId,
        state = item.State.ToString(),
        version = item.Version,
        item.Reason,
        item.RequestedBy,
        item.RequestedAtUtc,
        decidedBy = item.Decision?.DecidedBy,
        decidedAtUtc = item.Decision?.DecidedAtUtc,
        rejectionReason = item.Decision?.RejectionReason,
        item.ValidOn,
    }, Json);
}
