using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Quantities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed class PurchaseAcceptanceParticipant : IPurchaseAcceptanceParticipantV1
{
    private const decimal ColumnBound = 1_000_000_000_000_000m;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly PurchasingDbContext _dbContext;
    private readonly AuditEventCollector _auditEvents;
    private readonly DbConnection _connection;
    private readonly DbTransaction _transaction;
    private readonly Guid _companyId;
    private int _stageStarted;
    private int _disposed;

    private PurchaseAcceptanceParticipant(
        PurchasingDbContext dbContext,
        AuditEventCollector auditEvents,
        DbConnection connection,
        DbTransaction transaction,
        Guid companyId)
    {
        _dbContext = dbContext;
        _auditEvents = auditEvents;
        _connection = connection;
        _transaction = transaction;
        _companyId = companyId;
    }

    public static async Task<IPurchaseAcceptanceParticipantV1> EnlistAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Şirket kimliği boş olamaz.", nameof(companyId));
        }

        if (connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection))
        {
            throw new InvalidOperationException("Purchasing participant aynı açık bağlantının aktif transaction'ına katılmalıdır.");
        }

        var companyContext = new CompanyContext();
        companyContext.Set(companyId);
        var auditEvents = new AuditEventCollector();
        var options = new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer(connection)
            .AddInterceptors(new AuditSaveChangesInterceptor(auditEvents, companyContext))
            .Options;
        var dbContext = new PurchasingDbContext(options, companyContext);
        try
        {
            await dbContext.Database.UseTransactionAsync(transaction, cancellationToken);
            return new PurchaseAcceptanceParticipant(dbContext, auditEvents, connection, transaction, companyId);
        }
        catch
        {
            await dbContext.DisposeAsync();
            throw;
        }
    }

    public async Task<PurchaseAcceptanceStageResultV1> StageAsync(
        PurchaseAcceptanceStageRequestV1 request,
        PurchasingWriteEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (Interlocked.Exchange(ref _stageStarted, 1) != 0)
        {
            throw new InvalidOperationException("Bir Purchasing participant yalnız bir kabul yazmasını stage eder.");
        }

        EnsureActiveTransaction();
        var validated = PurchaseAcceptanceParticipantContractV1.Validate(request);
        ValidateEnvelope(envelope);
        if (request.CompanyId != _companyId)
        {
            throw Refused(PurchaseAcceptanceParticipantCodes.InvalidReference, 404, "Sipariş satırı bulunamadı.");
        }

        if (validated.AcceptedQuantity >= ColumnBound || Quantity.SignificantDecimals(validated.AcceptedQuantity) > Quantity.KilogramScale)
        {
            throw Refused(PurchaseAcceptanceParticipantCodes.InvalidQuantity, 400, "Kabul miktarı kalıcı miktar ölçeğine sığmıyor.");
        }

        var keyHash = PurchasingStore.IdempotencyKeyHash(envelope.IdempotencyKey);
        var requestHash = RequestHash(request);
        var actorId = envelope.ActorId.Trim();
        var existing = await _dbContext.Acceptances.AsNoTracking()
            .SingleOrDefaultAsync(row => row.IdempotencyKeyHash == keyHash, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.ActorId, actorId, StringComparison.Ordinal)
                || !string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
            {
                throw Refused(PurchaseAcceptanceParticipantCodes.IdempotencyConflict, 409,
                    "Aynı Idempotency-Key başka bir aktör veya içerikle kullanıldı.");
            }

            var replay = JsonSerializer.Deserialize<PurchaseAcceptanceStageResultV1>(existing.ResultJson, Json)
                ?? throw Refused(PurchaseAcceptanceParticipantCodes.Unavailable, 503, "Kayıtlı kabul sonucu okunamadı.");
            return replay with { IsReplay = true, OriginalCorrelationId = existing.CorrelationId };
        }

        if (await _dbContext.Acceptances.AsNoTracking().AnyAsync(row => row.AcceptanceId == request.AcceptanceId, cancellationToken))
        {
            throw Refused(PurchaseAcceptanceParticipantCodes.IdempotencyConflict, 409, "Kabul kimliği başka bir yazmada kullanıldı.");
        }

        var orderRow = await _dbContext.Orders.SingleOrDefaultAsync(row => row.Id == request.PurchaseOrderId, cancellationToken);
        if (orderRow is null)
        {
            throw Refused(PurchaseAcceptanceParticipantCodes.InvalidReference, 404, "Sipariş satırı bulunamadı.");
        }

        var lineRows = await _dbContext.OrderLines.Where(row => row.OrderId == request.PurchaseOrderId)
            .OrderBy(row => row.LineNo).ToListAsync(cancellationToken);
        var acceptanceRows = await _dbContext.Acceptances.AsNoTracking().Where(row => row.OrderId == request.PurchaseOrderId)
            .OrderBy(row => row.RecordedAtUtc).ThenBy(row => row.AcceptanceId).ToListAsync(cancellationToken);
        var lineRow = lineRows.SingleOrDefault(row => row.LineId == request.PurchaseOrderLineId);
        if (lineRow is null || lineRow.ProductId != request.ProductId)
        {
            throw Refused(PurchaseAcceptanceParticipantCodes.InvalidReference, 404, "Sipariş satırı bulunamadı.");
        }

        if (!string.Equals(lineRow.ProductVersion, request.ExpectedProductVersion, StringComparison.Ordinal))
        {
            throw Refused(PurchaseAcceptanceParticipantCodes.ProductVersionConflict, 409, "Ürün sürümü sipariş satırıyla uyuşmuyor.");
        }

        if (lineRow.UnitId != request.UnitId)
        {
            throw Refused(PurchaseAcceptanceParticipantCodes.UnitConflict, 409, "Birim sipariş satırıyla uyuşmuyor.");
        }

        var order = PurchasingStore.RestoreOrder(orderRow, lineRows, acceptanceRows);
        if (order.Version != validated.ExpectedPurchaseOrderVersion)
        {
            throw Refused(PurchaseAcceptanceParticipantCodes.OrderVersionConflict, 409, "Sipariş sürümü değişti.");
        }

        if (order.Status != PurchaseOrderStatus.Approved)
        {
            throw Refused(PurchaseAcceptanceParticipantCodes.OrderStateConflict, 409, "Yalnız onaylı sipariş kabul payı alır.");
        }

        var line = order.Lines.Single(candidate => candidate.Id == request.PurchaseOrderLineId);
        if (validated.AcceptedQuantity > line.RemainingQuantity.Value)
        {
            throw Refused(PurchaseAcceptanceParticipantCodes.QuantityExceedsRemaining, 409, "Kabul miktarı satırın kalan payını aşıyor.");
        }

        var versionBefore = order.Version;
        var acceptedBefore = line.AcceptedQuantity.Value;
        var quantity = Quantity.Of(validated.AcceptedQuantity, line.Quantity.Unit);
        order.RecordAcceptance(order.Version, request.AcceptanceId, request.PurchaseOrderLineId, quantity, request.OccurredAtUtc);
        var changedLine = order.Lines.Single(candidate => candidate.Id == request.PurchaseOrderLineId);
        orderRow.Version = order.Version;
        orderRow.UpdatedAtUtc = order.UpdatedAtUtc;
        lineRow.Accepted = changedLine.AcceptedQuantity.Value;

        var result = new PurchaseAcceptanceStageResultV1(
            request.CompanyId,
            request.AcceptanceId,
            request.PurchaseOrderId,
            request.PurchaseOrderLineId,
            order.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            PurchaseAcceptanceParticipantContractV1.FormatQuantity(validated.AcceptedQuantity),
            PurchaseAcceptanceParticipantContractV1.FormatQuantity(changedLine.RemainingQuantity.Value),
            false,
            envelope.CorrelationId.Trim());
        _dbContext.Acceptances.Add(new PurchaseAcceptanceRow
        {
            AcceptanceId = request.AcceptanceId,
            CompanyId = request.CompanyId,
            OrderId = request.PurchaseOrderId,
            LineId = request.PurchaseOrderLineId,
            Quantity = validated.AcceptedQuantity,
            OrderVersionAfter = order.Version,
            RecordedAtUtc = request.OccurredAtUtc,
            IdempotencyKeyHash = keyHash,
            RequestHash = requestHash,
            ActorId = actorId,
            CorrelationId = envelope.CorrelationId.Trim(),
            ResultJson = JsonSerializer.Serialize(result, Json),
        });
        _auditEvents.Enqueue(new AuditEventDraft(
            request.CompanyId,
            "purchasing.purchase-order.acceptance-recorded.v1",
            "Purchasing.PurchaseOrder",
            request.PurchaseOrderId.ToString("D"),
            "RecordAcceptance",
            versionBefore,
            order.Version,
            AuditActorTypes.User,
            actorId,
            envelope.PermissionCode,
            envelope.RoleCodes,
            JsonSerializer.Serialize(new { version = versionBefore, accepted = acceptedBefore }, Json),
            JsonSerializer.Serialize(new { version = order.Version, accepted = changedLine.AcceptedQuantity.Value, remaining = changedLine.RemainingQuantity.Value }, Json),
            "Satınalma siparişi kabul payı kaydedildi",
            ApprovalContextJson: null,
            AuditAiInvolvement.None,
            AiContextJson: null,
            envelope.SourceChannel,
            envelope.ClientIpAddress,
            envelope.UserAgent,
            envelope.CorrelationId,
            envelope.CausationId,
            envelope.IdempotencyKey));

        try
        {
            EnsureActiveTransaction();
            await _dbContext.SaveChangesAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            _dbContext.ChangeTracker.Clear();
            throw Refused(PurchaseAcceptanceParticipantCodes.ConcurrentChange, 409,
                "Sipariş başka bir yazmayla değişti; kabul payı stage edilmedi.", exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            _dbContext.ChangeTracker.Clear();
            throw Refused(PurchaseAcceptanceParticipantCodes.ConcurrentChange, 409,
                "Kalıcı tekillik başka bir yazmanın lehine sonuçlandı; kabul payı stage edilmedi.", exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            await _dbContext.DisposeAsync();
        }
    }

    private void EnsureActiveTransaction()
    {
        if (_connection.State != ConnectionState.Open || !ReferenceEquals(_transaction.Connection, _connection)
            || _dbContext.Database.CurrentTransaction is not { } current
            || !ReferenceEquals(current.GetDbTransaction(), _transaction))
        {
            throw new InvalidOperationException("Purchasing participant host transaction'ından ayrıldı.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    private static void ValidateEnvelope(PurchasingWriteEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (string.IsNullOrWhiteSpace(envelope.ActorId) || envelope.ActorId.Trim().Length > 128
            || string.IsNullOrWhiteSpace(envelope.PermissionCode) || envelope.RoleCodes is null
            || string.IsNullOrWhiteSpace(envelope.CorrelationId) || envelope.CorrelationId.Trim().Length > 128
            || string.IsNullOrWhiteSpace(envelope.SourceChannel)
            || string.IsNullOrWhiteSpace(envelope.IdempotencyKey)
            || !string.Equals(envelope.IdempotencyKey, envelope.IdempotencyKey.Trim(), StringComparison.Ordinal)
            || envelope.IdempotencyKey.Length > 200)
        {
            throw Refused(PurchaseAcceptanceParticipantCodes.InvalidRequest, 400,
                "Satınalma kabul yazması aktör, izin, correlation, idempotency ve kanal bilgisi ister.");
        }
    }

    private static string RequestHash(PurchaseAcceptanceStageRequestV1 request)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            request.CompanyId,
            request.AcceptanceId,
            request.PurchaseOrderId,
            request.PurchaseOrderLineId,
            request.ExpectedPurchaseOrderVersion,
            request.ProductId,
            request.ExpectedProductVersion,
            request.UnitId,
            request.AcceptedQuantity,
            occurredAtUtc = request.OccurredAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        }, Json);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static PurchaseAcceptanceParticipantException Refused(string code, int status, string message, Exception? inner = null) =>
        new(code, status, message, inner);
}
