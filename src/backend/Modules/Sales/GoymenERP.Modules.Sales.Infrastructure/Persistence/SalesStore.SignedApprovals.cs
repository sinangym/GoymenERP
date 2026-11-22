using System.Data;
using System.Text.Json;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.SignedApprovals;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// WP13 (YOL-D11): the signed approval half of the Sales store. The attempt rules are the contract's (ADR-011 §2–§8) in the signed
/// approval's own attempt table: (company, command, key hash) is the identity, the raw key is never stored, the same key, content and
/// actor answers again, another content or actor is one 409, a lost race stores nothing. A record is written in its own repeatable-read
/// transaction that re-reads the order row, so an order changed in between (version, revision, approval, lifecycle) refuses the record
/// instead of pinning a signature to a revision the person did not see; the order row itself is never written. One save writes the new
/// record, the supersession of the previous active record, the attempt and the audit events. Sales writes only its own tables.
/// </summary>
public sealed partial class SalesStore : ISalesOrderSignedApprovalStore
{
    private const string SignedApprovalEntityType = "Sales.OrderSignedApproval";

    public async Task<SalesOrderSignedApprovalReplay?> FindSignedApprovalReplayAsync(SalesDraftWriteEnvelope envelope,
        string requestHash, string commandName, CancellationToken cancellationToken = default)
    {
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireSignedApprovalCommand(commandName);
        RequireResolvedCompany();
        var attempt = await FindSignedApprovalAttemptAsync(commandName, IdempotencyKeyHash(envelope.IdempotencyKey), cancellationToken);
        if (attempt is null)
        {
            return null;
        }

        RequireSameSignedApprovalRequest(attempt, envelope.ActorId.Trim(), requestHash);
        return ReadSignedApprovalReplay(attempt);
    }

    public async Task<SalesOrderSignedApprovalOrderFactsV1?> FindSignedApprovalOrderAsync(Guid orderId,
        CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        if (orderId == Guid.Empty)
        {
            return null;
        }

        // Tenant-filtered: another company's order is null (404 to the caller).
        var row = await dbContext.Drafts.AsNoTracking()
            .Where(candidate => candidate.Id == orderId && candidate.Kind == (int)SalesDraftKind.Order)
            .Select(candidate => new { candidate.Id, candidate.Version, candidate.Revision, candidate.State, candidate.ApprovalState })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null
            ? null
            : new SalesOrderSignedApprovalOrderFactsV1(row.Id, row.Version, row.Revision, row.State == (int)SalesDraftState.Draft,
                row.ApprovalState == (int)InternalApprovalState.Approved);
    }

    public async Task<SalesOrderSignedApprovalStoreResult> StoreSignedApprovalAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        long expectedOrderVersion, SalesOrderSignedApproval approval, SalesDraftStateAudit audit, SalesDraftStateAudit supersededAudit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(approval);
        RequireSignedApprovalAudit(audit);
        RequireSignedApprovalAudit(supersededAudit);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        var companyId = RequireResolvedCompany();
        RequireCompany(approval.CompanyId);
        var actorId = envelope.ActorId.Trim();
        if (approval.State != SalesOrderSignedApprovalState.Active || approval.Version != 1
            || !string.Equals(approval.RecordedBy, actorId, StringComparison.Ordinal) || approval.OrderVersion != expectedOrderVersion)
        {
            throw new ArgumentException("Yeni imzalı onay etkin, ilk sürümde ve yazma zarfının aktörü adına kaydedilir.", nameof(approval));
        }

        var command = SalesOrderSignedApprovalCommands.RecordV1;
        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        return await InSignedApprovalTransactionAsync(async () =>
        {
            var attempt = await FindSignedApprovalAttemptAsync(command, keyHash, cancellationToken);
            if (attempt is not null)
            {
                return ReplaySignedApprovalAttempt(attempt, actorId, requestHash);
            }

            // Read inside the transaction (repeatable read): the order cannot move under the record until it commits.
            var order = await dbContext.Drafts
                .Where(candidate => candidate.Id == approval.OrderId && candidate.Kind == (int)SalesDraftKind.Order)
                .Select(candidate => new { candidate.Version, candidate.Revision, candidate.State, candidate.ApprovalState })
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Sipariş okunamadı; hiçbir şey kaydedilmedi.");
            if (order.State != (int)SalesDraftState.Draft || order.ApprovalState != (int)InternalApprovalState.Approved)
            {
                throw new SalesRuleException(SalesRuleCodes.StateConflict, "Sipariş açık ve onaylı değil.");
            }

            if (order.Version != expectedOrderVersion || order.Revision != approval.OrderRevision)
            {
                throw new SalesVersionConflictException(expectedOrderVersion, order.Version);
            }

            var now = timeProvider.GetUtcNow();
            var activeRow = await dbContext.OrderSignedApprovals
                .SingleOrDefaultAsync(row => row.OrderId == approval.OrderId && row.State == (int)SalesOrderSignedApprovalState.Active,
                    cancellationToken);
            if (activeRow is not null)
            {
                var previous = SalesSignedApprovalRows.Restore(activeRow);
                var before = SignedApprovalAuditState(previous);
                previous.Supersede(approval.Id, actorId, now);
                SalesSignedApprovalRows.ApplyDeactivation(activeRow, previous);
                EnqueueSignedApprovalAudit(envelope, companyId, supersededAudit, previous, 1, before);
            }

            dbContext.OrderSignedApprovals.Add(SalesSignedApprovalRows.ToRow(approval, envelope.CorrelationId));
            dbContext.OrderSignedApprovalAttempts.Add(NewSignedApprovalAttempt(companyId, command, keyHash, requestHash, actorId,
                AttemptOutcomeRecorded, approval.OrderId, null, envelope.CorrelationId, now));
            EnqueueSignedApprovalAudit(envelope, companyId, audit, approval, null, "{}");
            await SaveAsync(cancellationToken);
            return new SalesOrderSignedApprovalStoreResult(approval.OrderId, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId.Trim());
        }, cancellationToken);
    }

    public async Task<SalesOrderSignedApprovalStoreResult?> DeactivateSignedApprovalAsync(SalesDraftWriteEnvelope envelope,
        string requestHash, Guid orderId, Guid approvalId, Action<SalesOrderSignedApproval, DateTimeOffset> mutate,
        SalesDraftStateAudit audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        RequireSignedApprovalAudit(audit);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        var companyId = RequireResolvedCompany();
        var command = SalesOrderSignedApprovalCommands.DeactivateV1;
        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        var actorId = envelope.ActorId.Trim();
        var attempt = await FindSignedApprovalAttemptAsync(command, keyHash, cancellationToken);
        if (attempt is not null)
        {
            return ReplaySignedApprovalAttempt(attempt, actorId, requestHash);
        }

        if (orderId == Guid.Empty || approvalId == Guid.Empty)
        {
            return null;
        }

        var row = await dbContext.OrderSignedApprovals
            .SingleOrDefaultAsync(candidate => candidate.Id == approvalId && candidate.OrderId == orderId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var item = SalesSignedApprovalRows.Restore(row);
        var before = SignedApprovalAuditState(item);
        var versionBefore = item.Version;
        var now = timeProvider.GetUtcNow();
        mutate(item, now);
        if (item.Version != versionBefore + 1 || item.Id != row.Id || item.CompanyId != companyId
            || item.State != SalesOrderSignedApprovalState.Passive
            || item.Deactivation?.Kind != SalesOrderSignedApprovalDeactivationKind.Withdrawn
            || !string.Equals(item.Deactivation.DeactivatedBy, actorId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("İmzalı onayın pasifleştirilmesi tam olarak bir sürüm ilerletir ve aktörün adına yapılır.");
        }

        SalesSignedApprovalRows.ApplyDeactivation(row, item);
        dbContext.OrderSignedApprovalAttempts.Add(NewSignedApprovalAttempt(companyId, command, keyHash, requestHash, actorId,
            AttemptOutcomeRecorded, orderId, null, envelope.CorrelationId, now));
        EnqueueSignedApprovalAudit(envelope, companyId, audit, item, versionBefore, before);
        await SaveAsync(cancellationToken);
        return new SalesOrderSignedApprovalStoreResult(orderId, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }

    public async Task RecordSignedApprovalRefusalAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
        Guid orderId, SalesDraftTerminalRefusal refusal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireSignedApprovalCommand(commandName);
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
        if (await FindSignedApprovalAttemptAsync(commandName, keyHash, cancellationToken) is not null)
        {
            throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Bu anahtar zaten bir deneme taşıyor; tekrar onu alır.");
        }

        dbContext.OrderSignedApprovalAttempts.Add(NewSignedApprovalAttempt(companyId, commandName, keyHash, requestHash,
            envelope.ActorId.Trim(), AttemptOutcomeRefused, orderId, JsonSerializer.Serialize(refusal, Json), envelope.CorrelationId,
            timeProvider.GetUtcNow()));
        await SaveAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SalesOrderSignedApproval>> ListSignedApprovalsAsync(Guid orderId, int limit,
        CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        if (limit < 1 || limit > SalesOrderSignedApprovalApiV1.MaximumListed)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        var rows = await dbContext.OrderSignedApprovals.AsNoTracking()
            .Where(row => row.OrderId == orderId)
            .OrderByDescending(row => row.RecordedAtUtc).ThenByDescending(row => row.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(SalesSignedApprovalRows.Restore)];
    }

    private async Task<T> InSignedApprovalTransactionAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        // An owner command with its own clean transaction: pending changes or a borrowed transaction would commit unrelated work.
        if (dbContext.Database.CurrentTransaction is not null || dbContext.ChangeTracker.HasChanges())
        {
            throw new InvalidOperationException("A signed approval record requires its own clean Sales transaction.");
        }

        var companyId = RequireResolvedCompany();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        try
        {
            var result = await action();
            if (RequireResolvedCompany() != companyId)
            {
                throw new UnauthorizedAccessException();
            }

            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception exception) when (exception is SqlException { Number: 1205 }
            || exception is DbUpdateException { InnerException: SqlException { Number: 1205 } }
            || exception is InvalidOperationException
                { InnerException: DbUpdateException { InnerException: SqlException { Number: 1205 } } })
        {
            // SQL Server has already rolled back a deadlock victim; a second rollback could mask the refusal.
            dbContext.ChangeTracker.Clear();
            throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Sipariş başka bir işlemle değişti; yeniden okuyun.", exception);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    private Task<OrderSignedApprovalAttemptRow?> FindSignedApprovalAttemptAsync(string commandName, string keyHash,
        CancellationToken cancellationToken) =>
        dbContext.OrderSignedApprovalAttempts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == commandName && row.IdempotencyKeyHash == keyHash, cancellationToken);

    private static OrderSignedApprovalAttemptRow NewSignedApprovalAttempt(Guid companyId, string commandName, string keyHash,
        string requestHash, string actorId, int outcome, Guid orderId, string? refusalJson, string correlationId, DateTimeOffset now) => new()
    {
        CompanyId = companyId,
        CommandName = commandName,
        IdempotencyKeyHash = keyHash,
        RequestHash = requestHash,
        ActorId = actorId,
        Outcome = outcome,
        OrderId = orderId,
        RefusalJson = refusalJson,
        CorrelationId = correlationId.Trim(),
        RecordedAtUtc = now,
    };

    private void EnqueueSignedApprovalAudit(SalesDraftWriteEnvelope envelope, Guid companyId, SalesDraftStateAudit audit,
        SalesOrderSignedApproval item, long? versionBefore, string before) =>
        auditEvents.Enqueue(new AuditEventDraft(
            companyId,
            audit.EventType,
            SignedApprovalEntityType,
            item.Id.ToString("D"),
            audit.Operation,
            EntityVersionBefore: versionBefore,
            EntityVersionAfter: item.Version,
            AuditActorTypes.User,
            envelope.ActorId,
            envelope.PermissionCode,
            envelope.RoleCodes,
            before,
            SignedApprovalAuditState(item),
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

    private static void RequireSignedApprovalCommand(string commandName)
    {
        if (!SalesOrderSignedApprovalCommands.IsCommand(commandName))
        {
            throw new ArgumentException("Yalnız imzalı onay kayıt ve pasifleştirme komutları bu deneme kaydını taşır.", nameof(commandName));
        }
    }

    private static void RequireSignedApprovalAudit(SalesDraftStateAudit audit)
    {
        ArgumentNullException.ThrowIfNull(audit);
        if (string.IsNullOrWhiteSpace(audit.EventType) || string.IsNullOrWhiteSpace(audit.Operation) || string.IsNullOrWhiteSpace(audit.Reason))
        {
            throw new ArgumentException("İmzalı onayın audit olayı tür, işlem ve gerekçe ister.", nameof(audit));
        }
    }

    private static void RequireSameSignedApprovalRequest(OrderSignedApprovalAttemptRow attempt, string actorId, string requestHash)
    {
        if (!string.Equals(attempt.ActorId, actorId, StringComparison.Ordinal)
            || !string.Equals(attempt.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw Refused(SalesDraftStoreCodes.SameKeyPayloadMismatch,
                "Aynı Idempotency-Key başka bir istekle geldi; ilk cevap bu isteğe verilmez.");
        }
    }

    private static SalesOrderSignedApprovalReplay ReadSignedApprovalReplay(OrderSignedApprovalAttemptRow attempt)
    {
        if (attempt.Outcome == AttemptOutcomeRecorded && attempt.RefusalJson is null)
        {
            return new SalesOrderSignedApprovalReplay(attempt.OrderId, null, attempt.CorrelationId);
        }

        var refusal = attempt.RefusalJson is null ? null : JsonSerializer.Deserialize<SalesDraftTerminalRefusal>(attempt.RefusalJson, Json);
        if (attempt.Outcome != AttemptOutcomeRefused || refusal is null || refusal.Status is < 400 or > 599
            || string.IsNullOrWhiteSpace(refusal.Code) || refusal.Errors is null)
        {
            throw new InvalidOperationException("İmzalı onay deneme kaydı okunamadı.");
        }

        return new SalesOrderSignedApprovalReplay(null, refusal, attempt.CorrelationId);
    }

    private static SalesOrderSignedApprovalStoreResult ReplaySignedApprovalAttempt(OrderSignedApprovalAttemptRow attempt, string actorId,
        string requestHash)
    {
        RequireSameSignedApprovalRequest(attempt, actorId, requestHash);
        var replay = ReadSignedApprovalReplay(attempt);
        if (replay.OrderId is not { } orderId)
        {
            // ADR-011 §7: the key holds a recorded terminal refusal; the caller replays it through FindSignedApprovalReplayAsync.
            throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Bu anahtar kayıtlı bir terminal reddi taşıyor; tekrar aynı reddi alır.");
        }

        return new SalesOrderSignedApprovalStoreResult(orderId, SalesDraftWriteOutcome.Replayed, attempt.CorrelationId);
    }

    private static string SignedApprovalAuditState(SalesOrderSignedApproval item) => JsonSerializer.Serialize(new
    {
        item.Id,
        item.OrderId,
        item.OrderRevision,
        item.OrderVersion,
        documentId = item.Document.DocumentId,
        documentVersionNo = item.Document.VersionNo,
        documentSha256 = item.Document.Sha256,
        state = item.State.ToString(),
        version = item.Version,
        item.RecordedBy,
        item.RecordedAtUtc,
        deactivationKind = item.Deactivation?.Kind.ToString(),
        deactivatedBy = item.Deactivation?.DeactivatedBy,
        deactivatedAtUtc = item.Deactivation?.DeactivatedAtUtc,
        deactivationReason = item.Deactivation?.Reason,
        supersededById = item.Deactivation?.SupersededById,
    }, Json);
}
