using System.Text.Json;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Quotes;
using GoymenERP.Modules.Sales.Domain.Requests;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// API-22 / API-23 / API-24: the sales quote half of the Sales store, on the draft's attempt table and rules (ADR-011
/// §2–§8), one save per command with its attempt and audit event. The conversion (No 162) writes the ORDER through the same
/// draft rows as a create, with its source-quote reference (UX_Drafts_SourceQuoteId: the database refuses a second order for
/// the quote), the quote's conversion, the attempt (answering the order) and both audit events — together or not at all.
/// </summary>
public sealed partial class SalesStore
{
    private const string QuoteEntityType = "Sales.Quote";

    /// <summary>The display-number series of a sales quote (<c>SQ-&lt;UTC year&gt;-&lt;n&gt;</c>): technical text, not an official document (Ö8).</summary>
    internal const string QuoteSeries = "SQ";

    public async Task<SalesQuoteStoreResult> StoreQuoteAsync(SalesDraftWriteEnvelope envelope, string requestHash, Func<string, SalesQuote> build,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(build);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        var companyId = RequireResolvedCompany();
        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        var actorId = envelope.ActorId.Trim();
        var attempt = await dbContext.DraftAttempts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == SalesQuoteCommands.CreateV1 && row.IdempotencyKeyHash == keyHash, cancellationToken);
        if (attempt is not null)
        {
            return await ReplayQuoteAttemptAsync(attempt, actorId, requestHash, cancellationToken);
        }

        var now = timeProvider.GetUtcNow();
        var number = await NextDocumentNumberAsync(companyId, QuoteSeries, now, cancellationToken);
        var quote = build(SeriesDocumentNo(QuoteSeries, now, number)) ?? throw new ArgumentException("Teklif kurulmadı.", nameof(build));
        RequireCompany(quote.CompanyId);
        if (!string.Equals(quote.CreatedBy, actorId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Teklifi kaydeden, yazma zarfının aktörüdür.", nameof(build));
        }

        if (await dbContext.Quotes.AsNoTracking().AnyAsync(row => row.Id == quote.Id, cancellationToken))
        {
            throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Bu teklif kimliği şirketin başka bir teklifinde kullanılıyor.");
        }

        SalesQuoteRows.Add(dbContext, quote, envelope.CorrelationId);
        AddQuoteAttempt(companyId, SalesQuoteCommands.CreateV1, keyHash, requestHash, actorId, quote.Id, envelope, now);
        auditEvents.Enqueue(QuoteAudit(envelope, companyId, "sales.quote.created.v1", "Create", quote, null, "{}",
            QuoteAuditState(quote, withLines: true), "Satış teklifi kaydedildi"));
        await SaveAsync(cancellationToken);
        return new SalesQuoteStoreResult(quote, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }

    public async Task<SalesQuoteReplay?> FindQuoteReplayAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
        CancellationToken cancellationToken = default)
    {
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireQuoteCommand(commandName);
        RequireResolvedCompany();
        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        var attempt = await dbContext.DraftAttempts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == commandName && row.IdempotencyKeyHash == keyHash, cancellationToken);
        if (attempt is null)
        {
            return null;
        }

        if (!string.Equals(attempt.ActorId, envelope.ActorId.Trim(), StringComparison.Ordinal)
            || !string.Equals(attempt.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw Refused(SalesDraftStoreCodes.SameKeyPayloadMismatch, "Aynı Idempotency-Key başka bir istekle geldi; ilk cevap bu isteğe verilmez.");
        }

        if (attempt.Outcome == AttemptOutcomeRecorded && attempt.QuoteId is { } quoteId)
        {
            var stored = await FindQuoteAsync(quoteId, cancellationToken)
                ?? throw new InvalidOperationException("Kayıtlı deneme kaydının teklifi bulunamadı.");
            return new SalesQuoteReplay(stored, null, attempt.CorrelationId);
        }

        var refusal = attempt.RefusalJson is null ? null : JsonSerializer.Deserialize<SalesDraftTerminalRefusal>(attempt.RefusalJson, Json);
        if (refusal is null || refusal.Status is < 400 or > 599 || string.IsNullOrWhiteSpace(refusal.Code) || refusal.Errors is null)
        {
            throw new InvalidOperationException("Kayıtlı terminal ret okunamadı.");
        }

        return new SalesQuoteReplay(null, refusal, attempt.CorrelationId);
    }

    public async Task RecordQuoteRefusalAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName, Guid? quoteId,
        SalesDraftTerminalRefusal refusal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireQuoteCommand(commandName);
        var companyId = RequireResolvedCompany();
        if (refusal.Status is < 400 or > 599 || string.IsNullOrWhiteSpace(refusal.Code) || refusal.Errors is null
            || refusal.Errors.Any(error => error is null || string.IsNullOrWhiteSpace(error.Field) || string.IsNullOrWhiteSpace(error.Code)))
        {
            throw new ArgumentException("Terminal ret bir durum, bir kod ve tam alan hataları taşır.", nameof(refusal));
        }

        if (quoteId == Guid.Empty)
        {
            throw new ArgumentException("Teklif kimliği boş olamaz.", nameof(quoteId));
        }

        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        if (await dbContext.DraftAttempts.AsNoTracking()
                .AnyAsync(row => row.CommandName == commandName && row.IdempotencyKeyHash == keyHash, cancellationToken))
        {
            throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Bu anahtar zaten bir deneme taşıyor; tekrar onu alır.");
        }

        dbContext.DraftAttempts.Add(new DraftAttemptRow
        {
            CompanyId = companyId,
            CommandName = commandName,
            IdempotencyKeyHash = keyHash,
            RequestHash = requestHash,
            ActorId = envelope.ActorId.Trim(),
            Outcome = AttemptOutcomeRefused,
            QuoteId = quoteId,
            RefusalJson = JsonSerializer.Serialize(refusal, Json),
            CorrelationId = envelope.CorrelationId.Trim(),
            RecordedAtUtc = timeProvider.GetUtcNow(),
        });
        await SaveAsync(cancellationToken);
    }

    public async Task<SalesQuoteStoreResult?> UpdateQuoteAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName, Guid quoteId,
        Action<SalesQuote, DateTimeOffset> mutate, SalesDraftStateAudit audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        ArgumentNullException.ThrowIfNull(audit);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        if (commandName is not (SalesQuoteCommands.ReviseV1 or SalesQuoteCommands.LoseV1))
        {
            throw new ArgumentException("Yalnız revizyon ve kayıp bir teklifi günceller.", nameof(commandName));
        }

        var companyId = RequireResolvedCompany();
        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        var actorId = envelope.ActorId.Trim();
        var attempt = await dbContext.DraftAttempts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == commandName && row.IdempotencyKeyHash == keyHash, cancellationToken);
        if (attempt is not null)
        {
            return await ReplayQuoteAttemptAsync(attempt, actorId, requestHash, cancellationToken);
        }

        if (quoteId == Guid.Empty)
        {
            return null;
        }

        var row = await dbContext.Quotes.SingleOrDefaultAsync(candidate => candidate.Id == quoteId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var quote = await RestoreQuoteAsync(row, cancellationToken);
        var before = QuoteAuditState(quote, withLines: false);
        var versionBefore = quote.Version;
        var revisionBefore = quote.Revision;
        var now = timeProvider.GetUtcNow();
        mutate(quote, now);
        if (quote.Version != versionBefore + 1 || quote.Id != row.Id || quote.CompanyId != companyId)
        {
            throw new InvalidOperationException("Teklif güncellemesi tam olarak bir sürüm ilerletir ve teklifi değiştirmez.");
        }

        row.State = (int)quote.State;
        row.Revision = quote.Revision;
        row.LossReason = quote.Loss?.Reason;
        row.LostBy = quote.Loss?.LostBy;
        row.LostAtUtc = quote.Loss?.LostAtUtc;
        row.Version = quote.Version;
        row.UpdatedAtUtc = quote.UpdatedAtUtc;
        if (quote.Revision != revisionBefore)
        {
            SalesQuoteRows.AddRevision(dbContext, quote, actorId, envelope.CorrelationId);
        }

        if (quote.State == SalesQuoteState.Lost)
        {
            // The lost quote no longer covers its requests: they may be quoted again (talep → teklif N:1 for the LIVE quote).
            foreach (var link in await dbContext.QuoteRequests.Where(candidate => candidate.QuoteId == quote.Id && candidate.IsActive)
                         .ToListAsync(cancellationToken))
            {
                link.IsActive = false;
            }
        }

        AddQuoteAttempt(companyId, commandName, keyHash, requestHash, actorId, quote.Id, envelope, now);
        auditEvents.Enqueue(QuoteAudit(envelope, companyId, audit.EventType, audit.Operation, quote, versionBefore, before,
            QuoteAuditState(quote, withLines: quote.Revision != revisionBefore), audit.Reason));
        await SaveAsync(cancellationToken);
        return new SalesQuoteStoreResult(quote, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }

    public async Task<SalesQuote?> FindQuoteAsync(Guid quoteId, CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        var row = await dbContext.Quotes.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == quoteId, cancellationToken);
        return row is null ? null : await RestoreQuoteAsync(row, cancellationToken);
    }

    public async Task<SalesQuotePage> ListQuotesAsync(int pageSize, string? cursor, SalesQuoteState? state, CancellationToken cancellationToken = default)
    {
        var companyId = RequireResolvedCompany();
        if (pageSize is < 1 or > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), $"Sayfa boyutu 1–{MaximumPageSize} olmalıdır (varsayılan {DefaultPageSize}).");
        }

        var query = dbContext.Quotes.AsNoTracking();
        if (state is { } filter)
        {
            if (!Enum.IsDefined(filter))
            {
                throw new ArgumentOutOfRangeException(nameof(state));
            }

            var stateValue = (int)filter;
            query = query.Where(row => row.State == stateValue);
        }

        if (cursor is not null)
        {
            var (createdAtUtc, id) = ListCursor.Decode(cursor, companyId, ListCursor.QuoteOrdering);
            query = query.Where(row => row.CreatedAtUtc < createdAtUtc || (row.CreatedAtUtc == createdAtUtc && row.Id.CompareTo(id) < 0));
        }

        var rows = await query.OrderByDescending(row => row.CreatedAtUtc).ThenByDescending(row => row.Id).Take(pageSize + 1).ToListAsync(cancellationToken);
        var hasMore = rows.Count > pageSize;
        var page = hasMore ? rows.Take(pageSize).ToList() : rows;
        var quotes = new List<SalesQuote>(page.Count);
        foreach (var row in page)
        {
            quotes.Add(await RestoreQuoteAsync(row, cancellationToken));
        }

        var last = page.LastOrDefault();
        return new SalesQuotePage(quotes,
            hasMore && last is not null ? ListCursor.Encode(companyId, last.CreatedAtUtc, last.Id, ListCursor.QuoteOrdering) : null);
    }

    public Task<bool> IsRequestQuotedAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        return dbContext.QuoteRequests.AsNoTracking().AnyAsync(link => link.RequestId == requestId && link.IsActive, cancellationToken);
    }

    public async Task<SalesDraftStoreResult> StoreConvertedDraftAsync(SalesDraftWriteEnvelope envelope, string requestHash, Guid quoteId,
        long expectedQuoteVersion, Func<string, SalesOrderDraft> build, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(build);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        var companyId = RequireResolvedCompany();
        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        var actorId = envelope.ActorId.Trim();
        var attempt = await dbContext.DraftAttempts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == SalesQuoteCommands.ConvertV1 && row.IdempotencyKeyHash == keyHash, cancellationToken);
        if (attempt is not null)
        {
            if (!string.Equals(attempt.ActorId, actorId, StringComparison.Ordinal)
                || !string.Equals(attempt.RequestHash, requestHash, StringComparison.Ordinal))
            {
                throw Refused(SalesDraftStoreCodes.SameKeyPayloadMismatch, "Aynı Idempotency-Key başka bir istekle geldi; ilk cevap bu isteğe verilmez.");
            }

            if (attempt.Outcome != AttemptOutcomeRecorded || attempt.DraftId is not { } draftId)
            {
                throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Bu anahtar kayıtlı bir terminal reddi taşıyor; tekrar aynı reddi alır.");
            }

            var replayed = await FindDraftAsync(draftId, cancellationToken)
                ?? throw new InvalidOperationException("Kayıtlı deneme kaydının siparişi bulunamadı.");
            return new SalesDraftStoreResult(replayed, SalesDraftWriteOutcome.Replayed, attempt.CorrelationId);
        }

        // The quote is tracked: its version is the concurrency token of this save, so a revision or a loss that lands
        // meanwhile makes this conversion the loser (nothing stored), and the unique source-quote index refuses a second order.
        var row = await dbContext.Quotes.SingleOrDefaultAsync(candidate => candidate.Id == quoteId, cancellationToken)
            ?? throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Teklif bu şirkette bulunamadı.");
        var quote = await RestoreQuoteAsync(row, cancellationToken);
        quote.RequireConvertible(expectedQuoteVersion);
        await RequireApprovedRequestsAsync(quote, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var counter = await dbContext.DraftNumbers.SingleOrDefaultAsync(cancellationToken);
        long number;
        if (counter is null)
        {
            number = 1;
            dbContext.DraftNumbers.Add(new DraftNumberRow { CompanyId = companyId, Next = 2, Version = 1, UpdatedAtUtc = now });
        }
        else
        {
            number = counter.Next;
            counter.Next = checked(counter.Next + 1);
            counter.Version = checked(counter.Version + 1);
            counter.UpdatedAtUtc = now;
        }

        var draft = build(DocumentNo(now, number)) ?? throw new ArgumentException("Sipariş kurulmadı.", nameof(build));
        RequireCompany(draft.CompanyId);
        draft.RequireStorable();
        if (await dbContext.Drafts.AsNoTracking().AnyAsync(candidate => candidate.Id == draft.Id, cancellationToken))
        {
            throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Bu sipariş kimliği şirketin başka bir siparişinde kullanılıyor.");
        }

        var versionBefore = quote.Version;
        var before = QuoteAuditState(quote, withLines: false);
        quote.Convert(expectedQuoteVersion, draft.Id, actorId, now);
        SalesDraftRows.Add(dbContext, draft, envelope.ActorId, envelope.CorrelationId, quote.Id, quote.Revision);
        draft.RestoreCreator(envelope.ActorId);
        row.State = (int)quote.State;
        row.ConvertedDraftId = quote.Conversion!.DraftId;
        row.ConvertedBy = quote.Conversion.ConvertedBy;
        row.ConvertedAtUtc = quote.Conversion.ConvertedAtUtc;
        row.Version = quote.Version;
        row.UpdatedAtUtc = quote.UpdatedAtUtc;
        dbContext.DraftAttempts.Add(new DraftAttemptRow
        {
            CompanyId = companyId,
            CommandName = SalesQuoteCommands.ConvertV1,
            IdempotencyKeyHash = keyHash,
            RequestHash = requestHash,
            ActorId = actorId,
            Outcome = AttemptOutcomeRecorded,
            DraftId = draft.Id,
            CorrelationId = envelope.CorrelationId.Trim(),
            RecordedAtUtc = now,
        });
        Audit(envelope, companyId, draft, now);
        auditEvents.Enqueue(QuoteAudit(envelope, companyId, "sales.quote.converted.v1", "Convert", quote, versionBefore, before,
            QuoteAuditState(quote, withLines: false), "Satış teklifi siparişe dönüştürüldü"));
        await SaveAsync(cancellationToken);
        return new SalesDraftStoreResult(draft, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }

    /// <summary>No 160 (default-deny), inside the conversion's own read: every request the quote covers is approved now.</summary>
    private async Task RequireApprovedRequestsAsync(SalesQuote quote, CancellationToken cancellationToken)
    {
        if (quote.RequestIds.Count == 0)
        {
            return;
        }

        var ids = quote.RequestIds.ToArray();
        var approved = (int)SalesRequestState.Approved;
        var count = await dbContext.Requests.AsNoTracking().CountAsync(row => ids.Contains(row.Id) && row.State == approved, cancellationToken);
        if (count != ids.Length)
        {
            throw new SalesRuleException(SalesRuleCodes.QuoteRequestNotApproved, "Teklifin karşıladığı her talep onaylı olmadan sipariş açılmaz.");
        }
    }

    /// <summary>
    /// No 161: whether the request's live quote became an APPROVED order that is not cancelled. Asked inside the rejection's
    /// own save path, before the domain decides.
    /// </summary>
    private async Task<bool> HasApprovedOrderAsync(Guid requestId, CancellationToken cancellationToken)
    {
        var approved = (int)InternalApprovalState.Approved;
        var live = (int)SalesDraftState.Draft;
        var quoteIds = dbContext.QuoteRequests.AsNoTracking().Where(link => link.RequestId == requestId && link.IsActive).Select(link => link.QuoteId);
        return await dbContext.Drafts.AsNoTracking().AnyAsync(draft => draft.SourceQuoteId != null && quoteIds.Contains(draft.SourceQuoteId.Value)
            && draft.ApprovalState == approved && draft.State == live, cancellationToken);
    }

    private async Task<SalesQuote> RestoreQuoteAsync(QuoteRow row, CancellationToken cancellationToken)
    {
        var revision = await dbContext.QuoteRevisions.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.QuoteId == row.Id && candidate.Revision == row.Revision, cancellationToken)
            ?? throw new InvalidOperationException("Kayıtlı teklifin güncel revizyonu bulunamadı.");
        var lines = await dbContext.QuoteLines.AsNoTracking()
            .Where(line => line.QuoteId == row.Id && line.Revision == row.Revision).ToListAsync(cancellationToken);
        var requests = await dbContext.QuoteRequests.AsNoTracking().Where(link => link.QuoteId == row.Id).ToListAsync(cancellationToken);
        return SalesQuoteRows.Restore(row, revision, lines, requests);
    }

    private async Task<SalesQuoteStoreResult> ReplayQuoteAttemptAsync(DraftAttemptRow attempt, string actorId, string requestHash,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(attempt.ActorId, actorId, StringComparison.Ordinal)
            || !string.Equals(attempt.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw Refused(SalesDraftStoreCodes.SameKeyPayloadMismatch, "Aynı Idempotency-Key başka bir istekle geldi; ilk cevap bu isteğe verilmez.");
        }

        if (attempt.Outcome != AttemptOutcomeRecorded || attempt.QuoteId is not { } recordedId)
        {
            throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Bu anahtar kayıtlı bir terminal reddi taşıyor; tekrar aynı reddi alır.");
        }

        var stored = await FindQuoteAsync(recordedId, cancellationToken)
            ?? throw new InvalidOperationException("Kayıtlı deneme kaydının teklifi bulunamadı.");
        return new SalesQuoteStoreResult(stored, SalesDraftWriteOutcome.Replayed, attempt.CorrelationId);
    }

    private void AddQuoteAttempt(Guid companyId, string commandName, string keyHash, string requestHash, string actorId, Guid quoteId,
        SalesDraftWriteEnvelope envelope, DateTimeOffset now) =>
        dbContext.DraftAttempts.Add(new DraftAttemptRow
        {
            CompanyId = companyId,
            CommandName = commandName,
            IdempotencyKeyHash = keyHash,
            RequestHash = requestHash,
            ActorId = actorId,
            Outcome = AttemptOutcomeRecorded,
            QuoteId = quoteId,
            CorrelationId = envelope.CorrelationId.Trim(),
            RecordedAtUtc = now,
        });

    private static AuditEventDraft QuoteAudit(SalesDraftWriteEnvelope envelope, Guid companyId, string eventType, string operation,
        SalesQuote quote, long? versionBefore, string beforeJson, string afterJson, string reason) => new(
        companyId, eventType, QuoteEntityType, quote.Id.ToString("D"), operation,
        EntityVersionBefore: versionBefore, EntityVersionAfter: quote.Version,
        AuditActorTypes.User, envelope.ActorId, envelope.PermissionCode, envelope.RoleCodes,
        beforeJson, afterJson, reason, ApprovalContextJson: null, AuditAiInvolvement.None, AiContextJson: null,
        envelope.SourceChannel, envelope.ClientIpAddress, envelope.UserAgent, envelope.CorrelationId, envelope.CausationId,
        envelope.IdempotencyKey);

    private static string QuoteAuditState(SalesQuote quote, bool withLines) => JsonSerializer.Serialize(new
    {
        quote.Id,
        quote.DocumentNo,
        state = quote.State.ToString(),
        revision = quote.Revision,
        version = quote.Version,
        quote.Header.PartyId,
        quote.Header.CompanyAccountId,
        quote.Header.CurrencyId,
        quote.Header.BusinessDate,
        requests = quote.RequestIds,
        terms = quote.Terms,
        loss = quote.Loss,
        conversion = quote.Conversion,
        lines = withLines
            ? quote.Lines.Select(line => new
            {
                line.LineNo,
                line.Id,
                line.Product.ProductId,
                line.Product.ProductVersion,
                line.OrderUnit.UnitId,
                quantity = line.QuantityText,
                line.UnitPrice,
                line.PriceUnitId,
                line.DiscountPercent,
            }).ToArray()
            : null,
    }, Json);

    private static void RequireQuoteCommand(string commandName)
    {
        if (!SalesQuoteCommands.IsQuoteCommand(commandName))
        {
            throw new ArgumentException("Bilinmeyen satış teklifi komut adı.", nameof(commandName));
        }
    }
}
