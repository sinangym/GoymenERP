using System.Text.Json;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Invoices;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// S5 API-36 (Fatura çekirdeği 4): finalisation and the Finance hand-off ledger. The finalisation is ONE save: the invoice (state and
/// version), its finalisation row (the ledger starts Waiting, the instalment plan with its generated ids), the outbox event, the
/// attempt and the audit. The hand-off answer is a second, separate save of the ledger only (no version step), so Finance's answer is
/// never lost or invented (SAT-D208).
/// </summary>
public sealed partial class SalesStore
{
    internal const string InvoiceFinalizedEventType = "sales.invoice.finalized.v1";
    private const string InvoiceOutboxAggregateType = "Sales.Invoice";
    private const int InvoiceFinalizedSchemaVersion = 1;

    public async Task<SalesInvoiceStoreResult?> FinalizeInvoiceAsync(SalesDraftWriteEnvelope envelope, string requestHash, Guid invoiceId,
        Action<SalesInvoice, DateTimeOffset> finalize, SalesDraftStateAudit audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(finalize);
        ArgumentNullException.ThrowIfNull(audit);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireAudit(audit);
        var companyId = RequireResolvedCompany();
        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        var actorId = envelope.ActorId.Trim();
        var attempt = await dbContext.InvoiceAttempts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == SalesInvoiceCommands.FinalizeV1 && row.IdempotencyKeyHash == keyHash,
                cancellationToken);
        if (attempt is not null)
        {
            return await ReplayInvoiceAttemptAsync(attempt, actorId, requestHash, cancellationToken);
        }

        if (invoiceId == Guid.Empty)
        {
            return null;
        }

        var row = await dbContext.Invoices.SingleOrDefaultAsync(candidate => candidate.Id == invoiceId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var lineRows = await dbContext.InvoiceLines.Where(line => line.InvoiceId == invoiceId).ToListAsync(cancellationToken);
        var invoice = SalesInvoiceRows.Restore(row, lineRows);
        var match = await dbContext.InvoiceMatches.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.InvoiceId == invoiceId, cancellationToken);
        if (match is not null)
        {
            invoice.RestoreMatch(SalesInvoiceRows.ToMatchFacts(match));
        }

        var before = InvoiceAuditState(invoice);
        var versionBefore = invoice.Version;
        var now = timeProvider.GetUtcNow();
        finalize(invoice, now);
        if (invoice.Version != versionBefore + 1 || invoice.Id != row.Id || invoice.CompanyId != companyId
            || invoice.State != SalesInvoiceState.Finalized || invoice.Finalization is not { } finalization
            || finalization.FinalizedVersion != invoice.Version || finalization.AttemptCount != 0
            || finalization.FinanceStatus != SalesInvoiceFinanceStatus.Waiting
            || !string.Equals(finalization.FinalizedBy, actorId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Fatura kesinleştirme tam olarak bir sürüm ilerletir, bekleyen bir defter açar ve aktörün adınadır.");
        }

        SalesInvoiceRows.ApplyDecision(row, invoice);
        dbContext.InvoiceFinalizations.Add(SalesInvoiceRows.ToFinalizationRow(companyId, invoiceId, finalization, envelope.CorrelationId));
        dbContext.OutboxMessages.Add(new SalesOutboxRow
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            EventType = InvoiceFinalizedEventType,
            SchemaVersion = InvoiceFinalizedSchemaVersion,
            SourceOperation = audit.Operation,
            AggregateType = InvoiceOutboxAggregateType,
            AggregateId = invoiceId,
            AggregateVersion = invoice.Version,
            Sequence = 1,
            PayloadJson = FinalizedPayload(invoice, finalization),
            OccurredAtUtc = now,
            CorrelationId = envelope.CorrelationId.Trim(),
            CausationId = envelope.CausationId?.Trim(),
            RecordedAtUtc = now,
        });
        dbContext.InvoiceAttempts.Add(AttemptRow(companyId, SalesInvoiceCommands.FinalizeV1, keyHash, requestHash, actorId, invoiceId,
            envelope.CorrelationId, now));
        auditEvents.Enqueue(Audit(companyId, envelope, audit, invoiceId, versionBefore, invoice.Version, before, InvoiceAuditState(invoice)));
        await SaveAsync(cancellationToken);
        return new SalesInvoiceStoreResult(invoice, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }

    public async Task<SalesInvoiceStoreResult?> RecordFinanceAttemptAsync(SalesDraftWriteEnvelope envelope, string? requestHash,
        string? commandName, Guid invoiceId, Action<SalesInvoice, DateTimeOffset> record, SalesDraftStateAudit audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(audit);
        RequireEnvelope(envelope);
        RequireAudit(audit);
        if ((commandName is null) != (requestHash is null) || (commandName is not null && commandName != SalesInvoiceCommands.ResendFinanceV1))
        {
            throw new ArgumentException("Yalnız Finans yeniden gönderimi bir deneme kaydı taşır; ilk gönderim taşımaz.", nameof(commandName));
        }

        if (requestHash is not null)
        {
            RequireHash(requestHash);
        }

        var companyId = RequireResolvedCompany();
        var actorId = envelope.ActorId.Trim();
        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        if (commandName is not null)
        {
            var attempt = await dbContext.InvoiceAttempts.AsNoTracking()
                .SingleOrDefaultAsync(row => row.CommandName == commandName && row.IdempotencyKeyHash == keyHash, cancellationToken);
            if (attempt is not null)
            {
                return await ReplayInvoiceAttemptAsync(attempt, actorId, requestHash!, cancellationToken);
            }
        }

        if (invoiceId == Guid.Empty)
        {
            return null;
        }

        var invoice = await FindInvoiceAsync(invoiceId, cancellationToken);
        if (invoice is null)
        {
            return null;
        }

        var ledger = await dbContext.InvoiceFinalizations.SingleOrDefaultAsync(candidate => candidate.InvoiceId == invoiceId, cancellationToken);
        var before = InvoiceAuditState(invoice);
        var version = invoice.Version;
        var now = timeProvider.GetUtcNow();
        record(invoice, now);
        if (ledger is null || invoice.Version != version || invoice.Id != invoiceId || invoice.CompanyId != companyId
            || invoice.Finalization is not { } finalization || finalization.AttemptCount != ledger.AttemptCount + 1)
        {
            throw new InvalidOperationException("Finans gönderim kaydı faturayı değiştirmez ve deneme sayısını tam bir artırır.");
        }

        SalesInvoiceRows.ApplyLedger(ledger, finalization);
        if (commandName is not null)
        {
            dbContext.InvoiceAttempts.Add(AttemptRow(companyId, commandName, keyHash, requestHash!, actorId, invoiceId, envelope.CorrelationId, now));
        }

        auditEvents.Enqueue(Audit(companyId, envelope, audit, invoiceId, version, version, before, InvoiceAuditState(invoice)));
        await SaveAsync(cancellationToken);
        return new SalesInvoiceStoreResult(invoice, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }

    private static void RequireAudit(SalesDraftStateAudit audit)
    {
        if (string.IsNullOrWhiteSpace(audit.EventType) || string.IsNullOrWhiteSpace(audit.Operation) || string.IsNullOrWhiteSpace(audit.Reason))
        {
            throw new ArgumentException("Fatura kesinleştirmesinin audit olayı tür, işlem ve gerekçe ister.", nameof(audit));
        }
    }

    private static InvoiceAttemptRow AttemptRow(Guid companyId, string commandName, string keyHash, string requestHash, string actorId,
        Guid invoiceId, string correlationId, DateTimeOffset now) => new()
        {
            CompanyId = companyId,
            CommandName = commandName,
            IdempotencyKeyHash = keyHash,
            RequestHash = requestHash,
            ActorId = actorId,
            Outcome = AttemptOutcomeRecorded,
            InvoiceId = invoiceId,
            CorrelationId = correlationId.Trim(),
            RecordedAtUtc = now,
        };

    private static AuditEventDraft Audit(Guid companyId, SalesDraftWriteEnvelope envelope, SalesDraftStateAudit audit, Guid invoiceId,
        long versionBefore, long versionAfter, string before, string after) => new(
            companyId,
            audit.EventType,
            InvoiceEntityType,
            invoiceId.ToString("D"),
            audit.Operation,
            EntityVersionBefore: versionBefore,
            EntityVersionAfter: versionAfter,
            AuditActorTypes.User,
            envelope.ActorId,
            envelope.PermissionCode,
            envelope.RoleCodes,
            before,
            after,
            audit.Reason,
            ApprovalContextJson: null,
            AuditAiInvolvement.None,
            AiContextJson: null,
            envelope.SourceChannel,
            envelope.ClientIpAddress,
            envelope.UserAgent,
            envelope.CorrelationId,
            envelope.CausationId,
            envelope.IdempotencyKey);

    private static string FinalizedPayload(SalesInvoice invoice, SalesInvoiceFinalization finalization) => JsonSerializer.Serialize(new
    {
        invoiceId = invoice.Id,
        invoice.DocumentNo,
        version = invoice.Version,
        finalization.AdmissionId,
        invoice.Header.PartyId,
        invoice.Header.CompanyAccountId,
        invoice.Header.CurrencyId,
        rate = invoice.Header.Rate?.Rate,
        payable = invoice.Totals.Payable.Amount,
        functionalPayable = invoice.FunctionalTotals?.Payable.Amount,
        match = invoice.Match is null ? null : new { invoice.Match.OfficialNo, invoice.Match.Ettn },
        instalments = finalization.Plan.Select(item => new
        {
            item.OpenItemId,
            item.InstallmentId,
            item.SequenceNo,
            item.DueDate,
            item.Amount,
            item.FunctionalAmount,
        }).ToArray(),
    }, Json);
}
