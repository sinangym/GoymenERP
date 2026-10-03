using System.Data;
using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Quantities;
using GoymenERP.Modules.Logistics.Domain.Returns;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence;

/// <summary>
/// Company-filtered persistence for customer returns. Every write requires the caller's already-open
/// Serializable transaction; this store never opens a weaker transaction or commits the borrowed one.
/// </summary>
public sealed class LogisticsCustomerReturnStore(
    LogisticsDbContext dbContext,
    ICompanyContext companyContext,
    IAuditEventCollector auditEvents,
    TimeProvider timeProvider) : ILogisticsCustomerReturnStoreV1
{
    private const string EntityType = "Logistics.CustomerReturn";
    private const decimal ColumnBound = 1_000_000_000_000_000m;

    public async Task<CustomerReturnReplayV1?> FindReplayAsync(LogisticsWriteEnvelope envelope, string requestHash,
        string command, CancellationToken cancellationToken = default)
    {
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireCommand(command);
        RequireCompany();
        var attempt = await FindAttemptAsync(command, LogisticsStore.IdempotencyKeyHash(envelope.IdempotencyKey), cancellationToken);
        if (attempt is null) return null;
        RequireSameAttempt(attempt, envelope.ActorId.Trim(), requestHash);
        var item = await FindAsync(attempt.CustomerReturnId, cancellationToken)
            ?? throw new InvalidOperationException("Kayitli iade denemesinin sonucu bulunamadi.");
        return new(item, attempt.CorrelationId);
    }

    public async Task<CustomerReturn?> FindAsync(Guid customerReturnId, CancellationToken cancellationToken = default)
    {
        RequireCompany();
        if (customerReturnId == Guid.Empty) return null;
        var row = await dbContext.CustomerReturns.AsNoTracking()
            .SingleOrDefaultAsync(item => item.CustomerReturnId == customerReturnId, cancellationToken);
        return row is null ? null : Restore(row);
    }

    public async Task<IReadOnlyList<CustomerReturn>> ListOrderReturnsAsync(Guid salesOrderId,
        CancellationToken cancellationToken = default)
    {
        RequireCompany();
        if (salesOrderId == Guid.Empty) return [];
        var rows = await dbContext.CustomerReturns.AsNoTracking()
            .Where(item => item.SourceOrderId == salesOrderId)
            .OrderBy(item => item.RequestedAtUtc).ThenBy(item => item.CustomerReturnId)
            .ToListAsync(cancellationToken);
        return rows.Select(Restore).ToArray();
    }

    public async Task<Quantity> SumApprovedForDeliveryAsync(Guid sourceDeliveryId, QuantityUnit unit,
        CancellationToken cancellationToken = default)
    {
        RequireCompany();
        RequireSerializable();
        if (sourceDeliveryId == Guid.Empty || !Enum.IsDefined(unit))
            throw new ArgumentException("Kaynak teslimat ve birim zorunludur.");
        try
        {
            var value = await dbContext.CustomerReturns.AsNoTracking()
                .Where(item => item.SourceDeliveryId == sourceDeliveryId
                    && item.Status == (int)CustomerReturnStatus.Approved && item.QuantityUnit == (int)unit)
                .SumAsync(item => (decimal?)item.Quantity, cancellationToken) ?? 0m;
            return Quantity.Of(value, unit);
        }
        catch (Exception exception) when (ContainsSqlException(exception, 1205))
        {
            throw Refused(LogisticsStoreCodes.ConcurrentChange, "Iade toplam yarisi kaybedildi; hicbir sey kaydedilmedi.", exception);
        }
    }

    public async Task<CustomerReturnStoreResultV1> StoreRequestAsync(LogisticsWriteEnvelope envelope, string requestHash,
        CustomerReturn customerReturn, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customerReturn);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        var companyId = RequireCompany();
        RequireSerializable();
        if (customerReturn.CompanyId != companyId || customerReturn.Status != CustomerReturnStatus.Pending
            || customerReturn.Version != 1 || customerReturn.DecidedBy is not null || customerReturn.InventoryMovementId is not null)
            throw Refused(LogisticsStoreCodes.CompanyContextMismatch, "Iade istegi aktif sirketin yeni bekleyen kaydi degil.");

        var command = LogisticsCustomerReturnCommandsV1.Request;
        var keyHash = LogisticsStore.IdempotencyKeyHash(envelope.IdempotencyKey);
        var replay = await FindAttemptAsync(command, keyHash, cancellationToken);
        if (replay is not null)
        {
            RequireSameAttempt(replay, envelope.ActorId.Trim(), requestHash);
            var existing = await FindAsync(replay.CustomerReturnId, cancellationToken)
                ?? throw new InvalidOperationException("Kayitli iade istegi bulunamadi.");
            return new(existing, LogisticsWriteOutcome.Replayed, replay.CorrelationId);
        }

        if (await dbContext.CustomerReturns.AsNoTracking().AnyAsync(item => item.CustomerReturnId == customerReturn.Id, cancellationToken))
            throw Refused(LogisticsStoreCodes.ConcurrentChange, "Bu iade kimligi daha once kullanildi.");
        await ValidateSourceAsync(customerReturn, cancellationToken);
        dbContext.CustomerReturns.Add(ToRow(customerReturn));
        dbContext.CustomerReturnAttempts.Add(Attempt(companyId, command, keyHash, requestHash, envelope, customerReturn.Id));
        Audit(envelope, customerReturn, "logistics.customer-return.requested.v1", "Request", null, 1,
            "Musteri iadesi fiziksel etki olmadan hazirlandi");
        await SaveAsync(cancellationToken);
        return new(customerReturn, LogisticsWriteOutcome.Recorded, envelope.CorrelationId);
    }

    public async Task<CustomerReturnStoreResultV1> StoreDecisionAsync(LogisticsWriteEnvelope envelope, string requestHash,
        string command, CustomerReturn customerReturn, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customerReturn);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireCommand(command);
        var companyId = RequireCompany();
        RequireSerializable();
        if (command is not (LogisticsCustomerReturnCommandsV1.Approve or LogisticsCustomerReturnCommandsV1.Reject))
            throw new ArgumentException("Yalniz iade onayi veya reddi karar olarak kaydedilir.", nameof(command));
        if (customerReturn.CompanyId != companyId || customerReturn.Version != 2
            || customerReturn.Status == CustomerReturnStatus.Pending || string.IsNullOrWhiteSpace(customerReturn.DecidedBy))
            throw Refused(LogisticsStoreCodes.ConcurrentChange, "Iade karari tamamlanmis bir ileri gecis degil.");

        var keyHash = LogisticsStore.IdempotencyKeyHash(envelope.IdempotencyKey);
        var replay = await FindAttemptAsync(command, keyHash, cancellationToken);
        if (replay is not null)
        {
            RequireSameAttempt(replay, envelope.ActorId.Trim(), requestHash);
            var existing = await FindAsync(replay.CustomerReturnId, cancellationToken)
                ?? throw new InvalidOperationException("Kayitli iade karari bulunamadi.");
            return new(existing, LogisticsWriteOutcome.Replayed, replay.CorrelationId);
        }

        var row = await dbContext.CustomerReturns.SingleOrDefaultAsync(item => item.CustomerReturnId == customerReturn.Id,
            cancellationToken) ?? throw Refused(LogisticsStoreCodes.ConcurrentChange, "Bekleyen iade bulunamadi.");
        var before = Restore(row);
        if (before.Status != CustomerReturnStatus.Pending || before.Version != 1
            || !SameImmutable(before, customerReturn))
            throw Refused(LogisticsStoreCodes.ConcurrentChange, "Iade baska bir karar veya kaynak degisikligi tasiyor.");
        await ValidateSourceAsync(customerReturn, cancellationToken);

        if (customerReturn.Status == CustomerReturnStatus.Approved)
        {
            var approved = await dbContext.CustomerReturns.AsNoTracking()
                .Where(item => item.SourceDeliveryId == row.SourceDeliveryId
                    && item.Status == (int)CustomerReturnStatus.Approved && item.QuantityUnit == row.QuantityUnit)
                .SumAsync(item => (decimal?)item.Quantity, cancellationToken) ?? 0m;
            if (approved + row.Quantity > row.SourceDeliveredQuantity)
                throw Refused(LogisticsStoreCodes.AggregateLimitExceeded,
                    "Ayni teslimatin onayli iade toplami teslim edilen miktari asamaz.");
        }

        ApplyDecision(row, customerReturn);
        dbContext.CustomerReturnAttempts.Add(Attempt(companyId, command, keyHash, requestHash, envelope, customerReturn.Id));
        Audit(envelope, customerReturn,
            customerReturn.Status == CustomerReturnStatus.Approved
                ? "logistics.customer-return.approved.v1" : "logistics.customer-return.rejected.v1",
            customerReturn.Status == CustomerReturnStatus.Approved ? "Approve" : "Reject", 1, 2,
            customerReturn.Status == CustomerReturnStatus.Approved
                ? "Musteri iadesi Inventory telafi hareketiyle onaylandi" : "Musteri iadesi stok etkisi olmadan reddedildi");
        await SaveAsync(cancellationToken);
        return new(customerReturn, LogisticsWriteOutcome.Recorded, envelope.CorrelationId);
    }

    private async Task ValidateSourceAsync(CustomerReturn item, CancellationToken cancellationToken)
    {
        var delivery = await dbContext.Shares.AsNoTracking().SingleOrDefaultAsync(share =>
            share.InstructionId == item.LoadingInstructionId && share.ShareId == item.SourceDeliveryId
            && share.Kind == 3, cancellationToken);
        var line = await dbContext.Lines.AsNoTracking().SingleOrDefaultAsync(candidate =>
            candidate.InstructionId == item.LoadingInstructionId && candidate.LineId == item.LoadingInstructionLineId,
            cancellationToken);
        var expectedUnit = line?.UnitIsKilogram == true ? QuantityUnit.Kilogram : QuantityUnit.Piece;
        if (delivery is null || line is null || delivery.LineId != item.LoadingInstructionLineId
            || delivery.SourceShipmentId != item.SourceShipmentId || delivery.Quantity != item.SourceDeliveredQuantity.Value
            || line.SourceOrderId != item.SourceOrderId || line.SourceOrderLineId != item.SourceOrderLineId
            || line.UnitId != item.UnitId || expectedUnit != item.Quantity.Unit
            || item.SourceDeliveredQuantity.Unit != item.Quantity.Unit)
            throw Refused(LogisticsStoreCodes.ConcurrentChange, "Iadenin degismez teslimat kaynagi guncel Logistics iziyle uyusmuyor.");
    }

    private CustomerReturnAttemptRow? TrackedAttempt(string command, string keyHash) =>
        dbContext.CustomerReturnAttempts.Local.SingleOrDefault(item => item.CommandName == command && item.IdempotencyKeyHash == keyHash);

    private Task<CustomerReturnAttemptRow?> FindAttemptAsync(string command, string keyHash, CancellationToken cancellationToken) =>
        TrackedAttempt(command, keyHash) is { } tracked
            ? Task.FromResult<CustomerReturnAttemptRow?>(tracked)
            : dbContext.CustomerReturnAttempts.AsNoTracking().SingleOrDefaultAsync(item =>
                item.CommandName == command && item.IdempotencyKeyHash == keyHash, cancellationToken);

    private static CustomerReturnRow ToRow(CustomerReturn item) => new()
    {
        CompanyId = item.CompanyId,
        CustomerReturnId = item.Id,
        LoadingInstructionId = item.LoadingInstructionId,
        LoadingInstructionLineId = item.LoadingInstructionLineId,
        SourceDeliveryId = item.SourceDeliveryId,
        SourceShipmentId = item.SourceShipmentId,
        SourceOrderId = item.SourceOrderId,
        SourceOrderLineId = item.SourceOrderLineId,
        UnitId = item.UnitId,
        QuantityUnit = (int)item.Quantity.Unit,
        Quantity = RequireFits(item.Quantity.Value),
        SourceDeliveredQuantity = RequireFits(item.SourceDeliveredQuantity.Value),
        Reason = item.Reason,
        RequestedBy = item.RequestedBy,
        OccurredAtUtc = item.OccurredAtUtc,
        RequestedAtUtc = item.RequestedAtUtc,
        Status = (int)item.Status,
        Version = item.Version,
    };

    private static void ApplyDecision(CustomerReturnRow row, CustomerReturn item)
    {
        row.Status = (int)item.Status;
        row.Version = item.Version;
        row.DecidedBy = item.DecidedBy;
        row.DecidedAtUtc = item.DecidedAtUtc;
        row.DecisionNote = item.DecisionNote;
        row.InventoryMovementId = item.InventoryMovementId;
        row.OriginalLabelId = item.OriginalLabelId;
        row.OriginalShipmentMovementId = item.OriginalShipmentMovementId;
    }

    private static CustomerReturn Restore(CustomerReturnRow row)
    {
        var unit = (QuantityUnit)row.QuantityUnit;
        var item = CustomerReturn.Request(row.CustomerReturnId, row.CompanyId, row.LoadingInstructionId,
            row.LoadingInstructionLineId, row.SourceDeliveryId, row.SourceShipmentId, row.SourceOrderId,
            row.SourceOrderLineId, row.UnitId, Quantity.Positive(row.Quantity, unit),
            Quantity.Positive(row.SourceDeliveredQuantity, unit), row.Reason, row.RequestedBy,
            row.OccurredAtUtc, row.RequestedAtUtc);
        if (row.Status == (int)CustomerReturnStatus.Approved)
            item.Approve(1, row.DecidedBy!, row.InventoryMovementId!.Value, row.OriginalLabelId!.Value,
                row.OriginalShipmentMovementId!.Value, row.DecidedAtUtc!.Value, row.DecisionNote);
        else if (row.Status == (int)CustomerReturnStatus.Rejected)
            item.Reject(1, row.DecidedBy!, row.DecidedAtUtc!.Value, row.DecisionNote);
        if (item.Version != row.Version || (int)item.Status != row.Status)
            throw new InvalidOperationException("Kalici iade satiri domain gecisiyle yeniden kurulamadi.");
        return item;
    }

    private static bool SameImmutable(CustomerReturn left, CustomerReturn right) =>
        left.CompanyId == right.CompanyId && left.LoadingInstructionId == right.LoadingInstructionId
        && left.LoadingInstructionLineId == right.LoadingInstructionLineId
        && left.SourceDeliveryId == right.SourceDeliveryId && left.SourceShipmentId == right.SourceShipmentId
        && left.SourceOrderId == right.SourceOrderId && left.SourceOrderLineId == right.SourceOrderLineId
        && left.UnitId == right.UnitId && left.Quantity == right.Quantity
        && left.SourceDeliveredQuantity == right.SourceDeliveredQuantity
        && string.Equals(left.RequestedBy, right.RequestedBy, StringComparison.Ordinal)
        && string.Equals(left.Reason, right.Reason, StringComparison.Ordinal)
        && left.OccurredAtUtc == right.OccurredAtUtc && left.RequestedAtUtc == right.RequestedAtUtc;

    private static CustomerReturnAttemptRow Attempt(Guid companyId, string command, string keyHash, string requestHash,
        LogisticsWriteEnvelope envelope, Guid returnId) => new()
    {
        CompanyId = companyId,
        CommandName = command,
        IdempotencyKeyHash = keyHash,
        RequestHash = requestHash,
        ActorId = envelope.ActorId.Trim(),
        CustomerReturnId = returnId,
        CorrelationId = envelope.CorrelationId.Trim(),
        RecordedAtUtc = DateTimeOffset.UtcNow,
    };

    private void Audit(LogisticsWriteEnvelope envelope, CustomerReturn item, string eventType, string operation,
        long? beforeVersion, long afterVersion, string reason) => auditEvents.Enqueue(new AuditEventDraft(
        item.CompanyId, eventType, EntityType, item.Id.ToString("D"), operation, beforeVersion, afterVersion,
        AuditActorTypes.User, envelope.ActorId, envelope.PermissionCode, envelope.RoleCodes,
        beforeVersion is null ? null : JsonSerializer.Serialize(new { status = CustomerReturnStatus.Pending, version = beforeVersion }),
        JsonSerializer.Serialize(new { status = item.Status, version = afterVersion, item.SourceDeliveryId, item.Quantity.Value }),
        reason, ApprovalContextJson: null, AuditAiInvolvement.None, AiContextJson: null, envelope.SourceChannel,
        envelope.ClientIpAddress, envelope.UserAgent, envelope.CorrelationId, envelope.CausationId, envelope.IdempotencyKey));

    private void RequireSerializable()
    {
        var transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        if (transaction is null || transaction.Connection is null || transaction.IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Musteri iadesi yazmasi sahibinin acik Serializable transaction'ini gerektirir.");
    }

    private Guid RequireCompany() => companyContext.IsResolved && companyContext.CompanyId != Guid.Empty
        ? companyContext.CompanyId
        : throw Refused(LogisticsStoreCodes.CompanyContextMismatch, "Aktif sirket baglami olmadan iade islenmez.");

    private static void RequireCommand(string command)
    {
        if (command is not (LogisticsCustomerReturnCommandsV1.Request or LogisticsCustomerReturnCommandsV1.Approve
            or LogisticsCustomerReturnCommandsV1.Reject)) throw new ArgumentException("Bilinmeyen iade komutu.", nameof(command));
    }

    private static void RequireEnvelope(LogisticsWriteEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (string.IsNullOrWhiteSpace(envelope.ActorId) || string.IsNullOrWhiteSpace(envelope.PermissionCode)
            || string.IsNullOrWhiteSpace(envelope.CorrelationId) || string.IsNullOrWhiteSpace(envelope.IdempotencyKey)
            || string.IsNullOrWhiteSpace(envelope.SourceChannel) || envelope.RoleCodes is null)
            throw new ArgumentException("Iade yazmasi tam sunucu zarfi ister.", nameof(envelope));
    }

    private static void RequireHash(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash) || hash.Length != 64 || !hash.All(Uri.IsHexDigit))
            throw new ArgumentException("Istek ozeti 64 onaltilik karakter olmalidir.", nameof(hash));
    }

    private static void RequireSameAttempt(CustomerReturnAttemptRow attempt, string actor, string requestHash)
    {
        if (!string.Equals(attempt.ActorId, actor, StringComparison.Ordinal)
            || !string.Equals(attempt.RequestHash, requestHash, StringComparison.Ordinal))
            throw Refused(LogisticsStoreCodes.SameKeyPayloadMismatch,
                "Ayni Idempotency-Key baska aktor veya icerikle geldi; ilk sonuc aciklanmaz.");
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException exception)
        {
            dbContext.ChangeTracker.Clear();
            throw Refused(LogisticsStoreCodes.ConcurrentChange, "Iade baska bir yazmayla degisti; hicbir sey kaydedilmedi.", exception);
        }
        catch (Exception exception) when (ContainsSqlException(exception, 1205))
        {
            dbContext.ChangeTracker.Clear();
            throw Refused(LogisticsStoreCodes.ConcurrentChange, "Iade miktar yarisi kaybedildi; hicbir sey kaydedilmedi.", exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            dbContext.ChangeTracker.Clear();
            throw Refused(LogisticsStoreCodes.ConcurrentChange, "Iade tekillik yarisi kaybedildi; hicbir sey kaydedilmedi.", exception);
        }
    }

    private static bool ContainsSqlException(Exception exception, int number)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is SqlException sql && sql.Number == number) return true;
        return false;
    }

    private static decimal RequireFits(decimal value) => value > 0m && value < ColumnBound
        && Quantity.SignificantDecimals(value) <= Quantity.KilogramScale
        ? value : throw Refused(LogisticsStoreCodes.ValueDoesNotFit, "Iade miktari decimal(18,3) sutununa sigmiyor.");

    private static LogisticsStoreException Refused(string code, string message, Exception? inner = null) => new(code, message, inner);
}
