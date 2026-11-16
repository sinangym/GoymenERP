using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Concurrency;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Quantities;
using GoymenERP.Modules.Purchasing.Domain.Requests;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

/// <summary>
/// The Purchasing store of the server-resolved company (PUR-4a; the Sales SALES-P2 / Logistics LOG-4a shape).
/// One call is one save; a stored record is rebuilt through the domain in recording order (lines, technical
/// slices, then the transition), so a corrupt row is refused, not repaired, and the stored version must be
/// the one the rebuild reaches. PUR-4b acceptance shares are replayed by identity after approval so the
/// persisted Accepted totals and aggregate version must agree. The Σ-shares rule of an approval (Sinan #66
/// 5738113623 §1) is measured inside the approval's save, and the source request's sourcing version moves
/// with it so two approvals against the same request collide on the request's row version.
/// </summary>
public sealed class PurchasingStore(PurchasingDbContext dbContext, ICompanyContext companyContext, IAuditEventCollector auditEvents, TimeProvider timeProvider)
    : IPurchasingStore
{
    private const string RequestEntityType = "Purchasing.PurchaseRequest";
    private const string OrderEntityType = "Purchasing.PurchaseOrder";
    private const int ActorMaximumLength = 128;
    private const int KeyMaximumLength = 200;
    private const int ProductVersionMaximumLength = 64;
    private const int MaximumPageSize = 100;
    private const int AttemptOutcomeRecorded = 1;
    private const int AttemptOutcomeRefused = 2;
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    // ---- requests -------------------------------------------------------------------------------

    public async Task<PurchasingRequestStoreResult> StoreRequestAsync(PurchasingWriteEnvelope envelope, string requestHash, DateOnly effectiveOn,
        Func<PurchaseRequest> build, IReadOnlyDictionary<Guid, string> productVersionsByLineId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(productVersionsByLineId);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        var companyId = RequireResolvedCompany();
        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        var actorId = envelope.ActorId.Trim();
        var attempt = await FindAttemptAsync(PurchasingCommands.RequestCreateV1, keyHash, cancellationToken);
        if (attempt is not null)
        {
            RequireSameContent(attempt, actorId, requestHash);
            if (attempt.Outcome != AttemptOutcomeRecorded || attempt.RequestId is not { } recordedId)
            {
                throw Refused(PurchasingStoreCodes.ConcurrentChange, "Bu anahtar kayıtlı bir terminal reddi taşıyor; tekrar aynı reddi alır.");
            }

            var stored = await FindRequestAsync(recordedId, cancellationToken) ?? throw new InvalidOperationException("Kayıtlı denemenin talebi bulunamadı.");
            return new PurchasingRequestStoreResult(stored, PurchasingWriteOutcome.Replayed, attempt.CorrelationId);
        }

        var request = build() ?? throw new ArgumentException("Talep kurulmadı.", nameof(build));
        if (request.CompanyId != companyId)
        {
            throw Refused(PurchasingStoreCodes.CompanyContextMismatch, "Talep aktif şirket bağlamının şirketine ait değil.");
        }

        if (request.Status != PurchaseRequestStatus.Draft || request.Lines.Count == 0)
        {
            throw new ArgumentException("Yeni talep satırlı bir taslaktır.", nameof(build));
        }

        if (await dbContext.Requests.AsNoTracking().AnyAsync(row => row.Id == request.Id, cancellationToken))
        {
            throw Refused(PurchasingStoreCodes.ConcurrentChange, "Bu talep kimliği şirketin başka bir talebinde kullanılıyor.");
        }

        var now = timeProvider.GetUtcNow();
        dbContext.Requests.Add(new PurchaseRequestRow
        {
            Id = request.Id,
            CompanyId = companyId,
            Status = (int)request.Status,
            Version = request.Version,
            SourcingVersion = 0,
            EffectiveOn = effectiveOn,
            CreatedAtUtc = request.CreatedAtUtc,
            UpdatedAtUtc = request.UpdatedAtUtc,
            CreatedBy = actorId,
            CorrelationId = envelope.CorrelationId.Trim(),
        });
        var lineNo = 0;
        foreach (var line in request.Lines)
        {
            dbContext.RequestLines.Add(new PurchaseRequestLineRow
            {
                CompanyId = companyId,
                RequestId = request.Id,
                LineNo = ++lineNo,
                LineId = line.Id,
                ProductId = line.ProductId,
                ProductVersion = ProductVersionOf(productVersionsByLineId, line.Id),
                UnitId = line.UnitId,
                UnitIsKilogram = line.Quantity.Unit == QuantityUnit.Kilogram,
                Quantity = RequireFits(line.Quantity.Value),
                SourceKind = line.Source is { } source ? (int)source.Kind : null,
                SourceDocumentId = line.Source?.DocumentId,
                SourceDocumentLineId = line.Source?.DocumentLineId,
            });
        }

        dbContext.Attempts.Add(Attempt(companyId, PurchasingCommands.RequestCreateV1, keyHash, requestHash, actorId, envelope, request.Id, null, now));
        Audit(envelope, companyId, RequestEntityType, request.Id, "purchasing.purchase-request.created.v1", "Create", null, request.Version,
            "{}", Snapshot(request), "Satınalma talebi açıldı");
        await SaveAsync(cancellationToken);
        return new PurchasingRequestStoreResult(new StoredPurchaseRequest(request, effectiveOn, Versions(productVersionsByLineId, request.Lines.Select(line => line.Id))),
            PurchasingWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }

    public async Task<PurchasingRequestStoreResult?> MutateRequestAsync(PurchasingWriteEnvelope envelope, string requestHash, string commandName,
        Guid requestId, Action<PurchaseRequest, PurchaseRequestBindingFacts, DateTimeOffset> mutate, PurchasingMutationAudit audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireCommand(commandName);
        RequireAudit(audit);
        if (!PurchasingCommands.IsRequestMutation(commandName))
        {
            throw new ArgumentException("Yalnız gönderme, onay ve ret komutları bir talebi değiştirir.", nameof(commandName));
        }

        var companyId = RequireResolvedCompany();
        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        var actorId = envelope.ActorId.Trim();
        var attempt = await FindAttemptAsync(commandName, keyHash, cancellationToken);
        if (attempt is not null)
        {
            RequireSameContent(attempt, actorId, requestHash);
            if (attempt.Outcome != AttemptOutcomeRecorded || attempt.RequestId is not { } recordedId)
            {
                throw Refused(PurchasingStoreCodes.ConcurrentChange, "Bu anahtar kayıtlı bir terminal reddi taşıyor; tekrar aynı reddi alır.");
            }

            var stored = await FindRequestAsync(recordedId, cancellationToken) ?? throw new InvalidOperationException("Kayıtlı denemenin talebi bulunamadı.");
            return new PurchasingRequestStoreResult(stored, PurchasingWriteOutcome.Replayed, attempt.CorrelationId);
        }

        if (requestId == Guid.Empty)
        {
            return null;
        }

        var row = await dbContext.Requests.SingleOrDefaultAsync(candidate => candidate.Id == requestId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var lineRows = await dbContext.RequestLines.AsNoTracking().Where(line => line.RequestId == requestId).OrderBy(line => line.LineNo)
            .ToListAsync(cancellationToken);
        var request = RestoreRequest(row, lineRows);
        // A1-1 (No.161 §1): whether an APPROVED order is bound to this request, read in this save; the request row is
        // the write's concurrency token and every binding order approval moves it (SourcingVersion), so the fact
        // cannot go stale unseen between this read and the commit.
        var approvedStatus = (int)PurchaseOrderStatus.Approved;
        var facts = new PurchaseRequestBindingFacts(await dbContext.Orders.AsNoTracking()
            .AnyAsync(order => order.SourceRequestId == requestId && order.Status == approvedStatus, cancellationToken));
        var versionBefore = request.Version;
        var before = Snapshot(request);
        var now = timeProvider.GetUtcNow();
        // The domain decides; a refusal leaves the tracked row untouched (nothing below has run).
        mutate(request, facts, now);
        if (request.Version != versionBefore + 1 || request.Id != row.Id || request.CompanyId != companyId || request.Lines.Count != lineRows.Count)
        {
            throw new InvalidOperationException("Bir komut tam olarak bir sürüm ilerletir ve satır eklemez.");
        }

        row.Status = (int)request.Status;
        row.Version = request.Version;
        row.UpdatedAtUtc = request.UpdatedAtUtc;
        row.SubmittedBy = request.SubmittedBy;
        row.ApprovedBy = request.Approval?.DecidedBy;
        row.ApprovedAtUtc = request.Approval?.DecidedAtUtc;
        row.RejectedBy = request.Rejection?.DecidedBy;
        row.RejectedAtUtc = request.Rejection?.DecidedAtUtc;
        row.RejectionReason = request.Rejection?.Reason;
        row.WithdrawnBy = request.Withdrawal?.DecidedBy;
        row.WithdrawnAtUtc = request.Withdrawal?.DecidedAtUtc;
        row.WithdrawalReason = request.Withdrawal?.Reason;
        dbContext.Attempts.Add(Attempt(companyId, commandName, keyHash, requestHash, actorId, envelope, request.Id, null, now));
        Audit(envelope, companyId, RequestEntityType, request.Id, audit.EventType, audit.Operation, versionBefore, request.Version, before, Snapshot(request), audit.Reason);
        await SaveAsync(cancellationToken);
        return new PurchasingRequestStoreResult(new StoredPurchaseRequest(request, row.EffectiveOn, VersionsOf(lineRows)),
            PurchasingWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }

    public async Task<StoredPurchaseRequest?> FindRequestAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        var row = await dbContext.Requests.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == requestId, cancellationToken);
        return row is null ? null : (await RestoreRequestsAsync([row], cancellationToken)).Single();
    }

    public async Task<PurchasingRequestPage> ListRequestsAsync(int pageSize, string? cursor, PurchaseRequestStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var companyId = RequireResolvedCompany();
        RequirePageSize(pageSize);
        var query = dbContext.Requests.AsNoTracking();
        if (status is { } wanted)
        {
            if (!Enum.IsDefined(wanted))
            {
                throw new ArgumentOutOfRangeException(nameof(status));
            }

            var value = (int)wanted;
            query = query.Where(row => row.Status == value);
        }

        if (cursor is not null)
        {
            var (createdAtUtc, id) = ListCursor.Decode(cursor, companyId);
            query = query.Where(row => row.CreatedAtUtc < createdAtUtc || (row.CreatedAtUtc == createdAtUtc && row.Id.CompareTo(id) < 0));
        }

        var rows = await query.OrderByDescending(row => row.CreatedAtUtc).ThenByDescending(row => row.Id).Take(pageSize + 1).ToListAsync(cancellationToken);
        var hasMore = rows.Count > pageSize;
        var page = hasMore ? rows.Take(pageSize).ToList() : rows;
        var last = page.LastOrDefault();
        return new PurchasingRequestPage(await RestoreRequestsAsync(page, cancellationToken),
            hasMore && last is not null ? ListCursor.Encode(companyId, last.CreatedAtUtc, last.Id) : null);
    }

    // ---- orders ---------------------------------------------------------------------------------

    public async Task<PurchasingOrderStoreResult> StoreOrderAsync(PurchasingWriteEnvelope envelope, string requestHash, Guid companyAccountId, DateOnly effectiveOn,
        Func<PurchaseOrder> build, IReadOnlyDictionary<Guid, string> productVersionsByLineId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(productVersionsByLineId);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        if (companyAccountId == Guid.Empty)
        {
            throw new ArgumentException("Tedarikçi cari hesabı boş olamaz.", nameof(companyAccountId));
        }

        var companyId = RequireResolvedCompany();
        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        var actorId = envelope.ActorId.Trim();
        var attempt = await FindAttemptAsync(PurchasingCommands.OrderCreateV1, keyHash, cancellationToken);
        if (attempt is not null)
        {
            RequireSameContent(attempt, actorId, requestHash);
            if (attempt.Outcome != AttemptOutcomeRecorded || attempt.OrderId is not { } recordedId)
            {
                throw Refused(PurchasingStoreCodes.ConcurrentChange, "Bu anahtar kayıtlı bir terminal reddi taşıyor; tekrar aynı reddi alır.");
            }

            var stored = await FindOrderAsync(recordedId, cancellationToken) ?? throw new InvalidOperationException("Kayıtlı denemenin siparişi bulunamadı.");
            return new PurchasingOrderStoreResult(stored, PurchasingWriteOutcome.Replayed, attempt.CorrelationId);
        }

        var order = build() ?? throw new ArgumentException("Sipariş kurulmadı.", nameof(build));
        if (order.CompanyId != companyId)
        {
            throw Refused(PurchasingStoreCodes.CompanyContextMismatch, "Sipariş aktif şirket bağlamının şirketine ait değil.");
        }

        if (order.Status != PurchaseOrderStatus.Draft || order.Lines.Count == 0 || order.Acceptances.Count > 0
            || order.Lines.Any(line => line.Kind != PurchaseOrderLineKind.General))
        {
            throw new ArgumentException("Yeni sipariş genel satırlı bir taslaktır; kabul payı ve teknik kırılımı olamaz.", nameof(build));
        }

        if (order.SourceRequestId is { } sourceRequestId
            && !await dbContext.Requests.AsNoTracking().AnyAsync(row => row.Id == sourceRequestId, cancellationToken))
        {
            throw new ArgumentException("Kaynak talep bu şirkette yok.", nameof(build));
        }

        if (await dbContext.Orders.AsNoTracking().AnyAsync(row => row.Id == order.Id, cancellationToken))
        {
            throw Refused(PurchasingStoreCodes.ConcurrentChange, "Bu sipariş kimliği şirketin başka bir siparişinde kullanılıyor.");
        }

        // A2a-5a (ALS-D3): the preparer the approval rule is checked against is the create envelope's actor (CreatedBy).
        order.StampPreparer(actorId);
        var now = timeProvider.GetUtcNow();
        dbContext.Orders.Add(new PurchaseOrderRow
        {
            Id = order.Id,
            CompanyId = companyId,
            SupplierPartyId = order.SupplierPartyId,
            CompanyAccountId = companyAccountId,
            SourceRequestId = order.SourceRequestId,
            Status = (int)order.Status,
            Version = order.Version,
            EffectiveOn = effectiveOn,
            CreatedAtUtc = order.CreatedAtUtc,
            UpdatedAtUtc = order.UpdatedAtUtc,
            CreatedBy = actorId,
            CorrelationId = envelope.CorrelationId.Trim(),
            // A2a-1 (TEK-01): the money snapshot the order was opened under, all five facts or none.
            CurrencyId = order.Money?.CurrencyId,
            CurrencyCode = order.Money?.CurrencyCode,
            MoneyProfileId = order.Money?.ProfileId,
            MoneyProfileVersion = order.Money?.ProfileVersion,
            MoneyScale = order.Money?.Scale,
            // A2a-1c (ALS-D72): the owner's unit-price scale, beside the snapshot (null when none was answered).
            UnitPriceScale = order.Money?.UnitPriceScale,
        });
        var lineNo = 0;
        foreach (var line in order.Lines)
        {
            dbContext.OrderLines.Add(new PurchaseOrderLineRow
            {
                CompanyId = companyId,
                OrderId = order.Id,
                LineNo = ++lineNo,
                LineId = line.Id,
                Kind = (int)line.Kind,
                ParentLineId = line.ParentLineId,
                SourceRequestLineId = line.SourceRequestLineId,
                ProductId = line.ProductId,
                ProductVersion = ProductVersionOf(productVersionsByLineId, line.Id),
                UnitId = line.UnitId,
                UnitIsKilogram = line.Quantity.Unit == QuantityUnit.Kilogram,
                Ordered = RequireFits(line.Quantity.Value),
                Technical = 0m,
                Accepted = 0m,
                Cancelled = 0m,
                UnitPrice = line.Price?.UnitPrice,
                PriceUnitId = line.Price?.PriceUnitId,
                DiscountPercent = line.Price?.DiscountPercent,
                DiscountAmount = line.Price?.DiscountAmount,
                // A2a-2 (ALS-D38): the maturity surcharge, written once with the price.
                DeferralPercent = line.Price?.DeferralPercent,
                DeferralAmount = line.Price?.DeferralAmount,
            });
        }

        if (order.LastExchangeRateChange is { } openingRate)
        {
            // ALC-SC33 (FNS-D79): the automatic default the order was opened with, recorded at the creation version (replayed on rebuild).
            if (openingRate.Previous is not null || openingRate.Current.Source != PurchaseOrderExchangeRateSource.Automatic
                || !ReferenceEquals(openingRate.Current, order.ExchangeRate))
            {
                throw new ArgumentException("Yeni siparişin kuru yalnız otomatik açılış kurudur.", nameof(build));
            }

            dbContext.ExchangeRateChanges.Add(ExchangeRateChangeRow(companyId, order.Id, OpeningExchangeRateVersion, openingRate,
                envelope.CorrelationId.Trim()));
        }
        else if (order.ExchangeRate is not null)
        {
            throw new ArgumentException("Siparişin kuru kaydı olmadan atanamaz.", nameof(build));
        }

        dbContext.Attempts.Add(Attempt(companyId, PurchasingCommands.OrderCreateV1, keyHash, requestHash, actorId, envelope, null, order.Id, now));
        Audit(envelope, companyId, OrderEntityType, order.Id, "purchasing.purchase-order.created.v1", "Create", null, order.Version,
            "{}", Snapshot(order), "Satınalma siparişi açıldı");
        await SaveAsync(cancellationToken);
        return new PurchasingOrderStoreResult(
            new StoredPurchaseOrder(order, companyAccountId, effectiveOn, Versions(productVersionsByLineId, order.Lines.Select(line => line.Id))),
            PurchasingWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }

    public Task<PurchasingOrderStoreResult?> MutateOrderAsync(PurchasingWriteEnvelope envelope, string requestHash, string commandName,
        Guid orderId, Action<PurchaseOrder, DateTimeOffset> mutate, PurchasingMutationAudit audit, CancellationToken cancellationToken = default) =>
        MutateOrderCoreAsync(envelope, requestHash, commandName, orderId, mutate, participant: null, audit, cancellationToken);

    public Task<PurchasingOrderStoreResult?> MutateOrderAsync(PurchasingWriteEnvelope envelope, string requestHash, string commandName,
        Guid orderId, Action<PurchaseOrder, DateTimeOffset> mutate, Func<PurchaseOrder, IDbTransaction, CancellationToken, Task> participant,
        PurchasingMutationAudit audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(participant);
        if (!PurchasingCommands.IsOrderCancellation(commandName) && !PurchasingCommands.IsLineClosure(commandName))
        {
            throw new ArgumentException("Yalnız kalan iptali ve satır kapanışı komutları bir katılımcı taşır.", nameof(commandName));
        }

        return MutateOrderCoreAsync(envelope, requestHash, commandName, orderId, mutate, participant, audit, cancellationToken);
    }

    /// <summary>A1-3 / API-11 / ALS-D85 and KLN-D105: serialize remainder and schedule changes with future allocation reduction.</summary>
    private static bool TakesOrderLineLocks(string commandName) =>
        PurchasingCommands.IsOrderCancellation(commandName) || PurchasingCommands.IsLineClosure(commandName)
        || commandName == PurchasingCommands.OrderLineScheduleV1;

    private async Task<PurchasingOrderStoreResult?> MutateOrderCoreAsync(PurchasingWriteEnvelope envelope, string requestHash, string commandName,
        Guid orderId, Action<PurchaseOrder, DateTimeOffset> mutate, Func<PurchaseOrder, IDbTransaction, CancellationToken, Task>? participant,
        PurchasingMutationAudit audit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireCommand(commandName);
        RequireAudit(audit);
        if (!PurchasingCommands.IsOrderMutation(commandName))
        {
            throw new ArgumentException("Yalnız onay, kalan iptali, termin, satır kapanışı ve kur komutları bir siparişi değiştirir.", nameof(commandName));
        }

        var companyId = RequireResolvedCompany();
        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        var actorId = envelope.ActorId.Trim();
        var attempt = await FindAttemptAsync(commandName, keyHash, cancellationToken);
        if (attempt is not null)
        {
            RequireSameContent(attempt, actorId, requestHash);
            if (attempt.Outcome != AttemptOutcomeRecorded || attempt.OrderId is not { } recordedId)
            {
                throw Refused(PurchasingStoreCodes.ConcurrentChange, "Bu anahtar kayıtlı bir terminal reddi taşıyor; tekrar aynı reddi alır.");
            }

            var stored = await FindOrderAsync(recordedId, cancellationToken) ?? throw new InvalidOperationException("Kayıtlı denemenin siparişi bulunamadı.");
            return new PurchasingOrderStoreResult(stored, PurchasingWriteOutcome.Replayed, attempt.CorrelationId);
        }

        if (orderId == Guid.Empty)
        {
            return null;
        }

        // F-017 F17-1 (§3.4): a remainder cancel changes every general line's cancelled share, so it takes every general line's
        // serialization lock (ascending, the one canonical order) in its own transaction BEFORE the order is read; an Inventory future
        // allocation or an acceptance share on one of those lines waits for, or is refused by, this write. Disposing the transaction
        // without its commit (a refusal, a domain refusal or a technical failure) rolls it back and releases the locks.
        // API-11 (§3.4): a line closure or reopening moves one general line's remainder (and its allocatable amount), so it takes the
        // same locks — every general line of the order, the canonical order, rather than a second lock path for the one line; the
        // command is rare and the order's lines are few.
        // KLN-D105: a schedule change can reorder the latest-delivery-first reduction choices. It uses these same locks,
        // so the owner schedule read remains stable until the Sales/Inventory transaction releases its purchase locks.
        await using var lockTransaction = TakesOrderLineLocks(commandName)
            ? await LockOrderGeneralLinesAsync(companyId, orderId, cancellationToken)
            : null;

        // The header and the lines are tracked (the header's version is this write's concurrency token).
        var row = await dbContext.Orders.SingleOrDefaultAsync(candidate => candidate.Id == orderId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var lineRows = await dbContext.OrderLines.Where(line => line.OrderId == orderId).OrderBy(line => line.LineNo).ToListAsync(cancellationToken);
        var acceptanceRows = await dbContext.Acceptances.AsNoTracking().Where(acceptance => acceptance.OrderId == orderId)
            .OrderBy(acceptance => acceptance.OrderVersionAfter).ThenBy(acceptance => acceptance.AcceptanceId).ToListAsync(cancellationToken);
        var sliceRows = await dbContext.DeliverySlices.AsNoTracking().Where(slice => slice.OrderId == orderId)
            .OrderBy(slice => slice.OrderVersionAfter).ThenBy(slice => slice.SliceNo).ToListAsync(cancellationToken);
        var closureRows = await dbContext.LineClosureEvents.AsNoTracking().Where(closure => closure.OrderId == orderId)
            .OrderBy(closure => closure.OrderVersionAfter).ToListAsync(cancellationToken);
        var rateRows = await dbContext.ExchangeRateChanges.AsNoTracking().Where(rate => rate.OrderId == orderId)
            .OrderBy(rate => rate.OrderVersionAfter).ToListAsync(cancellationToken);
        var order = RestoreOrder(row, lineRows, acceptanceRows, sliceRows, closureRows, rateRows);
        var versionBefore = order.Version;
        var rateChangeBefore = order.LastExchangeRateChange;
        var before = Snapshot(order);
        var schedulesBefore = order.Lines.ToDictionary(line => line.Id, line => line.DeliverySchedule);
        var closureChangesBefore = order.Lines.ToDictionary(line => line.Id, line => line.LastClosureChange);
        var now = timeProvider.GetUtcNow();
        // The domain decides; a refusal leaves the tracked rows untouched (nothing below has run).
        mutate(order, now);
        if (order.Version != versionBefore + 1 || order.Id != row.Id || order.CompanyId != companyId || order.Lines.Count != lineRows.Count
            || order.Acceptances.Count != acceptanceRows.Count)
        {
            throw new InvalidOperationException("Bir komut tam olarak bir sürüm ilerletir; satır ve kabul payı eklemez.");
        }

        // A3 API-30: the schedule command replaces exactly one line's schedule; every other command replaces none.
        var rescheduled = order.Lines.Where(line => !ReferenceEquals(schedulesBefore[line.Id], line.DeliverySchedule)).ToArray();
        if (rescheduled.Length != (commandName == PurchasingCommands.OrderLineScheduleV1 ? 1 : 0))
        {
            throw new InvalidOperationException("Termin komutu tam olarak bir satırın terminini değiştirir; başka komut termin değiştirmez.");
        }

        // API-11: a closure or reopening changes exactly one general line's closure; every other command changes none.
        var reclosed = order.Lines
            .Where(line => line.Kind == PurchaseOrderLineKind.General && !ReferenceEquals(closureChangesBefore[line.Id], line.LastClosureChange))
            .ToArray();
        if (reclosed.Length != (PurchasingCommands.IsLineClosure(commandName) ? 1 : 0))
        {
            throw new InvalidOperationException("Satır kapanışı komutu tam olarak bir genel satırın kapanışını değiştirir; başka komut değiştirmez.");
        }

        // ALC-SC33 (FNS-D79/D91): the rate command records exactly one rate change; every other command records none.
        var rateChanged = !ReferenceEquals(rateChangeBefore, order.LastExchangeRateChange);
        if (rateChanged != (commandName == PurchasingCommands.OrderExchangeRateChangeV1))
        {
            throw new InvalidOperationException("Kur komutu tam olarak bir kur değişikliği kaydeder; başka komut kuru değiştirmez.");
        }

        // A3 API-30: a schedule changes no ordered quantity, so only the schedule command is exempt; every other command keeps the check.
        // API-11: neither does a line closure or reopening (the ordered share stays; the closed remainder is not given back to the
        // request line's need, the ALS-D68 reading of a cancelled remainder), so it is exempt too.
        if (commandName != PurchasingCommands.OrderLineScheduleV1 && !PurchasingCommands.IsLineClosure(commandName)
            && order.Status == PurchaseOrderStatus.Approved && order.SourceRequestId is { } sourceRequestId)
        {
            await RequireSharesWithinNeedAsync(order, sourceRequestId, cancellationToken);
        }

        if (participant is not null)
        {
            // F-017 F17-4 (ALS-D85, §3.6): the Inventory owner's releases join this transaction while every general line lock is held;
            // a participant failure leaves the tracked rows unchanged and the transaction rolls back on dispose.
            var transaction = dbContext.Database.CurrentTransaction
                ?? throw new InvalidOperationException("Katılımcı için etkin işlem bulunamadı.");
            await participant(order, transaction.GetDbTransaction(), cancellationToken);
        }

        row.Status = (int)order.Status;
        row.Version = order.Version;
        row.UpdatedAtUtc = order.UpdatedAtUtc;
        row.ApprovedBy = order.Approval?.ApprovedBy;
        row.ApprovedAtUtc = order.Approval?.ApprovedAtUtc;
        row.CancelledBy = order.Cancellation?.CancelledBy;
        row.CancelledAtUtc = order.Cancellation?.CancelledAtUtc;
        row.CancellationReason = order.Cancellation?.Reason;
        foreach (var lineRow in lineRows)
        {
            var line = order.Lines.Single(candidate => candidate.Id == lineRow.LineId);
            lineRow.Technical = line.TechnicalQuantity.Value;
            lineRow.Accepted = line.AcceptedQuantity.Value;
            lineRow.Cancelled = line.CancelledQuantity.Value;
            lineRow.Closed = line.ClosedQuantity.Value;
        }

        foreach (var line in reclosed)
        {
            // API-11: the closure history (replayed on every rebuild) and the outbox message for the Inventory owner (F-017 §3.6),
            // in the same save as the line totals.
            var change = line.LastClosureChange ?? throw new InvalidOperationException("Satır kapanışı değişikliğinin kaydı yok.");
            if (change.Closed != line.IsClosed)
            {
                throw new InvalidOperationException("Satır kapanışı kaydı satırın durumuyla uyuşmuyor.");
            }

            dbContext.LineClosureEvents.Add(new PurchaseOrderLineClosureEventRow
            {
                CompanyId = companyId,
                OrderId = order.Id,
                LineId = line.Id,
                OrderVersionAfter = order.Version,
                Closed = change.Closed,
                Reason = change.Reason,
                RecordedBy = change.RecordedBy,
                RecordedAtUtc = change.RecordedAtUtc,
                CorrelationId = envelope.CorrelationId.Trim(),
            });
            dbContext.OutboxMessages.Add(LineClosedMessage(companyId, order, line.Id, change.Closed, audit, envelope, now));
        }

        foreach (var line in rescheduled)
        {
            foreach (var slice in line.DeliverySchedule)
            {
                dbContext.DeliverySlices.Add(new PurchaseOrderLineDeliverySliceRow
                {
                    CompanyId = companyId,
                    OrderId = order.Id,
                    LineId = line.Id,
                    OrderVersionAfter = order.Version,
                    SliceNo = slice.SliceNo,
                    SliceId = slice.SliceId,
                    Quantity = RequireFits(slice.Quantity.Value),
                    DeliveryDate = slice.DeliveryDate,
                    RecordedBy = actorId,
                    RecordedAtUtc = now,
                    CorrelationId = envelope.CorrelationId.Trim(),
                });
            }
        }

        if (rateChanged)
        {
            // ALC-SC33 (FNS-D91): the rate history (replayed on every rebuild); the audit below carries the old and the new rate.
            var rateChange = order.LastExchangeRateChange ?? throw new InvalidOperationException("Kur değişikliğinin kaydı yok.");
            dbContext.ExchangeRateChanges.Add(ExchangeRateChangeRow(companyId, order.Id, order.Version, rateChange, envelope.CorrelationId.Trim()));
        }

        dbContext.Attempts.Add(Attempt(companyId, commandName, keyHash, requestHash, actorId, envelope, null, order.Id, now));
        Audit(envelope, companyId, OrderEntityType, order.Id, audit.EventType, audit.Operation, versionBefore, order.Version, before, Snapshot(order), audit.Reason);
        await SaveAsync(cancellationToken);
        if (lockTransaction is not null)
        {
            await lockTransaction.CommitAsync(cancellationToken);
        }

        return new PurchasingOrderStoreResult(new StoredPurchaseOrder(order, row.CompanyAccountId, row.EffectiveOn, VersionsOf(lineRows)),
            PurchasingWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }

    public async Task<PurchasingOrderStoreResult?> ReleaseOrderFutureAllocationAsync(PurchasingWriteEnvelope envelope, string requestHash, Guid orderId,
        Func<PurchaseOrder, IDbTransaction, CancellationToken, Task<PurchasingLinkAuditSnapshots>> participant, PurchasingMutationAudit audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(participant);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireAudit(audit);
        const string commandName = PurchasingCommands.OrderFutureAllocationReleaseV1;
        var companyId = RequireResolvedCompany();
        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        var actorId = envelope.ActorId.Trim();
        var attempt = await FindAttemptAsync(commandName, keyHash, cancellationToken);
        if (attempt is not null)
        {
            RequireSameContent(attempt, actorId, requestHash);
            if (attempt.Outcome != AttemptOutcomeRecorded || attempt.OrderId is not { } recordedId)
            {
                throw Refused(PurchasingStoreCodes.ConcurrentChange, "Bu anahtar kayıtlı bir terminal reddi taşıyor; tekrar aynı reddi alır.");
            }

            var replayed = await FindOrderAsync(recordedId, cancellationToken) ?? throw new InvalidOperationException("Kayıtlı denemenin siparişi bulunamadı.");
            return new PurchasingOrderStoreResult(replayed, PurchasingWriteOutcome.Replayed, attempt.CorrelationId);
        }

        if (orderId == Guid.Empty)
        {
            return null;
        }

        // F-017 §3.5 F17-4 (ALS-D83): the same locks as a cancellation (every general line, ascending) before the order is read; the
        // Inventory owner re-takes the allocation's purchase line lock re-entrantly in this transaction.
        await using var lockTransaction = await LockOrderGeneralLinesAsync(companyId, orderId, cancellationToken);
        var row = await dbContext.Orders.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == orderId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var stored = (await RestoreOrdersAsync([row], cancellationToken)).Single();
        var transaction = dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Katılımcı için etkin işlem bulunamadı.");
        var snapshots = await participant(stored.Order, transaction.GetDbTransaction(), cancellationToken);
        if (snapshots is null || string.IsNullOrWhiteSpace(snapshots.Before) || string.IsNullOrWhiteSpace(snapshots.After))
        {
            throw new InvalidOperationException("Bağ düşürme audit kaydı önce/sonra görüntüsü ister.");
        }

        var now = timeProvider.GetUtcNow();
        var version = stored.Order.Version;
        dbContext.Attempts.Add(Attempt(companyId, commandName, keyHash, requestHash, actorId, envelope, null, stored.Order.Id, now));
        // The order is not changed (ALS-D83: the purchase quantities stay); its audit record names the released link.
        Audit(envelope, companyId, OrderEntityType, stored.Order.Id, audit.EventType, audit.Operation, version, version, snapshots.Before,
            snapshots.After, audit.Reason);
        await SaveAsync(cancellationToken);
        if (lockTransaction is not null)
        {
            await lockTransaction.CommitAsync(cancellationToken);
        }

        return new PurchasingOrderStoreResult(stored, PurchasingWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }

    public async Task<PurchasingOrderLinkRead<T>?> ReadOrderFutureAllocationsAsync<T>(Guid orderId,
        Func<PurchaseOrder, IDbTransaction, CancellationToken, Task<T>> read, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(read);
        RequireResolvedCompany();
        if (orderId == Guid.Empty)
        {
            return null;
        }

        var owned = dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            var stored = await FindOrderAsync(orderId, cancellationToken);
            if (stored is null)
            {
                return null;
            }

            var transaction = dbContext.Database.CurrentTransaction
                ?? throw new InvalidOperationException("Okuma için etkin işlem bulunamadı.");
            var value = await read(stored.Order, transaction.GetDbTransaction(), cancellationToken);
            if (owned is not null)
            {
                // Nothing was written; the commit only ends the read transaction.
                await owned.CommitAsync(cancellationToken);
            }

            return new PurchasingOrderLinkRead<T>(stored, value);
        }
        finally
        {
            if (owned is not null)
            {
                await owned.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// F-017 F17-1 (§3.4): takes the serialization lock of every general line of the order in one transaction and returns that
    /// transaction for the caller to commit; when the caller already runs inside a transaction the locks join it and null is returned
    /// (the outer owner commits). A lock that is not granted in time refuses the write with <see cref="PurchasingStoreCodes.LineBusy"/>
    /// and nothing is written. An order without lines (or not in this company) takes no lock; the caller's own read answers it.
    /// </summary>
    private async Task<IDbContextTransaction?> LockOrderGeneralLinesAsync(Guid companyId, Guid orderId, CancellationToken cancellationToken)
    {
        var owned = dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            var transaction = dbContext.Database.CurrentTransaction
                ?? throw new InvalidOperationException("Satır kilidi için etkin işlem bulunamadı.");
            var generalLineIds = await dbContext.OrderLines.AsNoTracking()
                .Where(line => line.OrderId == orderId && line.Kind == (int)PurchaseOrderLineKind.General)
                .Select(line => line.LineId)
                .ToListAsync(cancellationToken);
            if (generalLineIds.Count > 0)
            {
                await PurchaseLineSerializationLock.Bind(dbContext.Database.GetDbConnection(), transaction.GetDbTransaction(), companyId)
                    .AcquireAllAsync(generalLineIds, cancellationToken);
            }

            return owned;
        }
        catch (PurchaseLineBusyException exception)
        {
            if (owned is not null)
            {
                await owned.DisposeAsync();
            }

            throw Refused(PurchasingStoreCodes.LineBusy, "Sipariş satırı başka bir işlem tarafından kilitli; hiçbir şey kaydedilmedi.", exception);
        }
        catch
        {
            if (owned is not null)
            {
                await owned.DisposeAsync();
            }

            throw;
        }
    }

    /// <summary>
    /// Sinan #66 5738113623 §1: the ordered quantities of the APPROVED orders' general lines bound to one request
    /// line, plus this order's, never exceed the request line's quantity. Measured in the approval's save; the
    /// request's sourcing version moves with every passing approval so a concurrent approval collides and measures again.
    /// <para>
    /// A1-3, ALS-D68 (EK10, Sinan 2026-11-03: "Hayır, yeni talep gerekir"): a CANCELLED order keeps counting with its full ordered
    /// share — No.161 §2 removes the cancelled remainder only from the future supply and the order's remaining; the request line's
    /// need is NOT given back, so the cancelled quantity is bought from another supplier through a new request.
    /// </para>
    /// </summary>
    private async Task RequireSharesWithinNeedAsync(PurchaseOrder order, Guid sourceRequestId, CancellationToken cancellationToken)
    {
        var requestRow = await dbContext.Requests.SingleOrDefaultAsync(candidate => candidate.Id == sourceRequestId, cancellationToken)
            ?? throw new InvalidOperationException("Siparişin kaynak talebi bu şirkette bulunamadı.");
        if (requestRow.Status != (int)PurchaseRequestStatus.Approved)
        {
            // A1-1 (ALS-D2, fail-closed): a draft order of a request that is not (or no longer) approved never becomes a
            // commitment; the request row is tracked, so a concurrent rejection collides on its row version.
            dbContext.ChangeTracker.Clear();
            throw new PurchasingStoreException(PurchasingStoreCodes.SourceRequestNotApproved, "Siparişin kaynak talebi onaylı değil.", null,
                [new PurchasingFieldError("sourceRequestId", "not_approved")]);
        }

        var requestLines = await dbContext.RequestLines.AsNoTracking().Where(line => line.RequestId == sourceRequestId).ToListAsync(cancellationToken);
        var approvedStatus = (int)PurchaseOrderStatus.Approved;
        var cancelledStatus = (int)PurchaseOrderStatus.Cancelled;
        var otherApprovedOrderIds = await dbContext.Orders.AsNoTracking()
            .Where(candidate => candidate.SourceRequestId == sourceRequestId
                                && (candidate.Status == approvedStatus || candidate.Status == cancelledStatus) && candidate.Id != order.Id)
            .Select(candidate => candidate.Id)
            .ToListAsync(cancellationToken);
        var generalKind = (int)PurchaseOrderLineKind.General;
        List<PurchaseOrderLineRow> approvedShares = otherApprovedOrderIds.Count == 0
            ? new List<PurchaseOrderLineRow>()
            : await dbContext.OrderLines.AsNoTracking()
                .Where(line => otherApprovedOrderIds.Contains(line.OrderId) && line.Kind == generalKind && line.SourceRequestLineId != null)
                .ToListAsync(cancellationToken);
        var errors = new List<PurchasingFieldError>();
        for (var index = 0; index < order.Lines.Count; index++)
        {
            var line = order.Lines[index];
            if (line.Kind != PurchaseOrderLineKind.General || line.SourceRequestLineId is not { } sourceLineId)
            {
                continue;
            }

            var requestLine = requestLines.SingleOrDefault(candidate => candidate.LineId == sourceLineId)
                ?? throw new InvalidOperationException("Siparişin bağlı olduğu talep satırı bulunamadı.");
            var sum = approvedShares.Where(share => share.SourceRequestLineId == sourceLineId).Sum(share => share.Ordered) + line.Quantity.Value;
            if (sum > requestLine.Quantity)
            {
                errors.Add(new PurchasingFieldError($"lines[{index}].sourceRequestLineId", PurchasingStoreCodes.RequestLineOverSourced));
            }
        }

        if (errors.Count > 0)
        {
            dbContext.ChangeTracker.Clear();
            throw new PurchasingStoreException(PurchasingStoreCodes.RequestLineOverSourced, "Talep satırının onaylı payları ihtiyacını aşıyor.", null, errors);
        }

        requestRow.SourcingVersion = checked(requestRow.SourcingVersion + 1);
    }

    public async Task<StoredPurchaseOrder?> FindOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        var row = await dbContext.Orders.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == orderId, cancellationToken);
        return row is null ? null : (await RestoreOrdersAsync([row], cancellationToken)).Single();
    }

    public async Task<IReadOnlyList<StoredScheduleRevision>> ListLineScheduleRevisionsAsync(Guid orderId, Guid lineId,
        CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        // F-027: the append-only slice rows of one line, read as stored; the tenant filter keeps another company's rows invisible.
        var rows = await dbContext.DeliverySlices.AsNoTracking()
            .Where(slice => slice.OrderId == orderId && slice.LineId == lineId)
            .OrderBy(slice => slice.OrderVersionAfter).ThenBy(slice => slice.SliceNo)
            .ToListAsync(cancellationToken);
        return [.. rows.GroupBy(slice => slice.OrderVersionAfter).OrderBy(revision => revision.Key).Select(revision =>
        {
            var slices = revision.OrderBy(slice => slice.SliceNo).ToArray();
            var first = slices[0];
            if (slices.Any(slice => !string.Equals(slice.RecordedBy, first.RecordedBy, StringComparison.Ordinal)
                || slice.RecordedAtUtc != first.RecordedAtUtc))
            {
                throw new PurchasingStoreException(PurchasingStoreCodes.ScheduleHistoryInconsistent,
                    "Kayıtlı termin revizyonu tek bir kayıt olarak okunmuyor.");
            }

            return new StoredScheduleRevision(revision.Key, first.RecordedBy, first.RecordedAtUtc,
                [.. slices.Select(slice => new StoredScheduleSlice(slice.SliceId, slice.SliceNo, slice.Quantity, slice.DeliveryDate))]);
        })];
    }

    public async Task<PurchasingOrderPage> ListOrdersAsync(int pageSize, string? cursor, PurchaseOrderStatus? status = null, Guid? supplierPartyId = null,
        CancellationToken cancellationToken = default)
    {
        var companyId = RequireResolvedCompany();
        RequirePageSize(pageSize);
        var query = dbContext.Orders.AsNoTracking();
        if (status is { } wanted)
        {
            if (!Enum.IsDefined(wanted))
            {
                throw new ArgumentOutOfRangeException(nameof(status));
            }

            var value = (int)wanted;
            query = query.Where(row => row.Status == value);
        }

        if (supplierPartyId is { } supplier)
        {
            if (supplier == Guid.Empty)
            {
                throw new ArgumentOutOfRangeException(nameof(supplierPartyId));
            }

            query = query.Where(row => row.SupplierPartyId == supplier);
        }

        if (cursor is not null)
        {
            var (createdAtUtc, id) = ListCursor.Decode(cursor, companyId);
            query = query.Where(row => row.CreatedAtUtc < createdAtUtc || (row.CreatedAtUtc == createdAtUtc && row.Id.CompareTo(id) < 0));
        }

        var rows = await query.OrderByDescending(row => row.CreatedAtUtc).ThenByDescending(row => row.Id).Take(pageSize + 1).ToListAsync(cancellationToken);
        var hasMore = rows.Count > pageSize;
        var page = hasMore ? rows.Take(pageSize).ToList() : rows;
        var last = page.LastOrDefault();
        return new PurchasingOrderPage(await RestoreOrdersAsync(page, cancellationToken),
            hasMore && last is not null ? ListCursor.Encode(companyId, last.CreatedAtUtc, last.Id) : null);
    }

    // ---- replay ---------------------------------------------------------------------------------

    public async Task<PurchasingReplay?> FindReplayAsync(PurchasingWriteEnvelope envelope, string requestHash, string commandName,
        CancellationToken cancellationToken = default)
    {
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireCommand(commandName);
        RequireResolvedCompany();
        var attempt = await FindAttemptAsync(commandName, IdempotencyKeyHash(envelope.IdempotencyKey), cancellationToken);
        if (attempt is null)
        {
            return null;
        }

        RequireSameContent(attempt, envelope.ActorId.Trim(), requestHash);
        if (attempt.Outcome == AttemptOutcomeRecorded)
        {
            if (attempt.RequestId is { } requestId)
            {
                var request = await FindRequestAsync(requestId, cancellationToken) ?? throw new InvalidOperationException("Kayıtlı denemenin talebi bulunamadı.");
                return new PurchasingReplay(request, null, null, attempt.CorrelationId);
            }

            if (attempt.OrderId is { } orderId)
            {
                var order = await FindOrderAsync(orderId, cancellationToken) ?? throw new InvalidOperationException("Kayıtlı denemenin siparişi bulunamadı.");
                return new PurchasingReplay(null, order, null, attempt.CorrelationId);
            }

            throw new InvalidOperationException("Kayıtlı deneme bir sonuç taşımıyor.");
        }

        var refusal = attempt.RefusalJson is null ? null : JsonSerializer.Deserialize<PurchasingTerminalRefusal>(attempt.RefusalJson, Json);
        if (refusal is null || refusal.Status is < 400 or > 599 || string.IsNullOrWhiteSpace(refusal.Code) || refusal.Errors is null)
        {
            throw new InvalidOperationException("Kayıtlı terminal ret okunamadı.");
        }

        return new PurchasingReplay(null, null, refusal, attempt.CorrelationId);
    }

    public async Task RecordRefusalAsync(PurchasingWriteEnvelope envelope, string requestHash, string commandName, PurchasingTerminalRefusal refusal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireCommand(commandName);
        var companyId = RequireResolvedCompany();
        if (refusal.Status is < 400 or > 599 || string.IsNullOrWhiteSpace(refusal.Code) || refusal.Errors is null
            || refusal.Errors.Any(error => error is null || string.IsNullOrWhiteSpace(error.Field) || string.IsNullOrWhiteSpace(error.Code)))
        {
            throw new ArgumentException("Terminal ret bir durum, bir kod ve tam alan hataları taşır.", nameof(refusal));
        }

        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        if (await FindAttemptAsync(commandName, keyHash, cancellationToken) is not null)
        {
            throw Refused(PurchasingStoreCodes.ConcurrentChange, "Bu anahtar zaten bir deneme taşıyor; tekrar onu alır.");
        }

        var row = Attempt(companyId, commandName, keyHash, requestHash, envelope.ActorId.Trim(), envelope, null, null, timeProvider.GetUtcNow());
        row.Outcome = AttemptOutcomeRefused;
        row.RefusalJson = JsonSerializer.Serialize(refusal, Json);
        dbContext.Attempts.Add(row);
        await SaveAsync(cancellationToken);
    }

    // ---- restore --------------------------------------------------------------------------------

    private async Task<IReadOnlyList<StoredPurchaseRequest>> RestoreRequestsAsync(IReadOnlyList<PurchaseRequestRow> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(row => row.Id).ToArray();
        var lines = await dbContext.RequestLines.AsNoTracking().Where(line => ids.Contains(line.RequestId)).ToListAsync(cancellationToken);
        return [.. rows.Select(row =>
        {
            var own = lines.Where(line => line.RequestId == row.Id).OrderBy(line => line.LineNo).ToList();
            return new StoredPurchaseRequest(RestoreRequest(row, own), row.EffectiveOn, VersionsOf(own));
        })];
    }

    /// <summary>
    /// Rebuilds the request through the domain in recording order (lines, submission, approval, rejection or withdrawal); the stored
    /// version and status must be the ones the rebuild reaches. A submission recorded without its actor (before A1-1) is
    /// rebuilt without one, so the domain refuses any decision on it (fail-closed).
    /// </summary>
    private static PurchaseRequest RestoreRequest(PurchaseRequestRow row, IReadOnlyList<PurchaseRequestLineRow> lines)
    {
        var request = PurchaseRequest.Create(row.Id, row.CompanyId, row.CreatedBy, row.CreatedAtUtc);
        foreach (var line in lines)
        {
            request.AddLine(request.Version, line.LineId, line.ProductId, line.UnitId, Quantity.Of(line.Quantity, UnitOf(line.UnitIsKilogram)), row.CreatedAtUtc,
                SourceOf(line));
        }

        if (row.Status != (int)PurchaseRequestStatus.Draft)
        {
            if (row.SubmittedBy is { } submittedBy)
            {
                request.Submit(request.Version, submittedBy, row.UpdatedAtUtc);
            }
            else
            {
                request.Submit(request.Version, row.UpdatedAtUtc);
            }
        }

        if (row.ApprovedBy is { } approvedBy)
        {
            request.Approve(request.Version, approvedBy,
                row.ApprovedAtUtc ?? throw new InvalidOperationException("Kayıtlı talep onayının zamanı yok."));
        }

        if (row.Status == (int)PurchaseRequestStatus.Rejected)
        {
            // The bound-order rule held when the rejection was recorded; the rebuild does not measure it again.
            request.Reject(request.Version, row.RejectedBy ?? throw new InvalidOperationException("Kayıtlı talep reddinin aktörü yok."),
                row.RejectionReason, hasApprovedOrder: false,
                row.RejectedAtUtc ?? throw new InvalidOperationException("Kayıtlı talep reddinin zamanı yok."));
        }

        if (row.Status == (int)PurchaseRequestStatus.Withdrawn)
        {
            // A1-1b: the bound-order rule held when the withdrawal was recorded; the rebuild does not measure it again.
            request.Withdraw(request.Version, row.WithdrawnBy ?? throw new InvalidOperationException("Kayıtlı talep geri çekmesinin aktörü yok."),
                row.WithdrawalReason, hasApprovedOrder: false,
                row.WithdrawnAtUtc ?? throw new InvalidOperationException("Kayıtlı talep geri çekmesinin zamanı yok."));
        }

        if (request.Version != row.Version || request.Status != (PurchaseRequestStatus)row.Status)
        {
            throw new InvalidOperationException("Kayıtlı talebin sürümü satırlarından yeniden kurulan sürümle uyuşmuyor.");
        }

        return request;
    }

    /// <summary>
    /// A1-2: the stored source through the domain (null = manual). A stored kind the domain does not define, or a kind
    /// without its identities, is never read as manual: the rebuild fails (the CHECK keeps such a row out).
    /// </summary>
    private static PurchaseRequestLineSource? SourceOf(PurchaseRequestLineRow line) =>
        line.SourceKind is not { } kind
            ? (line.SourceDocumentId is null && line.SourceDocumentLineId is null
                ? null
                : throw new InvalidOperationException("Kayıtlı talep satırı kaynağının türü yok."))
            : PurchaseRequestLineSource.Of((PurchaseRequestSourceKind)kind,
                line.SourceDocumentId ?? throw new InvalidOperationException("Kayıtlı talep satırı kaynağının belgesi yok."),
                line.SourceDocumentLineId ?? throw new InvalidOperationException("Kayıtlı talep satırı kaynağının belge satırı yok."));

    private async Task<IReadOnlyList<StoredPurchaseOrder>> RestoreOrdersAsync(IReadOnlyList<PurchaseOrderRow> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(row => row.Id).ToArray();
        var lines = await dbContext.OrderLines.AsNoTracking().Where(line => ids.Contains(line.OrderId)).ToListAsync(cancellationToken);
        var acceptances = await dbContext.Acceptances.AsNoTracking().Where(acceptance => ids.Contains(acceptance.OrderId)).ToListAsync(cancellationToken);
        var slices = await dbContext.DeliverySlices.AsNoTracking().Where(slice => ids.Contains(slice.OrderId)).ToListAsync(cancellationToken);
        var closures = await dbContext.LineClosureEvents.AsNoTracking().Where(closure => ids.Contains(closure.OrderId)).ToListAsync(cancellationToken);
        var rates = await dbContext.ExchangeRateChanges.AsNoTracking().Where(rate => ids.Contains(rate.OrderId)).ToListAsync(cancellationToken);
        return [.. rows.Select(row =>
        {
            var own = lines.Where(line => line.OrderId == row.Id).OrderBy(line => line.LineNo).ToList();
            var ownAcceptances = acceptances.Where(acceptance => acceptance.OrderId == row.Id)
                .OrderBy(acceptance => acceptance.OrderVersionAfter).ThenBy(acceptance => acceptance.AcceptanceId).ToList();
            var ownSlices = slices.Where(slice => slice.OrderId == row.Id).ToList();
            var ownClosures = closures.Where(closure => closure.OrderId == row.Id).OrderBy(closure => closure.OrderVersionAfter).ToList();
            var ownRates = rates.Where(rate => rate.OrderId == row.Id).OrderBy(rate => rate.OrderVersionAfter).ToList();
            return new StoredPurchaseOrder(RestoreOrder(row, own, ownAcceptances, ownSlices, ownClosures, ownRates), row.CompanyAccountId, row.EffectiveOn,
                VersionsOf(own));
        })];
    }

    /// <summary>
    /// Rebuilds the order through the domain in recording order (general lines, technical slices, approval, acceptance shares);
    /// the stored version and totals must be the ones the rebuild reaches. A3 API-30: each delivery-schedule revision is replayed at
    /// the version it produced — before the approval when it was recorded on the Draft, otherwise between the acceptance shares in
    /// version order — so every revision passes the domain's rules again (Σ slices = the line's ordered quantity). API-11: each line
    /// closure or reopening is replayed the same way at the version it produced, so the stored Closed totals and the line's current
    /// closure (who, when, why) are the domain's again.
    /// </summary>
    internal static PurchaseOrder RestoreOrder(
        PurchaseOrderRow row,
        IReadOnlyList<PurchaseOrderLineRow> lines,
        IReadOnlyList<PurchaseAcceptanceRow>? acceptances = null,
        IReadOnlyList<PurchaseOrderLineDeliverySliceRow>? deliverySlices = null,
        IReadOnlyList<PurchaseOrderLineClosureEventRow>? closureEvents = null,
        IReadOnlyList<PurchaseOrderExchangeRateChangeRow>? exchangeRateChanges = null)
    {
        var money = MoneyOf(row);
        var order = row.SourceRequestId is { } sourceRequestId
            ? PurchaseOrder.CreateForRequest(row.Id, row.CompanyId, row.SupplierPartyId, sourceRequestId, money, row.CreatedAtUtc, row.CreatedBy)
            : PurchaseOrder.Create(row.Id, row.CompanyId, row.SupplierPartyId, money, row.CreatedAtUtc, row.CreatedBy);
        // ALC-SC33 (FNS-D79): the automatic opening rate is part of the opening (no version of its own); each later change is replayed
        // at the version it produced, on the Draft, between the schedule revisions.
        var rateChanges = new Queue<PurchaseOrderExchangeRateChangeRow>((exchangeRateChanges ?? []).OrderBy(rate => rate.OrderVersionAfter));
        if (rateChanges.TryPeek(out var openingRate) && openingRate.OrderVersionAfter == OpeningExchangeRateVersion)
        {
            RequireOwnRate(row, rateChanges.Dequeue());
            order.AssignDefaultExchangeRate(ExchangeRateOf(openingRate), openingRate.RecordedBy, openingRate.RecordedAtUtc);
        }

        foreach (var line in lines)
        {
            var quantity = Quantity.Of(line.Ordered, UnitOf(line.UnitIsKilogram));
            switch ((PurchaseOrderLineKind)line.Kind)
            {
                case PurchaseOrderLineKind.General:
                    order.AddLine(order.Version, line.LineId, line.ProductId, line.UnitId, quantity, line.SourceRequestLineId, PriceOf(line),
                        row.CreatedAtUtc);
                    break;
                case PurchaseOrderLineKind.Technical:
                    if (PriceOf(line) is not null)
                    {
                        throw new InvalidOperationException("Kayıtlı teknik satır fiyat taşıyor; fiyat genel satırdadır.");
                    }

                    order.BreakDown(order.Version, line.ParentLineId ?? Guid.Empty, line.LineId, line.ProductId, quantity, row.UpdatedAtUtc);
                    break;
                default:
                    throw new InvalidOperationException("Kayıtlı satır türü bilinmiyor.");
            }
        }

        var revisions = new Queue<PurchaseOrderLineDeliverySliceRow[]>((deliverySlices ?? [])
            .GroupBy(slice => slice.OrderVersionAfter)
            .OrderBy(revision => revision.Key)
            .Select(revision => revision.OrderBy(slice => slice.SliceNo).ToArray()));
        // A3 API-30: the revisions recorded on the Draft come before the approval; ALC-SC33: so do the rate changes, in version order.
        while (true)
        {
            var draftVersionAfter = checked(order.Version + 1);
            if (revisions.TryPeek(out var draftRevision) && draftRevision[0].OrderVersionAfter == draftVersionAfter)
            {
                ReplaySchedule(order, row, lines, revisions.Dequeue());
                continue;
            }

            if (rateChanges.TryPeek(out var rateChange) && rateChange.OrderVersionAfter == draftVersionAfter)
            {
                RequireOwnRate(row, rateChanges.Dequeue());
                order.SetExchangeRate(order.Version, ExchangeRateOf(rateChange), rateChange.RecordedBy, rateChange.Reason, rateChange.RecordedAtUtc);
                continue;
            }

            break;
        }

        if (rateChanges.Count > 0)
        {
            // FNS-D80: the rate is locked at approval; a rate row past the Draft history is not the domain's.
            throw new InvalidOperationException("Kayıtlı kur değişikliği taslak sipariş sürümünde ardışık değil.");
        }

        if (row.Status is (int)PurchaseOrderStatus.Approved or (int)PurchaseOrderStatus.Cancelled)
        {
            // A2a-5a (ALS-D3): a recorded approver is replayed through the maker ≠ checker rule; an order approved before A2a-5a
            // has none and is replayed without one (the fact is not invented, the rule is not applied backwards).
            if (row.ApprovedBy is { } approvedBy)
            {
                order.Approve(order.Version, approvedBy,
                    row.ApprovedAtUtc ?? throw new InvalidOperationException("Kayıtlı sipariş onayının zamanı yok."));
            }
            else
            {
                order.RestoreLegacyApproval(order.Version, row.UpdatedAtUtc);
            }
        }

        var orderedAcceptances = new Queue<PurchaseAcceptanceRow>((acceptances ?? [])
            .OrderBy(acceptance => acceptance.OrderVersionAfter)
            .ThenBy(acceptance => acceptance.AcceptanceId));
        var orderedClosures = new Queue<PurchaseOrderLineClosureEventRow>((closureEvents ?? [])
            .OrderBy(closure => closure.OrderVersionAfter));
        while (orderedAcceptances.Count > 0 || revisions.Count > 0 || orderedClosures.Count > 0)
        {
            var expectedVersionAfter = checked(order.Version + 1);
            if (revisions.TryPeek(out var revision) && revision[0].OrderVersionAfter == expectedVersionAfter)
            {
                ReplaySchedule(order, row, lines, revisions.Dequeue());
                continue;
            }

            if (orderedClosures.TryPeek(out var closureEvent) && closureEvent.OrderVersionAfter == expectedVersionAfter)
            {
                ReplayClosure(order, row, lines, orderedClosures.Dequeue());
                continue;
            }

            if (!orderedAcceptances.TryDequeue(out var acceptance))
            {
                throw new InvalidOperationException("Kayıtlı termin revizyonu veya satır kapanışı sipariş sürümünde ardışık değil.");
            }

            if (acceptance.CompanyId != row.CompanyId || acceptance.OrderId != row.Id
                || acceptance.OrderVersionAfter != expectedVersionAfter)
            {
                throw new InvalidOperationException("Kayıtlı kabul payı sipariş sürümünde ardışık değil.");
            }

            var sourceLine = lines.SingleOrDefault(line => line.LineId == acceptance.LineId)
                ?? throw new InvalidOperationException("Kayıtlı kabul payının sipariş satırı bulunamadı.");
            order.RecordAcceptance(
                order.Version,
                acceptance.AcceptanceId,
                acceptance.LineId,
                Quantity.Of(acceptance.Quantity, UnitOf(sourceLine.UnitIsKilogram)),
                acceptance.RecordedAtUtc);
            if (order.Version != acceptance.OrderVersionAfter)
            {
                throw new InvalidOperationException("Kayıtlı kabul payının sürümü domain sürümüyle uyuşmuyor.");
            }
        }

        if (row.Status == (int)PurchaseOrderStatus.Cancelled)
        {
            // A1-3: the cancellation follows every acceptance share (none is recorded on a cancelled order); the remainder it
            // cancelled is rebuilt by the domain and must equal the stored Cancelled totals below.
            order.Cancel(order.Version, row.CancelledBy ?? throw new InvalidOperationException("Kayıtlı sipariş iptalinin aktörü yok."),
                row.CancellationReason,
                row.CancelledAtUtc ?? throw new InvalidOperationException("Kayıtlı sipariş iptalinin zamanı yok."));
        }

        if (order.Version != row.Version || order.Status != (PurchaseOrderStatus)row.Status)
        {
            throw new InvalidOperationException("Kayıtlı siparişin sürümü satırlarından yeniden kurulan sürümle uyuşmuyor.");
        }

        foreach (var line in lines)
        {
            var rebuilt = order.Lines.Single(candidate => candidate.Id == line.LineId);
            if (rebuilt.TechnicalQuantity.Value != line.Technical || rebuilt.AcceptedQuantity.Value != line.Accepted
                || rebuilt.CancelledQuantity.Value != line.Cancelled || rebuilt.ClosedQuantity.Value != line.Closed)
            {
                throw new InvalidOperationException("Kayıtlı satır toplamları yeniden kurulan toplamlarla uyuşmuyor.");
            }
        }

        return order;
    }

    /// <summary>ALC-SC33 (FNS-D79): the opening rate is recorded at the order's creation version.</summary>
    internal const long OpeningExchangeRateVersion = 1;

    private static void RequireOwnRate(PurchaseOrderRow row, PurchaseOrderExchangeRateChangeRow rate)
    {
        if (rate.CompanyId != row.CompanyId || rate.OrderId != row.Id)
        {
            throw new InvalidOperationException("Kayıtlı kur başka bir siparişe ait.");
        }
    }

    /// <summary>ALC-SC33: a stored rate through the domain record, so its bounds and its source/bulletin shape apply again.</summary>
    private static PurchaseOrderExchangeRate ExchangeRateOf(PurchaseOrderExchangeRateChangeRow rate) =>
        new(rate.Rate, rate.TargetCurrencyId, (PurchaseOrderExchangeRateKind)rate.Kind, rate.RateDay,
            (PurchaseOrderExchangeRateSource)rate.Source, rate.BulletinDate);

    private static PurchaseOrderExchangeRateChangeRow ExchangeRateChangeRow(Guid companyId, Guid orderId, long orderVersionAfter,
        PurchaseOrderExchangeRateChange change, string correlationId) => new()
        {
            CompanyId = companyId,
            OrderId = orderId,
            OrderVersionAfter = orderVersionAfter,
            Rate = change.Current.Rate,
            TargetCurrencyId = change.Current.TargetCurrencyId,
            Kind = (int)change.Current.Kind,
            RateDay = change.Current.RateDay,
            Source = (int)change.Current.Source,
            BulletinDate = change.Current.BulletinDate,
            Reason = change.Reason,
            RecordedBy = change.ChangedBy,
            RecordedAtUtc = change.ChangedAtUtc,
            CorrelationId = correlationId,
        };

    /// <summary>
    /// API-11: one stored closure or reopening through the domain — this order's general line, recorded at the version the replay
    /// reaches; the domain's rules (Approved order, general line, alternating direction, a non-blank reason) apply again.
    /// </summary>
    private static void ReplayClosure(PurchaseOrder order, PurchaseOrderRow row, IReadOnlyList<PurchaseOrderLineRow> lines,
        PurchaseOrderLineClosureEventRow closure)
    {
        if (closure.CompanyId != row.CompanyId || closure.OrderId != row.Id
            || lines.SingleOrDefault(candidate => candidate.LineId == closure.LineId) is not { Kind: (int)PurchaseOrderLineKind.General })
        {
            throw new InvalidOperationException("Kayıtlı satır kapanışı bu siparişin genel satırına ait değil.");
        }

        if (closure.Closed)
        {
            order.CloseLine(order.Version, closure.LineId, closure.RecordedBy, closure.Reason, closure.RecordedAtUtc);
        }
        else
        {
            order.ReopenLine(order.Version, closure.LineId, closure.RecordedBy, closure.Reason, closure.RecordedAtUtc);
        }

        if (order.Version != closure.OrderVersionAfter)
        {
            throw new InvalidOperationException("Kayıtlı satır kapanışının sürümü domain sürümüyle uyuşmuyor.");
        }
    }

    /// <summary>
    /// A3 API-30: one stored schedule revision through the domain — one line, slices numbered 1..n without a gap, recorded at the
    /// version the replay reaches; the domain measures every slice and the Σ = ordered rule again.
    /// </summary>
    private static void ReplaySchedule(PurchaseOrder order, PurchaseOrderRow row, IReadOnlyList<PurchaseOrderLineRow> lines,
        IReadOnlyList<PurchaseOrderLineDeliverySliceRow> revision)
    {
        var first = revision[0];
        if (revision.Any(slice => slice.CompanyId != row.CompanyId || slice.OrderId != row.Id || slice.LineId != first.LineId
                                  || slice.OrderVersionAfter != first.OrderVersionAfter)
            || revision.Select((slice, index) => slice.SliceNo != index + 1).Any(gap => gap))
        {
            throw new InvalidOperationException("Kayıtlı termin revizyonu tek satırın 1..n sıralı dilimleri değil.");
        }

        var line = lines.SingleOrDefault(candidate => candidate.LineId == first.LineId)
            ?? throw new InvalidOperationException("Kayıtlı termin diliminin sipariş satırı bulunamadı.");
        var unit = UnitOf(line.UnitIsKilogram);
        var recorded = order.SetLineDeliverySchedule(order.Version, first.LineId,
            [.. revision.Select(slice => new PurchaseOrderDeliverySliceInput(slice.SliceId, Quantity.Of(slice.Quantity, unit), slice.DeliveryDate))],
            first.RecordedBy, first.RecordedAtUtc);
        if (order.Version != first.OrderVersionAfter || recorded.Count != revision.Count)
        {
            throw new InvalidOperationException("Kayıtlı termin revizyonunun sürümü domain sürümüyle uyuşmuyor.");
        }
    }

    private static QuantityUnit UnitOf(bool isKilogram) => isKilogram ? QuantityUnit.Kilogram : QuantityUnit.Piece;

    /// <summary>A2a-1: the stored money snapshot, or null when the order has none (the CHECK keeps the five facts together); A2a-1c: with the
    /// stored unit-price scale, null for an order recorded before it (its unit prices keep the amount scale).</summary>
    private static PurchaseOrderMoney? MoneyOf(PurchaseOrderRow row) =>
        row.CurrencyId is { } currencyId
            ? PurchaseOrderMoney.Of(currencyId,
                row.CurrencyCode ?? throw new InvalidOperationException("Kayıtlı para profilinin kodu yok."),
                row.MoneyProfileId ?? throw new InvalidOperationException("Kayıtlı para profilinin kimliği yok."),
                row.MoneyProfileVersion ?? throw new InvalidOperationException("Kayıtlı para profilinin sürümü yok."),
                row.MoneyScale ?? throw new InvalidOperationException("Kayıtlı para profilinin ölçeği yok."),
                row.UnitPriceScale)
            : null;

    /// <summary>A2a-1: the stored line price, or null for an unpriced line (rebuilt through the domain, which checks it again).</summary>
    private static PurchaseOrderLinePrice? PriceOf(PurchaseOrderLineRow line) =>
        line.UnitPrice is { } unitPrice
            ? new PurchaseOrderLinePrice(unitPrice,
                line.PriceUnitId ?? throw new InvalidOperationException("Kayıtlı fiyatın fiyat birimi yok."),
                line.DiscountPercent, line.DiscountAmount, line.DeferralPercent, line.DeferralAmount)
            : null;

    // ---- helpers --------------------------------------------------------------------------------

    private Task<PurchaseAttemptRow?> FindAttemptAsync(string commandName, string keyHash, CancellationToken cancellationToken) =>
        dbContext.Attempts.AsNoTracking().SingleOrDefaultAsync(row => row.CommandName == commandName && row.IdempotencyKeyHash == keyHash, cancellationToken);

    private static void RequireSameContent(PurchaseAttemptRow attempt, string actorId, string requestHash)
    {
        // ADR-011 §3: another content or another actor under the same key is one 409 that discloses nothing.
        if (!string.Equals(attempt.ActorId, actorId, StringComparison.Ordinal) || !string.Equals(attempt.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw Refused(PurchasingStoreCodes.SameKeyPayloadMismatch, "Aynı Idempotency-Key başka bir istekle geldi; ilk cevap bu isteğe verilmez.");
        }
    }

    private static PurchaseAttemptRow Attempt(Guid companyId, string commandName, string keyHash, string requestHash, string actorId,
        PurchasingWriteEnvelope envelope, Guid? requestId, Guid? orderId, DateTimeOffset now) => new PurchaseAttemptRow()
    {
        CompanyId = companyId,
        CommandName = commandName,
        IdempotencyKeyHash = keyHash,
        RequestHash = requestHash,
        ActorId = actorId,
        Outcome = AttemptOutcomeRecorded,
        RequestId = requestId,
        OrderId = orderId,
        CorrelationId = envelope.CorrelationId.Trim(),
        RecordedAtUtc = now,
    };

    private void Audit(PurchasingWriteEnvelope envelope, Guid companyId, string entityType, Guid entityId, string eventType, string operation,
        long? versionBefore, long versionAfter, string before, string after, string reason) =>
        auditEvents.Enqueue(new AuditEventDraft(companyId, eventType, entityType, entityId.ToString("D"), operation, versionBefore, versionAfter,
            AuditActorTypes.User, envelope.ActorId, envelope.PermissionCode, envelope.RoleCodes, before, after, reason, ApprovalContextJson: null,
            AuditAiInvolvement.None, AiContextJson: null, envelope.SourceChannel, envelope.ClientIpAddress, envelope.UserAgent, envelope.CorrelationId,
            envelope.CausationId, envelope.IdempotencyKey));

    private static string Snapshot(PurchaseRequest request) => JsonSerializer.Serialize(new
    {
        status = request.Status.ToString(),
        version = request.Version,
        submittedBy = request.SubmittedBy,
        approval = request.Approval is { } approval ? new { decidedBy = approval.DecidedBy, decidedAtUtc = approval.DecidedAtUtc } : null,
        rejection = request.Rejection is { } rejection
            ? new { decidedBy = rejection.DecidedBy, decidedAtUtc = rejection.DecidedAtUtc, reason = rejection.Reason }
            : null,
        withdrawal = request.Withdrawal is { } withdrawal
            ? new { decidedBy = withdrawal.DecidedBy, decidedAtUtc = withdrawal.DecidedAtUtc, reason = withdrawal.Reason }
            : null,
        // A1-2 (plan API-02): the line's source (null = manual) is part of the audited record.
        lines = request.Lines.Select(line => new
        {
            lineId = line.Id, productId = line.ProductId, quantity = line.Quantity.Value,
            source = line.Source is { } source
                ? new { kind = source.Kind.ToString(), documentId = source.DocumentId, documentLineId = source.DocumentLineId }
                : null,
        }),
    }, Json);

    private static string Snapshot(PurchaseOrder order) => JsonSerializer.Serialize(new
    {
        status = order.Status.ToString(),
        version = order.Version,
        sourceRequestId = order.SourceRequestId,
        // A2a-1 (TEK-01): the currency snapshot and each line's price are part of the audited record.
        currency = order.Money is { } money
            ? new { currencyId = money.CurrencyId, currencyCode = money.CurrencyCode, profileId = money.ProfileId, profileVersion = money.ProfileVersion, scale = money.Scale,
                unitPriceScale = money.UnitPriceScale }
            : null,
        // ALC-SC33 (FNS-D91): the order's rate is part of the audited record, so a change's before/after carry the old and the new rate.
        exchangeRate = order.ExchangeRate is { } rate
            ? new { rate = rate.Rate, targetCurrencyId = rate.TargetCurrencyId, kind = rate.Kind.ToString(), rateDay = rate.RateDay,
                source = rate.Source.ToString(), bulletinDate = rate.BulletinDate }
            : null,
        // FNS-D91: who recorded the current rate, when, and the optional reason of that change.
        exchangeRateChange = order.LastExchangeRateChange is { } rateChange
            ? new { changedBy = rateChange.ChangedBy, changedAtUtc = rateChange.ChangedAtUtc, reason = rateChange.Reason }
            : null,
        // A2a-5a (ALS-D3): who prepared and who approved the order are part of the audited record.
        preparedBy = order.PreparedBy,
        approval = order.Approval is { } approval
            ? new { approvedBy = approval.ApprovedBy, approvedAtUtc = approval.ApprovedAtUtc }
            : null,
        // A1-3 (No.161 §2): the cancellation facts and each line's cancelled remainder are part of the audited record.
        cancellation = order.Cancellation is { } cancellation
            ? new { cancelledBy = cancellation.CancelledBy, cancelledAtUtc = cancellation.CancelledAtUtc, reason = cancellation.Reason }
            : null,
        lines = order.Lines.Select(line => new
        {
            lineId = line.Id, ordered = line.Quantity.Value, technical = line.TechnicalQuantity.Value, accepted = line.AcceptedQuantity.Value,
            cancelled = line.CancelledQuantity.Value, sourceRequestLineId = line.SourceRequestLineId,
            // API-11 (ALS-D10/D12): the closed remainder and the line's current closure are part of the audited record.
            closed = line.ClosedQuantity.Value,
            closure = line.Closure is { } lineClosure
                ? new { closedBy = lineClosure.ClosedBy, closedAtUtc = lineClosure.ClosedAtUtc, reason = lineClosure.Reason }
                : null,
            // A3 API-30 (ALS-D33): the line's current delivery schedule is part of the audited record.
            deliverySchedule = line.DeliverySchedule.Select(slice => new
            {
                sliceId = slice.SliceId, sliceNo = slice.SliceNo, quantity = slice.Quantity.Value, deliveryDate = slice.DeliveryDate,
            }),
            price = line.Price is { } price
                ? new
                {
                    unitPrice = price.UnitPrice, priceUnitId = price.PriceUnitId, discountPercent = price.DiscountPercent, discountAmount = price.DiscountAmount,
                    deferralPercent = price.DeferralPercent, deferralAmount = price.DeferralAmount,
                }
                : null,
        }),
    }, Json);

    /// <summary>
    /// API-11 (F-017 §3.6, ADR-009): the outbox message of one closure or reopening — the general line is the aggregate and the order
    /// version the command produced is both its version and its sequence, so (type, line, sequence) is unique.
    /// </summary>
    private static PurchasingOutboxRow LineClosedMessage(Guid companyId, PurchaseOrder order, Guid lineId, bool closed, PurchasingMutationAudit audit,
        PurchasingWriteEnvelope envelope, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = companyId,
        EventType = PurchasingOrderLineClosedEventV1.EventType,
        SchemaVersion = PurchasingOrderLineClosedEventV1.SchemaVersion,
        SourceOperation = audit.Operation,
        AggregateType = PurchasingOrderLineClosedEventV1.AggregateType,
        AggregateId = lineId,
        AggregateVersion = order.Version,
        Sequence = checked((int)order.Version),
        PayloadJson = JsonSerializer.Serialize(new PurchasingOrderLineClosedPayloadV1(companyId, order.Id, lineId, closed, order.Version), Json),
        OccurredAtUtc = now,
        CorrelationId = envelope.CorrelationId.Trim(),
        CausationId = envelope.CausationId?.Trim(),
        RecordedAtUtc = now,
    };

    private static string ProductVersionOf(IReadOnlyDictionary<Guid, string> versions, Guid lineId)
    {
        if (!versions.TryGetValue(lineId, out var version) || string.IsNullOrWhiteSpace(version)
            || !string.Equals(version, version.Trim(), StringComparison.Ordinal) || version.Length > ProductVersionMaximumLength)
        {
            throw new ArgumentException("Her satır Product sahibinin cevapladığı ürün sürümünü taşır.", nameof(versions));
        }

        return version;
    }

    private static IReadOnlyDictionary<Guid, string> Versions(IReadOnlyDictionary<Guid, string> versions, IEnumerable<Guid> lineIds) =>
        lineIds.ToDictionary(lineId => lineId, lineId => ProductVersionOf(versions, lineId));

    private static IReadOnlyDictionary<Guid, string> VersionsOf(IEnumerable<PurchaseRequestLineRow> lines) =>
        lines.ToDictionary(line => line.LineId, line => line.ProductVersion);

    private static IReadOnlyDictionary<Guid, string> VersionsOf(IEnumerable<PurchaseOrderLineRow> lines) =>
        lines.ToDictionary(line => line.LineId, line => line.ProductVersion);

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            dbContext.ChangeTracker.Clear();
            throw Refused(PurchasingStoreCodes.ConcurrentChange, "Kayıt başka bir yazmayla değişti; hiçbir şey kaydedilmedi.", exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Only unique-key violations are recognised here (the Idempotency / Inventory / Sales pattern): the attempt key,
            // the record id or a line id lost a race; the loser re-reads the winner's result afresh.
            dbContext.ChangeTracker.Clear();
            throw Refused(PurchasingStoreCodes.ConcurrentChange, "Kalıcı tekillik kuralı başka bir yazmanın lehine sonuçlandı; hiçbir şey kaydedilmedi.", exception);
        }
    }

    private Guid RequireResolvedCompany() =>
        companyContext.IsResolved && companyContext.CompanyId != Guid.Empty
            ? companyContext.CompanyId
            : throw Refused(PurchasingStoreCodes.CompanyContextMismatch, "Aktif şirket bağlamı olmadan satınalma kaydı işlenmez.");

    private static void RequireEnvelope(PurchasingWriteEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (string.IsNullOrWhiteSpace(envelope.ActorId) || string.IsNullOrWhiteSpace(envelope.PermissionCode) || string.IsNullOrWhiteSpace(envelope.CorrelationId)
            || string.IsNullOrWhiteSpace(envelope.SourceChannel) || envelope.RoleCodes is null)
        {
            throw new ArgumentException("Satınalma yazması aktör, izin, correlation, idempotency ve kanal bilgisi ister.", nameof(envelope));
        }

        if (string.IsNullOrWhiteSpace(envelope.IdempotencyKey) || !string.Equals(envelope.IdempotencyKey, envelope.IdempotencyKey.Trim(), StringComparison.Ordinal)
            || envelope.IdempotencyKey.Length > KeyMaximumLength)
        {
            throw new ArgumentException("Idempotency-Key kanonik ve en fazla 200 karakter olmalıdır.", nameof(envelope));
        }

        if (envelope.ActorId.Trim().Length > ActorMaximumLength || envelope.CorrelationId.Trim().Length > 128)
        {
            throw new PurchasingStoreException(PurchasingStoreCodes.ValueDoesNotFit, "Aktör veya correlation kimliği sütununa sığmıyor.");
        }
    }

    private static void RequireHash(string requestHash)
    {
        if (string.IsNullOrWhiteSpace(requestHash) || requestHash.Length != 64 || !requestHash.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("İstek özeti 64 onaltılık karakter olmalıdır.", nameof(requestHash));
        }
    }

    private static void RequireCommand(string commandName)
    {
        if (!PurchasingCommands.IsKnown(commandName))
        {
            throw new ArgumentException("Bilinmeyen komut adı.", nameof(commandName));
        }
    }

    private static void RequireAudit(PurchasingMutationAudit audit)
    {
        ArgumentNullException.ThrowIfNull(audit);
        if (string.IsNullOrWhiteSpace(audit.EventType) || string.IsNullOrWhiteSpace(audit.Operation) || string.IsNullOrWhiteSpace(audit.Reason))
        {
            throw new ArgumentException("Komutun audit olayı tür, işlem ve gerekçe ister.", nameof(audit));
        }
    }

    private static void RequirePageSize(int pageSize)
    {
        if (pageSize is < 1 or > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), $"Sayfa boyutu 1–{MaximumPageSize} olmalıdır.");
        }
    }

    /// <summary>ADR-011 §2: the attempt identity is the uppercase SHA-256 of the trimmed key; the raw key is never stored.</summary>
    public static string IdempotencyKeyHash(string idempotencyKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey.Trim())));

    private static PurchasingStoreException Refused(string code, string message, Exception? inner = null) => new PurchasingStoreException(code, message, inner);

    // decimal(18,3): 15 integer digits and 3 decimals; a value outside is refused before the save, never a provider exception (FİNANS #350 P3-2).
    private const decimal ColumnBound = 1_000_000_000_000_000m;

    private static decimal RequireFits(decimal value) =>
        value < ColumnBound && Quantity.SignificantDecimals(value) <= Quantity.KilogramScale
            ? value
            : throw Refused(PurchasingStoreCodes.ValueDoesNotFit, "Miktar decimal(18,3) sütununa sığmıyor; hiçbir şey kaydedilmedi.");

    /// <summary>The opaque, company-bound list cursor (the Sales shape): base64url of company | createdAtUtc ticks | id.</summary>
    internal static class ListCursor
    {
        public static string Encode(Guid companyId, DateTimeOffset createdAtUtc, Guid id)
        {
            var bytes = new byte[16 + 8 + 16];
            companyId.TryWriteBytes(bytes.AsSpan(0, 16));
            BitConverter.TryWriteBytes(bytes.AsSpan(16, 8), createdAtUtc.UtcTicks);
            id.TryWriteBytes(bytes.AsSpan(24, 16));
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static (DateTimeOffset CreatedAtUtc, Guid Id) Decode(string cursor, Guid companyId)
        {
            byte[] bytes;
            try
            {
                var padded = cursor.Replace('-', '+').Replace('_', '/');
                bytes = Convert.FromBase64String(padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '='));
            }
            catch (FormatException exception)
            {
                throw Refused(PurchasingStoreCodes.InvalidCursor, "Liste imleci çözülemedi.", exception);
            }

            if (bytes.Length != 40 || new Guid(bytes.AsSpan(0, 16)) != companyId)
            {
                throw Refused(PurchasingStoreCodes.InvalidCursor, "Liste imleci bu şirketin değil ya da bozuk.");
            }

            return (new DateTimeOffset(BitConverter.ToInt64(bytes, 16), TimeSpan.Zero), new Guid(bytes.AsSpan(24, 16)));
        }
    }
}
