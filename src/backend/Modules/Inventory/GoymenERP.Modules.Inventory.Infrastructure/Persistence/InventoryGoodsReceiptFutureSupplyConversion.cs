using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Concurrency;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Allocations;
using GoymenERP.Modules.Inventory.Domain.FutureSupply;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Engine = GoymenERP.Modules.Inventory.Infrastructure.Persistence.InventoryFutureAllocationEngine;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// F-017 STOK §3.5 (F017-c): Inventory's answer to <see cref="IInventoryGoodsReceiptFutureSupplyPortV1"/>. The session is an
/// InventoryDbContext on the goods receipt host's open connection and transaction (the one the purchase acceptance and the Inventory
/// label were staged on, ALS-D84), for the server-resolved company, with Inventory's own audit interceptor. The caller owns commit
/// and rollback and Inventory never closes the connection; Inventory still writes only its own tables.
/// </summary>
internal sealed class InventoryGoodsReceiptFutureSupplyPort(
    ICompanyContext companyContext,
    IPurchaseLineSerializationLockBinderV1 purchaseLineLocks,
    IInventoryFutureDemandSourceV1 demandSource,
    IInventoryFutureUnitConverterV1 unitConverter,
    TimeProvider timeProvider) : IInventoryGoodsReceiptFutureSupplyPortV1
{
    public async Task<IInventoryGoodsReceiptFutureSupplySessionV1> EnlistAsync(DbConnection connection,
        DbTransaction transaction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty || connection.State != ConnectionState.Open
            || !ReferenceEquals(transaction.Connection, connection))
        {
            throw new InvalidOperationException("Mal kabul gelecek tedarik çevirimi çözümlenmiş şirket ve sahibin açık işlemini ister.");
        }

        var companyId = companyContext.CompanyId;
        var company = new CompanyContext();
        company.Set(companyId);
        var audit = new AuditEventCollector();
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer(connection, contextOwnsConnection: false)
            .AddInterceptors(new AuditSaveChangesInterceptor(audit, company))
            .Options;
        var context = new InventoryDbContext(options, company);
        try
        {
            await context.Database.UseTransactionAsync(transaction, cancellationToken);
            return new InventoryGoodsReceiptFutureSupplySession(context, audit, connection, transaction, companyId,
                companyContext, purchaseLineLocks, demandSource, unitConverter, timeProvider);
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }
}

/// <summary>
/// One goods receipt's conversion. Order of work: idempotency (the receipt's own key) → the purchase general line lock (re-entrant:
/// the acceptance already holds it) → the line's active future allocations (tracked) → the Sales demand lines → the consumption order
/// → the received label's Free position → per allocation, consume and create the physical allocation, or release it with
/// acceptance-link-failed when a business link fails → one save. A technical failure throws and writes nothing.
/// </summary>
internal sealed class InventoryGoodsReceiptFutureSupplySession(
    InventoryDbContext context,
    AuditEventCollector auditEvents,
    DbConnection connection,
    DbTransaction transaction,
    Guid companyId,
    ICompanyContext companyContext,
    IPurchaseLineSerializationLockBinderV1 purchaseLineLocks,
    IInventoryFutureDemandSourceV1 demandSource,
    IInventoryFutureUnitConverterV1 unitConverter,
    TimeProvider timeProvider) : IInventoryGoodsReceiptFutureSupplySessionV1
{
    internal const string PhysicalAllocationEntityType = "Inventory.StockAllocation";
    internal const string PhysicalAllocationCreatedEvent = "inventory.physical-allocation.created.v1";
    internal const string PhysicalAllocationCreatedOperation = "Create";

    private const int MaximumQuantityTextLength = 40;

    private bool disposed;

    public async Task<InventoryGoodsReceiptFutureSupplyResultV1> ConvertAsync(InventoryGoodsReceiptFutureSupplyRequestV1 request,
        InventoryCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Engine.RequireMetadata(metadata);
        EnsureActive();
        cancellationToken.ThrowIfCancellationRequested();

        // The host builds the request from its own staged acceptance and label: a malformed one is a technical failure.
        if (request.CompanyId != companyId || request.AcceptanceId == Guid.Empty || request.PurchaseOrderId == Guid.Empty
            || request.PurchaseOrderGeneralLineId == Guid.Empty || request.PurchaseUnitId == Guid.Empty
            || request.ProductId == Guid.Empty || string.IsNullOrWhiteSpace(request.ProductVersion)
            || request.LabelId == Guid.Empty)
        {
            throw Unavailable();
        }

        var keyHash = Engine.KeyHash(metadata.IdempotencyKey);
        var payloadHash = Engine.PayloadHash(new
        {
            request.CompanyId,
            request.AcceptanceId,
            request.PurchaseOrderId,
            request.PurchaseOrderGeneralLineId,
            request.PurchaseUnitId,
            request.ProductId,
            request.ProductVersion,
            request.LabelId,
            request.AcceptedQuantityText,
            request.ReceivedQuantityKgText,
            AllocationOrder = request.AllocationOrder ?? Array.Empty<Guid>(),
        });
        var prior = await context.Commands.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == InventoryGoodsReceiptFutureSupplyV1.CommandName
                && row.IdempotencyKeyHash == keyHash, cancellationToken);
        if (prior is not null)
        {
            return Replay(prior, request, payloadHash, metadata);
        }

        if (request.ReceiptIsReplay)
        {
            // The receipt answered its stored result: nothing was converted then (no line allocation), so nothing is now.
            return new InventoryGoodsReceiptFutureSupplyResultV1(request.AcceptanceId, [], true);
        }

        var accepted = ParseAccepted(request.AcceptedQuantityText);
        var receivedKg = ParseKilograms(request.ReceivedQuantityKgText);

        // §3.3/§3.5: the purchase general line lock in the receipt's transaction (re-entrant; the acceptance took it first).
        try
        {
            await purchaseLineLocks.Bind(connection, transaction)
                .AcquireAsync(request.PurchaseOrderGeneralLineId, cancellationToken);
        }
        catch (PurchaseLineBusyException exception)
        {
            throw Engine.Error(409, exception.Code, exception);
        }

        var rows = await context.FutureSupplyAllocations
            .Where(row => row.PurchaseOrderLineId == request.PurchaseOrderGeneralLineId
                && row.Status == (int)FutureSupplyAllocationStatus.Active)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return new InventoryGoodsReceiptFutureSupplyResultV1(request.AcceptanceId, [], false);
        }

        if (rows.Any(row => row.PurchaseOrderId != request.PurchaseOrderId || row.PurchaseUnitId != request.PurchaseUnitId
                || row.ProductId != request.ProductId
                || !string.Equals(row.DemandKind, InventoryFutureAllocationKindsV1.SalesOrderLine, StringComparison.Ordinal)))
        {
            throw Unavailable();
        }

        var ledgerRow = await context.FutureSupplyLines
            .SingleOrDefaultAsync(ledger => ledger.PurchaseOrderLineId == request.PurchaseOrderGeneralLineId, cancellationToken)
            ?? throw Unavailable();
        if (ledgerRow.PurchaseOrderId != request.PurchaseOrderId || ledgerRow.PurchaseUnitId != request.PurchaseUnitId)
        {
            throw Unavailable();
        }

        var demands = await ReadDemandsAsync(rows, cancellationToken);
        var candidates = rows
            .Select(row => new InventoryFutureSupplyConsumptionCandidateV1(row.Id, row.ManualSequence,
                demands[row.DemandLineId].Line?.DueDate, row.CreatedAtUtc))
            .ToList();
        if (!InventoryFutureSupplyConsumptionOrderV1.TryOrder(candidates, request.AllocationOrder, out var ordered))
        {
            throw Engine.Error(422, InventoryGoodsReceiptFutureSupplyV1.AllocationOrderInvalid);
        }

        var label = await context.Labels.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == request.LabelId, cancellationToken)
            ?? throw Unavailable();
        if (label.ProductId != request.ProductId)
        {
            throw Unavailable();
        }

        var freeRows = await context.Balances.AsNoTracking()
            .Where(row => row.LabelId == label.Id && row.Block == (int)BlockState.Free && row.QuantityKg > 0m)
            .ToListAsync(cancellationToken);
        if (freeRows.Count > 1)
        {
            throw Unavailable();
        }

        var source = freeRows.Count == 1 ? freeRows[0] : null;
        string? labelFailure = null;
        var unheldKg = 0m;
        if (source is null)
        {
            labelFailure = InventoryFutureSupplyLinkFailuresV1.LabelBlocked;
        }
        else if (!InventorySalesOwnershipGuardV1.IsCompanyOwnedAndHeld((StockPartyKind)source.OwnerKind, source.OwnerId,
                     (StockPartyKind)source.CustodianKind, source.CustodianId, companyId))
        {
            // Same rule as a Sales-sourced physical allocation (#479 review P2-2); no fason policy is chosen here.
            labelFailure = InventoryFutureSupplyLinkFailuresV1.OwnerNotCompany;
        }
        else
        {
            var held = await context.Allocations.AsNoTracking()
                .Where(row => row.LabelId == label.Id
                    && row.OwnerKind == source.OwnerKind && row.OwnerId == source.OwnerId
                    && row.CustodianKind == source.CustodianKind && row.CustodianId == source.CustodianId
                    && row.LocationId == source.LocationId && row.Block == (int)BlockState.Free
                    && row.Status == (int)StockAllocationStatus.Active)
                .SumAsync(row => (decimal?)row.RemainingKg, cancellationToken) ?? 0m;
            unheldKg = source.QuantityKg - held;
        }

        var now = timeProvider.GetUtcNow();
        var byId = rows.ToDictionary(row => row.Id);
        var leftover = accepted;
        var conversions = new List<InventoryFutureSupplyConversionV1>();
        foreach (var candidate in ordered)
        {
            if (leftover.Value == 0m)
            {
                break;
            }

            var row = byId[candidate.AllocationId];
            var remaining = Engine.RestoreStored(row).Remaining;
            var share = remaining < leftover ? remaining : leftover;
            var demand = demands[row.DemandLineId];
            var failure = labelFailure ?? DemandFailure(row, demand, label);
            Kilograms kilograms = default;
            if (failure is null)
            {
                (kilograms, failure) = await KilogramsAsync(row, share, accepted, receivedKg, cancellationToken);
            }

            if (failure is null && kilograms.Value > unheldKg)
            {
                failure = InventoryFutureSupplyLinkFailuresV1.InsufficientLabelQuantity;
            }

            if (failure is null)
            {
                conversions.Add(Convert(request, metadata, row, ledgerRow, share, kilograms, demand.Line!, label, source!, now));
                unheldKg -= kilograms.Value;
                // §3.5: only a consumed allocation takes its share of the accepted quantity. A failed link is released
                // (acceptance-link-failed), so its share stays on the label for the next allocation in the order.
                leftover -= share;
            }
            else
            {
                conversions.Add(await FailAsync(request, metadata, row, failure, now, cancellationToken));
            }
        }

        var result = new InventoryGoodsReceiptFutureSupplyResultV1(request.AcceptanceId, conversions, false);
        if (conversions.Count == 0)
        {
            return result;
        }

        context.Commands.Add(new InventoryCommandRow
        {
            CompanyId = companyId,
            CommandName = InventoryGoodsReceiptFutureSupplyV1.CommandName,
            IdempotencyKeyHash = keyHash,
            ActorId = metadata.ActorId,
            PayloadHash = payloadHash,
            TargetId = request.AcceptanceId,
            ResultJson = JsonSerializer.Serialize(result, Engine.Json),
            CorrelationId = metadata.CorrelationId,
            RecordedAtUtc = now,
        });
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            // The receipt's transaction is no longer usable for this conversion; the host rolls the receipt back.
            context.ChangeTracker.Clear();
            throw Engine.Error(409, InventoryFutureAllocationCodesV1.ConcurrentChange, exception);
        }
        catch (DbUpdateException exception)
        {
            context.ChangeTracker.Clear();
            throw Engine.Error(503, InventoryFutureAllocationCodesV1.ServiceUnavailable, exception);
        }

        return result;
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        await context.DisposeAsync();
    }

    private static InventoryAllocationException Unavailable(Exception? inner = null) =>
        Engine.Error(503, InventoryFutureAllocationCodesV1.ServiceUnavailable, inner);

    private static InventoryGoodsReceiptFutureSupplyResultV1 Replay(InventoryCommandRow prior,
        InventoryGoodsReceiptFutureSupplyRequestV1 request, string payloadHash, InventoryCommandMetadata metadata)
    {
        if (!string.Equals(prior.PayloadHash, payloadHash, StringComparison.Ordinal)
            || !string.Equals(prior.ActorId, metadata.ActorId, StringComparison.Ordinal))
        {
            throw Engine.Error(409, InventoryFutureAllocationCodesV1.IdempotencyConflict);
        }

        if (!request.ReceiptIsReplay)
        {
            // A first receipt can never find its own conversion already recorded.
            throw Unavailable();
        }

        InventoryGoodsReceiptFutureSupplyResultV1? stored;
        try
        {
            stored = JsonSerializer.Deserialize<InventoryGoodsReceiptFutureSupplyResultV1>(prior.ResultJson, Engine.Json);
        }
        catch (JsonException exception)
        {
            throw Unavailable(exception);
        }

        if (stored is null || stored.AcceptanceId != request.AcceptanceId || stored.Conversions is null)
        {
            throw Unavailable();
        }

        return stored with { IsReplay = true };
    }

    private static FutureSupplyQuantity ParseAccepted(string? text)
    {
        try
        {
            return InventoryFutureAllocationTextV1.TryPositiveQuantity(text, out var quantity) ? quantity : throw Unavailable();
        }
        catch (FutureSupplyRuleException exception)
        {
            throw Unavailable(exception);
        }
    }

    private static Kilograms ParseKilograms(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > MaximumQuantityTextLength
            || text.Any(character => !char.IsAsciiDigit(character) && character != '.')
            || !decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            throw Unavailable();
        }

        try
        {
            return Kilograms.Positive(value);
        }
        catch (InventoryRuleException exception)
        {
            throw Unavailable(exception);
        }
    }

    /// <summary>Every distinct Sales line once; an unreadable Sales answer is a technical failure (the receipt rolls back).</summary>
    private async Task<Dictionary<Guid, InventoryFutureDemandSourceAnswerV1>> ReadDemandsAsync(
        IReadOnlyList<FutureSupplyAllocationRow> rows, CancellationToken cancellationToken)
    {
        var demands = new Dictionary<Guid, InventoryFutureDemandSourceAnswerV1>();
        foreach (var row in rows)
        {
            if (demands.ContainsKey(row.DemandLineId))
            {
                continue;
            }

            InventoryFutureDemandSourceAnswerV1 answer;
            try
            {
                answer = await demandSource.GetDemandLineAsync(row.DemandId, row.DemandLineId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw Unavailable(exception);
            }

            if (answer is null || answer.Outcome == InventoryFutureDemandSourceOutcomeV1.Unavailable
                || (answer.Outcome == InventoryFutureDemandSourceOutcomeV1.Open
                    && (answer.Line is null || answer.Line.OrderId != row.DemandId || answer.Line.LineId != row.DemandLineId)))
            {
                throw Unavailable();
            }

            demands[row.DemandLineId] = answer;
        }

        return demands;
    }

    private static string? DemandFailure(FutureSupplyAllocationRow row, InventoryFutureDemandSourceAnswerV1 demand, LabelRow label)
    {
        if (demand.Outcome != InventoryFutureDemandSourceOutcomeV1.Open || demand.Line is null)
        {
            return InventoryFutureSupplyLinkFailuresV1.DemandNotApproved;
        }

        return demand.Line.ProductId != label.ProductId || demand.Line.ProductId != row.ProductId
            ? InventoryFutureSupplyLinkFailuresV1.ProductMismatch
            : null;
    }

    /// <summary>
    /// The share's kilograms. The whole accepted quantity is the label's measured kilograms (no factor); a partial share is the central
    /// conversion's exact answer (at most three decimals, never rounded) or a link failure. Nothing is guessed.
    /// </summary>
    private async Task<(Kilograms Kilograms, string? Failure)> KilogramsAsync(FutureSupplyAllocationRow row,
        FutureSupplyQuantity share, FutureSupplyQuantity accepted, Kilograms receivedKg, CancellationToken cancellationToken)
    {
        if (share == accepted)
        {
            return (receivedKg, null);
        }

        InventoryFutureUnitConversionV1 answer;
        try
        {
            answer = await unitConverter.ConvertToKilogramsAsync(row.ProductId, row.ProductVersion, share.Value, row.PurchaseUnitId,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw Unavailable(exception);
        }

        if (answer is not { Outcome: InventoryFutureUnitConversionOutcomeV1.Converted, Quantity: { } converted })
        {
            return (default, InventoryFutureSupplyLinkFailuresV1.UnitConversionUnavailable);
        }

        try
        {
            return (Kilograms.Positive(converted), null);
        }
        catch (InventoryRuleException)
        {
            return (default, InventoryFutureSupplyLinkFailuresV1.UnitConversionUnavailable);
        }
    }

    private InventoryFutureSupplyConversionV1 Convert(
        InventoryGoodsReceiptFutureSupplyRequestV1 request,
        InventoryCommandMetadata metadata,
        FutureSupplyAllocationRow row,
        FutureSupplyLineRow ledgerRow,
        FutureSupplyQuantity share,
        Kilograms kilograms,
        InventoryFutureDemandLineV1 demandLine,
        LabelRow label,
        BalanceRow source,
        DateTimeOffset now)
    {
        var allocation = Engine.RestoreStored(row);
        var before = Engine.View(row);
        var versionBefore = row.Version;
        StockAllocation physical;
        try
        {
            allocation.Consume(row.Version, share);
            var identity = new MaterialLabel(label.Id, label.CompanyId, label.Code, label.ProductId, label.LotId,
                label.ProductVersion);
            var position = new StockPosition(companyId,
                new StockOwner((StockPartyKind)source.OwnerKind, source.OwnerId),
                new StockCustodian((StockPartyKind)source.CustodianKind, source.CustodianId),
                source.LocationId, identity, BlockState.Free);
            physical = StockAllocation.Create(Guid.NewGuid(), position, kilograms);
        }
        catch (Exception exception) when (exception is ArgumentException or FutureSupplyRuleException or InventoryRuleException
            or StockAllocationException)
        {
            throw Unavailable(exception);
        }

        ReturnToLedger(ledgerRow, share, now);
        row.RemainingQuantity = allocation.Remaining.Value;
        row.Status = (int)allocation.Status;
        row.Version = allocation.Version;
        row.UpdatedAtUtc = now;
        var after = Engine.View(row);

        // The physical allocation on the received label: same shape as a Sales-sourced physical allocation (INV-AL-1).
        var physicalRow = new AllocationRow
        {
            Id = physical.Id,
            CompanyId = companyId,
            OwnerKind = source.OwnerKind,
            OwnerId = source.OwnerId,
            CustodianKind = source.CustodianKind,
            CustodianId = source.CustodianId,
            LocationId = source.LocationId,
            LabelId = label.Id,
            Block = (int)BlockState.Free,
            DemandKind = InventoryAllocationDemandKindsV1.SalesOrderLine,
            DemandId = row.DemandId,
            DemandLineId = row.DemandLineId,
            DemandProductId = demandLine.ProductId,
            DemandProductVersion = demandLine.ProductVersion,
            Reason = InventoryGoodsReceiptFutureSupplyV1.PhysicalAllocationReason,
            ReservedUntilUtc = null,
            AllocatedKg = physical.Allocated.Value,
            RemainingKg = physical.Remaining.Value,
            Status = (int)physical.Status,
            Version = physical.Version,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        context.Allocations.Add(physicalRow);

        var link = new FutureSupplyConversionRow
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            AcceptanceId = request.AcceptanceId,
            FutureAllocationId = row.Id,
            LabelId = label.Id,
            Outcome = (int)InventoryFutureSupplyConversionOutcomeV1.Converted,
            Quantity = share.Value,
            PhysicalAllocationId = physicalRow.Id,
            PhysicalQuantityKg = physicalRow.AllocatedKg,
            FailureReason = null,
            CreatedAtUtc = now,
        };
        context.FutureSupplyConversions.Add(link);

        var kilogramsText = physical.Allocated.Value.ToString("0.###", CultureInfo.InvariantCulture);
        Engine.Audit(auditEvents, metadata, companyId, row.Id, InventoryFutureAllocationConvertedOutboxV1.EventType,
            InventoryGoodsReceiptFutureSupplyV1.ConvertOperation, versionBefore, row.Version,
            JsonSerializer.Serialize(before, Engine.Json),
            JsonSerializer.Serialize(new
            {
                Allocation = after,
                request.AcceptanceId,
                ConsumedQuantity = share.ToString(),
                PhysicalAllocationId = physicalRow.Id,
                LabelId = label.Id,
                PhysicalQuantityKg = kilogramsText,
            }, Engine.Json),
            InventoryGoodsReceiptFutureSupplyV1.ConvertedAuditReason);
        PhysicalAudit(metadata, physicalRow, request.AcceptanceId, row.Id);
        AddConvertedOutbox(row, request, share, after, physicalRow, kilogramsText, metadata, now);

        return new InventoryFutureSupplyConversionV1(row.Id, InventoryFutureSupplyConversionOutcomeV1.Converted.ToString(),
            share.ToString(), physicalRow.Id, kilogramsText, null);
    }

    /// <summary>§3.5: a failed business link releases the WHOLE remainder with acceptance-link-failed; the receipt and label stay.</summary>
    private async Task<InventoryFutureSupplyConversionV1> FailAsync(
        InventoryGoodsReceiptFutureSupplyRequestV1 request,
        InventoryCommandMetadata metadata,
        FutureSupplyAllocationRow row,
        string failure,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var reasonText = InventoryFutureSupplyLinkFailuresV1.ReleaseText(failure);
        Engine.ReleaseStep step;
        try
        {
            step = await Engine.ReleaseTrackedAsync(context, row, row.Version, null,
                InventoryGoodsReceiptFutureSupplyV1.LinkFailedReleaseReason, InventoryFutureAllocationOriginsV1.Inventory,
                reasonText, now, cancellationToken);
        }
        catch (InventoryAllocationException exception) when (exception.Status != 503)
        {
            // The row was read Active under the line lock: a domain refusal here is an inconsistency, not a user error.
            throw Unavailable(exception);
        }

        Engine.Audit(auditEvents, metadata, companyId, row.Id, InventoryFutureAllocationReleasedOutboxV1.EventType,
            Engine.ReleasedOperation, step.VersionBefore, row.Version, JsonSerializer.Serialize(step.Before, Engine.Json),
            JsonSerializer.Serialize(step.After, Engine.Json), reasonText);
        Engine.AddReleasedOutbox(context, row, step, metadata, now);
        context.FutureSupplyConversions.Add(new FutureSupplyConversionRow
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            AcceptanceId = request.AcceptanceId,
            FutureAllocationId = row.Id,
            LabelId = request.LabelId,
            Outcome = (int)InventoryFutureSupplyConversionOutcomeV1.LinkFailed,
            Quantity = step.Released.Value,
            PhysicalAllocationId = null,
            PhysicalQuantityKg = null,
            FailureReason = failure,
            CreatedAtUtc = now,
        });
        return new InventoryFutureSupplyConversionV1(row.Id, InventoryFutureSupplyConversionOutcomeV1.LinkFailed.ToString(),
            step.Released.ToString(), null, null, failure);
    }

    /// <summary>The consumed part leaves the line's active sum in the same change set (the snapshot is not touched).</summary>
    private static void ReturnToLedger(FutureSupplyLineRow ledgerRow, FutureSupplyQuantity quantity, DateTimeOffset now)
    {
        FutureSupplyLedger ledger;
        try
        {
            ledger = FutureSupplyLedger.Restore(ledgerRow.CompanyId, ledgerRow.PurchaseOrderLineId,
                FutureSupplyQuantity.Of(ledgerRow.AllocatableSnapshot), FutureSupplyQuantity.Of(ledgerRow.ActiveAllocated),
                ledgerRow.Version);
            ledger.Return(quantity);
        }
        catch (Exception exception) when (exception is ArgumentException or FutureSupplyRuleException)
        {
            throw Unavailable(exception);
        }

        ledgerRow.ActiveAllocated = ledger.ActiveAllocated.Value;
        ledgerRow.Version = ledger.Version;
        ledgerRow.UpdatedAtUtc = now;
    }

    private void PhysicalAudit(InventoryCommandMetadata metadata, AllocationRow row, Guid acceptanceId, Guid futureAllocationId)
    {
        var after = JsonSerializer.Serialize(new
        {
            row.Id,
            row.CompanyId,
            row.LabelId,
            row.LocationId,
            row.OwnerKind,
            row.OwnerId,
            row.CustodianKind,
            row.CustodianId,
            row.DemandKind,
            row.DemandId,
            row.DemandLineId,
            row.DemandProductId,
            row.DemandProductVersion,
            AllocatedKg = row.AllocatedKg.ToString("0.###", CultureInfo.InvariantCulture),
            RemainingKg = row.RemainingKg.ToString("0.###", CultureInfo.InvariantCulture),
            Status = ((StockAllocationStatus)row.Status).ToString(),
            row.Version,
            AcceptanceId = acceptanceId,
            FutureAllocationId = futureAllocationId,
        }, Engine.Json);
        auditEvents.Enqueue(new AuditEventDraft(
            companyId,
            PhysicalAllocationCreatedEvent,
            PhysicalAllocationEntityType,
            row.Id.ToString("D"),
            PhysicalAllocationCreatedOperation,
            null,
            row.Version,
            AuditActorTypes.User,
            metadata.ActorId,
            metadata.PermissionCode,
            metadata.RoleCodes,
            "{}",
            after,
            InventoryGoodsReceiptFutureSupplyV1.PhysicalAllocationReason,
            ApprovalContextJson: null,
            AuditAiInvolvement.None,
            AiContextJson: null,
            metadata.SourceChannel,
            metadata.ClientIpAddress,
            metadata.UserAgent,
            metadata.CorrelationId,
            metadata.CausationId,
            metadata.IdempotencyKey));
    }

    private void AddConvertedOutbox(FutureSupplyAllocationRow row, InventoryGoodsReceiptFutureSupplyRequestV1 request,
        FutureSupplyQuantity share, InventoryFutureAllocationViewV1 after, AllocationRow physicalRow, string kilogramsText,
        InventoryCommandMetadata metadata, DateTimeOffset now)
    {
        var payload = new InventoryFutureAllocationConvertedV1(
            row.CompanyId,
            row.Id,
            row.Version.ToString(CultureInfo.InvariantCulture),
            row.DemandId,
            row.DemandLineId,
            row.PurchaseOrderId,
            row.PurchaseOrderLineId,
            row.PurchaseUnitId,
            request.AcceptanceId,
            share.ToString(),
            after.RemainingQuantity,
            after.Status,
            physicalRow.Id,
            physicalRow.LabelId,
            kilogramsText,
            metadata.CorrelationId,
            metadata.CausationId);
        context.OutboxMessages.Add(new InventoryOutboxRow
        {
            Id = Guid.NewGuid(),
            CompanyId = row.CompanyId,
            EventType = InventoryFutureAllocationConvertedOutboxV1.EventType,
            SchemaVersion = InventoryFutureAllocationConvertedOutboxV1.SchemaVersion,
            SourceOperation = InventoryFutureAllocationConvertedOutboxV1.SourceOperation,
            AggregateType = InventoryFutureAllocationConvertedOutboxV1.AggregateType,
            AggregateId = row.Id,
            AggregateVersion = row.Version,
            Sequence = row.Version,
            PayloadJson = JsonSerializer.Serialize(payload, Engine.Json),
            OccurredAtUtc = now,
            CorrelationId = metadata.CorrelationId,
            CausationId = metadata.CausationId,
            RecordedAtUtc = now,
        });
    }

    /// <summary>The session works only while the caller's transaction is live and the request's company is still the session's.</summary>
    private void EnsureActive()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection)
            || context.Database.CurrentTransaction is not { } enlisted
            || !ReferenceEquals(enlisted.GetDbTransaction(), transaction)
            || !companyContext.IsResolved || companyContext.CompanyId != companyId)
        {
            throw new InvalidOperationException("Mal kabul gelecek tedarik çevirimi oturumu artık sahibin işleminde değil.");
        }
    }
}
