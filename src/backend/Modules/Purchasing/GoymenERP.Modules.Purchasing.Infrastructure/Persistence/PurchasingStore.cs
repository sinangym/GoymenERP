using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Quantities;
using GoymenERP.Modules.Purchasing.Domain.Requests;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

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
        Guid requestId, Action<PurchaseRequest, DateTimeOffset> mutate, PurchasingMutationAudit audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireCommand(commandName);
        RequireAudit(audit);
        if (commandName != PurchasingCommands.RequestSubmitV1)
        {
            throw new ArgumentException("Yalnız gönderme komutu bir talebi değiştirir.", nameof(commandName));
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
        var versionBefore = request.Version;
        var before = Snapshot(request);
        var now = timeProvider.GetUtcNow();
        // The domain decides; a refusal leaves the tracked row untouched (nothing below has run).
        mutate(request, now);
        if (request.Version != versionBefore + 1 || request.Id != row.Id || request.CompanyId != companyId || request.Lines.Count != lineRows.Count)
        {
            throw new InvalidOperationException("Bir komut tam olarak bir sürüm ilerletir ve satır eklemez.");
        }

        row.Status = (int)request.Status;
        row.Version = request.Version;
        row.UpdatedAtUtc = request.UpdatedAtUtc;
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
            });
        }

        dbContext.Attempts.Add(Attempt(companyId, PurchasingCommands.OrderCreateV1, keyHash, requestHash, actorId, envelope, null, order.Id, now));
        Audit(envelope, companyId, OrderEntityType, order.Id, "purchasing.purchase-order.created.v1", "Create", null, order.Version,
            "{}", Snapshot(order), "Satınalma siparişi açıldı");
        await SaveAsync(cancellationToken);
        return new PurchasingOrderStoreResult(
            new StoredPurchaseOrder(order, companyAccountId, effectiveOn, Versions(productVersionsByLineId, order.Lines.Select(line => line.Id))),
            PurchasingWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }

    public async Task<PurchasingOrderStoreResult?> MutateOrderAsync(PurchasingWriteEnvelope envelope, string requestHash, string commandName,
        Guid orderId, Action<PurchaseOrder, DateTimeOffset> mutate, PurchasingMutationAudit audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireCommand(commandName);
        RequireAudit(audit);
        if (commandName != PurchasingCommands.OrderApproveV1)
        {
            throw new ArgumentException("Yalnız onay komutu bir siparişi değiştirir.", nameof(commandName));
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

        // The header and the lines are tracked (the header's version is this write's concurrency token).
        var row = await dbContext.Orders.SingleOrDefaultAsync(candidate => candidate.Id == orderId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var lineRows = await dbContext.OrderLines.Where(line => line.OrderId == orderId).OrderBy(line => line.LineNo).ToListAsync(cancellationToken);
        var acceptanceRows = await dbContext.Acceptances.AsNoTracking().Where(acceptance => acceptance.OrderId == orderId)
            .OrderBy(acceptance => acceptance.RecordedAtUtc).ThenBy(acceptance => acceptance.AcceptanceId).ToListAsync(cancellationToken);
        var order = RestoreOrder(row, lineRows, acceptanceRows);
        var versionBefore = order.Version;
        var before = Snapshot(order);
        var now = timeProvider.GetUtcNow();
        // The domain decides; a refusal leaves the tracked rows untouched (nothing below has run).
        mutate(order, now);
        if (order.Version != versionBefore + 1 || order.Id != row.Id || order.CompanyId != companyId || order.Lines.Count != lineRows.Count
            || order.Acceptances.Count != acceptanceRows.Count)
        {
            throw new InvalidOperationException("Bir komut tam olarak bir sürüm ilerletir; satır ve kabul payı eklemez.");
        }

        if (order.Status == PurchaseOrderStatus.Approved && order.SourceRequestId is { } sourceRequestId)
        {
            await RequireSharesWithinNeedAsync(order, sourceRequestId, cancellationToken);
        }

        row.Status = (int)order.Status;
        row.Version = order.Version;
        row.UpdatedAtUtc = order.UpdatedAtUtc;
        foreach (var lineRow in lineRows)
        {
            var line = order.Lines.Single(candidate => candidate.Id == lineRow.LineId);
            lineRow.Technical = line.TechnicalQuantity.Value;
            lineRow.Accepted = line.AcceptedQuantity.Value;
        }

        dbContext.Attempts.Add(Attempt(companyId, commandName, keyHash, requestHash, actorId, envelope, null, order.Id, now));
        Audit(envelope, companyId, OrderEntityType, order.Id, audit.EventType, audit.Operation, versionBefore, order.Version, before, Snapshot(order), audit.Reason);
        await SaveAsync(cancellationToken);
        return new PurchasingOrderStoreResult(new StoredPurchaseOrder(order, row.CompanyAccountId, row.EffectiveOn, VersionsOf(lineRows)),
            PurchasingWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }

    /// <summary>
    /// Sinan #66 5738113623 §1: the ordered quantities of the APPROVED orders' general lines bound to one request
    /// line, plus this order's, never exceed the request line's quantity. Measured in the approval's save; the
    /// request's sourcing version moves with every passing approval so a concurrent approval collides and measures again.
    /// </summary>
    private async Task RequireSharesWithinNeedAsync(PurchaseOrder order, Guid sourceRequestId, CancellationToken cancellationToken)
    {
        var requestRow = await dbContext.Requests.SingleOrDefaultAsync(candidate => candidate.Id == sourceRequestId, cancellationToken)
            ?? throw new InvalidOperationException("Siparişin kaynak talebi bu şirkette bulunamadı.");
        var requestLines = await dbContext.RequestLines.AsNoTracking().Where(line => line.RequestId == sourceRequestId).ToListAsync(cancellationToken);
        var approvedStatus = (int)PurchaseOrderStatus.Approved;
        var otherApprovedOrderIds = await dbContext.Orders.AsNoTracking()
            .Where(candidate => candidate.SourceRequestId == sourceRequestId && candidate.Status == approvedStatus && candidate.Id != order.Id)
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

    public async Task<PurchasingOrderPage> ListOrdersAsync(int pageSize, string? cursor, PurchaseOrderStatus? status = null,
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

    /// <summary>Rebuilds the request through the domain in recording order; the stored version must be the one the rebuild reaches.</summary>
    private static PurchaseRequest RestoreRequest(PurchaseRequestRow row, IReadOnlyList<PurchaseRequestLineRow> lines)
    {
        var request = PurchaseRequest.Create(row.Id, row.CompanyId, row.CreatedAtUtc);
        foreach (var line in lines)
        {
            request.AddLine(request.Version, line.LineId, line.ProductId, line.UnitId, Quantity.Of(line.Quantity, UnitOf(line.UnitIsKilogram)), row.CreatedAtUtc);
        }

        if (row.Status == (int)PurchaseRequestStatus.Submitted)
        {
            request.Submit(request.Version, row.UpdatedAtUtc);
        }

        if (request.Version != row.Version || request.Status != (PurchaseRequestStatus)row.Status)
        {
            throw new InvalidOperationException("Kayıtlı talebin sürümü satırlarından yeniden kurulan sürümle uyuşmuyor.");
        }

        return request;
    }

    private async Task<IReadOnlyList<StoredPurchaseOrder>> RestoreOrdersAsync(IReadOnlyList<PurchaseOrderRow> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(row => row.Id).ToArray();
        var lines = await dbContext.OrderLines.AsNoTracking().Where(line => ids.Contains(line.OrderId)).ToListAsync(cancellationToken);
        var acceptances = await dbContext.Acceptances.AsNoTracking().Where(acceptance => ids.Contains(acceptance.OrderId)).ToListAsync(cancellationToken);
        return [.. rows.Select(row =>
        {
            var own = lines.Where(line => line.OrderId == row.Id).OrderBy(line => line.LineNo).ToList();
            var ownAcceptances = acceptances.Where(acceptance => acceptance.OrderId == row.Id)
                .OrderBy(acceptance => acceptance.RecordedAtUtc).ThenBy(acceptance => acceptance.AcceptanceId).ToList();
            return new StoredPurchaseOrder(RestoreOrder(row, own, ownAcceptances), row.CompanyAccountId, row.EffectiveOn, VersionsOf(own));
        })];
    }

    /// <summary>
    /// Rebuilds the order through the domain in recording order (general lines, technical slices, approval, acceptance shares);
    /// the stored version and totals must be the ones the rebuild reaches.
    /// </summary>
    internal static PurchaseOrder RestoreOrder(
        PurchaseOrderRow row,
        IReadOnlyList<PurchaseOrderLineRow> lines,
        IReadOnlyList<PurchaseAcceptanceRow>? acceptances = null)
    {
        var order = row.SourceRequestId is { } sourceRequestId
            ? PurchaseOrder.CreateForRequest(row.Id, row.CompanyId, row.SupplierPartyId, sourceRequestId, row.CreatedAtUtc)
            : PurchaseOrder.Create(row.Id, row.CompanyId, row.SupplierPartyId, row.CreatedAtUtc);
        foreach (var line in lines)
        {
            var quantity = Quantity.Of(line.Ordered, UnitOf(line.UnitIsKilogram));
            switch ((PurchaseOrderLineKind)line.Kind)
            {
                case PurchaseOrderLineKind.General:
                    order.AddLine(order.Version, line.LineId, line.ProductId, line.UnitId, quantity, line.SourceRequestLineId, row.CreatedAtUtc);
                    break;
                case PurchaseOrderLineKind.Technical:
                    order.BreakDown(order.Version, line.ParentLineId ?? Guid.Empty, line.LineId, line.ProductId, quantity, row.UpdatedAtUtc);
                    break;
                default:
                    throw new InvalidOperationException("Kayıtlı satır türü bilinmiyor.");
            }
        }

        if (row.Status == (int)PurchaseOrderStatus.Approved)
        {
            order.Approve(order.Version, row.UpdatedAtUtc);
        }

        foreach (var acceptance in acceptances ?? [])
        {
            var sourceLine = lines.SingleOrDefault(line => line.LineId == acceptance.LineId)
                ?? throw new InvalidOperationException("Kayıtlı kabul payının sipariş satırı bulunamadı.");
            order.RecordAcceptance(
                order.Version,
                acceptance.AcceptanceId,
                acceptance.LineId,
                Quantity.Of(acceptance.Quantity, UnitOf(sourceLine.UnitIsKilogram)),
                acceptance.RecordedAtUtc);
        }

        if (order.Version != row.Version || order.Status != (PurchaseOrderStatus)row.Status)
        {
            throw new InvalidOperationException("Kayıtlı siparişin sürümü satırlarından yeniden kurulan sürümle uyuşmuyor.");
        }

        foreach (var line in lines)
        {
            var rebuilt = order.Lines.Single(candidate => candidate.Id == line.LineId);
            if (rebuilt.TechnicalQuantity.Value != line.Technical || rebuilt.AcceptedQuantity.Value != line.Accepted)
            {
                throw new InvalidOperationException("Kayıtlı satır toplamları yeniden kurulan toplamlarla uyuşmuyor.");
            }
        }

        return order;
    }

    private static QuantityUnit UnitOf(bool isKilogram) => isKilogram ? QuantityUnit.Kilogram : QuantityUnit.Piece;

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
        lines = request.Lines.Select(line => new { lineId = line.Id, productId = line.ProductId, quantity = line.Quantity.Value }),
    }, Json);

    private static string Snapshot(PurchaseOrder order) => JsonSerializer.Serialize(new
    {
        status = order.Status.ToString(),
        version = order.Version,
        sourceRequestId = order.SourceRequestId,
        lines = order.Lines.Select(line => new
        {
            lineId = line.Id, ordered = line.Quantity.Value, technical = line.TechnicalQuantity.Value, accepted = line.AcceptedQuantity.Value,
            sourceRequestLineId = line.SourceRequestLineId,
        }),
    }, Json);

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
