using System.Text.Json;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Invoices;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// F2 (SATIS_RISK_SEVK_KAPISI_VE_FASON_FATURA_PLANI_v1 §F2): the subcontract service invoice half of the Sales store. The attempt
/// rules are the invoice's (ADR-011 §2–§8) in the subcontract invoice's own attempt table: (company, command, key hash) is the
/// identity, the raw key is never stored, the same key, content and actor answers the stored invoice, another content or actor is
/// one 409, a lost race stores nothing. One save writes the invoice, its service lines, the attempt, the number counter and the audit
/// event; the unique index UX_SubcontractServiceInvoices_Weighing makes a second invoice of one weighing for one order the save's
/// concurrent change (LOJ-D12: a correction after invoicing is a difference invoice, never a second invoice).
/// </summary>
public sealed partial class SalesStore : ISalesSubcontractServiceInvoiceStore
{
    private const string SubcontractServiceInvoiceEntityType = "Sales.SubcontractServiceInvoice";

    /// <summary>The display-number series of a subcontract service invoice draft: technical text, never the official number (Ö8).</summary>
    internal const string SubcontractServiceInvoiceSeries = "SFH";

    public async Task<SalesSubcontractServiceInvoiceStoreResult> StoreSubcontractServiceInvoiceAsync(SalesDraftWriteEnvelope envelope,
        string requestHash, Func<string, SalesSubcontractServiceInvoice> build, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(build);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        var companyId = RequireResolvedCompany();
        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        var actorId = envelope.ActorId.Trim();
        var attempt = await dbContext.SubcontractServiceInvoiceAttempts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == SalesSubcontractServiceInvoiceCommands.CreateV1
                && row.IdempotencyKeyHash == keyHash, cancellationToken);
        if (attempt is not null)
        {
            return await ReplaySubcontractServiceInvoiceAttemptAsync(attempt, actorId, requestHash, cancellationToken);
        }

        var now = timeProvider.GetUtcNow();
        var number = await NextDocumentNumberAsync(companyId, SubcontractServiceInvoiceSeries, now, cancellationToken);
        var invoice = build(SeriesDocumentNo(SubcontractServiceInvoiceSeries, now, number))
            ?? throw new ArgumentException("Fason hizmet faturası kurulmadı.", nameof(build));
        RequireCompany(invoice.CompanyId);
        if (!string.Equals(invoice.CreatedBy, actorId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Faturayı hazırlayan, yazma zarfının aktörüdür.", nameof(build));
        }

        SalesSubcontractServiceInvoiceRows.Add(dbContext, invoice, envelope.CorrelationId);
        AppendSubcontractLifecycle(invoice, 1, envelope.CorrelationId, now);
        dbContext.SubcontractServiceInvoiceAttempts.Add(new SubcontractServiceInvoiceAttemptRow
        {
            CompanyId = companyId,
            CommandName = SalesSubcontractServiceInvoiceCommands.CreateV1,
            IdempotencyKeyHash = keyHash,
            RequestHash = requestHash,
            ActorId = actorId,
            InvoiceId = invoice.Id,
            CorrelationId = envelope.CorrelationId.Trim(),
            RecordedAtUtc = now,
        });
        auditEvents.Enqueue(new AuditEventDraft(
            companyId,
            "sales.subcontract-service-invoice.created.v1",
            SubcontractServiceInvoiceEntityType,
            invoice.Id.ToString("D"),
            "Create",
            EntityVersionBefore: null,
            EntityVersionAfter: invoice.Version,
            AuditActorTypes.User,
            envelope.ActorId,
            envelope.PermissionCode,
            envelope.RoleCodes,
            "{}",
            SubcontractServiceInvoiceAuditState(invoice),
            "Fason hizmet faturası taslağı teslim tartısının net kg'ı ile hazırlandı",
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
        return new SalesSubcontractServiceInvoiceStoreResult(invoice, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }

    public async Task<SalesSubcontractServiceInvoiceStoreResult?> FindSubcontractServiceInvoiceReplayAsync(SalesDraftWriteEnvelope envelope,
        string requestHash, CancellationToken cancellationToken = default)
    {
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireResolvedCompany();
        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        var attempt = await dbContext.SubcontractServiceInvoiceAttempts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == SalesSubcontractServiceInvoiceCommands.CreateV1
                && row.IdempotencyKeyHash == keyHash, cancellationToken);
        return attempt is null
            ? null
            : await ReplaySubcontractServiceInvoiceAttemptAsync(attempt, envelope.ActorId.Trim(), requestHash, cancellationToken);
    }

    public async Task<SalesSubcontractServiceInvoice?> FindSubcontractServiceInvoiceAsync(Guid invoiceId,
        CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        var row = await dbContext.SubcontractServiceInvoices.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == invoiceId, cancellationToken);
        return row is null ? null : await RestoreSubcontractServiceInvoiceAsync(row, cancellationToken);
    }

    public async Task<SalesSubcontractServiceInvoice?> FindSubcontractServiceInvoiceByWeighingAsync(Guid salesOrderId,
        Guid loadingInstructionId, Guid weighingId, CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        var row = await dbContext.SubcontractServiceInvoices.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.SalesOrderId == salesOrderId
                && candidate.LoadingInstructionId == loadingInstructionId
                && candidate.WeighingId == weighingId && (candidate.State == 1 || candidate.State == 2
                    || candidate.State == 3 || candidate.State == 6), cancellationToken);
        return row is null ? null : await RestoreSubcontractServiceInvoiceAsync(row, cancellationToken);
    }

    private async Task<SalesSubcontractServiceInvoice> RestoreSubcontractServiceInvoiceAsync(SubcontractServiceInvoiceRow row,
        CancellationToken cancellationToken)
    {
        var lines = await dbContext.SubcontractServiceInvoiceLines.AsNoTracking()
            .Where(line => line.InvoiceId == row.Id)
            .ToListAsync(cancellationToken);
        var lifecycle = await dbContext.SubcontractInvoiceLifecycles.AsNoTracking()
            .Where(item => item.InvoiceId == row.Id).OrderByDescending(item => item.Sequence).FirstOrDefaultAsync(cancellationToken);
        if (lifecycle is not null && (lifecycle.InvoiceVersion != row.Version || lifecycle.State != row.State))
            throw new InvalidOperationException("Fason faturanın yaşam döngüsü sürümü başlıkla uyuşmuyor.");
        return SalesSubcontractServiceInvoiceRows.Restore(row, lines, lifecycle is null ? null
            : JsonSerializer.Deserialize<SalesSubcontractInvoiceLifecycleFacts>(lifecycle.FactsJson, Json)
                ?? throw new InvalidOperationException("Fason fatura yaşam döngüsü okunamadı."));
    }

    private async Task<SalesSubcontractServiceInvoiceStoreResult> ReplaySubcontractServiceInvoiceAttemptAsync(
        SubcontractServiceInvoiceAttemptRow attempt, string actorId, string requestHash, CancellationToken cancellationToken)
    {
        if (!string.Equals(attempt.ActorId, actorId, StringComparison.Ordinal)
            || !string.Equals(attempt.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw Refused(SalesDraftStoreCodes.SameKeyPayloadMismatch,
                "Aynı Idempotency-Key başka bir istekle geldi; ilk cevap bu isteğe verilmez.");
        }

        var row = await dbContext.SubcontractServiceInvoices.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == attempt.InvoiceId, cancellationToken)
            ?? throw new InvalidOperationException("Fason hizmet faturası denemesinin faturası okunamadı.");
        var invoice = await RestoreSubcontractServiceInvoiceAsync(row, cancellationToken);
        return new SalesSubcontractServiceInvoiceStoreResult(invoice, SalesDraftWriteOutcome.Replayed, attempt.CorrelationId);
    }

    private static string SubcontractServiceInvoiceAuditState(SalesSubcontractServiceInvoice invoice) => JsonSerializer.Serialize(new
    {
        invoice.Id,
        invoice.DocumentNo,
        state = invoice.State.ToString(),
        version = invoice.Version,
        invoice.Header.PartyId,
        invoice.Header.CompanyAccountId,
        invoice.Header.CurrencyId,
        invoice.Header.BusinessDate,
        invoice.Source.SalesOrderId,
        invoice.Source.LoadingInstructionId,
        invoice.Source.WeighingId,
        invoice.Source.WeighingRevision,
        invoice.Source.InstructionVersion,
        netKg = invoice.Source.NetKgText,
        net = invoice.Totals.Net.Amount,
        vat = invoice.Totals.Vat.Amount,
        withholding = invoice.Totals.Withholding.Amount,
        lines = invoice.Lines.Select(line => new
        {
            line.Id,
            line.LineNo,
            line.Input.SalesOrderLineId,
            quantity = line.Input.QuantityText,
            net = line.Amounts.Net.Amount,
        }),
    });
}
