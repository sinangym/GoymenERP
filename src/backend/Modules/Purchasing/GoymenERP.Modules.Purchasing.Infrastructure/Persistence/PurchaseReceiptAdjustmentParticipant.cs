using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Concurrency;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Quantities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

/// <summary>ALC-S6: immutable replay and one net mutation on the caller's existing transaction.</summary>
public sealed partial class PurchaseReceiptAdjustmentParticipant : IPurchaseReceiptAdjustmentParticipantV1, IPurchaseReceiptAdjustmentBatchParticipantV1
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly PurchasingDbContext database;
    private readonly CompanyContext company;
    private readonly DbConnection connection;
    private readonly DbTransaction transaction;
    private readonly IAuditEventCollector audit;
    private readonly IAuditTransactionWriterV1 auditWriter;
    private int staged;
    private int disposed;

    private PurchaseReceiptAdjustmentParticipant(PurchasingDbContext database, CompanyContext company,
        DbConnection connection, DbTransaction transaction, IAuditEventCollector audit, IAuditTransactionWriterV1 auditWriter)
    {
        this.database = database; this.company = company; this.connection = connection;
        this.transaction = transaction; this.audit = audit; this.auditWriter = auditWriter;
    }

    public static async Task<IPurchaseReceiptAdjustmentParticipantV1> EnlistAsync(DbConnection connection,
        DbTransaction transaction, Guid companyId, IAuditEventCollector audit, IAuditTransactionWriterV1 auditWriter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(auditWriter);
        if (companyId == Guid.Empty || connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection)
            || auditWriter.HasPendingDrafts) throw Unavailable();
        var company = new CompanyContext(); company.Set(companyId);
        // No Audit interceptor or foreign-table writer. Audit is appended by its owner port below.
        var database = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>().UseSqlServer(connection).Options, company);
        try
        {
            await database.Database.UseTransactionAsync(transaction, cancellationToken);
            return new PurchaseReceiptAdjustmentParticipant(database, company, connection, transaction, audit, auditWriter);
        }
        catch { await database.DisposeAsync(); throw; }
    }

    public async Task<PurchaseReceiptAdjustmentResultV1?> FindReplayAsync(string commandName, string requestHash,
        PurchasingWriteEnvelope envelope, CancellationToken cancellationToken = default)
    {
        EnsureTransaction(); Validate(commandName, requestHash, envelope);
        var key = PurchasingStore.IdempotencyKeyHash(envelope.IdempotencyKey);
        var attempt = await database.Attempts.AsNoTracking()
            .SingleOrDefaultAsync(item => item.CommandName == commandName && item.IdempotencyKeyHash == key, cancellationToken);
        if (attempt is null) return null;
        if (attempt.CompanyId != company.CompanyId || attempt.ActorId != envelope.ActorId.Trim() || attempt.RequestHash != requestHash)
            throw new PurchasingServiceException(409, PurchasingStoreCodes.SameKeyPayloadMismatch);
        if (attempt.Outcome == 2)
        {
            PurchasingTerminalRefusal? refusal;
            try { refusal = JsonSerializer.Deserialize<PurchasingTerminalRefusal>(attempt.RefusalJson ?? "null", Json); }
            catch (JsonException) { throw Unavailable(); }
            if (refusal is null || refusal.Status is < 400 or > 499 || string.IsNullOrWhiteSpace(refusal.Code) || refusal.Errors is null)
                throw Unavailable();
            throw new PurchasingServiceException(refusal.Status, refusal.Code, refusal.Errors, true, attempt.CorrelationId);
        }
        if (attempt.Outcome != 1 || attempt.ReceiptAdjustmentId is not { } adjustmentId || attempt.OrderId is not { } orderId
            || attempt.RefusalJson is not null || attempt.RequestId is not null || attempt.ReceiptAdjustmentBatchId is not null) throw Unavailable();
        // Immutable ledger only: neither RestoreOrder nor today's quantity/version/cost is consulted on replay.
        var row = await database.ReceiptAdjustments.AsNoTracking().SingleOrDefaultAsync(item => item.AdjustmentId == adjustmentId, cancellationToken)
            ?? throw Unavailable();
        PurchaseReceiptAdjustmentResultV1? result;
        try { result = JsonSerializer.Deserialize<PurchaseReceiptAdjustmentResultV1>(row.ResultJson, Json); }
        catch (JsonException) { throw Unavailable(); }
        if (result is null || row.CompanyId != company.CompanyId || row.OrderId != orderId || row.BatchId is not null || row.BatchLineNo is not null
            || result.CompanyId != row.CompanyId || result.OrderId != row.OrderId || result.AdjustmentId != row.AdjustmentId
            || result.AcceptanceId != row.AcceptanceId || result.LineId != row.LineId
            || result.OrderVersion != row.OrderVersionAfter.ToString(CultureInfo.InvariantCulture)
            || result.OriginalCorrelationId != attempt.CorrelationId || row.CorrelationId != attempt.CorrelationId
            || result.NetOpenedQuantity != Text(row.NetOpenedQuantity) || result.NewAcceptanceId != row.NewAcceptanceId
            || result.NewAcceptedQuantity != Text(row.NewAcceptedQuantity) || result.Revived != row.Revived) throw Unavailable();
        return result with { IsReplay = true };
    }

    public async Task<PurchaseReceiptAdjustmentTargetV1?> ReadTargetAsync(Guid orderId, Guid acceptanceId,
        CancellationToken cancellationToken = default)
    {
        EnsureTransaction();
        var original = await database.Acceptances.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OrderId == orderId && item.AcceptanceId == acceptanceId, cancellationToken);
        if (original is null) return null;
        var line = await database.OrderLines.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OrderId == orderId && item.LineId == original.LineId, cancellationToken) ?? throw Unavailable();
        var order = await database.Orders.AsNoTracking().SingleOrDefaultAsync(item => item.Id == orderId, cancellationToken) ?? throw Unavailable();
        var general = General(line);
        if (!await database.OrderLines.AsNoTracking().AnyAsync(item => item.OrderId == orderId && item.LineId == general
            && item.Kind == (int)PurchaseOrderLineKind.General && item.ParentLineId == null, cancellationToken)) throw Unavailable();
        var undone = await database.ReceiptAdjustments.AsNoTracking().Where(item => item.AcceptanceId == acceptanceId)
            .SumAsync(item => (decimal?)item.UndoQuantity, cancellationToken) ?? 0m;
        if (undone < 0m || undone > original.Quantity || order.Version < 1 || string.IsNullOrWhiteSpace(line.ProductVersion)) throw Unavailable();
        return new(company.CompanyId, orderId, acceptanceId, line.LineId, general, line.ProductId, line.ProductVersion,
            line.UnitId, line.UnitIsKilogram, order.Version.ToString(CultureInfo.InvariantCulture), Text(original.Quantity), Text(undone));
    }

    public async Task<PurchaseReceiptAdjustmentResultV1> StageAsync(PurchaseReceiptAdjustmentStageV1 request,
        PurchasingWriteEnvelope envelope, CancellationToken cancellationToken = default)
    {
        EnsureTransaction();
        if (Interlocked.Exchange(ref staged, 1) != 0) throw new InvalidOperationException("A receipt adjustment participant is single-use.");
        Validate(request.CommandName, request.RequestHash, envelope);
        var replay = await FindReplayAsync(request.CommandName, request.RequestHash, envelope, cancellationToken);
        if (replay is not null) return replay;
        if (request.CompanyId != company.CompanyId) throw new PurchasingServiceException(404, PurchaseReceiptAdjustmentCodesV1.OriginalAcceptanceNotFound);
        if (request.AdjustmentId == Guid.Empty || request.OrderId == Guid.Empty || request.AcceptanceId == Guid.Empty
            || !long.TryParse(request.ExpectedOrderVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var expected) || expected < 1
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 4000) throw InvalidRequest();
        if (auditWriter.HasPendingDrafts) throw Unavailable();
        var target = await ReadTargetAsync(request.OrderId, request.AcceptanceId, cancellationToken)
            ?? throw new PurchasingServiceException(404, PurchaseReceiptAdjustmentCodesV1.OriginalAcceptanceNotFound);
        // The host already holds every Sales lock before every Purchase lock. Reacquiring this owned lock is idempotent.
        await PurchaseLineSerializationLock.Bind(connection, transaction, company.CompanyId).AcquireAsync(target.GeneralLineId, cancellationToken);
        var row = await database.Orders.SingleOrDefaultAsync(item => item.Id == request.OrderId, cancellationToken) ?? throw Unavailable();
        var lines = await database.OrderLines.Where(item => item.OrderId == request.OrderId).OrderBy(item => item.LineNo).ToListAsync(cancellationToken);
        var shares = await database.Acceptances.AsNoTracking().Where(item => item.OrderId == row.Id).ToListAsync(cancellationToken);
        var slices = await database.DeliverySlices.AsNoTracking().Where(item => item.OrderId == row.Id).ToListAsync(cancellationToken);
        var closures = await database.LineClosureEvents.AsNoTracking().Where(item => item.OrderId == row.Id).ToListAsync(cancellationToken);
        var rates = await database.ExchangeRateChanges.AsNoTracking().Where(item => item.OrderId == row.Id).ToListAsync(cancellationToken);
        var adjustments = await database.ReceiptAdjustments.AsNoTracking().Where(item => item.OrderId == row.Id).ToListAsync(cancellationToken);
        var cancellations = await database.CancellationHistory.AsNoTracking().Where(item => item.OrderId == row.Id).ToListAsync(cancellationToken);
        var batches = await database.ReceiptAdjustmentBatches.AsNoTracking().Where(item => item.OrderId == row.Id).ToListAsync(cancellationToken);
        var order = PurchasingStore.RestoreOrder(row, lines, shares, slices, closures, rates, adjustments, cancellations, batches);
        var unit = lines.Single(item => item.LineId == target.LineId).UnitIsKilogram ? QuantityUnit.Kilogram : QuantityUnit.Piece;
        var undo = ParseQuantity(request.UndoQuantity, unit);
        var replacement = ParseQuantity(request.NewAcceptedQuantity ?? "0", unit);
        if (await database.ReceiptAdjustments.AsNoTracking().AnyAsync(item => item.AdjustmentId == request.AdjustmentId, cancellationToken)
            || (request.NewAcceptanceId is { } newId && await database.Acceptances.AsNoTracking().AnyAsync(item => item.AcceptanceId == newId, cancellationToken)))
            throw new PurchasingServiceException(409, PurchasingStoreCodes.SameKeyPayloadMismatch);
        var beforeVersion = order.Version;
        PurchaseReceiptAdjustment change;
        try
        {
            change = order.AdjustReceipt(expected, request.AdjustmentId, request.AcceptanceId, undo, request.IsSupplierReturn,
                request.ReplacementExpected, envelope.ActorId, request.Reason, request.OccurredAtUtc, request.NewAcceptanceId, replacement);
        }
        catch (PurchasingRuleException exception) { throw new PurchasingServiceException(409, exception.Code); }
        catch (PurchasingVersionConflictException) { throw new PurchasingServiceException(409, PurchasingStoreCodes.ConcurrentChange); }
        var notificationTargets = change.Revived ? NotificationTargets(request.NotificationSnapshots,
            order.Id, target.GeneralLineId, beforeVersion) : null;
        row.Status = (int)order.Status; row.Version = order.Version; row.UpdatedAtUtc = order.UpdatedAtUtc;
        row.CancelledBy = order.Cancellation?.CancelledBy; row.CancelledAtUtc = order.Cancellation?.CancelledAtUtc;
        row.CancellationReason = order.Cancellation?.Reason;
        foreach (var item in lines)
        {
            var current = order.Lines.Single(line => line.Id == item.LineId);
            item.Accepted = current.AcceptedQuantity.Value; item.Cancelled = current.CancelledQuantity.Value;
            item.Closed = current.ClosedQuantity.Value; item.DefinitiveReturned = current.DefinitiveReturnQuantity.Value;
            item.ReceiptClosureRevived = current.ReceiptClosureRevived;
        }
        var group = order.Lines.Where(item => item.Id == target.GeneralLineId || item.ParentLineId == target.GeneralLineId).ToArray();
        var general = group.Single(item => item.Id == target.GeneralLineId);
        var allocatable = order.Status == PurchaseOrderStatus.Approved && !general.IsClosed
            ? group.Sum(item => item.RemainingQuantity.Value) : 0m;
        var result = new PurchaseReceiptAdjustmentResultV1(company.CompanyId, change.AdjustmentId, order.Id, change.AcceptanceId,
            change.LineId, general.Id, Text(order.Version), Text(change.NetOpenedQuantity.Value), Text(allocatable), change.Revived,
            false, envelope.CorrelationId.Trim(), change.NewAcceptanceId, Text(change.NewAcceptedQuantity.Value));
        var key = PurchasingStore.IdempotencyKeyHash(envelope.IdempotencyKey);
        if (change.NewAcceptanceId is { } replacementId)
        {
            var acceptance = new PurchaseAcceptanceStageResultV1(company.CompanyId, replacementId, order.Id, change.LineId,
                Text(order.Version), Text(change.NewAcceptedQuantity.Value),
                Text(order.Lines.Single(item => item.Id == change.LineId).RemainingQuantity.Value), false, envelope.CorrelationId.Trim(), general.Id);
            database.Acceptances.Add(new PurchaseAcceptanceRow { CompanyId = company.CompanyId, AcceptanceId = replacementId,
                OrderId = order.Id, LineId = change.LineId, Quantity = change.NewAcceptedQuantity.Value, OrderVersionAfter = order.Version,
                RecordedAtUtc = change.OccurredAtUtc, ActorId = envelope.ActorId.Trim(), CorrelationId = envelope.CorrelationId.Trim(),
                IdempotencyKeyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.CommandName + ":" + key + ":replacement:" + replacementId))),
                RequestHash = request.RequestHash, ResultJson = JsonSerializer.Serialize(acceptance, Json) });
        }
        database.ReceiptAdjustments.Add(new PurchaseReceiptAdjustmentRow { CompanyId = company.CompanyId,
            AdjustmentId = change.AdjustmentId, OrderId = order.Id, AcceptanceId = change.AcceptanceId, LineId = change.LineId,
            UndoQuantity = change.UndoQuantity.Value, IsSupplierReturn = change.IsSupplierReturn, ReplacementExpected = change.ReplacementExpected,
            NewAcceptanceId = change.NewAcceptanceId, NewAcceptedQuantity = change.NewAcceptedQuantity.Value, OrderVersionAfter = order.Version,
            Revived = change.Revived, NetOpenedQuantity = change.NetOpenedQuantity.Value, RecordedBy = change.Actor,
            Reason = change.Reason, RecordedAtUtc = change.OccurredAtUtc, CorrelationId = envelope.CorrelationId.Trim(),
            ResultJson = JsonSerializer.Serialize(result, Json) });
        database.Attempts.Add(new PurchaseAttemptRow { CompanyId = company.CompanyId, CommandName = request.CommandName,
            IdempotencyKeyHash = key, RequestHash = request.RequestHash, ActorId = envelope.ActorId.Trim(), Outcome = 1,
            OrderId = order.Id, ReceiptAdjustmentId = change.AdjustmentId, CorrelationId = envelope.CorrelationId.Trim(), RecordedAtUtc = change.OccurredAtUtc });
        if (change.Revived)
            database.OutboxMessages.Add(RevivedMessage(order, general.Id, change.NetOpenedQuantity.Value, envelope,
                change.OccurredAtUtc, change.AdjustmentId, null, notificationTargets!));
        try
        {
            EnsureTransaction();
            await database.SaveChangesAsync(cancellationToken);
            audit.Enqueue(new AuditEventDraft(company.CompanyId, "purchasing.purchase-order.receipt-adjusted.v1", "Purchasing.PurchaseOrder",
                order.Id.ToString("D"), "AdjustReceipt", beforeVersion, order.Version, AuditActorTypes.User, envelope.ActorId.Trim(),
                envelope.PermissionCode, envelope.RoleCodes, JsonSerializer.Serialize(new { version = beforeVersion }, Json),
                JsonSerializer.Serialize(result, Json), change.Reason, null, AuditAiInvolvement.None, null, envelope.SourceChannel,
                envelope.ClientIpAddress, envelope.UserAgent, envelope.CorrelationId, envelope.CausationId, envelope.IdempotencyKey));
            await auditWriter.WritePendingAsync(transaction, cancellationToken);
            EnsureTransaction();
            return result;
        }
        catch (DbUpdateConcurrencyException) { auditWriter.DiscardPending(); throw new PurchasingServiceException(409, PurchasingStoreCodes.ConcurrentChange); }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        { auditWriter.DiscardPending(); throw new PurchasingServiceException(409, PurchasingStoreCodes.ConcurrentChange); }
        catch { auditWriter.DiscardPending(); throw; }
    }

    public async Task<PurchaseReceiptAdjustmentSupplyV1?> ReadSupplyAsync(Guid orderId, Guid generalLineId, CancellationToken cancellationToken = default)
    {
        EnsureTransaction();
        var answer = await new PurchaseFutureSupplyReadPort(database, company).GetAllocatableLineAsync(orderId, generalLineId, cancellationToken);
        if (answer.Outcome == PurchaseFutureSupplyOutcomeV1.NotFound) return null;
        var line = answer.Line ?? throw Unavailable();
        return new(company.CompanyId, line.OrderId, line.LineId, Text(line.ObservedOrderVersion),
            answer.Outcome == PurchaseFutureSupplyOutcomeV1.Allocatable, line.AllocatableText);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0) await database.DisposeAsync();
    }

    private void EnsureTransaction()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection)
            || database.Database.CurrentTransaction is not { } current || !ReferenceEquals(current.GetDbTransaction(), transaction)) throw Unavailable();
    }

    private static Guid General(PurchaseOrderLineRow line) => line.Kind switch
    {
        (int)PurchaseOrderLineKind.General when line.ParentLineId is null => line.LineId,
        (int)PurchaseOrderLineKind.Technical when line.ParentLineId is { } parent && parent != Guid.Empty => parent,
        _ => throw Unavailable(),
    };

    private static Quantity ParseQuantity(string text, QuantityUnit unit)
    {
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            || value < 0 || value >= 1_000_000_000_000_000m || Text(value) != text) throw InvalidRequest();
        try { return Quantity.Of(value, unit); } catch (ArgumentException) { throw InvalidRequest(); }
    }

    private static void Validate(string commandName, string requestHash, PurchasingWriteEnvelope envelope)
    {
        if (string.IsNullOrWhiteSpace(commandName) || commandName.Length > 160 || requestHash.Length != 64 || !requestHash.All(Uri.IsHexDigit)
            || string.IsNullOrWhiteSpace(envelope.ActorId) || envelope.ActorId.Trim().Length > 128 || string.IsNullOrWhiteSpace(envelope.PermissionCode)
            || envelope.RoleCodes is null || string.IsNullOrWhiteSpace(envelope.SourceChannel)
            || string.IsNullOrWhiteSpace(envelope.CorrelationId) || envelope.CorrelationId.Trim().Length > 128
            || string.IsNullOrWhiteSpace(envelope.IdempotencyKey) || envelope.IdempotencyKey.Length > 200
            || envelope.IdempotencyKey != envelope.IdempotencyKey.Trim()) throw InvalidRequest();
    }

    private static string Text(decimal quantity) => quantity.ToString("0.###", CultureInfo.InvariantCulture);
    private static PurchasingServiceException Unavailable() => new(503, PurchaseReceiptAdjustmentCodesV1.Unavailable);
    private static PurchasingServiceException InvalidRequest() => new(400, "purchasing_receipt_adjustment_invalid");
}
