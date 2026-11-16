using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Allocations;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.FutureSupply;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Engine = GoymenERP.Modules.Inventory.Infrastructure.Persistence.InventoryFutureAllocationEngine;
using Codes = GoymenERP.Modules.Inventory.Application.InventoryGoodsReceiptReversalCodesV1;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// F017-e / 7B. The host MUST prelock the full affected Sales set, then the purchase union, and reread its subset
/// before calling StageAsync once. This owner never commits borrowed handles and never writes Purchasing or Sales.
/// Owner sources passed here must preserve the host transaction, company and already-authorized command scope.
/// </summary>
public sealed class InventoryGoodsReceiptReversalParticipant : IInventoryGoodsReceiptReversalParticipantV1
{
    private const int ResizeChangeKind = 1;
    private readonly InventoryDbContext context;
    private readonly InventoryStore store;
    private readonly AuditEventCollector audit;
    private readonly DbConnection connection;
    private readonly DbTransaction transaction;
    private readonly Guid companyId;
    private readonly ISettingsPrerequisiteProvider settings;
    private readonly IInventoryFutureDemandSourceV1 demands;
    private readonly IInventoryFutureUnitConverterV1 units;
    private readonly IInventoryFutureSupplySourceV1 supply;
    private readonly TimeProvider clock;
    private bool disposed;
    private int started;

    private InventoryGoodsReceiptReversalParticipant(InventoryDbContext context, InventoryStore store, AuditEventCollector audit,
        DbConnection connection, DbTransaction transaction, Guid companyId, ISettingsPrerequisiteProvider settings,
        IInventoryFutureDemandSourceV1 demands, IInventoryFutureUnitConverterV1 units,
        IInventoryFutureSupplySourceV1 supply, TimeProvider clock)
    {
        this.context = context; this.store = store; this.audit = audit; this.connection = connection;
        this.transaction = transaction; this.companyId = companyId; this.settings = settings;
        this.demands = demands; this.units = units; this.supply = supply; this.clock = clock;
    }

    public static async Task<IInventoryGoodsReceiptReversalParticipantV1> EnlistAsync(DbConnection connection,
        DbTransaction transaction, Guid companyId, ISettingsPrerequisiteProvider settings,
        IInventoryFutureDemandSourceV1 demands, IInventoryFutureUnitConverterV1 units,
        IInventoryFutureSupplySourceV1 sameTransactionSupplySource, CancellationToken cancellationToken = default,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(connection); ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(settings); ArgumentNullException.ThrowIfNull(demands);
        ArgumentNullException.ThrowIfNull(units); ArgumentNullException.ThrowIfNull(sameTransactionSupplySource);
        if (companyId == Guid.Empty || connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection))
            throw new InvalidOperationException("Kabul ters kaydı sahibin açık işlemini ve şirketini ister.");
        var company = new CompanyContext(); company.Set(companyId);
        var collector = new AuditEventCollector();
        var options = new DbContextOptionsBuilder<InventoryDbContext>().UseSqlServer(connection, contextOwnsConnection: false)
            .AddInterceptors(new AuditSaveChangesInterceptor(collector, company)).Options;
        var context = new InventoryDbContext(options, company);
        try
        {
            await context.Database.UseTransactionAsync(transaction, cancellationToken);
            var effectiveClock = clock ?? TimeProvider.System;
            var store = new InventoryStore(context, company, collector, new InventoryKilogramScale(settings), effectiveClock);
            return new InventoryGoodsReceiptReversalParticipant(context, store, collector, connection, transaction,
                companyId, settings, demands, units, sameTransactionSupplySource, effectiveClock);
        }
        catch { await context.DisposeAsync(); throw; }
    }

    public async Task<InventoryGoodsReceiptReversalTargetV1?> ReadTargetAsync(Guid acceptanceId,
        CancellationToken cancellationToken = default)
    {
        EnsureActive();
        var original = await InventoryGoodsReceiptSourceProjection.ReadAsync(context, companyId, acceptanceId, cancellationToken);
        if (original is null) return null;
        var label = await context.Labels.AsNoTracking().SingleAsync(row => row.Id == original.OriginalResult.LabelId, cancellationToken);
        var links = await context.FutureSupplyConversions.AsNoTracking().Where(row => row.AcceptanceId == acceptanceId
                && row.Outcome == (int)InventoryFutureSupplyConversionOutcomeV1.Converted)
            .OrderBy(row => row.Id).ToListAsync(cancellationToken);
        var originalOrder = await ReadOriginalConsumptionOrderAsync(acceptanceId, links, cancellationToken);
        var ids = links.Select(row => row.Id).ToArray();
        var undone = await context.FutureSupplyConversionReversals.AsNoTracking().Where(row => ids.Contains(row.ConversionId))
            .GroupBy(row => row.ConversionId).Select(group => new { Id = group.Key, Quantity = group.Sum(row => row.UndoneQuantity) })
            .ToDictionaryAsync(row => row.Id, row => row.Quantity, cancellationToken);
        var views = new List<InventoryGoodsReceiptReversalLinkV1>();
        foreach (var link in links)
        {
            var future = await context.FutureSupplyAllocations.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == link.FutureAllocationId, cancellationToken);
            if (future is null || future.PurchaseOrderId != original.OriginalRequest.PurchaseOrderId
                || link.LabelId != label.Id || link.PhysicalAllocationId is not { } physicalId || physicalId == Guid.Empty
                || link.PhysicalQuantityKg is not > 0m || link.Quantity <= 0m || undone.GetValueOrDefault(link.Id) > link.Quantity)
                throw Error(503, Codes.Unavailable);
            Engine.RestoreStored(future);
            if (undone.GetValueOrDefault(link.Id) == link.Quantity) continue;
            views.Add(new(link.Id, future.Id, future.DemandId, future.DemandLineId, future.PurchaseOrderLineId,
                Text(future.Version), Text(link.Quantity), Text(link.PhysicalQuantityKg.Value),
                Text(undone.GetValueOrDefault(link.Id)), physicalId, originalOrder[link.FutureAllocationId]));
        }
        var returned = await ReversedKgAsync(original.OriginalResult.MovementId, cancellationToken);
        return new(original, Text(label.Version), Text(returned), views);
    }

    public async Task<InventoryGoodsReceiptReversalResultV1?> ReadReplayAsync(InventoryGoodsReceiptReversalRequestV1 request,
        InventoryCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        EnsureActive(); Validate(request, metadata);
        var key = Engine.KeyHash(metadata.IdempotencyKey);
        var row = await context.Commands.AsNoTracking().SingleOrDefaultAsync(item => item.CommandName == Codes.CommandName
            && item.IdempotencyKeyHash == key, cancellationToken);
        if (row is null) return null;
        if (row.ActorId != metadata.ActorId || row.PayloadHash != Engine.PayloadHash(request) || row.TargetId != request.ReversalId)
            throw Error(409, Codes.IdempotencyConflict);
        try
        {
            var result = JsonSerializer.Deserialize<InventoryGoodsReceiptReversalResultV1>(row.ResultJson, Engine.Json)
                ?? throw Error(503, Codes.Unavailable);
            if (result.CompanyId != companyId || result.ReversalId != request.ReversalId || result.AcceptanceId != request.AcceptanceId)
                throw Error(503, Codes.Unavailable);
            return result with { IsReplay = true, OriginalCorrelationId = row.CorrelationId };
        }
        catch (JsonException exception) { throw Error(503, Codes.Unavailable, exception); }
    }

    public async Task<InventoryGoodsReceiptReversalResultV1> StageAsync(InventoryGoodsReceiptReversalRequestV1 request,
        InventoryGoodsReceiptReversalSupplyV1 expectedSupply, InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        try { return await StageCoreAsync(request, expectedSupply, metadata, cancellationToken); }
        catch (DbUpdateConcurrencyException exception) { throw Error(409, Codes.ConcurrentChange, exception); }
        catch (DbUpdateException exception) { throw Error(503, Codes.Unavailable, exception); }
        catch (InventoryStoreException exception) { throw StoreRefusal(exception.Code, exception); }
        catch (InventoryRuleException exception) when (exception.Code == InventoryRuleCodes.NegativePhysicalStock)
        { throw Error(409, Codes.StockNotAvailable, exception); }
        catch (InventoryRuleException exception) { throw Error(503, Codes.Unavailable, exception); }
        catch (FutureSupplyRuleException exception) when (exception.Failure == FutureSupplyRuleFailure.VersionConflict)
        { throw Error(409, Codes.ConcurrentChange, exception); }
        catch (FutureSupplyRuleException exception) { throw Error(503, Codes.Unavailable, exception); }
        catch (FutureSupplyReceiptReversalPrecisionException exception) { throw Error(422, Codes.PartialQuantityNotRepresentable, exception); }
        catch (ArgumentException exception) { throw Error(503, Codes.Unavailable, exception); }
        catch (OverflowException exception) { throw Error(503, Codes.Unavailable, exception); }
    }

    private async Task<InventoryGoodsReceiptReversalResultV1> StageCoreAsync(InventoryGoodsReceiptReversalRequestV1 request,
        InventoryGoodsReceiptReversalSupplyV1 expectedSupply, InventoryCommandMetadata metadata, CancellationToken cancellationToken)
    {
        EnsureActive(); Validate(request, metadata);
        if (Interlocked.Exchange(ref started, 1) != 0) throw new InvalidOperationException("Kabul ters participant bir kez yazılır.");
        var replay = await ReadReplayAsync(request, metadata, cancellationToken);
        if (replay is not null) return replay;
        var target = await ReadTargetAsync(request.AcceptanceId, cancellationToken) ?? throw Error(404, Codes.InvalidRequest);
        var original = target.Original.OriginalRequest;
        var undoKg = Quantity(request.QuantityKg);
        if (Quantity(target.AlreadyReversedKg) + undoKg > Quantity(original.QuantityKg)) throw Error(409, Codes.ExceedsReceipt);
        if (expectedSupply.CompanyId != companyId || expectedSupply.PurchaseOrderId != original.PurchaseOrderId
            || expectedSupply.GeneralLineId == Guid.Empty || expectedSupply.OrderVersion < 1) throw Error(503, Codes.Unavailable);
        var supplyAmount = Quantity(expectedSupply.AllocatableQuantity);
        var opened = Quantity(expectedSupply.NetOpenedQuantity);
        var replacement = Quantity(expectedSupply.ImmediateReplacementQuantity);
        var supplyBudget = opened + replacement;
        if (target.Links.Count > 0)
        {
            var actual = await supply.GetAllocatableLineAsync(original.PurchaseOrderId, expectedSupply.GeneralLineId, cancellationToken);
            if (actual is null || !Enum.IsDefined(actual.Outcome)
                || actual.Outcome is InventoryFutureSupplySourceOutcomeV1.Unavailable or InventoryFutureSupplySourceOutcomeV1.NotFound
                || actual.Line is not { } line || line.OrderId != original.PurchaseOrderId || line.LineId != expectedSupply.GeneralLineId
                || line.ProductId != original.ProductId || line.ProductVersion != original.ExpectedProductVersion || line.UnitId != original.PurchaseUnitId
                || line.ObservedOrderVersion != expectedSupply.OrderVersion || Quantity(line.AllocatableText) != supplyAmount
                || opened > 0m && actual.Outcome != InventoryFutureSupplySourceOutcomeV1.Allocatable)
                throw Error(503, Codes.Unavailable);
        }

        // Read EVERY awaited owner/conversion input before a terminal fulfilled-label decision or any mutation.
        var demand = new Dictionary<Guid, InventoryFutureDemandSourceAnswerV1>();
        var openKg = new Dictionary<Guid, decimal>();
        var heldKg = new Dictionary<Guid, decimal>();
        foreach (var link in target.Links)
        {
            if (demand.ContainsKey(link.SalesOrderLineId)) continue;
            var answer = await demands.GetDemandLineAsync(link.SalesOrderId, link.SalesOrderLineId, cancellationToken);
            if (answer is null || !Enum.IsDefined(answer.Outcome)
                || answer.Outcome is InventoryFutureDemandSourceOutcomeV1.Unavailable or InventoryFutureDemandSourceOutcomeV1.NotFound)
                throw Error(503, Codes.Unavailable);
            demand.Add(link.SalesOrderLineId, answer);
            if (answer.Outcome != InventoryFutureDemandSourceOutcomeV1.Open) continue;
            if (answer.Line is not { } line || line.OrderId != link.SalesOrderId || line.LineId != link.SalesOrderLineId
                || line.ProductId != original.ProductId || string.IsNullOrWhiteSpace(line.ProductVersion) || line.UnitId == Guid.Empty)
                throw Error(503, Codes.Unavailable);
            openKg.Add(link.SalesOrderLineId, await ToKgAsync(line.ProductId, line.ProductVersion,
                Quantity(line.OpenQuantityText), line.UnitId, cancellationToken));
            var physical = await context.Allocations.AsNoTracking().Where(row => row.DemandKind == InventoryAllocationDemandKindsV1.SalesOrderLine
                && row.DemandId == link.SalesOrderId && row.DemandLineId == link.SalesOrderLineId
                && row.Status == (int)StockAllocationStatus.Active).SumAsync(row => (decimal?)row.RemainingKg, cancellationToken) ?? 0m;
            var future = await context.FutureSupplyAllocations.AsNoTracking().Where(row => row.DemandId == link.SalesOrderId
                && row.DemandLineId == link.SalesOrderLineId && row.Status == (int)FutureSupplyAllocationStatus.Active).ToListAsync(cancellationToken);
            foreach (var row in future) physical += await ToKgAsync(row.ProductId, row.ProductVersion, row.RemainingQuantity, row.PurchaseUnitId, cancellationToken);
            heldKg.Add(link.SalesOrderLineId, physical);
        }
        var futureLedger = await context.FutureSupplyLines.SingleOrDefaultAsync(row => row.PurchaseOrderLineId == expectedSupply.GeneralLineId, cancellationToken);
        var activeOnLine = await context.FutureSupplyAllocations.AsNoTracking().Where(row => row.PurchaseOrderLineId == expectedSupply.GeneralLineId
            && row.Status == (int)FutureSupplyAllocationStatus.Active).SumAsync(row => (decimal?)row.RemainingQuantity, cancellationToken) ?? 0m;
        if (target.Links.Count > 0 && (futureLedger is null || futureLedger.ActiveAllocated != activeOnLine)) throw Error(503, Codes.Unavailable);
        var originalAllocations = new Dictionary<Guid, FutureSupplyAllocationRow>();
        var originalPhysical = new Dictionary<Guid, AllocationRow>();
        var consumedPhysical = false;
        foreach (var link in target.Links)
        {
            var row = await context.FutureSupplyAllocations.SingleAsync(item => item.Id == link.FutureAllocationId, cancellationToken);
            if (row.PurchaseOrderLineId != expectedSupply.GeneralLineId || row.ProductId != original.ProductId
                || row.PurchaseUnitId != original.PurchaseUnitId) throw Error(503, Codes.Unavailable);
            Engine.RestoreStored(row);
            var physical = await context.Allocations.SingleOrDefaultAsync(item => item.Id == link.PhysicalAllocationId, cancellationToken)
                ?? throw Error(503, Codes.Unavailable);
            if (physical.LabelId != original.LabelId || physical.DemandId != link.SalesOrderId
                || physical.DemandLineId != link.SalesOrderLineId || !Enum.IsDefined((StockAllocationStatus)physical.Status))
                throw Error(503, Codes.Unavailable);
            originalAllocations.Add(link.FutureAllocationId, row);
            originalPhysical.Add(link.PhysicalAllocationId, physical);
            consumedPhysical |= physical.Status == (int)StockAllocationStatus.Consumed;
        }
        var issued = await (from movement in context.Movements.AsNoTracking()
                            join line in context.MovementLines.AsNoTracking() on movement.Id equals line.MovementId
                            where line.LabelId == original.LabelId && movement.Kind == (int)StockMovementKind.Issue
                            select line.MovementId).AnyAsync(cancellationToken);
        if (consumedPhysical || issued) throw Error(409, Codes.AlreadyFulfilled);
        var supplyRemaining = Math.Min(supplyBudget, Math.Max(0m, supplyAmount + replacement - activeOnLine));
        var remainingKg = undoKg;
        var results = new List<InventoryGoodsReceiptConversionReversalV1>();
        var reversalRows = new List<FutureSupplyConversionReversalRow>();
        // Reverse the ACTUAL recorded receipt order, including a host override or manual sequence; never today's due date.
        var ordered = target.Links.OrderByDescending(link => link.OriginalConsumptionSequence);
        foreach (var link in ordered)
        {
            if (remainingKg == 0m) break;
            var originalQuantity = Quantity(link.OriginalQuantity);
            var originalKg = Quantity(link.OriginalKg);
            var undone = Quantity(link.AlreadyUndoneQuantity);
            var outstandingKg = FutureSupplyReceiptReversal.ExactRatio(originalQuantity - undone, originalKg, originalQuantity);
            if (outstandingKg == 0m) continue;
            var takeKg = Math.Min(remainingKg, outstandingKg);
            var row = originalAllocations[link.FutureAllocationId];
            var physical = originalPhysical[link.PhysicalAllocationId];
            var activeKg = physical.Status == (int)StockAllocationStatus.Active ? physical.RemainingKg : 0m;
            var physicalUndo = Math.Min(takeKg, activeKg);
            var isOpen = demand[link.SalesOrderLineId].Outcome == InventoryFutureDemandSourceOutcomeV1.Open;
            if (isOpen) heldKg[link.SalesOrderLineId] -= physicalUndo;
            var plan = FutureSupplyReceiptReversal.Plan(originalQuantity, originalKg, undone, takeKg, activeKg, isOpen,
                isOpen ? Math.Max(0m, openKg[link.SalesOrderLineId] - heldKg[link.SalesOrderLineId]) : 0m, supplyRemaining);
            if (physicalUndo > 0m) ReleasePhysical(physical, physicalUndo, request.Reason, metadata);
            var eligible = plan.ReopenedQuantity + plan.OverflowReleasedQuantity;
            if (eligible > 0m)
            {
                var before = Engine.View(row); var previousVersion = row.Version;
                var aggregate = Engine.RestoreStored(row);
                aggregate.ReverseConsumption(row.Version, FutureSupplyQuantity.Of(eligible), FutureSupplyQuantity.Of(plan.ReopenedQuantity));
                row.RemainingQuantity = aggregate.Remaining.Value; row.ReleasedQuantity = aggregate.Released.Value;
                row.Status = (int)aggregate.Status; row.Version = aggregate.Version; row.UpdatedAtUtc = clock.GetUtcNow();
                if (plan.OverflowReleasedQuantity > 0m)
                {
                    row.ReleaseReasonCode = Codes.NoSupplyReason; row.ReleaseOrigin = InventoryFutureAllocationOriginsV1.Purchasing;
                    row.ReleaseReason = request.Reason; row.ReleasedAtUtc = clock.GetUtcNow();
                    Engine.AddReleasedOutbox(context, row, new(before, Engine.View(row), FutureSupplyQuantity.Of(plan.OverflowReleasedQuantity), previousVersion), metadata, clock.GetUtcNow());
                }
                Engine.Audit(audit, metadata, companyId, row.Id, "inventory.future-allocation.receipt-reversed.v1", "ReverseReceipt",
                    previousVersion, row.Version, JsonSerializer.Serialize(before, Engine.Json), JsonSerializer.Serialize(Engine.View(row), Engine.Json), request.Reason);
            }
            if (plan.ReopenedQuantity > 0m)
            {
                futureLedger!.ActiveAllocated += plan.ReopenedQuantity;
                futureLedger.AllocatableSnapshot = supplyAmount + replacement;
                futureLedger.Version = checked(futureLedger.Version + 1); futureLedger.UpdatedAtUtc = clock.GetUtcNow();
                supplyRemaining -= plan.ReopenedQuantity;
                heldKg[link.SalesOrderLineId] += FutureSupplyReceiptReversal.ExactRatio(plan.ReopenedQuantity, originalKg, originalQuantity);
            }
            reversalRows.Add(new() { CompanyId = companyId, ReversalId = request.ReversalId,
                ConversionId = link.ConversionId, UndoneQuantity = plan.UndoneQuantity, UndoneKg = plan.UndoneKg,
                ReopenedQuantity = plan.ReopenedQuantity, OverflowReleasedQuantity = plan.OverflowReleasedQuantity, UnheldQuantity = plan.UnheldQuantity });
            results.Add(new(link.ConversionId, link.FutureAllocationId, Text(plan.UndoneQuantity), Text(plan.UndoneKg),
                Text(plan.ReopenedQuantity), Text(plan.OverflowReleasedQuantity), Text(plan.UnheldQuantity)));
            remainingKg -= takeKg;
        }
        // Persist deallocations before the physical participant checks the remaining reservations. Everything is still uncommitted.
        await context.SaveChangesAsync(cancellationToken);
        string labelVersion;
        if (request.Kind == InventoryGoodsReceiptReversalKindV1.SupplierReturn)
        {
            await using var exit = await InventorySupplierReturnExitParticipant.EnlistAsync(connection, transaction, companyId,
                settings, cancellationToken, clock);
            var result = await exit.StageAsync(new(companyId, request.ReversalId, request.ReversalLineId, request.AcceptanceId,
                request.InventoryMovementId, request.SourceBlock, request.QuantityKg, request.OriginalUnitCostTryPerKg,
                request.OccurredAtUtc), metadata, cancellationToken);
            if (result.IsReplay) throw Error(409, Codes.IdempotencyConflict);
            labelVersion = result.LabelVersion;
        }
        else labelVersion = await StageAcceptanceExitAsync(original, request, metadata, cancellationToken);

        var answerResult = new InventoryGoodsReceiptReversalResultV1(companyId, request.ReversalId, request.AcceptanceId,
            request.InventoryMovementId, original.MovementId, original.LabelId, labelVersion, request.QuantityKg,
            results, false, metadata.CorrelationId);
        context.GoodsReceiptReversals.Add(new() { CompanyId = companyId, Id = request.ReversalId, AcceptanceId = request.AcceptanceId,
            LabelId = original.LabelId, OriginalMovementId = original.MovementId, MovementId = request.InventoryMovementId,
            Kind = (int)request.Kind, QuantityKg = undoKg, Reason = request.Reason, RecordedBy = metadata.ActorId,
            CorrelationId = metadata.CorrelationId, RecordedAtUtc = clock.GetUtcNow() });
        context.FutureSupplyConversionReversals.AddRange(reversalRows);
        context.Commands.Add(new() { CompanyId = companyId, CommandName = Codes.CommandName, IdempotencyKeyHash = Engine.KeyHash(metadata.IdempotencyKey),
            PayloadHash = Engine.PayloadHash(request), ActorId = metadata.ActorId, TargetId = request.ReversalId,
            ResultJson = JsonSerializer.Serialize(answerResult, Engine.Json), CorrelationId = metadata.CorrelationId, RecordedAtUtc = clock.GetUtcNow() });
        context.OutboxMessages.Add(new() { Id = Guid.NewGuid(), CompanyId = companyId, EventType = Codes.EventType,
            SchemaVersion = 1, SourceOperation = Codes.CommandName, AggregateType = "Inventory.GoodsReceiptReversal",
            AggregateId = request.ReversalId, AggregateVersion = 1, Sequence = 1, PayloadJson = JsonSerializer.Serialize(answerResult, Engine.Json),
            OccurredAtUtc = request.OccurredAtUtc, CorrelationId = metadata.CorrelationId, CausationId = metadata.CausationId, RecordedAtUtc = clock.GetUtcNow() });
        await context.SaveChangesAsync(cancellationToken);
        return answerResult;
    }

    private async Task<string> StageAcceptanceExitAsync(InventoryGoodsReceiptRequestV1 original,
        InventoryGoodsReceiptReversalRequestV1 request, InventoryCommandMetadata metadata, CancellationToken token)
    {
        var label = await context.Labels.SingleAsync(row => row.Id == original.LabelId, token);
        var physical = await context.Balances.AsNoTracking().Where(row => row.LabelId == label.Id)
            .SumAsync(row => (decimal?)row.QuantityKg, token) ?? 0m;
        var allocated = await context.Allocations.AsNoTracking().Where(row => row.LabelId == label.Id
            && row.Status == (int)StockAllocationStatus.Active).SumAsync(row => (decimal?)row.RemainingKg, token) ?? 0m;
        if (allocated > physical - Quantity(request.QuantityKg)) throw Error(409, Codes.StockAllocated);
        var position = new StockPosition(companyId, new StockOwner(original.OwnerKind, original.OwnerId),
            new StockCustodian(original.CustodianKind, original.CustodianId), original.LocationId,
            new MaterialLabel(label.Id, companyId, label.Code, label.ProductId, label.LotId, label.ProductVersion), request.SourceBlock);
        var movement = new StockMovement(request.InventoryMovementId, companyId, StockMovementKind.PurchaseAcceptanceReversal,
            request.OccurredAtUtc, new StockSourceReference(Codes.SourceKind, original.MovementId, request.ReversalLineId),
            [new StockMovementLine(position, StockDirection.Out, Kilograms.Positive(Quantity(request.QuantityKg)))], CostResult.NotCalculated);
        if (await store.StageMovementAsync(movement, metadata, recordAudit: true, token) != InventoryWriteOutcome.Recorded)
            throw Error(409, Codes.IdempotencyConflict);
        return Text(label.Version);
    }

    private void ReleasePhysical(AllocationRow row, decimal kg, string reason, InventoryCommandMetadata metadata)
    {
        var before = row.RemainingKg; var version = row.Version;
        if (kg == before) { row.RemainingKg = 0m; row.Status = (int)StockAllocationStatus.Released; row.ReleasedAtUtc = clock.GetUtcNow(); row.ReleaseReason = reason; }
        else { row.RemainingKg -= kg; row.AllocatedKg -= kg; }
        row.Version = checked(row.Version + 1); row.UpdatedAtUtc = clock.GetUtcNow();
        if (row.RemainingKg > 0m)
            context.AllocationChanges.Add(new() { Id = Guid.NewGuid(), CompanyId = companyId, Kind = ResizeChangeKind,
                SourceAllocationId = row.Id, SourceVersion = version, SourceLabelId = row.LabelId, SourceRemainingKg = before,
                TargetAllocationId = row.Id, TargetLabelId = row.LabelId, TargetRemainingKg = row.RemainingKg, Reason = reason,
                ChangedBy = metadata.ActorId, CorrelationId = metadata.CorrelationId, ChangedAtUtc = clock.GetUtcNow() });
        audit.Enqueue(new AuditEventDraft(companyId, "inventory.physical-allocation.receipt-reversed.v1", "Inventory.StockAllocation",
            row.Id.ToString("D"), "ReverseReceiptHold", version, row.Version, AuditActorTypes.User, metadata.ActorId,
            metadata.PermissionCode, metadata.RoleCodes, JsonSerializer.Serialize(new { remainingKg = before }, Engine.Json),
            JsonSerializer.Serialize(new { remainingKg = row.RemainingKg, row.Status }, Engine.Json), reason,
            ApprovalContextJson: null, AuditAiInvolvement.None, AiContextJson: null, metadata.SourceChannel,
            metadata.ClientIpAddress, metadata.UserAgent, metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));
    }

    private async Task<decimal> ReversedKgAsync(Guid movementId, CancellationToken token) =>
        await (from movement in context.Movements.AsNoTracking()
               join line in context.MovementLines.AsNoTracking() on movement.Id equals line.MovementId
               where movement.SourceId == movementId && (movement.Kind == (int)StockMovementKind.SupplierReturnExit
                   && movement.SourceKind == InventorySupplierReturnExitContractV1.SourceKind
                   || movement.Kind == (int)StockMovementKind.PurchaseAcceptanceReversal && movement.SourceKind == Codes.SourceKind)
               select (decimal?)line.QuantityKg).SumAsync(token) ?? 0m;

    private async Task<Dictionary<Guid, int>> ReadOriginalConsumptionOrderAsync(Guid acceptanceId,
        IReadOnlyList<FutureSupplyConversionRow> links, CancellationToken token)
    {
        if (links.Count == 0) return [];
        var command = await context.Commands.AsNoTracking().SingleOrDefaultAsync(row =>
            row.CommandName == InventoryGoodsReceiptFutureSupplyV1.CommandName && row.TargetId == acceptanceId, token);
        if (command is null) throw Error(503, Codes.Unavailable);
        InventoryGoodsReceiptFutureSupplyResultV1? result;
        try { result = JsonSerializer.Deserialize<InventoryGoodsReceiptFutureSupplyResultV1>(command.ResultJson, Engine.Json); }
        catch (JsonException exception) { throw Error(503, Codes.Unavailable, exception); }
        if (result is null || result.AcceptanceId != acceptanceId || result.Conversions is null
            || result.Conversions.Any(item => item is null || item.FutureAllocationId == Guid.Empty)
            || result.Conversions.Select(item => item.FutureAllocationId).Distinct().Count() != result.Conversions.Count)
            throw Error(503, Codes.Unavailable);
        var order = new Dictionary<Guid, int>();
        for (var index = 0; index < result.Conversions.Count; index++)
        {
            var item = result.Conversions[index];
            if (item.Outcome != nameof(InventoryFutureSupplyConversionOutcomeV1.Converted)) continue;
            var link = links.SingleOrDefault(row => row.FutureAllocationId == item.FutureAllocationId);
            if (link is null || item.PhysicalAllocationId != link.PhysicalAllocationId
                || Quantity(item.Quantity) != link.Quantity || item.PhysicalQuantityKg is null
                || Quantity(item.PhysicalQuantityKg) != link.PhysicalQuantityKg) throw Error(503, Codes.Unavailable);
            order.Add(item.FutureAllocationId, index);
        }
        if (order.Count != links.Count) throw Error(503, Codes.Unavailable);
        return order;
    }

    private async Task<decimal> ToKgAsync(Guid product, string version, decimal quantity, Guid unit, CancellationToken token)
    {
        var answer = await units.ConvertToKilogramsAsync(product, version, quantity, unit, token);
        return answer is { Outcome: InventoryFutureUnitConversionOutcomeV1.Converted, Quantity: >= 0m }
            ? answer.Quantity.Value : throw Error(503, Codes.Unavailable);
    }

    private void Validate(InventoryGoodsReceiptReversalRequestV1 request, InventoryCommandMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(request); Engine.RequireMetadata(metadata); Engine.RequireCanonicalKeys(metadata);
        if (request.CompanyId != companyId || request.ReversalId == Guid.Empty || request.ReversalLineId == Guid.Empty
            || request.AcceptanceId == Guid.Empty || request.InventoryMovementId == Guid.Empty || !Enum.IsDefined(request.Kind)
            || request.SourceBlock is not (BlockState.Free or BlockState.Blocked) || request.OccurredAtUtc == default
            || request.OccurredAtUtc.Offset != TimeSpan.Zero || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 1000
            || !InventoryFutureAllocationTextV1.TryPositiveQuantity(request.QuantityKg, out _)) throw Error(400, Codes.InvalidRequest);
    }

    private static decimal Quantity(string value) => InventoryFutureAllocationTextV1.TryNonNegativeQuantity(value, out var quantity)
        ? quantity.Value : throw Error(503, Codes.Unavailable);
    private static string Text(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static InventoryGoodsReceiptReversalException Error(int status, string code, Exception? inner = null) => new(status, code, inner);
    internal static InventoryGoodsReceiptReversalException StoreRefusal(string code, Exception? inner = null) => code switch
    {
        InventoryStoreCodes.MovementIdConflict or InventoryStoreCodes.DuplicateSource or InventoryStoreCodes.SourceGrainMismatch
            => Error(409, Codes.IdempotencyConflict, inner),
        InventoryStoreCodes.ConcurrentChange => Error(409, Codes.ConcurrentChange, inner),
        InventoryScaleCodes.ProfileUnusable => Error(503, Codes.QuantityProfileUnavailable, inner),
        _ when code.StartsWith("settings_", StringComparison.Ordinal) => Error(503, Codes.QuantityProfileUnavailable, inner),
        _ => Error(503, Codes.Unavailable, inner),
    };
    private void EnsureActive()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection)
            || context.Database.CurrentTransaction is not { } current || !ReferenceEquals(current.GetDbTransaction(), transaction))
            throw new InvalidOperationException("Kabul ters participant sahibinin işleminden ayrıldı.");
    }
    public async ValueTask DisposeAsync() { if (disposed) return; disposed = true; await context.DisposeAsync(); }
}
