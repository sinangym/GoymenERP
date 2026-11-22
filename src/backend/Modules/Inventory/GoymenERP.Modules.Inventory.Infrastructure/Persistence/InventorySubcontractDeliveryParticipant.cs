using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.BuildingBlocks.Concurrency;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Allocations;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using static GoymenERP.Modules.Inventory.Application.InventorySubcontractDeliveryContractV1;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// LOJ-D1..D5: a customer's own processed goods, on one borrowed host transaction. Reservation and
/// whole-instruction shipment are separate attempts; neither uses a normal sales allocation or SMM event.
/// The host verifies current Sales/Manufacturing proofs and final authority, and alone commits or rolls back.
/// </summary>
public sealed partial class InventorySubcontractDeliveryParticipant : IInventorySubcontractDeliveryParticipantV1
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true };
    private readonly InventoryDbContext context;
    private readonly InventoryStore store;
    private readonly DbConnection connection;
    private readonly DbTransaction transaction;
    private readonly Guid companyId;
    private readonly IAuditEventCollector audit;
    private readonly IAuditTransactionWriterV1 auditWriter;
    private readonly TimeProvider clock;
    private readonly ISalesOrderLineSerializationLockBinderV1 salesLineLocks;
    private bool disposed;
    private int started;

    private InventorySubcontractDeliveryParticipant(InventoryDbContext context, InventoryStore store, DbConnection connection,
        DbTransaction transaction, Guid companyId, IAuditEventCollector audit, IAuditTransactionWriterV1 auditWriter, TimeProvider clock,
        ISalesOrderLineSerializationLockBinderV1 salesLineLocks)
    {
        this.context = context; this.store = store; this.connection = connection; this.transaction = transaction;
        this.companyId = companyId; this.audit = audit; this.auditWriter = auditWriter; this.clock = clock;
        this.salesLineLocks = salesLineLocks;
    }

    public static async Task<InventorySubcontractDeliveryParticipant> EnlistAsync(DbConnection connection,
        DbTransaction transaction, Guid companyId, ISettingsPrerequisiteProvider settings,
        IAuditEventCollector audit, IAuditTransactionWriterV1 auditWriter, ISalesOrderLineSerializationLockBinderV1 salesLineLocks,
        CancellationToken token = default, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(connection); ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(settings); ArgumentNullException.ThrowIfNull(audit); ArgumentNullException.ThrowIfNull(auditWriter);
        ArgumentNullException.ThrowIfNull(salesLineLocks);
        if (companyId == Guid.Empty || connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection)
            || transaction.IsolationLevel != IsolationLevel.Serializable
            || auditWriter.HasPendingDrafts) throw Refused(503, Unavailable);
        var company = new CompanyContext(); company.Set(companyId);
        var context = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer(connection, contextOwnsConnection: false).Options, company);
        try
        {
            await context.Database.UseTransactionAsync(transaction, token);
            var effectiveClock = clock ?? TimeProvider.System;
            return new InventorySubcontractDeliveryParticipant(context, new InventoryStore(context, company, audit, new InventoryKilogramScale(settings), effectiveClock),
                connection, transaction, companyId, audit, auditWriter, effectiveClock, salesLineLocks);
        }
        catch { await context.DisposeAsync(); throw; }
    }

    public async Task<InventorySubcontractReservationResultV1> ReserveAsync(InventorySubcontractReservationRequestV1 request,
        InventoryCommandMetadata metadata, Func<CancellationToken, Task> beforeWrite, CancellationToken cancellationToken = default)
    {
        Start(metadata); ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(beforeWrite);
        if (request.CompanyId != companyId || request.ReservationId == Guid.Empty || request.Lines is null
            || request.Lines.Count is < 1 or > 500 || request.Lines.Any(x => x is null)
            || request.Lines.Select(x => x.AllocationId).Distinct().Count() != request.Lines.Count) throw Refused(400, Invalid);
        foreach (var line in request.Lines)
        {
            Source(line.Source, companyId, line.LabelId); _ = Version(line.ExpectedLabelVersion); _ = Quantity(line.QuantityKg);
            if (line.AllocationId == Guid.Empty || !Text(line.Reason, 1000)) throw Refused(400, Invalid);
            if (line.PlanExcess is { } proof && (proof.CompanyId != companyId || proof.ExceptionId == Guid.Empty
                || proof.InstructionId == Guid.Empty || proof.InstructionLineId == Guid.Empty || proof.ApprovedVersion != 2
                || proof.SourceVersion != "logistics.plan-excess.v1" || Quantity(proof.ReservedQuantityKg).Value != Quantity(line.QuantityKg).Value
                || Quantity(proof.ReservedQuantityKg).Value > Quantity(proof.ApprovedQuantityKg).Value)) throw Refused(503, SourceMismatch);
        }
        var hash = Hash(request);
        if (await ReplayAsync<InventorySubcontractReservationResultV1>(ReservationCommand, request.ReservationId, hash, metadata, cancellationToken) is { } replay)
            return replay.Result with { IsReplay = true, OriginalCorrelationId = replay.Correlation };
        await LockDemandAsync(request.Lines.Select(x => x.Source.SalesOrderLineId), cancellationToken);
        await beforeWrite(cancellationToken);
        await AcquirePhysicalValidationAsync(store.LockPhysicalValidationAsync, cancellationToken);
        var labels = new Dictionary<Guid, LabelRow>(); var reserved = new Dictionary<Guid, decimal>();
        var results = new List<InventoryPhysicalAllocationViewV1>();
        foreach (var line in request.Lines)
        {
            if (!labels.TryGetValue(line.LabelId, out var label))
            {
                label = await context.Labels.SingleOrDefaultAsync(x => x.Id == line.LabelId, cancellationToken)
                    ?? throw Refused(404, SourceMismatch);
                labels.Add(line.LabelId, label);
            }
            // All input versions describe the same initial frame, even when two lines reserve one label.
            if (label.Version != Version(line.ExpectedLabelVersion)) throw Refused(409, InventoryDefiniteShipmentCodesV1.LabelVersionConflict);
            RequireLabel(label, line.Source);
            var rows = await context.Balances.AsNoTracking().Where(x => x.LabelId == line.LabelId && x.Block == (int)BlockState.Free
                && x.QuantityKg > 0m).ToListAsync(cancellationToken);
            if (rows.Count != 1) throw Refused(rows.Count == 0 ? 409 : 503, InventoryDefiniteShipmentCodesV1.PhysicalStockInsufficient);
            var balance = rows[0]; RequireOwner(balance.OwnerKind, balance.OwnerId, balance.CustodianKind, balance.CustodianId, line.Source.CustomerPartyId);
            var held = await context.Allocations.AsNoTracking().Where(x => x.LabelId == line.LabelId && x.LocationId == balance.LocationId
                && x.OwnerKind == balance.OwnerKind && x.OwnerId == balance.OwnerId && x.CustodianKind == balance.CustodianKind
                && x.CustodianId == balance.CustodianId && x.Block == balance.Block && x.Status == (int)StockAllocationStatus.Active)
                .SumAsync(x => (decimal?)x.RemainingKg, cancellationToken) ?? 0m;
            var kg = Quantity(line.QuantityKg).Value;
            if (kg > Quantity(line.Source.OutputNetKg).Value || kg > balance.QuantityKg - held - reserved.GetValueOrDefault(line.LabelId))
                throw Refused(409, InventoryAllocationCodesV1.InsufficientFreeStock);
            if (await context.Allocations.AnyAsync(x => x.Id == line.AllocationId, cancellationToken))
                throw Refused(409, InventoryDefiniteShipmentCodesV1.IdempotencyConflict);
            reserved[line.LabelId] = reserved.GetValueOrDefault(line.LabelId) + kg;
            var now = clock.GetUtcNow();
            var row = new AllocationRow { Id = line.AllocationId, CompanyId = companyId, LabelId = label.Id,
                OwnerKind = balance.OwnerKind, OwnerId = balance.OwnerId, CustodianKind = balance.CustodianKind, CustodianId = balance.CustodianId,
                LocationId = balance.LocationId, Block = balance.Block, DemandKind = InventoryAllocationDemandKindsV1.SubcontractServiceOrderLine,
                DemandId = line.Source.SalesOrderId, DemandLineId = line.Source.SalesOrderLineId, DemandProductId = label.ProductId,
                DemandProductVersion = label.ProductVersion, Reason = line.Reason, AllocatedKg = kg, RemainingKg = kg,
                Status = (int)StockAllocationStatus.Active, Version = 1, CreatedAtUtc = now, UpdatedAtUtc = now };
            context.Allocations.Add(row);
            context.SubcontractAllocationSources.Add(SourceRow(row.Id, line.Source));
            var view = View(row); results.Add(view);
            Audit(metadata, row.Id, "Create", null, 1, "{}", line.PlanExcess is null ? JsonSerializer.Serialize(new { allocation = view, source = line.Source }, Json)
                    : JsonSerializer.Serialize(new { allocation = view, source = line.Source, line.PlanExcess }, Json), line.Reason);
        }
        foreach (var label in labels.Values) { label.Version = checked(label.Version + 1); label.UpdatedAtUtc = clock.GetUtcNow(); }
        var result = new InventorySubcontractReservationResultV1(companyId, request.ReservationId, results, false, metadata.CorrelationId);
        RecordCommand(ReservationCommand, request.ReservationId, hash, metadata, result);
        await FlushAsync(cancellationToken); return result;
    }

    public async Task<InventorySubcontractShipmentResultV1> ShipAsync(InventorySubcontractShipmentRequestV1 request,
        InventoryCommandMetadata metadata, Func<CancellationToken, Task> beforeWrite, CancellationToken cancellationToken = default)
    {
        Start(metadata); ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(beforeWrite);
        if (request.CompanyId != companyId || request.LoadingInstructionId == Guid.Empty || request.CustomerPartyId == Guid.Empty
            || request.OccurredAtUtc.Offset != TimeSpan.Zero || request.Lines is null || request.Lines.Count is < 1 or > 500
            || request.Lines.Any(x => x is null) || request.Lines.Select(x => x.AllocationId).Distinct().Count() != request.Lines.Count
            || request.Lines.Select(x => x.ShipmentId).Distinct().Count() != request.Lines.Count
            || request.Lines.Select(x => x.MovementId).Distinct().Count() != request.Lines.Count
            || request.Lines.Select(x => x.LoadingInstructionLineId).Distinct().Count() != request.Lines.Count) throw Refused(400, Invalid);
        foreach (var line in request.Lines)
        {
            Source(line.Source, companyId, line.LabelId); _ = Version(line.ExpectedLabelVersion); _ = Version(line.ExpectedAllocationVersion);
            _ = Quantity(line.QuantityKg);
            if (line.PieceCount is < 0) throw Refused(400, Invalid);
            if (line.Source.CustomerPartyId != request.CustomerPartyId || line.AllocationId == Guid.Empty || line.ShipmentId == Guid.Empty
                || line.MovementId == Guid.Empty || line.LoadingInstructionLineId == Guid.Empty) throw Refused(400, Invalid);
        }
        if (request.Lines.Any(line => line.AdditionalAllocations is { } extras && (extras.Count is < 1 or > 500 || extras.Any(extra => extra is null))))
            throw Refused(400, Invalid);
        var allAllocationIds = request.Lines.SelectMany(line => new[] { line.AllocationId }
            .Concat(line.AdditionalAllocations?.Select(slice => slice.AllocationId) ?? [])).ToArray();
        if (allAllocationIds.Distinct().Count() != allAllocationIds.Length) throw Refused(400, Invalid);
        var hash = Hash(request);
        if (await ReplayAsync<InventorySubcontractShipmentResultV1>(ShipmentCommand, request.LoadingInstructionId, hash, metadata, cancellationToken) is { } replay)
            return replay.Result with { IsReplay = true, OriginalCorrelationId = replay.Correlation };
        await LockDemandAsync(request.Lines.Select(x => x.Source.SalesOrderLineId), cancellationToken);
        await beforeWrite(cancellationToken);
        await AcquirePhysicalValidationAsync(store.LockPhysicalValidationAsync, cancellationToken);
        // Validate every source and initial version before the first stock write; one label can legitimately serve two orders.
        var prepared = new List<(InventorySubcontractShipmentLineV1 Input, AllocationRow Row, LabelRow Label, StockAllocation Allocation,
            IReadOnlyList<(AllocationRow Row, StockAllocation Allocation)> Extras)>();
        foreach (var line in request.Lines)
        {
            var row = await context.Allocations.SingleOrDefaultAsync(x => x.Id == line.AllocationId, cancellationToken)
                ?? throw Refused(404, SourceMismatch);
            var label = await context.Labels.SingleOrDefaultAsync(x => x.Id == line.LabelId, cancellationToken)
                ?? throw Refused(404, SourceMismatch);
            var original = await context.SubcontractAllocationSources.AsNoTracking().SingleOrDefaultAsync(x => x.AllocationId == row.Id, cancellationToken)
                ?? throw Refused(503, SourceMismatch);
            RequireOriginal(original, line.Source);
            if (row.DemandKind != InventoryAllocationDemandKindsV1.SubcontractServiceOrderLine || row.DemandId != line.Source.SalesOrderId
                || row.DemandLineId != line.Source.SalesOrderLineId || row.LabelId != label.Id || row.DemandProductId != label.ProductId
                || row.DemandProductVersion != label.ProductVersion) throw Refused(409, SourceMismatch);
            if (label.Version != Version(line.ExpectedLabelVersion)) throw Refused(409, InventoryDefiniteShipmentCodesV1.LabelVersionConflict);
            RequireLabel(label, line.Source); RequireOwner(row.OwnerKind, row.OwnerId, row.CustodianKind, row.CustodianId, request.CustomerPartyId);
            var material = await MaterialAsync(label, cancellationToken);
            if (!material.IsReleasableForProductionOrShipment) throw Refused(409, InventoryDefiniteShipmentCodesV1.LabelNotReleasable);
            var position = new StockPosition(companyId, new StockOwner((StockPartyKind)row.OwnerKind, row.OwnerId),
                new StockCustodian((StockPartyKind)row.CustodianKind, row.CustodianId), row.LocationId, material, (BlockState)row.Block);
            var allocation = StockAllocation.Restore(row.Id, position, Kilograms.Positive(row.AllocatedKg),
                Kilograms.Of(row.RemainingKg), row.Version, (StockAllocationStatus)row.Status);
            var physical = Quantity(line.QuantityKg).Value;
            var primaryQuantity = Math.Min(physical, row.RemainingKg);
            if (primaryQuantity <= 0m) throw Refused(409, InventoryDefiniteShipmentCodesV1.AllocationInsufficient);
            try { allocation.Consume(Version(line.ExpectedAllocationVersion), line.LabelId, Kilograms.Positive(primaryQuantity)); }
            catch (StockAllocationException error)
            {
                throw Refused(409, error.Failure == StockAllocationFailure.VersionConflict ? InventoryDefiniteShipmentCodesV1.AllocationVersionConflict
                    : error.Failure == StockAllocationFailure.Insufficient ? InventoryDefiniteShipmentCodesV1.AllocationInsufficient
                    : InventoryDefiniteShipmentCodesV1.AllocationState, error);
            }
            var extras = await PrepareAdditionalAllocationsAsync(line, row, label, allocation.Position, physical - primaryQuantity, cancellationToken);
            prepared.Add((line, row, label, allocation, extras));
        }
        var lines = new List<InventorySubcontractShipmentLineResultV1>();
        foreach (var item in prepared)
        {
            var line = item.Input; var row = item.Row; var before = JsonSerializer.Serialize(View(row), Json); var beforeVersion = row.Version;
            var movement = new StockMovement(line.MovementId, companyId, StockMovementKind.Issue, request.OccurredAtUtc,
                new StockSourceReference(MovementSourceKind, line.ShipmentId, line.LoadingInstructionLineId),
                [new StockMovementLine(item.Allocation.Position, StockDirection.Out, Quantity(line.QuantityKg),
                    line.PieceCount is { } pieces ? PieceCount.Of(pieces) : null)], CostResult.NotCalculated);
            try
            {
                if (await store.StageMovementAsync(movement, metadata, false, cancellationToken) != InventoryWriteOutcome.Recorded)
                    throw Refused(409, InventoryDefiniteShipmentCodesV1.IdempotencyConflict);
            }
            catch (InventoryRuleException error) when (error.Code == InventoryRuleCodes.NegativePhysicalStock)
            { throw Refused(409, InventoryDefiniteShipmentCodesV1.PhysicalStockInsufficient, error); }
            catch (InventoryRuleException error) when (error.Code is InventoryRuleCodes.PieceCountRequired
                or InventoryRuleCodes.PieceCountNotTracked or InventoryRuleCodes.NegativePieceCount
                or InventoryRuleCodes.InvalidPieceCount)
            { throw Refused(400, Invalid, error); } // KLN-D119: an unknown, extra or impossible piece count for this label.
            row.RemainingKg = item.Allocation.Remaining.Value; row.Status = (int)item.Allocation.Status; row.Version = item.Allocation.Version;
            row.UpdatedAtUtc = clock.GetUtcNow(); var eventId = Guid.NewGuid();
            foreach (var extra in item.Extras)
            {
                var previous = JsonSerializer.Serialize(View(extra.Row), Json); var previousVersion = extra.Row.Version;
                extra.Row.RemainingKg = extra.Allocation.Remaining.Value; extra.Row.Status = (int)extra.Allocation.Status;
                extra.Row.Version = extra.Allocation.Version; extra.Row.UpdatedAtUtc = clock.GetUtcNow();
                Audit(metadata, extra.Row.Id, "ConsumeForSubcontractDelivery", previousVersion, extra.Row.Version, previous,
                    JsonSerializer.Serialize(new { allocation = View(extra.Row), line.ShipmentId, line.MovementId, line.LoadingInstructionLineId }, Json),
                    "Onaylı plan üstü payın gerçek ek tahsis tüketimi");
            }
            var projected = new InventorySubcontractShipmentLineResultV1(line.ShipmentId, line.LoadingInstructionLineId, row.Id,
                row.Version.ToString(CultureInfo.InvariantCulture), line.LabelId, item.Label.Version.ToString(CultureInfo.InvariantCulture),
                line.MovementId, eventId, line.QuantityKg, line.PieceCount);
            lines.Add(projected);
            context.OutboxMessages.Add(new InventoryOutboxRow { Id = eventId, CompanyId = companyId, EventType = EventType, SchemaVersion = 1,
                SourceOperation = ShipmentCommand, AggregateType = "Inventory.StockAllocation", AggregateId = row.Id,
                AggregateVersion = row.Version, Sequence = row.Version, OccurredAtUtc = request.OccurredAtUtc, RecordedAtUtc = clock.GetUtcNow(),
                CorrelationId = metadata.CorrelationId, CausationId = metadata.CausationId,
                PayloadJson = item.Extras.Count == 0
                    ? JsonSerializer.Serialize(new { companyId, request.LoadingInstructionId, request.CustomerPartyId,
                        line = projected, source = line.Source, occurredAtUtc = request.OccurredAtUtc }, Json)
                    : JsonSerializer.Serialize(new { companyId, request.LoadingInstructionId, request.CustomerPartyId,
                        line = projected, source = line.Source, occurredAtUtc = request.OccurredAtUtc,
                        additionalAllocations = item.Extras.Select(extra => View(extra.Row)).ToArray() }, Json) });
            Audit(metadata, row.Id, "ConsumeForSubcontractDelivery", beforeVersion, row.Version, before,
                JsonSerializer.Serialize(new { allocation = View(row), delivery = projected }, Json), "Müşteri malının sahibine fason teslimi");
        }
        var result = new InventorySubcontractShipmentResultV1(companyId, request.LoadingInstructionId, lines, false, metadata.CorrelationId);
        RecordCommand(ShipmentCommand, request.LoadingInstructionId, hash, metadata, result);
        await FlushAsync(cancellationToken); return result;
    }

    private void Start(InventoryCommandMetadata metadata)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Interlocked.Exchange(ref started, 1) != 0 || connection.State != ConnectionState.Open
            || !ReferenceEquals(transaction.Connection, connection) || transaction.IsolationLevel != IsolationLevel.Serializable
            || context.Database.CurrentTransaction is not { } enlisted
            || !ReferenceEquals(enlisted.GetDbTransaction(), transaction) || auditWriter.HasPendingDrafts) throw Refused(503, Unavailable);
        ArgumentNullException.ThrowIfNull(metadata);
        if (!Text(metadata.ActorId, 128) || !Text(metadata.PermissionCode, 128) || !Text(metadata.CorrelationId, 128)
            || !Text(metadata.IdempotencyKey, 128) || !Text(metadata.SourceChannel, 128)
            || metadata.CausationId is not null && !Text(metadata.CausationId, 128)
            || metadata.RoleCodes is null || metadata.RoleCodes.Any(x => !Text(x, 128))) throw Refused(400, Invalid);
    }
    private async Task LockDemandAsync(IEnumerable<Guid> lineIds, CancellationToken token)
    {
        var bound = salesLineLocks.Bind(connection, transaction);
        foreach (var lineId in lineIds.Distinct().Order())
        {
            try { await bound.AcquireAsync(lineId, token); }
            catch (SalesOrderLineBusyException error) { throw Refused(409, error.Code, error); }
        }
    }
    private void RequireOwner(int ownerKind, Guid ownerId, int custodianKind, Guid custodianId, Guid customer)
    {
        if (!InventorySubcontractOwnershipGuardV1.IsCustomerOwnedAndHeld((StockPartyKind)ownerKind, ownerId,
            (StockPartyKind)custodianKind, custodianId, companyId, customer)) throw Refused(409, OwnerMismatch);
    }
    private static void RequireLabel(LabelRow row, InventorySubcontractDeliverySourceV1 source)
    { if (row.CompanyId != source.CompanyId || row.Id != source.LabelId || row.ProductId != source.ProductId || row.ProductVersion != source.ProductVersion) throw Refused(409, SourceMismatch); }
    private static InventoryPhysicalAllocationViewV1 View(AllocationRow row) => new InventoryPhysicalAllocationViewV1(row.Id, row.CompanyId, row.Version.ToString(CultureInfo.InvariantCulture),
        ((StockAllocationStatus)row.Status).ToString(), row.LabelId, row.DemandProductId!.Value, row.DemandKind!, row.DemandId!.Value,
        row.DemandLineId!.Value, row.DemandProductVersion!, row.AllocatedKg.ToString("0.###", CultureInfo.InvariantCulture),
        row.RemainingKg.ToString("0.###", CultureInfo.InvariantCulture), row.Reason!, null, row.CreatedAtUtc, row.UpdatedAtUtc);
    private static InventorySubcontractAllocationSourceRow SourceRow(Guid allocationId, InventorySubcontractDeliverySourceV1 source) => new InventorySubcontractAllocationSourceRow()
    {
        CompanyId = source.CompanyId, AllocationId = allocationId, LabelId = source.LabelId, CustomerPartyId = source.CustomerPartyId,
        CustomerAccountId = source.CustomerAccountId, SalesOrderId = source.SalesOrderId, SalesOrderLineId = source.SalesOrderLineId,
        SalesOrderVersion = source.SalesOrderVersion, SalesLineRevision = source.SalesLineRevision, ManufacturingOrderId = source.ManufacturingOrderId,
        ManufacturingOrderVersion = source.ManufacturingOrderVersion, ManufacturingPlanId = source.ManufacturingPlanId,
        ManufacturingPlanVersion = source.ManufacturingPlanVersion, OutputFactId = source.OutputFactId, OutputNetKg = Quantity(source.OutputNetKg).Value,
        SourceJson = JsonSerializer.Serialize(source, Json), SourceHash = Hash(source),
    };
    private static void RequireOriginal(InventorySubcontractAllocationSourceRow row, InventorySubcontractDeliverySourceV1 fresh)
    {
        InventorySubcontractDeliverySourceV1 original;
        try { original = JsonSerializer.Deserialize<InventorySubcontractDeliverySourceV1>(row.SourceJson, Json) ?? throw Refused(503, SourceMismatch); }
        catch (JsonException error) { throw Refused(503, SourceMismatch, error); }
        Source(original, row.CompanyId, row.LabelId);
        var projected = SourceRow(row.AllocationId, original);
        if (Hash(original) != row.SourceHash || projected.CompanyId != row.CompanyId || projected.LabelId != row.LabelId || projected.CustomerPartyId != row.CustomerPartyId
            || projected.CustomerAccountId != row.CustomerAccountId || projected.SalesOrderId != row.SalesOrderId
            || projected.SalesOrderLineId != row.SalesOrderLineId || projected.SalesOrderVersion != row.SalesOrderVersion
            || projected.SalesLineRevision != row.SalesLineRevision || projected.ManufacturingOrderId != row.ManufacturingOrderId
            || projected.ManufacturingOrderVersion != row.ManufacturingOrderVersion || projected.ManufacturingPlanId != row.ManufacturingPlanId
            || projected.ManufacturingPlanVersion != row.ManufacturingPlanVersion || projected.OutputFactId != row.OutputFactId
            || projected.OutputNetKg != row.OutputNetKg) throw Refused(503, SourceMismatch);
        if (fresh.CompanyId != original.CompanyId || fresh.CustomerPartyId != original.CustomerPartyId || fresh.CustomerAccountId != original.CustomerAccountId
            || fresh.SalesOrderId != original.SalesOrderId || fresh.SalesOrderLineId != original.SalesOrderLineId
            || fresh.SalesLineRevision < original.SalesLineRevision || fresh.SalesOrderVersion < original.SalesOrderVersion
            || fresh.ProductId != original.ProductId || fresh.ProductVersion != original.ProductVersion || fresh.LabelId != original.LabelId
            || fresh.SalesServiceProductId != original.SalesServiceProductId
            || fresh.ManufacturingOrderId != original.ManufacturingOrderId || fresh.ManufacturingOrderVersion < original.ManufacturingOrderVersion
            || fresh.ManufacturingPlanId != original.ManufacturingPlanId || fresh.ManufacturingPlanVersion < original.ManufacturingPlanVersion
            || fresh.ManufacturingSalesLinkRevision < original.ManufacturingSalesLinkRevision
            || fresh.OutputFactId != original.OutputFactId) throw Refused(409, SourceMismatch);
    }
    private async Task<MaterialLabel> MaterialAsync(LabelRow row, CancellationToken token)
    {
        var required = JsonSerializer.Deserialize<string[]>(row.RequiredDocumentsJson, Json) ?? throw Refused(503, Unavailable);
        var documents = await context.LabelDocuments.AsNoTracking().Where(x => x.LabelId == row.Id).OrderBy(x => x.Sequence)
            .Select(x => new LabelDocumentAttachment(x.Kind, x.AttachedAtUtc, x.AttachedBy)).ToListAsync(token);
        return MaterialLabel.Restore(row.Id, row.CompanyId, row.Code, row.ProductId, row.LotId, row.ProductVersion, required,
            row.HeatNumber, row.CoilNumber, [], documents);
    }
    private async Task<(T Result, string Correlation)?> ReplayAsync<T>(string command, Guid targetId, string hash, InventoryCommandMetadata metadata, CancellationToken token) where T : class
    {
        var key = Hash(metadata.IdempotencyKey);
        var row = await context.Commands.AsNoTracking().SingleOrDefaultAsync(x => x.CommandName == command && x.IdempotencyKeyHash == key, token);
        if (row is null) return null;
        if (row.ActorId != metadata.ActorId || row.PayloadHash != hash || row.TargetId != targetId)
            throw Refused(409, InventoryDefiniteShipmentCodesV1.IdempotencyConflict);
        try
        {
            var result = JsonSerializer.Deserialize<T>(row.ResultJson, Json) ?? throw Refused(503, Unavailable);
            var valid = result switch
            {
                InventorySubcontractReservationResultV1 value => value.CompanyId == companyId && value.ReservationId == targetId
                    && value.Allocations is { Count: > 0 and <= 500 } && value.Allocations.All(x => x is not null && x.CompanyId == companyId
                        && x.DemandKind == InventoryAllocationDemandKindsV1.SubcontractServiceOrderLine && x.AllocationId != Guid.Empty && x.LabelId != Guid.Empty)
                    && value.Allocations.Select(x => x.AllocationId).Distinct().Count() == value.Allocations.Count,
                InventorySubcontractShipmentResultV1 value => value.CompanyId == companyId && value.LoadingInstructionId == targetId
                    && value.Lines is { Count: > 0 and <= 500 } && value.Lines.All(x => x is not null && x.AllocationId != Guid.Empty
                        && x.ShipmentId != Guid.Empty && x.LabelId != Guid.Empty && x.MovementId != Guid.Empty && x.OutboxMessageId != Guid.Empty)
                    && value.Lines.Select(x => x.ShipmentId).Distinct().Count() == value.Lines.Count,
                _ => false,
            };
            if (!valid || !Text(row.CorrelationId, 128)) throw Refused(503, Unavailable);
            return (result, row.CorrelationId);
        }
        catch (JsonException error) { throw Refused(503, Unavailable, error); }
    }
    private void RecordCommand<T>(string command, Guid targetId, string hash, InventoryCommandMetadata metadata, T result) =>
        context.Commands.Add(new InventoryCommandRow { CompanyId = companyId, CommandName = command, IdempotencyKeyHash = Hash(metadata.IdempotencyKey),
            ActorId = metadata.ActorId, PayloadHash = hash, TargetId = targetId, ResultJson = JsonSerializer.Serialize(result, Json),
            CorrelationId = metadata.CorrelationId, RecordedAtUtc = clock.GetUtcNow() });
    private void Audit(InventoryCommandMetadata metadata, Guid allocationId, string operation, long? previous, long version, string before, string after, string reason) =>
        audit.Enqueue(new AuditEventDraft(companyId, "inventory.subcontract-delivery.allocation.v1", "Inventory.StockAllocation", allocationId.ToString("D"),
            operation, previous, version, AuditActorTypes.User, metadata.ActorId, metadata.PermissionCode, metadata.RoleCodes, before, after, reason,
            null, AuditAiInvolvement.None, null, metadata.SourceChannel, metadata.ClientIpAddress, metadata.UserAgent,
            metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));
    internal static async Task AcquirePhysicalValidationAsync(Func<CancellationToken, Task> acquire, CancellationToken token)
    {
        try { await acquire(token); }
        catch (InventoryStoreException exception) when (exception.Code == InventoryStoreCodes.ConcurrentChange)
        {
            throw Refused(409, InventoryDefiniteShipmentCodesV1.ConcurrentChange, exception);
        }
    }

    private async Task FlushAsync(CancellationToken token)
    {
        try { await context.SaveChangesAsync(token); await auditWriter.WritePendingAsync(transaction, token); }
        catch (DbUpdateConcurrencyException error) { auditWriter.DiscardPending(); throw Refused(409, InventoryDefiniteShipmentCodesV1.ConcurrentChange, error); }
        catch (DbUpdateException error) when (error.InnerException is SqlException { Number: 2601 or 2627 })
        { auditWriter.DiscardPending(); throw Refused(409, InventoryDefiniteShipmentCodesV1.ConcurrentChange, error); }
        catch { auditWriter.DiscardPending(); throw; }
    }
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Json))));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public async ValueTask DisposeAsync() { if (!disposed) { disposed = true; await context.DisposeAsync(); } }
}
