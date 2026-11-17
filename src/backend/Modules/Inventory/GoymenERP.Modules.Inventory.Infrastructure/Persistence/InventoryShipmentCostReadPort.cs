using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// Historical owner evidence only. Current stock/average, current allocation status and invoice price are not read.
/// Logistics owns whether its shipment remains effective; the consumer owns authority and accounting admission.
/// </summary>
public sealed class InventoryShipmentCostReadPort(InventoryDbContext db, ICompanyContext company)
    : IInventoryShipmentCostReadPortV1
{
    private const string Version = "inventory.shipment-cost.v1";
    private const string ShipmentSource = "logistics.definite-shipment";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<InventoryShipmentCostReadV1> GetAsync(Guid companyId, Guid shipmentId, Guid movementId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!SameCompany(companyId) || shipmentId == Guid.Empty || movementId == Guid.Empty)
            return Failure(InventoryShipmentCostReadOutcomeV1.Unreadable, companyId, shipmentId, movementId);
        try
        {
            var movement = await db.Movements.AsNoTracking().SingleOrDefaultAsync(row => row.Id == movementId,
                cancellationToken);
            if (!SameCompany(companyId)) return Failure(InventoryShipmentCostReadOutcomeV1.Unreadable, companyId, shipmentId, movementId);
            if (movement is null || movement.CompanyId != companyId || movement.SourceKind != ShipmentSource
                || movement.SourceId != shipmentId || movement.Kind != (int)StockMovementKind.Issue)
                return Failure(InventoryShipmentCostReadOutcomeV1.NotFound, companyId, shipmentId, movementId);
            if (movement.SourceGrain != (int)StockSourceGrain.Line || movement.SourceLineId is not { } instructionLineId
                || instructionLineId == Guid.Empty || movement.OccurredAtUtc.Offset != TimeSpan.Zero)
                return Failure(InventoryShipmentCostReadOutcomeV1.Unreadable, companyId, shipmentId, movementId);
            var lines = await db.MovementLines.AsNoTracking().Where(row => row.MovementId == movementId)
                .ToArrayAsync(cancellationToken);
            if (lines is not [var line] || line.CompanyId != companyId || line.LineNumber != 1
                || line.Direction != (int)StockDirection.Out || line.QuantityKg <= 0m || line.LocationId == Guid.Empty
                || line.ProductId == Guid.Empty || line.LabelId == Guid.Empty || line.Block != (int)BlockState.Free
                || line.OwnerKind != (int)StockPartyKind.Company || line.OwnerId != companyId
                || line.CustodianKind != (int)StockPartyKind.Company || line.CustodianId != companyId)
                return Failure(InventoryShipmentCostReadOutcomeV1.Unreadable, companyId, shipmentId, movementId);
            var label = await db.Labels.AsNoTracking().SingleOrDefaultAsync(row => row.Id == line.LabelId,
                cancellationToken);
            var commands = await db.Commands.AsNoTracking().Where(row => row.TargetId == shipmentId
                && row.CommandName == InventoryDefiniteShipmentOutboxV1.SourceOperation).ToArrayAsync(cancellationToken);
            if (label is null || label.CompanyId != companyId || label.ProductId != line.ProductId
                || string.IsNullOrWhiteSpace(label.ProductVersion) || string.IsNullOrWhiteSpace(label.Code)
                || commands is not [var command] || command.CompanyId != companyId)
                return Failure(InventoryShipmentCostReadOutcomeV1.Unreadable, companyId, shipmentId, movementId);
            var result = JsonSerializer.Deserialize<InventoryDefiniteShipmentResultV1>(command.ResultJson, Json);
            if (result is null || result.CompanyId != companyId || result.ShipmentId != shipmentId || result.MovementId != movementId
                || result.LabelId != line.LabelId || result.AllocationId == Guid.Empty || result.OutboxMessageId == Guid.Empty
                || result.IsReplay || result.OriginalCorrelationId != command.CorrelationId
                || command.CorrelationId != movement.CorrelationId || command.ActorId != movement.RecordedBy
                || !PositiveVersion(result.LabelVersion)
                || !PositiveVersion(result.AllocationVersion))
                return Failure(InventoryShipmentCostReadOutcomeV1.Unreadable, companyId, shipmentId, movementId);
            var envelope = await db.OutboxMessages.AsNoTracking().SingleOrDefaultAsync(row => row.Id == result.OutboxMessageId,
                cancellationToken);
            if (envelope is null || envelope.CompanyId != companyId || envelope.EventType != InventoryDefiniteShipmentOutboxV1.EventType
                || envelope.SchemaVersion != InventoryDefiniteShipmentOutboxV1.SchemaVersion
                || envelope.SourceOperation != InventoryDefiniteShipmentOutboxV1.SourceOperation
                || envelope.AggregateType != InventoryDefiniteShipmentOutboxV1.AggregateType || envelope.AggregateId != result.AllocationId
                || envelope.AggregateVersion.ToString(CultureInfo.InvariantCulture) != result.AllocationVersion
                || envelope.Sequence != envelope.AggregateVersion || envelope.OccurredAtUtc != movement.OccurredAtUtc
                || envelope.CorrelationId != command.CorrelationId)
                return Failure(InventoryShipmentCostReadOutcomeV1.Unreadable, companyId, shipmentId, movementId);
            var original = JsonSerializer.Deserialize<InventoryDefiniteShipmentRecordedV1>(envelope.PayloadJson, Json);
            if (original is null || original.CompanyId != companyId || original.ShipmentId != shipmentId || original.MovementId != movementId
                || original.LoadingInstructionId == Guid.Empty || original.LoadingInstructionLineId != instructionLineId
                || original.LabelId != line.LabelId || original.AllocationId != result.AllocationId
                || original.LabelVersion != result.LabelVersion || original.AllocationVersion != result.AllocationVersion
                || original.QuantityKg != line.QuantityKg.ToString("0.###", CultureInfo.InvariantCulture)
                || original.OccurredAtUtc != movement.OccurredAtUtc || original.CorrelationId != envelope.CorrelationId
                || original.CausationId != envelope.CausationId)
                return Failure(InventoryShipmentCostReadOutcomeV1.Unreadable, companyId, shipmentId, movementId);
            var stamp = await db.MovementCostStamps.AsNoTracking().SingleOrDefaultAsync(row => row.MovementId == movementId
                && row.LineNumber == 1 && row.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira, cancellationToken);
            if (stamp is not null && (stamp.CompanyId != companyId || stamp.CorrelationId != movement.CorrelationId))
                return Failure(InventoryShipmentCostReadOutcomeV1.Unreadable, companyId, shipmentId, movementId);
            var tl = InventoryMovementCostStampProjection.Read(stamp, line.QuantityKg, SettingsCurrencyIdentitiesV1.TurkishLira);
            if (!SameCompany(companyId)) return Failure(InventoryShipmentCostReadOutcomeV1.Unreadable, companyId, shipmentId, movementId);
            var answer = new InventoryShipmentCostLineV1(1, original.LoadingInstructionId, instructionLineId, result.AllocationId,
                line.LabelId, label.Code, line.ProductId, label.ProductVersion, (StockPartyKind)line.OwnerKind, line.OwnerId,
                (StockPartyKind)line.CustodianKind, line.CustodianId, line.LocationId, (BlockState)line.Block, line.QuantityKg, tl);
            var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                new { companyId, shipmentId, movementId, OriginalOutboxId = envelope.Id, movement.PayloadHash,
                    OriginalCommandPayloadHash = command.PayloadHash, OriginalResult = command.ResultJson,
                    OriginalEnvelope = envelope.PayloadJson, StampEvidence = stamp?.EvidenceJson,
                    StampRecordedAtUtc = stamp?.RecordedAtUtc, StampCurrencySourceVersion = stamp?.CurrencySourceVersion,
                    Line = answer }, Json))));
            return new(InventoryShipmentCostReadOutcomeV1.Available, companyId, shipmentId, movementId, [answer], Version + "/" + fingerprint);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return Failure(InventoryShipmentCostReadOutcomeV1.Unreadable, companyId, shipmentId, movementId); }
    }

    private bool SameCompany(Guid id) => id != Guid.Empty && company.IsResolved && company.CompanyId == id;
    private static bool PositiveVersion(string value) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture,
        out var parsed) && parsed > 0 && value == parsed.ToString(CultureInfo.InvariantCulture);
    private static InventoryShipmentCostReadV1 Failure(InventoryShipmentCostReadOutcomeV1 outcome, Guid company, Guid shipment, Guid movement) =>
        new(outcome, company, shipment, movement, [], Version + (outcome == InventoryShipmentCostReadOutcomeV1.NotFound ? "/not-found" : "/unreadable"),
            outcome == InventoryShipmentCostReadOutcomeV1.NotFound ? "shipment_movement_not_found" : "shipment_cost_provenance_unreadable");
}
