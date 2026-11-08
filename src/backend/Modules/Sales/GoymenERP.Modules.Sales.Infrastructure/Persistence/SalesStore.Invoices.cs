using System.Text.Json;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Invoices;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// S5 API-32 (Fatura çekirdeği 1): the invoice half of the Sales store. The attempt rules are the request's (ADR-011 §2–§8) in
/// the invoice's own attempt table: (company, command, key hash) is the identity, the raw key is never stored, the same key,
/// content and actor answers the stored invoice, another content or actor is one 409, a lost race stores nothing. One save
/// writes the invoice, its lines, the attempt, the number counter and the audit event; the filtered unique index
/// UX_InvoiceLines_ActiveShipment makes a second active line of one shipment the save's concurrent change (AT-055, SAT-D94).
/// </summary>
public sealed partial class SalesStore : ISalesInvoiceStore
{
    private const string InvoiceEntityType = "Sales.Invoice";

    /// <summary>The display-number series of an invoice draft (<c>SF-&lt;UTC year&gt;-&lt;n&gt;</c>): technical text, never the official number (Ö8, SAT-D7).</summary>
    internal const string InvoiceSeries = "SF";

    public async Task<SalesInvoiceStoreResult> StoreInvoiceAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        Func<string, SalesInvoice> build, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(build);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        var companyId = RequireResolvedCompany();
        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        var actorId = envelope.ActorId.Trim();
        var attempt = await dbContext.InvoiceAttempts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == SalesInvoiceCommands.CreateV1 && row.IdempotencyKeyHash == keyHash,
                cancellationToken);
        if (attempt is not null)
        {
            return await ReplayInvoiceAttemptAsync(attempt, actorId, requestHash, cancellationToken);
        }

        var now = timeProvider.GetUtcNow();
        var number = await NextDocumentNumberAsync(companyId, InvoiceSeries, now, cancellationToken);
        var invoice = build(SeriesDocumentNo(InvoiceSeries, now, number)) ?? throw new ArgumentException("Fatura kurulmadı.", nameof(build));
        RequireCompany(invoice.CompanyId);
        if (!string.Equals(invoice.CreatedBy, actorId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Faturayı hazırlayan, yazma zarfının aktörüdür.", nameof(build));
        }

        SalesInvoiceRows.Add(dbContext, invoice, envelope.CorrelationId);
        dbContext.InvoiceAttempts.Add(new InvoiceAttemptRow
        {
            CompanyId = companyId,
            CommandName = SalesInvoiceCommands.CreateV1,
            IdempotencyKeyHash = keyHash,
            RequestHash = requestHash,
            ActorId = actorId,
            Outcome = AttemptOutcomeRecorded,
            InvoiceId = invoice.Id,
            CorrelationId = envelope.CorrelationId.Trim(),
            RecordedAtUtc = now,
        });
        auditEvents.Enqueue(new AuditEventDraft(
            companyId,
            "sales.invoice.created.v1",
            InvoiceEntityType,
            invoice.Id.ToString("D"),
            "Create",
            EntityVersionBefore: null,
            EntityVersionAfter: invoice.Version,
            AuditActorTypes.User,
            envelope.ActorId,
            envelope.PermissionCode,
            envelope.RoleCodes,
            "{}",
            InvoiceAuditState(invoice),
            "Satış fatura taslağı hazırlandı",
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
        return new SalesInvoiceStoreResult(invoice, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }

    public async Task<SalesInvoiceStoreResult?> FindInvoiceReplayAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        CancellationToken cancellationToken = default)
    {
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireResolvedCompany();
        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        var attempt = await dbContext.InvoiceAttempts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == SalesInvoiceCommands.CreateV1 && row.IdempotencyKeyHash == keyHash,
                cancellationToken);
        return attempt is null ? null : await ReplayInvoiceAttemptAsync(attempt, envelope.ActorId.Trim(), requestHash, cancellationToken);
    }

    public async Task<SalesInvoice?> FindInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        var row = await dbContext.Invoices.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == invoiceId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var lines = await dbContext.InvoiceLines.AsNoTracking().Where(line => line.InvoiceId == invoiceId).ToListAsync(cancellationToken);
        return SalesInvoiceRows.Restore(row, lines);
    }

    public async Task<SalesInvoicePage> ListInvoicesAsync(int pageSize, string? cursor, CancellationToken cancellationToken = default)
    {
        var companyId = RequireResolvedCompany();
        if (pageSize is < 1 or > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }

        var query = dbContext.Invoices.AsNoTracking();
        if (cursor is not null)
        {
            var (createdAtUtc, id) = ListCursor.Decode(cursor, companyId, ListCursor.InvoiceOrdering);
            query = query.Where(row => row.CreatedAtUtc < createdAtUtc || (row.CreatedAtUtc == createdAtUtc && row.Id.CompareTo(id) < 0));
        }

        var rows = await query.OrderByDescending(row => row.CreatedAtUtc).ThenByDescending(row => row.Id).Take(pageSize + 1)
            .ToListAsync(cancellationToken);
        var page = rows.Take(pageSize).ToList();
        var ids = page.Select(row => row.Id).ToArray();
        var lines = (await dbContext.InvoiceLines.AsNoTracking().Where(line => ids.Contains(line.InvoiceId)).ToListAsync(cancellationToken))
            .ToLookup(line => line.InvoiceId);
        var items = page.Select(row => SalesInvoiceRows.Restore(row, [.. lines[row.Id]])).ToList();
        var next = rows.Count > pageSize
            ? ListCursor.Encode(companyId, page[^1].CreatedAtUtc, page[^1].Id, ListCursor.InvoiceOrdering)
            : null;
        return new SalesInvoicePage(items, next);
    }

    public async Task<IReadOnlySet<Guid>> FindInvoicedShipmentsAsync(IReadOnlyCollection<Guid> shipmentIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shipmentIds);
        RequireResolvedCompany();
        var ids = shipmentIds.Distinct().ToArray();
        var found = await dbContext.InvoiceLines.AsNoTracking()
            .Where(line => line.ReleasedAtUtc == null && ids.Contains(line.ShipmentId))
            .Select(line => line.ShipmentId)
            .ToListAsync(cancellationToken);
        return found.ToHashSet();
    }

    private async Task<SalesInvoiceStoreResult> ReplayInvoiceAttemptAsync(InvoiceAttemptRow attempt, string actorId, string requestHash,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(attempt.ActorId, actorId, StringComparison.Ordinal)
            || !string.Equals(attempt.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw Refused(SalesDraftStoreCodes.SameKeyPayloadMismatch,
                "Aynı Idempotency-Key başka bir istekle geldi; ilk cevap bu isteğe verilmez.");
        }

        if (attempt.Outcome != AttemptOutcomeRecorded || attempt.InvoiceId is not { } invoiceId)
        {
            // No invoice command records a refusal in this slice; a refusal row would be a later slice's (never a guess here).
            throw new InvalidOperationException("Fatura deneme kaydı okunamadı.");
        }

        var stored = await FindInvoiceAsync(invoiceId, cancellationToken)
            ?? throw new InvalidOperationException("Kayıtlı deneme kaydının faturası bulunamadı.");
        return new SalesInvoiceStoreResult(stored, SalesDraftWriteOutcome.Replayed, attempt.CorrelationId);
    }

    private static string InvoiceAuditState(SalesInvoice invoice) => JsonSerializer.Serialize(new
    {
        invoice.Id,
        invoice.DocumentNo,
        state = invoice.State.ToString(),
        version = invoice.Version,
        invoice.Header.PartyId,
        invoice.Header.CompanyAccountId,
        invoice.Header.CurrencyId,
        invoice.Header.BusinessDate,
        rate = invoice.Header.Rate,
        payable = invoice.Totals.Payable.Amount,
        functionalPayable = invoice.FunctionalTotals?.Payable.Amount,
        lines = invoice.Lines.Select(line => new
        {
            line.LineNo,
            line.Id,
            line.Share.ShipmentId,
            line.Share.DispatchNoteId,
            line.Share.SalesOrderId,
            line.Share.SalesOrderLineId,
            line.Share.PriceBasisRevision,
            quantity = line.Share.QuantityText,
            net = line.Amounts.Net.Amount,
            vat = line.Amounts.Vat.Amount,
            withholding = line.Amounts.Withholding.Amount,
            functionalNet = line.Functional?.Amounts.Net.Amount,
            needsReview = line.Functional?.NeedsReview,
        }).ToArray(),
        instalments = invoice.Instalments.Select(item => new { item.SequenceNo, item.DueDate, amount = item.Amount.Amount }).ToArray(),
    }, Json);
}
