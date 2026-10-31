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
        var attempt = await dbContext.QuoteAttempts.AsNoTracking()
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
        var attempt = await dbContext.QuoteAttempts.AsNoTracking()
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
        if (await QuoteAttemptExistsAsync(commandName, keyHash, cancellationToken))
        {
            throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Bu anahtar zaten bir deneme taşıyor; tekrar onu alır.");
        }

        dbContext.QuoteAttempts.Add(new QuoteAttemptRow
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
        if (commandName is not (SalesQuoteCommands.ReviseV1 or SalesQuoteCommands.LoseV2 or SalesQuoteCommands.ApproveExpiredConversionV1))
        {
            throw new ArgumentException("Yalnız revizyon, kayıp ve süre onayı bir teklifi günceller.", nameof(commandName));
        }

        var companyId = RequireResolvedCompany();
        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        var actorId = envelope.ActorId.Trim();
        var attempt = await dbContext.QuoteAttempts.AsNoTracking()
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
        var approvalsBefore = quote.ExpiryApprovals.Count;
        var now = timeProvider.GetUtcNow();
        mutate(quote, now);
        if (quote.Version != versionBefore + 1 || quote.Id != row.Id || quote.CompanyId != companyId)
        {
            throw new InvalidOperationException("Teklif güncellemesi tam olarak bir sürüm ilerletir ve teklifi değiştirmez.");
        }

        row.State = (int)quote.State;
        row.Revision = quote.Revision;
        SalesQuoteRows.ApplyLoss(row, quote);
        row.Version = quote.Version;
        row.UpdatedAtUtc = quote.UpdatedAtUtc;
        if (quote.Revision != revisionBefore)
        {
            SalesQuoteRows.AddRevision(dbContext, quote, actorId, envelope.CorrelationId);
        }

        // SAT-D24 / SAT-D44: an approval is appended (one per revision; the primary key refuses a second for the same revision).
        SalesQuoteRows.AddExpiryApprovals(dbContext, quote, approvalsBefore, envelope.CorrelationId);

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
        var now = timeProvider.GetUtcNow();
        // No 160 inside the one save (the service's earlier read can be stale) AND the serialization with a rejection (No 161):
        // the covered request rows are read tracked here and touched in this save, so their row versions are this save's
        // concurrency tokens — a rejection committed after this read makes this conversion the loser, and a rejection still
        // in flight loses on its own row version once this commits (its re-read then finds the live order).
        await RequireApprovedRequestsAsync(quote, now, cancellationToken);
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
        // SAT-D24: expiry is decided on the SERVER's business day of this save's own clock (Europe/Istanbul, SAT-D10), never on
        // the order's client-chosen business date; an approval recorded for the current revision lets an expired quote convert.
        quote.Convert(expectedQuoteVersion, draft.Id, actorId, SalesBusinessDay.DateOf(now), now);
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

    /// <summary>
    /// No 160 (default-deny), inside the conversion's own read: every request the quote covers is approved NOW. The rows are
    /// tracked and their <c>UpdatedAtUtc</c> moves with the conversion, so each request's row version is a concurrency token of
    /// the conversion's save (the request's own version, its decision and its lines do not change).
    /// </summary>
    private async Task RequireApprovedRequestsAsync(SalesQuote quote, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (quote.RequestIds.Count == 0)
        {
            return;
        }

        var ids = quote.RequestIds.ToArray();
        var rows = await dbContext.Requests.Where(row => ids.Contains(row.Id)).ToListAsync(cancellationToken);
        if (rows.Count != ids.Length || rows.Any(row => row.State != (int)SalesRequestState.Approved))
        {
            throw new SalesRuleException(SalesRuleCodes.QuoteRequestNotApproved, "Teklifin karşıladığı her talep onaylı olmadan sipariş açılmaz.");
        }

        foreach (var row in rows)
        {
            row.UpdatedAtUtc = now;
            // Forced: EF sends an UPDATE only for a changed value, and a clock that repeats the stored instant (a fixed test
            // clock, a coarse host clock) would otherwise drop the row from the save and with it the serialization.
            dbContext.Entry(row).Property(candidate => candidate.UpdatedAtUtc).IsModified = true;
        }
    }

    /// <summary>
    /// The order's submission and approval (belt and braces for No 160): an order converted from a quote moves towards
    /// approval only while every request that quote covers is still approved. Read inside the transition's own save path.
    /// </summary>
    private async Task RequireSourceRequestsApprovedAsync(Guid? sourceQuoteId, CancellationToken cancellationToken)
    {
        if (sourceQuoteId is not { } quoteId)
        {
            return;
        }

        var approved = (int)SalesRequestState.Approved;
        var requestIds = dbContext.QuoteRequests.AsNoTracking().Where(link => link.QuoteId == quoteId).Select(link => link.RequestId);
        if (await dbContext.Requests.AsNoTracking().AnyAsync(request => requestIds.Contains(request.Id) && request.State != approved,
                cancellationToken))
        {
            throw new SalesRuleException(SalesRuleCodes.QuoteRequestNotApproved,
                "Teklifin karşıladığı talep artık onaylı değil; bu sipariş onaya ilerlemez.");
        }
    }

    /// <summary>
    /// No 161, fail-closed: whether ANY order converted from a quote that covers the request is not cancelled — Preparing
    /// (never approved), pending, revised back to Preparing, approved, rejected/withdrawn on its approval axis, or
    /// force-closed (<see cref="SalesDraftState.Closed"/>). Only a cancelled order releases the request (SAT-D23:
    /// every non-cancelled order blocks, the pending and force-closed ones included). Every link counts, active
    /// or not: a converted quote's order stays the request's order.
    /// </summary>
    private async Task<bool> HasLiveOrderAsync(Guid requestId, CancellationToken cancellationToken)
    {
        var cancelled = (int)SalesDraftState.Cancelled;
        var quoteIds = dbContext.QuoteRequests.AsNoTracking().Where(link => link.RequestId == requestId).Select(link => link.QuoteId);
        return await dbContext.Drafts.AsNoTracking().AnyAsync(draft => draft.SourceQuoteId != null && quoteIds.Contains(draft.SourceQuoteId.Value)
            && draft.State != cancelled, cancellationToken);
    }

    private async Task<SalesQuote> RestoreQuoteAsync(QuoteRow row, CancellationToken cancellationToken)
    {
        // Every revision header (SAT-D44 (1): who created and revised the quote); the lines of the current one only.
        var revisions = await dbContext.QuoteRevisions.AsNoTracking()
            .Where(candidate => candidate.QuoteId == row.Id).OrderBy(candidate => candidate.Revision).ToListAsync(cancellationToken);
        if (!revisions.Any(candidate => candidate.Revision == row.Revision))
        {
            throw new InvalidOperationException("Kayıtlı teklifin güncel revizyonu bulunamadı.");
        }

        var lines = await dbContext.QuoteLines.AsNoTracking()
            .Where(line => line.QuoteId == row.Id && line.Revision == row.Revision).ToListAsync(cancellationToken);
        var requests = await dbContext.QuoteRequests.AsNoTracking().Where(link => link.QuoteId == row.Id).ToListAsync(cancellationToken);
        var approvals = await dbContext.QuoteExpiryApprovals.AsNoTracking()
            .Where(approval => approval.QuoteId == row.Id).ToListAsync(cancellationToken);
        return SalesQuoteRows.Restore(row, revisions, lines, requests, approvals);
    }

    private async Task<SalesQuoteStoreResult> ReplayQuoteAttemptAsync(QuoteAttemptRow attempt, string actorId, string requestHash,
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

    private Task<bool> QuoteAttemptExistsAsync(string commandName, string keyHash, CancellationToken cancellationToken) =>
        dbContext.QuoteAttempts.AsNoTracking()
            .AnyAsync(row => row.CommandName == commandName && row.IdempotencyKeyHash == keyHash, cancellationToken);

    private void AddQuoteAttempt(Guid companyId, string commandName, string keyHash, string requestHash, string actorId, Guid quoteId,
        SalesDraftWriteEnvelope envelope, DateTimeOffset now) =>
        dbContext.QuoteAttempts.Add(new QuoteAttemptRow
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
        expiryApprovals = quote.ExpiryApprovals,
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
