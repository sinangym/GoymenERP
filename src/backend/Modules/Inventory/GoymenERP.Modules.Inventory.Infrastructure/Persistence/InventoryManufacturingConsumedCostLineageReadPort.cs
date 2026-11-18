using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>Reads one saved generation revision only. Never queries today's labels, balances or material graph.</summary>
public sealed class InventoryManufacturingConsumedCostLineageReadPort(InventoryDbContext db, ICompanyContext company)
    : IInventoryManufacturingConsumedCostLineageReadPortV1
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    private const string Source = InventoryManufacturingConsumedCostLineageEvidenceV1.SourceVersion;

    public async Task<InventoryManufacturingConsumedCostLineageReadV1> ReadAsync(Guid companyId, Guid generationId,
        string generationVersion, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InventoryManufacturingConsumedCostLineageReadV1 Failed(InventoryManufacturingConsumedCostLineageOutcomeV1 outcome) =>
            new InventoryManufacturingConsumedCostLineageReadV1(outcome, companyId, generationId, generationVersion, null, Source + "/" + outcome.ToString().ToLowerInvariant(),
                outcome == InventoryManufacturingConsumedCostLineageOutcomeV1.NotFound ? "generation_not_found"
                : outcome == InventoryManufacturingConsumedCostLineageOutcomeV1.UnreadableLegacy ? "generation_lineage_not_recorded"
                : "generation_lineage_unreadable");
        if (!SameCompany(companyId) || generationId == Guid.Empty)
            return Failed(InventoryManufacturingConsumedCostLineageOutcomeV1.NotFound);
        if (!InventoryManufacturingConsumedCostLineageEvidenceV1.TryVersion(generationVersion, out var version))
            return Failed(InventoryManufacturingConsumedCostLineageOutcomeV1.Unreadable);
        try
        {
            var history = await db.ManufacturingCostGenerationHistory.AsNoTracking().SingleOrDefaultAsync(row =>
                row.GenerationId == generationId && row.Version == version, cancellationToken);
            if (!SameCompany(companyId)) return Failed(InventoryManufacturingConsumedCostLineageOutcomeV1.Unreadable);
            if (history is null) return Failed(InventoryManufacturingConsumedCostLineageOutcomeV1.NotFound);
            var header = await db.ManufacturingCostGenerations.AsNoTracking().SingleOrDefaultAsync(row => row.Id == generationId,
                cancellationToken);
            if (!SameCompany(companyId)) return Failed(InventoryManufacturingConsumedCostLineageOutcomeV1.Unreadable);
            var result = JsonSerializer.Deserialize<InventoryManufacturingCostGenerationResultV1>(history.ResultJson, Json);
            if (header is null || header.CompanyId != companyId || history.CompanyId != companyId || header.Version < version
                || result is null || result.CompanyId != companyId || result.GenerationId != generationId
                || result.GenerationVersion != generationVersion || result.ManufacturingOrderId != header.ManufacturingOrderId
                || result.ClosedOrderVersion != header.ClosedOrderVersion
                || !InventoryManufacturingConsumedCostLineageEvidenceV1.TryVersion(result.ClosedOrderVersion, out _)
                || !Enum.IsDefined(result.State) || result.IsReplay || result.CostDifferenceEventId == Guid.Empty
                || string.IsNullOrWhiteSpace(result.InputFingerprint) || result.InputFingerprint != history.InputFingerprint
                || result.OriginalCorrelationId != history.CorrelationId || result.ConsumedLineageReference is not null
                || result.Outputs is null || result.Outputs.Count > InventoryManufacturingConsumedCostLineageEvidenceV1.MaximumMovementLines
                || result.Outputs.Any(output => output is null || output.OutputId == Guid.Empty || output.ReceiptMovementId == Guid.Empty
                    || output.LabelId == Guid.Empty || output.NetQuantityKg < 0m)
                || result.Outputs.Select(output => output.OutputId).Distinct().Count() != result.Outputs.Count
                || result.Outputs.Select(output => output.ReceiptMovementId).Distinct().Count() != result.Outputs.Count
                || result.Outputs.Select(output => output.LabelId).Distinct().Count() != result.Outputs.Count)
                return Failed(InventoryManufacturingConsumedCostLineageOutcomeV1.Unreadable);
            var facts = JsonSerializer.Serialize(result.Outputs.OrderBy(output => output.OutputId).Select(output =>
                new { output.OutputId, output.ReceiptMovementId, output.LabelId, output.NetQuantityKg }), Json);
            if (facts != header.OutputFactsJson) return Failed(InventoryManufacturingConsumedCostLineageOutcomeV1.Unreadable);
            // Legacy JSON has no recorded shipment list. A current graph cannot prove the old transaction's snapshot.
            if (result.Outputs.Any(output => output.ConsumedCostLineage is null))
                return Failed(InventoryManufacturingConsumedCostLineageOutcomeV1.UnreadableLegacy);
            if (result.Outputs.Any(output => !InventoryManufacturingConsumedCostLineageEvidenceV1.IsValid(companyId,
                    output.OutputId, output.ReceiptMovementId, output.LabelId, output.NetQuantityKg, output.ConsumedCostLineage)))
                return Failed(InventoryManufacturingConsumedCostLineageOutcomeV1.Unreadable);
            foreach (var output in result.Outputs)
            {
                var consumed = InventoryExactCostRatioV1.Resolve(output.ConsumedQuantityKg, output.ConsumedQuantityKgRatio);
                var remaining = InventoryExactCostRatioV1.Resolve(output.RemainingQuantityKg, output.RemainingQuantityKgRatio);
                if (consumed.Sign < 0 || remaining.Sign < 0 || consumed.Add(remaining).CompareTo(
                        GoymenERP.Modules.Inventory.Domain.Costing.ExactCostRatio.Of(output.NetQuantityKg)) != 0
                    || result.State is InventoryManufacturingCostGenerationStateV1.Recognized or InventoryManufacturingCostGenerationStateV1.Reversed
                        && output.OriginalTlAmount is not null
                        && consumed.CompareTo(output.ConsumedCostLineage!.NetShippedRootQuantityKg.Value()) != 0)
                    return Failed(InventoryManufacturingConsumedCostLineageOutcomeV1.Unreadable);
            }
            if (result.CostDifferenceEventId is { } eventId)
            {
                var envelope = await db.OutboxMessages.AsNoTracking().SingleOrDefaultAsync(row => row.Id == eventId, cancellationToken);
                if (!SameCompany(companyId)) return Failed(InventoryManufacturingConsumedCostLineageOutcomeV1.Unreadable);
                if (envelope is null || envelope.CompanyId != companyId || envelope.EventType != InventoryOutboxContractV1.ManufacturingCostDifferenceProposed
                    || envelope.SchemaVersion != 1 || envelope.AggregateType != "Inventory.ManufacturingCostGeneration"
                    || envelope.AggregateId != generationId || envelope.AggregateVersion != version || envelope.Sequence != version
                    || envelope.SourceOperation != (result.State == InventoryManufacturingCostGenerationStateV1.Reversed
                        ? "inventory.manufacturing-cost.reverse.v1" : "inventory.manufacturing-cost.stage.v1")
                    || envelope.CorrelationId != history.CorrelationId
                    || envelope.PayloadJson != history.ResultJson && envelope.PayloadJson != JsonSerializer.Serialize(
                        InventoryManufacturingConsumedCostLineageEvidenceV1.ForOutbox(result, history.ResultJson), Json))
                    return Failed(InventoryManufacturingConsumedCostLineageOutcomeV1.Unreadable);
            }
            if (!SameCompany(companyId)) return Failed(InventoryManufacturingConsumedCostLineageOutcomeV1.Unreadable);
            var snapshot = new InventoryManufacturingConsumedCostLineageSnapshotV1(companyId, result.ManufacturingOrderId,
                generationId, generationVersion, result.ClosedOrderVersion, result.State, result.InputFingerprint,
                result.CostDifferenceEventId, Array.AsReadOnly(result.Outputs.OrderBy(output => output.OutputId).Select(output =>
                    new InventoryManufacturingConsumedCostOutputV1(output.OutputId, output.ReceiptMovementId, output.LabelId,
                        output.NetQuantityKg, InventoryExactCostRatioV1.From(InventoryExactCostRatioV1.Resolve(
                            output.ConsumedQuantityKg, output.ConsumedQuantityKgRatio)), output.ConsumedCostLineage!)).ToArray()));
            return new InventoryManufacturingConsumedCostLineageReadV1(InventoryManufacturingConsumedCostLineageOutcomeV1.Available, companyId, generationId, generationVersion,
                snapshot, Source + "/" + InventoryManufacturingConsumedCostLineageEvidenceV1.Sha256(history.ResultJson));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return Failed(InventoryManufacturingConsumedCostLineageOutcomeV1.Unreadable); }
    }

    private bool SameCompany(Guid asked) => asked != Guid.Empty && company.IsResolved && company.CompanyId == asked;
}
