using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Concurrency;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Quantities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed partial class PurchaseReceiptAdjustmentParticipant
{
    public static async Task<IPurchaseReceiptAdjustmentBatchParticipantV1> EnlistBatchAsync(DbConnection connection,
        DbTransaction transaction, Guid companyId, IAuditEventCollector audit, IAuditTransactionWriterV1 auditWriter,
        CancellationToken cancellationToken = default) =>
        (IPurchaseReceiptAdjustmentBatchParticipantV1)await EnlistAsync(connection, transaction, companyId, audit, auditWriter, cancellationToken);

    public async Task<IReadOnlyList<PurchaseReceiptAdjustmentTargetV1>> ReadTargetsAsync(Guid orderId,
        IReadOnlyList<Guid> acceptanceIds, CancellationToken cancellationToken = default)
    {
        EnsureTransaction();
        if (orderId == Guid.Empty || acceptanceIds is null || acceptanceIds.Count is < 1 or > PurchaseOrder.MaximumReceiptCorrectionsPerCommand
            || acceptanceIds.Contains(Guid.Empty)) throw InvalidRequest();
        var ids = acceptanceIds.Distinct().ToArray();
        var originals = await database.Acceptances.AsNoTracking().Where(item => item.OrderId == orderId && ids.Contains(item.AcceptanceId))
            .ToListAsync(cancellationToken);
        if (originals.Count == 0) return [];
        var order = await database.Orders.AsNoTracking().SingleOrDefaultAsync(item => item.Id == orderId, cancellationToken) ?? throw Unavailable();
        var lines = await database.OrderLines.AsNoTracking().Where(item => item.OrderId == orderId).ToListAsync(cancellationToken);
        var undone = await database.ReceiptAdjustments.AsNoTracking().Where(item => item.OrderId == orderId && ids.Contains(item.AcceptanceId))
            .GroupBy(item => item.AcceptanceId).Select(group => new { AcceptanceId = group.Key, Quantity = group.Sum(item => item.UndoQuantity) })
            .ToListAsync(cancellationToken);
        var results = new List<PurchaseReceiptAdjustmentTargetV1>(originals.Count);
        foreach (var original in originals)
        {
            var line = lines.SingleOrDefault(item => item.LineId == original.LineId) ?? throw Unavailable();
            var general = General(line);
            var alreadyUndone = undone.SingleOrDefault(item => item.AcceptanceId == original.AcceptanceId)?.Quantity ?? 0m;
            if (original.CompanyId != company.CompanyId || line.CompanyId != company.CompanyId || order.CompanyId != company.CompanyId
                || order.Version < 1 || alreadyUndone < 0 || alreadyUndone > original.Quantity || string.IsNullOrWhiteSpace(line.ProductVersion)
                || !lines.Any(item => item.LineId == general && item.Kind == (int)PurchaseOrderLineKind.General && item.ParentLineId == null))
                throw Unavailable();
            results.Add(new(company.CompanyId, orderId, original.AcceptanceId, line.LineId, general, line.ProductId,
                line.ProductVersion, line.UnitId, line.UnitIsKilogram, Text(order.Version), Text(original.Quantity), Text(alreadyUndone)));
        }
        return results.AsReadOnly();
    }

    public async Task<PurchaseReceiptAdjustmentBatchResultV1?> FindBatchReplayAsync(string commandName, string requestHash,
        PurchasingWriteEnvelope envelope, CancellationToken cancellationToken = default)
    {
        EnsureTransaction(); Validate(commandName, requestHash, envelope);
        var key = PurchasingStore.IdempotencyKeyHash(envelope.IdempotencyKey);
        var attempt = await database.Attempts.AsNoTracking().SingleOrDefaultAsync(item => item.CommandName == commandName
            && item.IdempotencyKeyHash == key, cancellationToken);
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
        if (attempt.Outcome != 1 || attempt.ReceiptAdjustmentBatchId is not { } batchId || attempt.OrderId is not { } orderId
            || attempt.ReceiptAdjustmentId is not null || attempt.RefusalJson is not null || attempt.RequestId is not null) throw Unavailable();
        var batch = await database.ReceiptAdjustmentBatches.AsNoTracking().SingleOrDefaultAsync(item => item.BatchId == batchId, cancellationToken)
            ?? throw Unavailable();
        var rows = await database.ReceiptAdjustments.AsNoTracking().Where(item => item.BatchId == batchId)
            .OrderBy(item => item.BatchLineNo).ToListAsync(cancellationToken);
        PurchaseReceiptAdjustmentBatchResultV1? result;
        try { result = JsonSerializer.Deserialize<PurchaseReceiptAdjustmentBatchResultV1>(batch.ResultJson, Json); }
        catch (JsonException) { throw Unavailable(); }
        if (result is null || result.Lines is null || result.Supplies is null || result.IsReplay
            || result.Lines.Any(item => item is null) || result.Supplies.Any(item => item is null)
            || batch.CompanyId != company.CompanyId || batch.OrderId != orderId || batch.RecordedBy != attempt.ActorId
            || result.CompanyId != batch.CompanyId || result.OrderId != batch.OrderId || result.BatchId != batch.BatchId
            || result.OrderVersion != Text(batch.OrderVersionAfter) || result.OriginalCorrelationId != attempt.CorrelationId
            || batch.CorrelationId != attempt.CorrelationId || result.Lines.Count != batch.LineCount || rows.Count != batch.LineCount
            || result.Lines.Select(item => item.AdjustmentId).Distinct().Count() != rows.Count
            || result.Supplies.Select(item => item.GeneralLineId).Distinct().Count() != result.Supplies.Count) throw Unavailable();
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index]; var line = result.Lines[index];
            if (row.BatchId != batchId || row.BatchLineNo != index + 1 || row.CompanyId != batch.CompanyId || row.OrderId != batch.OrderId
                || row.OrderVersionAfter != batch.OrderVersionAfter || row.CorrelationId != batch.CorrelationId
                || row.RecordedBy != batch.RecordedBy || row.Reason != batch.Reason || row.RecordedAtUtc != batch.RecordedAtUtc
                || line.IsReplay || line.CompanyId != row.CompanyId || line.OrderId != row.OrderId || line.AdjustmentId != row.AdjustmentId
                || line.AcceptanceId != row.AcceptanceId || line.LineId != row.LineId || line.GeneralLineId == Guid.Empty
                || line.OrderVersion != result.OrderVersion || line.OriginalCorrelationId != result.OriginalCorrelationId
                || line.NetOpenedQuantity != Text(row.NetOpenedQuantity) || line.NewAcceptanceId != row.NewAcceptanceId
                || line.NewAcceptedQuantity != Text(row.NewAcceptedQuantity) || line.Revived != row.Revived
                || JsonSerializer.Serialize(line, Json) != row.ResultJson) throw Unavailable();
        }
        foreach (var supply in result.Supplies)
        {
            var own = result.Lines.Where(item => item.GeneralLineId == supply.GeneralLineId).ToArray();
            if (own.Length == 0 || supply.GeneralLineId == Guid.Empty || own.Any(item => item.AllocatableQuantity != supply.AllocatableQuantity)
                || supply.Revived != own.Any(item => item.Revived)
                || supply.NetOpenedQuantity != Text(rows.Where(row => own.Any(item => item.AdjustmentId == row.AdjustmentId)).Sum(row => row.NetOpenedQuantity))
                || !CanonicalQuantity(supply.AllocatableQuantity)) throw Unavailable();
        }
        if (result.Lines.Any(line => !result.Supplies.Any(supply => supply.GeneralLineId == line.GeneralLineId))) throw Unavailable();
        return result with { IsReplay = true, Lines = result.Lines.Select(item => item with { IsReplay = true }).ToArray() };
    }

    public async Task<PurchaseReceiptAdjustmentBatchResultV1> StageBatchAsync(PurchaseReceiptAdjustmentBatchStageV1 request,
        PurchasingWriteEnvelope envelope, CancellationToken cancellationToken = default)
    {
        EnsureTransaction();
        if (Interlocked.Exchange(ref staged, 1) != 0) throw new InvalidOperationException("A receipt adjustment participant is single-use.");
        ArgumentNullException.ThrowIfNull(request);
        Validate(request.CommandName, request.RequestHash, envelope);
        var replay = await FindBatchReplayAsync(request.CommandName, request.RequestHash, envelope, cancellationToken);
        if (replay is not null) return replay;
        if (request.CompanyId != company.CompanyId) throw new PurchasingServiceException(404, PurchaseReceiptAdjustmentCodesV1.OriginalAcceptanceNotFound);
        if (request.BatchId == Guid.Empty || request.OrderId == Guid.Empty || request.Lines is null
            || request.Lines.Count is < 1 or > PurchaseOrder.MaximumReceiptCorrectionsPerCommand || request.Lines.Any(item => item is null)
            || !long.TryParse(request.ExpectedOrderVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var expected) || expected < 1
            || request.ExpectedOrderVersion != Text(expected) || string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Length > PurchaseOrder.MaximumReasonLength) throw InvalidRequest();
        if (auditWriter.HasPendingDrafts) throw Unavailable();
        var targets = await ReadTargetsAsync(request.OrderId, request.Lines.Select(item => item.AcceptanceId).ToArray(), cancellationToken);
        if (targets.Count != request.Lines.Select(item => item.AcceptanceId).Distinct().Count())
            throw new PurchasingServiceException(404, PurchaseReceiptAdjustmentCodesV1.OriginalAcceptanceNotFound);
        await PurchaseLineSerializationLock.Bind(connection, transaction, company.CompanyId)
            .AcquireAllAsync(targets.Select(item => item.GeneralLineId), cancellationToken);
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
        var ids = request.Lines.Select(item => item.AdjustmentId).ToArray();
        var replacementIds = request.Lines.Where(item => item.NewAcceptanceId is not null).Select(item => item.NewAcceptanceId!.Value).ToArray();
        if (batches.Any(item => item.BatchId == request.BatchId)
            || await database.ReceiptAdjustments.AsNoTracking().AnyAsync(item => ids.Contains(item.AdjustmentId), cancellationToken)
            || await database.Acceptances.AsNoTracking().AnyAsync(item => replacementIds.Contains(item.AcceptanceId), cancellationToken))
            throw new PurchasingServiceException(409, PurchasingStoreCodes.SameKeyPayloadMismatch);
        var inputs = request.Lines.Select(item =>
        {
            var target = targets.Single(target => target.AcceptanceId == item.AcceptanceId);
            var unit = lines.Single(line => line.LineId == target.LineId).UnitIsKilogram ? QuantityUnit.Kilogram : QuantityUnit.Piece;
            return new PurchaseReceiptAdjustmentInput(item.AdjustmentId, item.AcceptanceId, ParseQuantity(item.UndoQuantity, unit),
                item.IsSupplierReturn, item.ReplacementExpected, item.NewAcceptanceId, ParseQuantity(item.NewAcceptedQuantity ?? "0", unit));
        }).ToArray();
        var beforeVersion = order.Version;
        PurchaseReceiptAdjustmentBatchChange change;
        try { change = order.AdjustReceipts(expected, inputs, envelope.ActorId, request.Reason, request.OccurredAtUtc); }
        catch (PurchasingRuleException exception) { throw new PurchasingServiceException(409, exception.Code); }
        catch (PurchasingVersionConflictException) { throw new PurchasingServiceException(409, PurchasingStoreCodes.ConcurrentChange); }
        var notificationTargets = change.Supplies.Where(item => item.Revived).ToDictionary(item => item.GeneralLineId,
            item => NotificationTargets(request.NotificationSnapshots, order.Id, item.GeneralLineId, beforeVersion));
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
        var correlation = envelope.CorrelationId.Trim();
        var results = change.Adjustments.Select(item =>
        {
            var target = targets.Single(target => target.AcceptanceId == item.AcceptanceId);
            var supply = change.Supplies.Single(supply => supply.GeneralLineId == target.GeneralLineId);
            return new PurchaseReceiptAdjustmentResultV1(company.CompanyId, item.AdjustmentId, order.Id, item.AcceptanceId, item.LineId,
                target.GeneralLineId, Text(order.Version), Text(item.NetOpenedQuantity.Value), Text(supply.AllocatableQuantity.Value),
                item.Revived, false, correlation, item.NewAcceptanceId, Text(item.NewAcceptedQuantity.Value));
        }).ToArray();
        var supplies = change.Supplies.Select(item => new PurchaseReceiptAdjustmentBatchSupplyV1(item.GeneralLineId,
            Text(item.NetOpenedQuantity.Value), Text(item.AllocatableQuantity.Value), item.Revived)).ToArray();
        var result = new PurchaseReceiptAdjustmentBatchResultV1(company.CompanyId, request.BatchId, order.Id, Text(order.Version),
            results, supplies, false, correlation);
        var occurred = request.OccurredAtUtc.ToUniversalTime();
        database.ReceiptAdjustmentBatches.Add(new() { CompanyId = company.CompanyId, BatchId = request.BatchId, OrderId = order.Id,
            OrderVersionAfter = order.Version, LineCount = results.Length, RecordedBy = envelope.ActorId.Trim(), Reason = request.Reason,
            RecordedAtUtc = occurred, CorrelationId = correlation, ResultJson = JsonSerializer.Serialize(result, Json) });
        var key = PurchasingStore.IdempotencyKeyHash(envelope.IdempotencyKey);
        for (var index = 0; index < change.Adjustments.Count; index++)
        {
            var item = change.Adjustments[index]; var response = results[index];
            database.ReceiptAdjustments.Add(new() { CompanyId = company.CompanyId, AdjustmentId = item.AdjustmentId, OrderId = order.Id,
                BatchId = request.BatchId, BatchLineNo = index + 1, AcceptanceId = item.AcceptanceId, LineId = item.LineId,
                UndoQuantity = item.UndoQuantity.Value, IsSupplierReturn = item.IsSupplierReturn, ReplacementExpected = item.ReplacementExpected,
                NewAcceptanceId = item.NewAcceptanceId, NewAcceptedQuantity = item.NewAcceptedQuantity.Value, OrderVersionAfter = order.Version,
                Revived = item.Revived, NetOpenedQuantity = item.NetOpenedQuantity.Value, RecordedBy = item.Actor, Reason = item.Reason,
                RecordedAtUtc = occurred, CorrelationId = correlation, ResultJson = JsonSerializer.Serialize(response, Json) });
            if (item.NewAcceptanceId is { } newId)
            {
                var acceptance = new PurchaseAcceptanceStageResultV1(company.CompanyId, newId, order.Id, item.LineId, Text(order.Version),
                    Text(item.NewAcceptedQuantity.Value), Text(order.Lines.Single(line => line.Id == item.LineId).RemainingQuantity.Value),
                    false, correlation, response.GeneralLineId);
                database.Acceptances.Add(new() { CompanyId = company.CompanyId, AcceptanceId = newId, OrderId = order.Id, LineId = item.LineId,
                    Quantity = item.NewAcceptedQuantity.Value, OrderVersionAfter = order.Version, RecordedAtUtc = occurred,
                    ActorId = envelope.ActorId.Trim(), CorrelationId = correlation, RequestHash = request.RequestHash,
                    IdempotencyKeyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.CommandName + ":" + key + ":replacement:" + newId))),
                    ResultJson = JsonSerializer.Serialize(acceptance, Json) });
            }
        }
        database.Attempts.Add(new() { CompanyId = company.CompanyId, CommandName = request.CommandName, IdempotencyKeyHash = key,
            RequestHash = request.RequestHash, ActorId = envelope.ActorId.Trim(), Outcome = 1, OrderId = order.Id,
            ReceiptAdjustmentBatchId = request.BatchId, CorrelationId = correlation, RecordedAtUtc = occurred });
        foreach (var supply in change.Supplies.Where(item => item.Revived))
            database.OutboxMessages.Add(RevivedMessage(order, supply.GeneralLineId, supply.NetOpenedQuantity.Value, envelope,
                occurred, null, request.BatchId, notificationTargets[supply.GeneralLineId]));
        try
        {
            EnsureTransaction();
            await database.SaveChangesAsync(cancellationToken);
            audit.Enqueue(new AuditEventDraft(company.CompanyId, "purchasing.purchase-order.receipts-adjusted.v1", "Purchasing.PurchaseOrder",
                order.Id.ToString("D"), "AdjustReceipts", beforeVersion, order.Version, AuditActorTypes.User, envelope.ActorId.Trim(),
                envelope.PermissionCode, envelope.RoleCodes, JsonSerializer.Serialize(new { version = beforeVersion }, Json),
                JsonSerializer.Serialize(result, Json), request.Reason, null, AuditAiInvolvement.None, null, envelope.SourceChannel,
                envelope.ClientIpAddress, envelope.UserAgent, correlation, envelope.CausationId, envelope.IdempotencyKey));
            await auditWriter.WritePendingAsync(transaction, cancellationToken);
            EnsureTransaction();
            return result;
        }
        catch (DbUpdateConcurrencyException) { auditWriter.DiscardPending(); throw new PurchasingServiceException(409, PurchasingStoreCodes.ConcurrentChange); }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        { auditWriter.DiscardPending(); throw new PurchasingServiceException(409, PurchasingStoreCodes.ConcurrentChange); }
        catch { auditWriter.DiscardPending(); throw; }
    }

    private static bool CanonicalQuantity(string text) => decimal.TryParse(text, NumberStyles.AllowDecimalPoint,
        CultureInfo.InvariantCulture, out var value) && value >= 0 && value < 1_000_000_000_000_000m && Text(value) == text;

    private static PurchasingOutboxRow RevivedMessage(PurchaseOrder order, Guid generalLineId, decimal netOpened,
        PurchasingWriteEnvelope envelope, DateTimeOffset now, Guid? adjustmentId, Guid? batchId,
        IReadOnlyList<PurchaseRevivalSalesTargetV1> salesTargets) => new()
    {
        Id = Guid.NewGuid(), CompanyId = order.CompanyId, EventType = PurchasingOrderLineRevivedEventV1.EventType,
        SchemaVersion = PurchasingOrderLineRevivedEventV1.SchemaVersion, SourceOperation = batchId is null ? "AdjustReceipt" : "AdjustReceipts",
        AggregateType = PurchasingOrderLineRevivedEventV1.AggregateType, AggregateId = generalLineId, AggregateVersion = order.Version,
        Sequence = checked((int)order.Version), OccurredAtUtc = now, RecordedAtUtc = now,
        CorrelationId = envelope.CorrelationId.Trim(), CausationId = envelope.CausationId?.Trim(),
        PayloadJson = JsonSerializer.Serialize(new PurchasingOrderLineRevivedPayloadV1(order.CompanyId, order.Id, generalLineId,
            order.Version, Text(netOpened), adjustmentId, batchId, salesTargets), Json),
    };

    private IReadOnlyList<PurchaseRevivalSalesTargetV1> NotificationTargets(
        IReadOnlyList<PurchaseRevivalNotificationSnapshotV1>? snapshots, Guid orderId, Guid generalLineId, long beforeVersion)
    {
        var matches = snapshots?.Where(item => item is not null && item.GeneralLineId == generalLineId).ToArray();
        if (matches is not { Length: 1 }) throw Unavailable();
        var snapshot = matches[0];
        if (snapshot.CompanyId != company.CompanyId || snapshot.PurchaseOrderId != orderId
            || snapshot.ObservedOrderVersion != Text(beforeVersion) || snapshot.SalesTargets is null
            || snapshot.SalesTargets.Any(item => item is null || item.SalesOrderId == Guid.Empty || item.SalesOrderLineId == Guid.Empty)
            || snapshot.SalesTargets.Distinct().Count() != snapshot.SalesTargets.Count) throw Unavailable();
        return snapshot.SalesTargets.OrderBy(item => item.SalesOrderId).ThenBy(item => item.SalesOrderLineId).ToArray();
    }
}
