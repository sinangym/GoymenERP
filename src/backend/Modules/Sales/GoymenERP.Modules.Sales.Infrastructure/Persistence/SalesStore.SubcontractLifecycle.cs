using System.Text.Json;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Invoices;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>SC34: one save owns header/version, append-only lifecycle, official claim, outbox, attempt and audit.</summary>
public sealed partial class SalesStore : ISalesSubcontractInvoiceLifecycleStoreV1
{
    public async Task<SalesSubcontractInvoiceReplayV1?> FindLifecycleReplayAsync(SalesDraftWriteEnvelope envelope,
        string requestHash, string commandName, CancellationToken cancellationToken = default)
    {
        RequireSubcontractCommand(envelope, requestHash, commandName);
        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        var attempt = await dbContext.SubcontractInvoiceCommandAttempts.AsNoTracking()
            .SingleOrDefaultAsync(item => item.CommandName == commandName && item.IdempotencyKeyHash == keyHash, cancellationToken);
        if (attempt is null) return null;
        if (attempt.ActorId != envelope.ActorId.Trim() || attempt.RequestHash != requestHash)
            throw Refused(SalesDraftStoreCodes.SameKeyPayloadMismatch, "Aynı anahtarın aktörü veya içeriği değiştirilemez.");
        if (attempt.RefusalStatus is { } status)
        {
            var errors = JsonSerializer.Deserialize<IReadOnlyList<SalesDraftFieldError>>(attempt.RefusalErrorsJson!, Json)
                ?? throw new InvalidOperationException("Fason fatura ret kaydı okunamadı.");
            return new(null, new(status, attempt.RefusalCode!, errors), attempt.CorrelationId);
        }
        // A legacy attempt without an exact version cannot be reconstructed from the latest header.
        // Keep its historical facts; refuse to invent a response or backfill a version from today's state.
        if (attempt.LifecycleSequence is not { } outcomeSequence || outcomeSequence < 1)
            throw new InvalidOperationException("Fason fatura komutunun ilk cevap sürümü kayıtlı değil.");
        var invoice = await RestoreSubcontractServiceInvoiceOutcomeAsync(attempt.InvoiceId!.Value, outcomeSequence, cancellationToken);
        return new(invoice, null, attempt.CorrelationId);
    }

    public async Task<SalesSubcontractInvoiceReplayV1> RecordLifecycleRefusalAsync(SalesDraftWriteEnvelope envelope,
        string requestHash, string commandName, SalesDraftTerminalRefusal refusal, CancellationToken cancellationToken = default)
    {
        RequireSubcontractCommand(envelope, requestHash, commandName);
        if (refusal.Status is not (400 or 403 or 404 or 409 or 422) || string.IsNullOrWhiteSpace(refusal.Code) || refusal.Errors is null)
            throw new ArgumentException("Kalıcı ret kaydı geçersiz.", nameof(refusal));
        var replay = await FindLifecycleReplayAsync(envelope, requestHash, commandName, cancellationToken);
        if (replay is not null) return replay;
        var attempt = SubcontractCommandAttempt(envelope, requestHash, commandName, null, timeProvider.GetUtcNow());
        attempt.RefusalStatus = refusal.Status;
        attempt.RefusalCode = refusal.Code;
        attempt.RefusalErrorsJson = JsonSerializer.Serialize(refusal.Errors, Json);
        dbContext.SubcontractInvoiceCommandAttempts.Add(attempt);
        await SaveAsync(cancellationToken);
        return new(null, refusal, envelope.CorrelationId.Trim());
    }

    public Task<Guid?> FindOfficialClaimAsync(string officialNo, string ettn, CancellationToken cancellationToken = default) =>
        FindMatchedInvoiceIdAsync(officialNo, ettn, cancellationToken);

    public Task<SalesSubcontractServiceInvoiceStoreResult?> UpdateLifecycleAsync(SalesDraftWriteEnvelope envelope,
        string requestHash, string commandName, Guid invoiceId, Action<SalesSubcontractServiceInvoice, DateTimeOffset> mutate,
        SalesDraftStateAudit audit, CancellationToken cancellationToken = default) =>
        UpdateLifecycleTransactionAsync(envelope, requestHash, commandName, invoiceId, mutate, audit, null, cancellationToken);

    public Task<SalesSubcontractServiceInvoiceStoreResult?> UpdateLifecycleWithFenceAsync(SalesDraftWriteEnvelope envelope,
        string requestHash, string commandName, Guid invoiceId, Action<SalesSubcontractServiceInvoice, DateTimeOffset> mutate,
        SalesDraftStateAudit audit, Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshFence);
        if (System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("A guarded subcontract invoice command cannot join an ambient transaction.");
        return UpdateLifecycleTransactionAsync(envelope, requestHash, commandName, invoiceId, mutate, audit, freshFence, cancellationToken);
    }

    public Task<SalesSubcontractInvoiceReplayV1> RecordLifecycleRefusalWithFenceAsync(SalesDraftWriteEnvelope envelope,
        string requestHash, string commandName, SalesDraftTerminalRefusal refusal, Func<CancellationToken, Task> freshFence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshFence);
        if (System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("A guarded subcontract invoice command cannot join an ambient transaction.");
        return InSubcontractTransactionAsync(async () =>
        {
            await freshFence(cancellationToken);
            var answer = await RecordLifecycleRefusalAsync(envelope, requestHash, commandName, refusal, cancellationToken);
            await freshFence(cancellationToken);
            return answer;
        }, cancellationToken);
    }

    private async Task<SalesSubcontractServiceInvoiceStoreResult?> UpdateLifecycleTransactionAsync(SalesDraftWriteEnvelope envelope,
        string requestHash, string commandName, Guid invoiceId, Action<SalesSubcontractServiceInvoice, DateTimeOffset> mutate,
        SalesDraftStateAudit audit, Func<CancellationToken, Task>? freshFence, CancellationToken cancellationToken)
    {
        RequireSubcontractCommand(envelope, requestHash, commandName);
        // Locate the immutable key without taking a header lock first. Both Logistics correction and
        // Sales mutation then use the same claim-range -> owner weighing -> invoice-header lock order.
        if (freshFence is not null) await freshFence(cancellationToken);
        var key = await dbContext.SubcontractServiceInvoices.AsNoTracking().Where(row => row.Id == invoiceId)
            .Select(row => new { row.LoadingInstructionId, row.WeighingId }).SingleOrDefaultAsync(cancellationToken);
        if (key is null) return null;
        return await InSubcontractTransactionAsync(async () =>
        {
            if (freshFence is not null) await freshFence(cancellationToken);
            await SalesSubcontractInvoiceClaimReadSession.ReadRangeAsync(dbContext, RequireResolvedCompany(),
                key.LoadingInstructionId, key.WeighingId, cancellationToken);
            var answer = await UpdateLifecycleCoreAsync(envelope, requestHash, commandName, invoiceId, mutate, audit, cancellationToken);
            // The save (including attempt, audit and lifecycle) is still uncommitted here.
            // This is the same owner-transaction fence pattern as OfficialTransaction.
            if (freshFence is not null) await freshFence(cancellationToken);
            return answer;
        }, cancellationToken);
    }

    private async Task<SalesSubcontractServiceInvoiceStoreResult?> UpdateLifecycleCoreAsync(SalesDraftWriteEnvelope envelope,
        string requestHash, string commandName, Guid invoiceId, Action<SalesSubcontractServiceInvoice, DateTimeOffset> mutate,
        SalesDraftStateAudit audit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        RequireAudit(audit);
        RequireSubcontractCommand(envelope, requestHash, commandName);
        var replay = await FindLifecycleReplayAsync(envelope, requestHash, commandName, cancellationToken);
        if (replay is not null)
        {
            if (replay.Invoice is null) throw new InvalidOperationException("Kalıcı ret, komut servisinde yeniden yanıtlanır.");
            return new(replay.Invoice, SalesDraftWriteOutcome.Replayed, replay.OriginalCorrelationId);
        }
        var companyId = RequireResolvedCompany();
        var row = await dbContext.SubcontractServiceInvoices.SingleOrDefaultAsync(item => item.Id == invoiceId, cancellationToken);
        if (row is null) return null;
        var sequence = await dbContext.SubcontractInvoiceLifecycles.Where(item => item.InvoiceId == invoiceId)
            .Select(item => (long?)item.Sequence).MaxAsync(cancellationToken) ?? 0;
        // Read sequence before the facts. A concurrent ledger append then collides at sequence+1;
        // reading MAX after restoring facts could otherwise overwrite a newer attempt with stale facts.
        var invoice = await RestoreSubcontractServiceInvoiceAsync(row, cancellationToken);
        var before = JsonSerializer.Serialize(invoice.LifecycleFacts, Json);
        var version = invoice.Version;
        var oldFinalization = invoice.Finalization;
        var now = timeProvider.GetUtcNow();
        mutate(invoice, now);
        var isFinanceAttempt = commandName == SalesSubcontractInvoiceLifecycleCommandsV1.ResendFinance;
        if (invoice.Id != row.Id || invoice.CompanyId != companyId || invoice.Version != version + (isFinanceAttempt ? 0 : 1)
            || (isFinanceAttempt && (oldFinalization is null || invoice.Finalization is null
                || invoice.Finalization.AttemptCount != oldFinalization.AttemptCount + 1)))
            throw new InvalidOperationException("Fason fatura komutu tek geçiş veya tek Finans denemesi kaydeder.");
        row.State = (int)invoice.State;
        row.Version = invoice.Version;
        AppendSubcontractLifecycle(invoice, checked(sequence + 1), envelope.CorrelationId, now);
        if (commandName == SalesSubcontractInvoiceLifecycleCommandsV1.Match)
        {
            var match = invoice.Match ?? throw new InvalidOperationException("Resmî eşleştirme kanıtı eksik.");
            if (match.MatchedBy != envelope.ActorId.Trim() || match.MatchedAtUtc != now)
                throw new InvalidOperationException("Eşleştirme komut aktörü adına kaydedilir.");
            dbContext.OfficialInvoiceClaims.Add(new OfficialInvoiceClaimRow
            {
                CompanyId = companyId, InvoiceId = invoice.Id, SourceKind = "subcontract",
                OfficialInvoiceNo = match.OfficialNo, Ettn = match.Ettn,
            });
        }
        if (commandName == SalesSubcontractInvoiceLifecycleCommandsV1.Finalize)
        {
            var final = invoice.Finalization ?? throw new InvalidOperationException("Kesinleştirme kanıtı eksik.");
            if (oldFinalization is not null || final.FinalizedVersion != invoice.Version || final.FinalizedBy != envelope.ActorId.Trim()
                || final.FinanceStatus != SalesInvoiceFinanceStatus.Waiting || final.AttemptCount != 0)
                throw new InvalidOperationException("Kesinleştirme tek bekleyen finans kaydı üretir.");
            dbContext.OutboxMessages.Add(new SalesOutboxRow
            {
                Id = Guid.NewGuid(), CompanyId = companyId, EventType = InvoiceFinalizedEventType,
                SchemaVersion = InvoiceFinalizedSchemaVersion, SourceOperation = audit.Operation,
                AggregateType = SubcontractServiceInvoiceEntityType, AggregateId = invoice.Id,
                AggregateVersion = invoice.Version, Sequence = 1, PayloadJson = FinalizedSubcontractPayload(invoice, final),
                OccurredAtUtc = now, RecordedAtUtc = now, CorrelationId = envelope.CorrelationId.Trim(),
                CausationId = envelope.CausationId?.Trim(),
            });
        }
        var acceptedAttempt = SubcontractCommandAttempt(envelope, requestHash, commandName, invoice.Id, now);
        acceptedAttempt.LifecycleSequence = checked(sequence + 1);
        dbContext.SubcontractInvoiceCommandAttempts.Add(acceptedAttempt);
        auditEvents.Enqueue(new AuditEventDraft(companyId, audit.EventType, SubcontractServiceInvoiceEntityType,
            invoice.Id.ToString("D"), audit.Operation, version, invoice.Version, AuditActorTypes.User,
            envelope.ActorId, envelope.PermissionCode, envelope.RoleCodes, before, JsonSerializer.Serialize(invoice.LifecycleFacts, Json),
            audit.Reason, null, AuditAiInvolvement.None, null, envelope.SourceChannel, envelope.ClientIpAddress,
            envelope.UserAgent, envelope.CorrelationId, envelope.CausationId, envelope.IdempotencyKey));
        await SaveAsync(cancellationToken);
        return new(invoice, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }

    private void RequireSubcontractCommand(SalesDraftWriteEnvelope envelope, string hash, string command)
    {
        RequireEnvelope(envelope);
        RequireHash(hash);
        RequireResolvedCompany();
        if (!SalesSubcontractInvoiceLifecycleCommandsV1.IsKnown(command)) throw new ArgumentException("Fason fatura komutu bilinmiyor.", nameof(command));
    }

    private SubcontractInvoiceCommandAttemptRow SubcontractCommandAttempt(SalesDraftWriteEnvelope envelope, string hash,
        string command, Guid? invoiceId, DateTimeOffset now) => new()
    {
        CompanyId = RequireResolvedCompany(), CommandName = command, IdempotencyKeyHash = IdempotencyKeyHash(envelope.IdempotencyKey),
        RequestHash = hash, ActorId = envelope.ActorId.Trim(), InvoiceId = invoiceId,
        CorrelationId = envelope.CorrelationId.Trim(), RecordedAtUtc = now,
    };

    private void AppendSubcontractLifecycle(SalesSubcontractServiceInvoice invoice, long sequence, string correlationId, DateTimeOffset now) =>
        dbContext.SubcontractInvoiceLifecycles.Add(new SubcontractInvoiceLifecycleRow
        {
            CompanyId = invoice.CompanyId, InvoiceId = invoice.Id, Sequence = sequence, InvoiceVersion = invoice.Version,
            State = (int)invoice.State, FactsJson = JsonSerializer.Serialize(invoice.LifecycleFacts, Json),
            CorrelationId = correlationId.Trim(), RecordedAtUtc = now,
        });
}
