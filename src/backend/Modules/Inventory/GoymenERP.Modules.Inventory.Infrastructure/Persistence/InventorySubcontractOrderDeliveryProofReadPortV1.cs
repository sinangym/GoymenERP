using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Allocations;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

public sealed class InventorySubcontractOrderDeliveryProofReadPortV1(InventoryDbContext db, ICompanyContext company)
    : IInventorySubcontractOrderDeliveryProofReadPortV1
{
    private const int Bound = 4096;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<InventorySubcontractOrderDeliveryProofReadV1> ReadAsync(Guid companyId, Guid salesOrderId,
        IReadOnlyList<Guid> shipmentIds, Func<CancellationToken, Task> freshConsumerFence, CancellationToken token = default)
    {
        InventorySubcontractOrderDeliveryProofReadV1 Failed() => new(companyId, salesOrderId, false, false, [], null);
        if (companyId == Guid.Empty || salesOrderId == Guid.Empty || freshConsumerFence is null || shipmentIds is null
            || shipmentIds.Count > 128 || shipmentIds.Any(x => x == Guid.Empty) || shipmentIds.Distinct().Count() != shipmentIds.Count
            || !company.IsResolved || company.CompanyId != companyId) return Failed();
        try
        {
            var sources = await Rows(db.SubcontractAllocationSources.AsNoTracking().Where(x => x.CompanyId == companyId && x.SalesOrderId == salesOrderId).OrderBy(x => x.AllocationId));
            var allocations = await Rows(db.Allocations.AsNoTracking().Where(x => x.CompanyId == companyId
                && x.DemandKind == InventoryAllocationDemandKindsV1.SubcontractServiceOrderLine && x.DemandId == salesOrderId).OrderBy(x => x.Id));
            // A released allocation needs its own native release receipt/history proof. This read does not infer it from absence.
            if (allocations.Any(x => (x.Status != (int)StockAllocationStatus.Active && x.Status != (int)StockAllocationStatus.Consumed)
                    || x.Status == (int)StockAllocationStatus.Consumed && x.RemainingKg != 0m)
                || !allocations.Select(x => x.Id).Order().SequenceEqual(sources.Select(x => x.AllocationId).Order())) return Failed();
            var active = allocations.Where(x => x.Status == (int)StockAllocationStatus.Active).ToArray();
            foreach (var hold in active)
            {
                var source = sources.SingleOrDefault(x => x.AllocationId == hold.Id);
                if (source is null || hold.RemainingKg <= 0m || hold.RemainingKg > hold.AllocatedKg
                    || hold.DemandLineId != source.SalesOrderLineId || hold.LabelId != source.LabelId) return Failed();
                var original = JsonSerializer.Deserialize<InventorySubcontractDeliverySourceV1>(source.SourceJson, Json);
                if (original is null) return Failed();
                InventorySubcontractDeliveryParticipant.ValidateReadSource(source, original);
                if (!InventorySubcontractOwnershipGuardV1.IsCustomerOwnedAndHeld((StockPartyKind)hold.OwnerKind, hold.OwnerId,
                    (StockPartyKind)hold.CustodianKind, hold.CustodianId, companyId, source.CustomerPartyId)) return Failed();
            }
            var ids = sources.Select(x => x.AllocationId).ToArray();
            var events = await Rows(db.OutboxMessages.AsNoTracking().Where(x => x.CompanyId == companyId
                && x.SourceOperation == InventorySubcontractDeliveryContractV1.ShipmentCommand && ids.Contains(x.AggregateId)).OrderBy(x => x.Id));
            var payloads = events.Select(x => (Event: x, Payload: JsonSerializer.Deserialize<ShipmentEnvelope>(x.PayloadJson, Json))).ToArray();
            var found = payloads.Where(x => x.Payload is not null && shipmentIds.Contains(x.Payload.Line.ShipmentId)).ToArray();
            if (found.Length != shipmentIds.Count || found.Select(x => x.Payload!.Line.ShipmentId).Distinct().Count() != shipmentIds.Count) return Failed();
            var movements = await Rows(db.Movements.AsNoTracking().Where(x => x.CompanyId == companyId
                && x.SourceKind == InventorySubcontractDeliveryContractV1.MovementSourceKind && shipmentIds.Contains(x.SourceId)).OrderBy(x => x.Id));
            if (movements.Length != shipmentIds.Count) return Failed();
            var movementIds = movements.Select(x => x.Id).ToArray();
            var movementLines = await Rows(db.MovementLines.AsNoTracking().Where(x => x.CompanyId == companyId && movementIds.Contains(x.MovementId)).OrderBy(x => x.MovementId).ThenBy(x => x.LineNumber));
            var instructionIds = found.Select(x => x.Payload!.LoadingInstructionId).Distinct().ToArray();
            var commands = await Rows(db.Commands.AsNoTracking().Where(x => x.CompanyId == companyId
                && x.CommandName == InventorySubcontractDeliveryContractV1.ShipmentCommand && x.TargetId != null && instructionIds.Contains(x.TargetId.Value)).OrderBy(x => x.IdempotencyKeyHash));
            var result = new List<InventorySubcontractShipmentProofV1>();
            foreach (var (envelope, payload) in found)
            {
                var p = payload!; var line = p.Line; var source = p.Source;
                var row = sources.SingleOrDefault(x => x.AllocationId == line.AllocationId);
                if (row is null || source.CompanyId != companyId || source.SalesOrderId != salesOrderId
                    || p.CompanyId != companyId || p.CustomerPartyId != source.CustomerPartyId
                    || p.LoadingInstructionId == Guid.Empty || line.LoadingInstructionLineId == Guid.Empty) return Failed();
                // Reuse the command owner's immutable source/hash/column guard; no second source policy.
                InventorySubcontractDeliveryParticipant.ValidateReadSource(row, source);
                var movement = movements.SingleOrDefault(x => x.Id == line.MovementId && x.SourceId == line.ShipmentId);
                var physical = movementLines.Where(x => x.MovementId == line.MovementId).ToArray();
                var receipts = commands.Where(x => x.TargetId == p.LoadingInstructionId).ToArray();
                if (movement is null || movement.Kind != (int)StockMovementKind.Issue || movement.PayloadHash.Length != 64
                    || movement.SourceLineId != line.LoadingInstructionLineId || movement.OccurredAtUtc != p.OccurredAtUtc
                    || physical is not [var stock] || stock.Direction != (int)StockDirection.Out
                    || stock.LocationId == Guid.Empty || stock.Block != (int)BlockState.Free
                    || stock.ProductId != source.ProductId || stock.LabelId != source.LabelId || stock.QuantityKg <= 0m
                    || !decimal.TryParse(line.QuantityKg, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var kg)
                    || stock.QuantityKg != kg || stock.PieceCount != line.PieceCount
                    || !InventorySubcontractOwnershipGuardV1.IsCustomerOwnedAndHeld((StockPartyKind)stock.OwnerKind, stock.OwnerId,
                        (StockPartyKind)stock.CustodianKind, stock.CustodianId, companyId, source.CustomerPartyId)
                    || receipts is not [var command] || command.PayloadHash.Length != 64
                    || command.ActorId != movement.RecordedBy || command.CorrelationId != movement.CorrelationId
                    || envelope.Id != line.OutboxMessageId || envelope.EventType != InventorySubcontractDeliveryContractV1.EventType
                    || envelope.SchemaVersion != 1 || envelope.AggregateId != line.AllocationId
                    || envelope.CorrelationId != command.CorrelationId || envelope.OccurredAtUtc != p.OccurredAtUtc
                    || !long.TryParse(line.AllocationVersion, out var version) || version < 1
                    || envelope.AggregateVersion != version || envelope.Sequence != version) return Failed();
                var receipt = JsonSerializer.Deserialize<InventorySubcontractShipmentResultV1>(command.ResultJson, Json);
                if (receipt is null || receipt.CompanyId != companyId || receipt.LoadingInstructionId != p.LoadingInstructionId
                    || receipt.IsReplay || receipt.OriginalCorrelationId != command.CorrelationId
                    || receipt.Lines.Count(x => x == line) != 1) return Failed();
                result.Add(new(line.ShipmentId, p.LoadingInstructionId, line.LoadingInstructionLineId,
                    line.MovementId, line.AllocationId, kg, source));
            }
            await Fence();
            // Only source facts enter the digest. Movement cost/average/accounting stamps are never queried or disclosed.
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
                { sources, Active = active.Select(x => new { x.Id, x.Version, x.RemainingKg }), result,
                    Events = found.Select(x => new { x.Event.Id, x.Event.AggregateVersion, x.Event.PayloadJson }),
                    Movements = movements.Select(x => new { x.Id, x.PayloadHash }),
                    Commands = commands.Select(x => new { x.PayloadHash, x.ResultJson }) }, Json))));
            return new(companyId, salesOrderId, true, active.Length > 0, result, "inventory.subcontract-delivery-proof.v1:" + hash);
        }
        catch (OperationCanceledException) { throw; }
        catch { await Fence(); return Failed(); }
        async Task<T[]> Rows<T>(IQueryable<T> query)
        {
            await Fence(); var rows = await query.Take(Bound + 1).ToArrayAsync(token); await Fence();
            if (rows.Length > Bound) throw new InvalidOperationException("Subcontract proof exceeds its bounded read.");
            return rows;
        }
        async Task Fence()
        {
            token.ThrowIfCancellationRequested();
            if (!company.IsResolved || company.CompanyId != companyId) throw new UnauthorizedAccessException();
            await freshConsumerFence(token);
            if (!company.IsResolved || company.CompanyId != companyId) throw new UnauthorizedAccessException();
        }
    }
    private sealed record ShipmentEnvelope(Guid CompanyId, Guid LoadingInstructionId, Guid CustomerPartyId,
        InventorySubcontractShipmentLineResultV1 Line, InventorySubcontractDeliverySourceV1 Source, DateTimeOffset OccurredAtUtc);
}

public sealed partial class InventorySubcontractDeliveryParticipant
{
    internal static void ValidateReadSource(InventorySubcontractAllocationSourceRow row, InventorySubcontractDeliverySourceV1 source) => RequireOriginal(row, source);
}
