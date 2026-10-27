using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Requests;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// API-20 / API-21: the sales request half of the Sales store. The attempt rules are the draft's own (ADR-011 §2–§8) in the
/// request's own attempt table (ADR-SAL-001 §2, <c>sales.RequestAttempts</c>) — (company, command, key hash) is the identity, the raw key is never stored, the same key, content
/// and actor answers the stored result, another content or actor is one 409, a lost race stores nothing — and every write
/// is one save with its attempt record and its audit event. Another company's request is invisible under the tenant filter.
/// </summary>
public sealed partial class SalesStore
{
    private const string RequestEntityType = "Sales.Request";

    /// <summary>The display-number series of a sales request (<c>SR-&lt;UTC year&gt;-&lt;n&gt;</c>): technical text, not an official document (Ö8).</summary>
    internal const string RequestSeries = "SR";

    public async Task<SalesRequestStoreResult> StoreRequestAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        Func<string, SalesRequest> build, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(build);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        var companyId = RequireResolvedCompany();
        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        var actorId = envelope.ActorId.Trim();
        var attempt = await dbContext.RequestAttempts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == SalesRequestCommands.CreateV1 && row.IdempotencyKeyHash == keyHash,
                cancellationToken);
        if (attempt is not null)
        {
            return await ReplayRequestAttemptAsync(attempt, actorId, requestHash, cancellationToken);
        }

        var now = timeProvider.GetUtcNow();
        var number = await NextDocumentNumberAsync(companyId, RequestSeries, now, cancellationToken);
        var request = build(SeriesDocumentNo(RequestSeries, now, number)) ?? throw new ArgumentException("Talep kurulmadı.", nameof(build));
        RequireCompany(request.CompanyId);
        if (!string.Equals(request.CreatedBy, actorId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Talebi kaydeden, yazma zarfının aktörüdür.", nameof(build));
        }

        if (await dbContext.Requests.AsNoTracking().AnyAsync(row => row.Id == request.Id, cancellationToken))
        {
            throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Bu talep kimliği şirketin başka bir talebinde kullanılıyor.");
        }

        SalesRequestRows.Add(dbContext, request, envelope.CorrelationId);
        AddRequestAttempt(companyId, SalesRequestCommands.CreateV1, keyHash, requestHash, actorId, request.Id, envelope, now);
        auditEvents.Enqueue(RequestAudit(envelope, companyId, "sales.request.created.v1", "Create", request, null, "{}",
            RequestAuditState(request, withLines: true), "Satış talebi kaydedildi"));
        await SaveAsync(cancellationToken);
        return new SalesRequestStoreResult(request, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }

    public async Task<SalesRequestReplay?> FindRequestReplayAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
        CancellationToken cancellationToken = default)
    {
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireRequestCommand(commandName);
        RequireResolvedCompany();
        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        var attempt = await dbContext.RequestAttempts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == commandName && row.IdempotencyKeyHash == keyHash, cancellationToken);
        if (attempt is null)
        {
            return null;
        }

        if (!string.Equals(attempt.ActorId, envelope.ActorId.Trim(), StringComparison.Ordinal)
            || !string.Equals(attempt.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw Refused(SalesDraftStoreCodes.SameKeyPayloadMismatch,
                "Aynı Idempotency-Key başka bir istekle geldi; ilk cevap bu isteğe verilmez.");
        }

        if (attempt.Outcome == AttemptOutcomeRecorded && attempt.RequestId is { } requestId)
        {
            var stored = await FindRequestAsync(requestId, cancellationToken)
                ?? throw new InvalidOperationException("Kayıtlı deneme kaydının talebi bulunamadı.");
            return new SalesRequestReplay(stored, null, attempt.CorrelationId);
        }

        var refusal = attempt.RefusalJson is null ? null : JsonSerializer.Deserialize<SalesDraftTerminalRefusal>(attempt.RefusalJson, Json);
        if (refusal is null || refusal.Status is < 400 or > 599 || string.IsNullOrWhiteSpace(refusal.Code) || refusal.Errors is null)
        {
            throw new InvalidOperationException("Kayıtlı terminal ret okunamadı.");
        }

        return new SalesRequestReplay(null, refusal, attempt.CorrelationId);
    }

    public async Task RecordRequestRefusalAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName, Guid? requestId,
        SalesDraftTerminalRefusal refusal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireRequestCommand(commandName);
        var companyId = RequireResolvedCompany();
        if (refusal.Status is < 400 or > 599 || string.IsNullOrWhiteSpace(refusal.Code) || refusal.Errors is null
            || refusal.Errors.Any(error => error is null || string.IsNullOrWhiteSpace(error.Field) || string.IsNullOrWhiteSpace(error.Code)))
        {
            throw new ArgumentException("Terminal ret bir durum, bir kod ve tam alan hataları taşır.", nameof(refusal));
        }

        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Talep kimliği boş olamaz.", nameof(requestId));
        }

        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        if (await RequestAttemptExistsAsync(commandName, keyHash, cancellationToken))
        {
            throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Bu anahtar zaten bir deneme taşıyor; tekrar onu alır.");
        }

        dbContext.RequestAttempts.Add(new RequestAttemptRow
        {
            CompanyId = companyId,
            CommandName = commandName,
            IdempotencyKeyHash = keyHash,
            RequestHash = requestHash,
            ActorId = envelope.ActorId.Trim(),
            Outcome = AttemptOutcomeRefused,
            RequestId = requestId,
            RefusalJson = JsonSerializer.Serialize(refusal, Json),
            CorrelationId = envelope.CorrelationId.Trim(),
            RecordedAtUtc = timeProvider.GetUtcNow(),
        });
        await SaveAsync(cancellationToken);
    }

    public async Task<SalesRequestStoreResult?> DecideRequestAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
        Guid requestId, Action<SalesRequest, DateTimeOffset> mutate, SalesDraftStateAudit audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        ArgumentNullException.ThrowIfNull(audit);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        if (commandName is not (SalesRequestCommands.ApproveV1 or SalesRequestCommands.RejectV1))
        {
            throw new ArgumentException("Yalnız onay ve ret bir talebin durumunu değiştirir.", nameof(commandName));
        }

        if (string.IsNullOrWhiteSpace(audit.EventType) || string.IsNullOrWhiteSpace(audit.Operation) || string.IsNullOrWhiteSpace(audit.Reason))
        {
            throw new ArgumentException("Talep kararının audit olayı tür, işlem ve gerekçe ister.", nameof(audit));
        }

        var companyId = RequireResolvedCompany();
        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        var actorId = envelope.ActorId.Trim();
        var attempt = await dbContext.RequestAttempts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == commandName && row.IdempotencyKeyHash == keyHash, cancellationToken);
        if (attempt is not null)
        {
            return await ReplayRequestAttemptAsync(attempt, actorId, requestHash, cancellationToken);
        }

        if (requestId == Guid.Empty)
        {
            return null;
        }

        // The header is tracked (its version is the concurrency token of this write); the lines are read as before.
        var row = await dbContext.Requests.SingleOrDefaultAsync(candidate => candidate.Id == requestId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var lines = await dbContext.RequestLines.AsNoTracking().Where(line => line.RequestId == requestId).ToListAsync(cancellationToken);
        var request = SalesRequestRows.Restore(row, lines);
        // No 161 (fail-closed): a request with any order that is not cancelled — converted from a quote covering it — is not
        // rejected; the order is cancelled first. Asked here, inside the rejection's own read, before the domain decides;
        // nothing is written when it holds. A conversion racing this rejection touches this request's row in its own save, so
        // the row version tracked here makes one of the two the loser (409 concurrent_change, nothing stored).
        if (commandName == SalesRequestCommands.RejectV1 && await HasLiveOrderAsync(request.Id, cancellationToken))
        {
            throw new SalesRuleException(SalesRuleCodes.RequestHasApprovedOrder,
                "Onaylı siparişi olan satış talebi reddedilmez; önce sipariş iptal edilir.");
        }

        var before = RequestAuditState(request, withLines: false);
        var versionBefore = request.Version;
        var now = timeProvider.GetUtcNow();
        // The domain decides (expected version, allowed transition, reason). A refusal leaves the tracked row untouched.
        mutate(request, now);
        if (request.Version != versionBefore + 1 || request.Id != row.Id || request.CompanyId != companyId)
        {
            throw new InvalidOperationException("Talep kararı tam olarak bir sürüm ilerletir ve talebi değiştirmez.");
        }

        row.State = (int)request.State;
        row.DecidedBy = request.Decision?.DecidedBy;
        row.DecidedAtUtc = request.Decision?.DecidedAtUtc;
        row.RejectionReason = request.Decision?.RejectionReason;
        row.Version = request.Version;
        row.UpdatedAtUtc = request.UpdatedAtUtc;
        AddRequestAttempt(companyId, commandName, keyHash, requestHash, actorId, request.Id, envelope, now);
        auditEvents.Enqueue(RequestAudit(envelope, companyId, audit.EventType, audit.Operation, request, versionBefore, before,
            RequestAuditState(request, withLines: false), audit.Reason));
        await SaveAsync(cancellationToken);
        return new SalesRequestStoreResult(request, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }

    public async Task<SalesRequest?> FindRequestAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        var row = await dbContext.Requests.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == requestId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var lines = await dbContext.RequestLines.AsNoTracking().Where(line => line.RequestId == requestId).ToListAsync(cancellationToken);
        return SalesRequestRows.Restore(row, lines);
    }

    public async Task<SalesRequestPage> ListRequestsAsync(int pageSize, string? cursor, SalesRequestState? state,
        CancellationToken cancellationToken = default)
    {
        var companyId = RequireResolvedCompany();
        if (pageSize is < 1 or > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), $"Sayfa boyutu 1–{MaximumPageSize} olmalıdır (varsayılan {DefaultPageSize}).");
        }

        var query = dbContext.Requests.AsNoTracking();
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
            var (createdAtUtc, id) = ListCursor.Decode(cursor, companyId, ListCursor.RequestOrdering);
            query = query.Where(row => row.CreatedAtUtc < createdAtUtc || (row.CreatedAtUtc == createdAtUtc && row.Id.CompareTo(id) < 0));
        }

        var rows = await query
            .OrderByDescending(row => row.CreatedAtUtc).ThenByDescending(row => row.Id)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);
        var hasMore = rows.Count > pageSize;
        var page = hasMore ? rows.Take(pageSize).ToList() : rows;
        var ids = page.Select(row => row.Id).ToArray();
        var lines = await dbContext.RequestLines.AsNoTracking().Where(line => ids.Contains(line.RequestId)).ToListAsync(cancellationToken);
        var requests = page.Select(row => SalesRequestRows.Restore(row, lines.Where(line => line.RequestId == row.Id))).ToArray();
        var last = page.LastOrDefault();
        return new SalesRequestPage(requests,
            hasMore && last is not null ? ListCursor.Encode(companyId, last.CreatedAtUtc, last.Id, ListCursor.RequestOrdering) : null);
    }

    private Task<bool> RequestAttemptExistsAsync(string commandName, string keyHash, CancellationToken cancellationToken) =>
        dbContext.RequestAttempts.AsNoTracking()
            .AnyAsync(row => row.CommandName == commandName && row.IdempotencyKeyHash == keyHash, cancellationToken);

    /// <summary>ADR-011 §3/§4: the same content of the same actor answers the stored record as it is now; anything else is one 409.</summary>
    private async Task<SalesRequestStoreResult> ReplayRequestAttemptAsync(RequestAttemptRow attempt, string actorId, string requestHash,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(attempt.ActorId, actorId, StringComparison.Ordinal)
            || !string.Equals(attempt.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw Refused(SalesDraftStoreCodes.SameKeyPayloadMismatch,
                "Aynı Idempotency-Key başka bir istekle geldi; ilk cevap bu isteğe verilmez.");
        }

        if (attempt.Outcome != AttemptOutcomeRecorded || attempt.RequestId is not { } recordedId)
        {
            // ADR-011 §7: the key holds a recorded terminal refusal; the caller replays it through FindRequestReplayAsync.
            throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Bu anahtar kayıtlı bir terminal reddi taşıyor; tekrar aynı reddi alır.");
        }

        var stored = await FindRequestAsync(recordedId, cancellationToken)
            ?? throw new InvalidOperationException("Kayıtlı deneme kaydının talebi bulunamadı.");
        return new SalesRequestStoreResult(stored, SalesDraftWriteOutcome.Replayed, attempt.CorrelationId);
    }

    /// <summary>The next display number of a series under the counter row's concurrency token; a lost race is the save's concurrent change.</summary>
    private async Task<long> NextDocumentNumberAsync(Guid companyId, string series, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var counter = await dbContext.DocumentCounters.SingleOrDefaultAsync(row => row.Series == series, cancellationToken);
        if (counter is null)
        {
            dbContext.DocumentCounters.Add(new DocumentCounterRow { CompanyId = companyId, Series = series, Next = 2, Version = 1, UpdatedAtUtc = now });
            return 1;
        }

        var number = counter.Next;
        counter.Next = checked(counter.Next + 1);
        counter.Version = checked(counter.Version + 1);
        counter.UpdatedAtUtc = now;
        return number;
    }

    private void AddRequestAttempt(Guid companyId, string commandName, string keyHash, string requestHash, string actorId, Guid requestId,
        SalesDraftWriteEnvelope envelope, DateTimeOffset now) =>
        dbContext.RequestAttempts.Add(new RequestAttemptRow
        {
            CompanyId = companyId,
            CommandName = commandName,
            IdempotencyKeyHash = keyHash,
            RequestHash = requestHash,
            ActorId = actorId,
            Outcome = AttemptOutcomeRecorded,
            RequestId = requestId,
            CorrelationId = envelope.CorrelationId.Trim(),
            RecordedAtUtc = now,
        });

    private static AuditEventDraft RequestAudit(SalesDraftWriteEnvelope envelope, Guid companyId, string eventType, string operation,
        SalesRequest request, long? versionBefore, string beforeJson, string afterJson, string reason) => new(
        companyId,
        eventType,
        RequestEntityType,
        request.Id.ToString("D"),
        operation,
        EntityVersionBefore: versionBefore,
        EntityVersionAfter: request.Version,
        AuditActorTypes.User,
        envelope.ActorId,
        envelope.PermissionCode,
        envelope.RoleCodes,
        beforeJson,
        afterJson,
        reason,
        ApprovalContextJson: null,
        AuditAiInvolvement.None,
        AiContextJson: null,
        envelope.SourceChannel,
        envelope.ClientIpAddress,
        envelope.UserAgent,
        envelope.CorrelationId,
        envelope.CausationId,
        envelope.IdempotencyKey);

    private static string RequestAuditState(SalesRequest request, bool withLines) => JsonSerializer.Serialize(new
    {
        request.Id,
        request.DocumentNo,
        state = request.State.ToString(),
        version = request.Version,
        decision = request.Decision,
        request.Header.PartyId,
        request.Header.CompanyAccountId,
        request.Header.BusinessDate,
        lines = withLines
            ? request.Lines.Select(line => new
            {
                line.LineNo,
                line.Id,
                line.Product.ProductId,
                line.Product.ProductVersion,
                line.OrderUnit.UnitId,
                quantity = line.QuantityText,
            }).ToArray()
            : null,
    }, Json);

    private static void RequireRequestCommand(string commandName)
    {
        if (!SalesRequestCommands.IsRequestCommand(commandName))
        {
            throw new ArgumentException("Bilinmeyen satış talebi komut adı.", nameof(commandName));
        }
    }

    /// <summary>Technical, company-scoped display text of a series; the counter, not the text, is the identity (Ö8).</summary>
    internal static string SeriesDocumentNo(string series, DateTimeOffset now, long number) =>
        string.Create(CultureInfo.InvariantCulture, $"{series}-{now.UtcDateTime.Year}-{number:D6}");
}
