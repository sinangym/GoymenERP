using System.Text.Json;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.CustomerReturns;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// API-40 (SAT-D288…SAT-D294): the customer return half of the Sales store. The attempt rules are the invoice's (ADR-011 §2–§8)
/// in the return's own attempt table: (company, command, key hash) is the identity, the raw key is never stored, the same key,
/// content and actor answers the stored return, another content or actor is one 409, a lost race stores nothing. One save writes
/// the return (or its step), the attempt and the audit event. UX_CustomerReturns_CustomerDocument makes a racing duplicate of the
/// customer's document the save's concurrent change, and UX_CustomerReturns_FinalizationSequence two finalisations computed against
/// the same finalised priors (SAT-D292 (6)). Sales writes only its own tables: no stock movement (SAT-D293 (2)), the shipment id is
/// an opaque reference, and the finance effect goes through the Finance port before this save (SAT-D294).
/// </summary>
public sealed partial class SalesStore : ISalesCustomerReturnStore
{
    private const string CustomerReturnEntityType = "Sales.CustomerReturn";

    public async Task<SalesCustomerReturnReplay?> FindCustomerReturnReplayAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        string commandName, CancellationToken cancellationToken = default)
    {
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireCustomerReturnCommand(commandName);
        RequireResolvedCompany();
        var attempt = await FindCustomerReturnAttemptAsync(commandName, IdempotencyKeyHash(envelope.IdempotencyKey), cancellationToken);
        if (attempt is null)
        {
            return null;
        }

        RequireSameCustomerReturnRequest(attempt, envelope.ActorId.Trim(), requestHash);
        return await ReadCustomerReturnReplayAsync(attempt, cancellationToken);
    }

    public async Task<SalesCustomerReturnStoreResult> StoreCustomerReturnAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        Func<DateTimeOffset, SalesCustomerReturn> build, SalesDraftStateAudit audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(build);
        RequireCustomerReturnAudit(audit);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        var companyId = RequireResolvedCompany();
        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        var actorId = envelope.ActorId.Trim();
        var attempt = await FindCustomerReturnAttemptAsync(SalesCustomerReturnCommands.CreateV1, keyHash, cancellationToken);
        if (attempt is not null)
        {
            return await ReplayCustomerReturnAttemptAsync(attempt, actorId, requestHash, cancellationToken);
        }

        var now = timeProvider.GetUtcNow();
        var item = build(now) ?? throw new ArgumentException("İade faturası kurulmadı.", nameof(build));
        RequireCompany(item.CompanyId);
        if (item.State != SalesCustomerReturnState.Draft || item.Version != 1
            || !string.Equals(item.CreatedBy, actorId, StringComparison.Ordinal))
        {
            throw new ArgumentException("İade faturası taslak olarak, yazma zarfının aktörü adına kaydedilir.", nameof(build));
        }

        dbContext.CustomerReturns.Add(SalesCustomerReturnRows.ToRow(item, envelope.CorrelationId));
        dbContext.CustomerReturnAttempts.Add(NewCustomerReturnAttempt(companyId, SalesCustomerReturnCommands.CreateV1, keyHash, requestHash,
            actorId, AttemptOutcomeRecorded, item.Id, item.Id, null, envelope.CorrelationId, now));
        EnqueueCustomerReturnAudit(envelope, companyId, audit, item, null, "{}");
        await SaveAsync(cancellationToken);
        return new SalesCustomerReturnStoreResult(item, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }

    public async Task RecordCustomerReturnRefusalAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
        Guid customerReturnId, SalesDraftTerminalRefusal refusal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireCustomerReturnCommand(commandName);
        var companyId = RequireResolvedCompany();
        if (refusal.Status is < 400 or > 599 || string.IsNullOrWhiteSpace(refusal.Code) || refusal.Errors is null
            || refusal.Errors.Any(error => error is null || string.IsNullOrWhiteSpace(error.Field) || string.IsNullOrWhiteSpace(error.Code)))
        {
            throw new ArgumentException("Terminal ret bir durum, bir kod ve tam alan hataları taşır.", nameof(refusal));
        }

        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        if (await FindCustomerReturnAttemptAsync(commandName, keyHash, cancellationToken) is not null)
        {
            throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Bu anahtar zaten bir deneme taşıyor; tekrar onu alır.");
        }

        dbContext.CustomerReturnAttempts.Add(NewCustomerReturnAttempt(companyId, commandName, keyHash, requestHash, envelope.ActorId.Trim(),
            AttemptOutcomeRefused, null, customerReturnId == Guid.Empty ? null : customerReturnId, JsonSerializer.Serialize(refusal, Json),
            envelope.CorrelationId, timeProvider.GetUtcNow()));
        await SaveAsync(cancellationToken);
    }

    public async Task<SalesCustomerReturnStoreResult?> DecideCustomerReturnAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        string commandName, Guid customerReturnId, Action<SalesCustomerReturn, DateTimeOffset> mutate, SalesDraftStateAudit audit,
        CancellationToken cancellationToken = default)
    {
        if (commandName is not (SalesCustomerReturnCommands.SubmitV1 or SalesCustomerReturnCommands.ApproveV1))
        {
            throw new ArgumentException("Yalnız gönderme ve onay bu mağaza yöntemini kullanır.", nameof(commandName));
        }

        return await StepCustomerReturnAsync(envelope, requestHash, commandName, customerReturnId, null, mutate, audit, cancellationToken);
    }

    public Task<SalesCustomerReturnStoreResult?> FinalizeCustomerReturnAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        Guid customerReturnId, int finalizedBefore, Action<SalesCustomerReturn, DateTimeOffset> finalize, SalesDraftStateAudit audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(finalizedBefore);
        return StepCustomerReturnAsync(envelope, requestHash, SalesCustomerReturnCommands.FinalizeV1, customerReturnId, finalizedBefore,
            finalize, audit, cancellationToken);
    }

    public async Task<SalesCustomerReturn?> FindCustomerReturnAsync(Guid customerReturnId, CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        var row = await dbContext.CustomerReturns.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == customerReturnId, cancellationToken);
        return row is null ? null : SalesCustomerReturnRows.Restore(row);
    }

    public async Task<IReadOnlyList<SalesCustomerReturn>> ListCustomerReturnsAsync(Guid? originalInvoiceId, int limit,
        CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        if (limit < 1 || limit > SalesCustomerReturnApiV1.MaximumListed)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        var query = dbContext.CustomerReturns.AsNoTracking();
        if (originalInvoiceId is { } invoiceId)
        {
            query = query.Where(row => row.OriginalInvoiceId == invoiceId);
        }

        var rows = await query.OrderByDescending(row => row.CreatedAtUtc).ThenBy(row => row.Id).Take(limit).ToListAsync(cancellationToken);
        return [.. rows.Select(SalesCustomerReturnRows.Restore)];
    }

    public async Task<IReadOnlyList<SalesCustomerReturn>> ListFinalizedCustomerReturnsAsync(Guid originalInvoiceId,
        CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        var rows = await dbContext.CustomerReturns.AsNoTracking()
            .Where(row => row.OriginalInvoiceId == originalInvoiceId && row.FinalizationSequence != null)
            .OrderBy(row => row.FinalizationSequence).ToListAsync(cancellationToken);
        return [.. rows.Select(SalesCustomerReturnRows.Restore)];
    }

    public async Task<bool> CustomerDocumentExistsAsync(Guid partyId, string customerDocumentNo, CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        ArgumentException.ThrowIfNullOrWhiteSpace(customerDocumentNo);
        var documentNo = customerDocumentNo.Trim();
        return await dbContext.CustomerReturns.AsNoTracking()
            .AnyAsync(row => row.PartyId == partyId && row.CustomerDocumentNo == documentNo, cancellationToken);
    }

    /// <summary>
    /// One approval step on the tracked row. A finalisation also takes the return's place among the invoice's finalised returns:
    /// the count of now must be the one the amounts were computed against, and the filtered unique index refuses a racing twin.
    /// </summary>
    private async Task<SalesCustomerReturnStoreResult?> StepCustomerReturnAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        string commandName, Guid customerReturnId, int? finalizedBefore, Action<SalesCustomerReturn, DateTimeOffset> mutate,
        SalesDraftStateAudit audit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        RequireCustomerReturnAudit(audit);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        var companyId = RequireResolvedCompany();
        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        var actorId = envelope.ActorId.Trim();
        var attempt = await FindCustomerReturnAttemptAsync(commandName, keyHash, cancellationToken);
        if (attempt is not null)
        {
            return await ReplayCustomerReturnAttemptAsync(attempt, actorId, requestHash, cancellationToken);
        }

        if (customerReturnId == Guid.Empty)
        {
            return null;
        }

        var row = await dbContext.CustomerReturns.SingleOrDefaultAsync(candidate => candidate.Id == customerReturnId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var item = SalesCustomerReturnRows.Restore(row);
        var before = CustomerReturnAuditState(item);
        var versionBefore = item.Version;
        var stateBefore = item.State;
        var now = timeProvider.GetUtcNow();
        mutate(item, now);
        var expectedState = commandName switch
        {
            SalesCustomerReturnCommands.SubmitV1 => SalesCustomerReturnState.PendingApproval,
            SalesCustomerReturnCommands.ApproveV1 => SalesCustomerReturnState.Approved,
            SalesCustomerReturnCommands.RejectV1 => SalesCustomerReturnState.Rejected,
            SalesCustomerReturnCommands.CancelV1 => SalesCustomerReturnState.Cancelled,
            _ => SalesCustomerReturnState.Finalized,
        };
        var actorFact = expectedState switch
        {
            SalesCustomerReturnState.PendingApproval => item.Facts.SubmittedBy,
            SalesCustomerReturnState.Approved => item.Facts.ApprovedBy,
            SalesCustomerReturnState.Rejected => item.Facts.RejectedBy,
            SalesCustomerReturnState.Cancelled => item.Facts.CancelledBy,
            _ => item.Facts.FinalizedBy,
        };
        if (item.Version != versionBefore + 1 || item.Id != row.Id || item.CompanyId != companyId || item.State != expectedState
            || !SalesCustomerReturnRows.IsStep(stateBefore, item.State) || !string.Equals(actorFact, actorId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("İade adımı tam olarak bir sürüm ve izinli bir durum ilerletir ve aktörün adına atılır.");
        }

        SalesCustomerReturnRows.Apply(row, item);
        if (finalizedBefore is { } count)
        {
            var finalizedNow = await dbContext.CustomerReturns.AsNoTracking()
                .CountAsync(candidate => candidate.OriginalInvoiceId == row.OriginalInvoiceId && candidate.FinalizationSequence != null
                    && candidate.Id != row.Id, cancellationToken);
            if (finalizedNow != count)
            {
                dbContext.ChangeTracker.Clear();
                throw Refused(SalesDraftStoreCodes.ConcurrentChange,
                    "Aynı faturanın başka bir iadesi kesinleşti; tutarlar yeniden hesaplanmalı, hiçbir şey kaydedilmedi.");
            }

            row.FinalizationSequence = checked(count + 1);
        }

        dbContext.CustomerReturnAttempts.Add(NewCustomerReturnAttempt(companyId, commandName, keyHash, requestHash, actorId,
            AttemptOutcomeRecorded, item.Id, item.Id, null, envelope.CorrelationId, now));
        EnqueueCustomerReturnAudit(envelope, companyId, audit, item, versionBefore, before);
        await SaveAsync(cancellationToken);
        return new SalesCustomerReturnStoreResult(item, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }

    private Task<CustomerReturnAttemptRow?> FindCustomerReturnAttemptAsync(string commandName, string keyHash,
        CancellationToken cancellationToken) =>
        dbContext.CustomerReturnAttempts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == commandName && row.IdempotencyKeyHash == keyHash, cancellationToken);

    private static CustomerReturnAttemptRow NewCustomerReturnAttempt(Guid companyId, string commandName, string keyHash, string requestHash,
        string actorId, int outcome, Guid? customerReturnId, Guid? targetId, string? refusalJson, string correlationId, DateTimeOffset now) =>
        new()
        {
            CompanyId = companyId,
            CommandName = commandName,
            IdempotencyKeyHash = keyHash,
            RequestHash = requestHash,
            ActorId = actorId,
            Outcome = outcome,
            CustomerReturnId = customerReturnId,
            TargetId = targetId,
            RefusalJson = refusalJson,
            CorrelationId = correlationId.Trim(),
            RecordedAtUtc = now,
        };

    private void EnqueueCustomerReturnAudit(SalesDraftWriteEnvelope envelope, Guid companyId, SalesDraftStateAudit audit,
        SalesCustomerReturn item, long? versionBefore, string before) =>
        auditEvents.Enqueue(new AuditEventDraft(
            companyId,
            audit.EventType,
            CustomerReturnEntityType,
            item.Id.ToString("D"),
            audit.Operation,
            EntityVersionBefore: versionBefore,
            EntityVersionAfter: item.Version,
            AuditActorTypes.User,
            envelope.ActorId,
            envelope.PermissionCode,
            envelope.RoleCodes,
            before,
            CustomerReturnAuditState(item),
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

    private static void RequireCustomerReturnCommand(string commandName)
    {
        if (!SalesCustomerReturnCommands.IsCustomerReturnCommand(commandName))
        {
            throw new ArgumentException("Yalnız iade faturası komutları bu deneme kaydını taşır.", nameof(commandName));
        }
    }

    private static void RequireCustomerReturnAudit(SalesDraftStateAudit audit)
    {
        ArgumentNullException.ThrowIfNull(audit);
        if (string.IsNullOrWhiteSpace(audit.EventType) || string.IsNullOrWhiteSpace(audit.Operation) || string.IsNullOrWhiteSpace(audit.Reason))
        {
            throw new ArgumentException("İade faturasının audit olayı tür, işlem ve gerekçe ister.", nameof(audit));
        }
    }

    private static void RequireSameCustomerReturnRequest(CustomerReturnAttemptRow attempt, string actorId, string requestHash)
    {
        if (!string.Equals(attempt.ActorId, actorId, StringComparison.Ordinal)
            || !string.Equals(attempt.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw Refused(SalesDraftStoreCodes.SameKeyPayloadMismatch,
                "Aynı Idempotency-Key başka bir istekle geldi; ilk cevap bu isteğe verilmez.");
        }
    }

    private async Task<SalesCustomerReturnReplay> ReadCustomerReturnReplayAsync(CustomerReturnAttemptRow attempt,
        CancellationToken cancellationToken)
    {
        if (attempt.Outcome == AttemptOutcomeRecorded && attempt.CustomerReturnId is { } customerReturnId)
        {
            var row = await dbContext.CustomerReturns.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == customerReturnId, cancellationToken)
                ?? throw new InvalidOperationException("Kayıtlı deneme kaydının iade faturası bulunamadı.");
            return new SalesCustomerReturnReplay(SalesCustomerReturnRows.Restore(row), null, attempt.CorrelationId);
        }

        var refusal = attempt.RefusalJson is null ? null : JsonSerializer.Deserialize<SalesDraftTerminalRefusal>(attempt.RefusalJson, Json);
        if (attempt.Outcome != AttemptOutcomeRefused || refusal is null || refusal.Status is < 400 or > 599
            || string.IsNullOrWhiteSpace(refusal.Code) || refusal.Errors is null)
        {
            throw new InvalidOperationException("İade faturası deneme kaydı okunamadı.");
        }

        return new SalesCustomerReturnReplay(null, refusal, attempt.CorrelationId);
    }

    private async Task<SalesCustomerReturnStoreResult> ReplayCustomerReturnAttemptAsync(CustomerReturnAttemptRow attempt, string actorId,
        string requestHash, CancellationToken cancellationToken)
    {
        RequireSameCustomerReturnRequest(attempt, actorId, requestHash);
        var replay = await ReadCustomerReturnReplayAsync(attempt, cancellationToken);
        if (replay.CustomerReturn is null)
        {
            // ADR-011 §7: the key holds a recorded terminal refusal; the caller replays it through FindCustomerReturnReplayAsync.
            throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Bu anahtar kayıtlı bir terminal reddi taşıyor; tekrar aynı reddi alır.");
        }

        return new SalesCustomerReturnStoreResult(replay.CustomerReturn, SalesDraftWriteOutcome.Replayed, attempt.CorrelationId);
    }

    /// <summary>Who prepared, submitted, approved and finalised, the links, the customer's document and the totals.</summary>
    private static string CustomerReturnAuditState(SalesCustomerReturn item) => JsonSerializer.Serialize(new
    {
        item.Id,
        item.Header.OriginalInvoiceId,
        item.Header.OriginalDocumentNo,
        item.ShipmentId,
        item.CustomerDocumentNo,
        item.CustomerDocumentDate,
        state = item.State.ToString(),
        version = item.Version,
        item.CreatedBy,
        item.Facts.SubmittedBy,
        item.Facts.ApprovedBy,
        item.Facts.FinalizedBy,
        item.Facts.RejectedBy,
        item.Facts.RejectionReason,
        item.Facts.CancelledBy,
        item.Facts.CancellationReason,
        lineCount = item.Lines.Count,
        lineTotalAmount = item.Totals.LineTotal.Amount,
        currencyId = item.Header.CurrencyId,
        item.NeedsReview,
    }, Json);
}
