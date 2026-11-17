using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Api.AccountingRuntime;

/// <summary>Copies one committed Inventory history's exact identities. No current graph, price, ratio rounding or status inference.</summary>
public sealed class ManufacturingAttributionLineageReaderV1(IInventoryManufacturingConsumedCostLineageReadPortV1 owner,
    ICompanyContext company) : IManufacturingAttributionLineageReaderV1
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<ManufacturingAttributionLineageV1> ReadAsync(ManufacturingCostDifferenceEventV1 source,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshConsumerFence);
        async Task Fence()
        {
            await freshConsumerFence(cancellationToken);
            if (!company.IsResolved || company.CompanyId != source.CompanyId || source.CompanyId == Guid.Empty)
                throw Refuse("company_context_mismatch");
        }
        await Fence();
        var reference = source.ConsumedLineageReference ?? throw Refuse("manufacturing_attribution_legacy_unproven");
        var read = await owner.ReadAsync(source.CompanyId, source.GenerationId, source.GenerationVersion, cancellationToken);
        await Fence();
        if (read is not { Outcome: InventoryManufacturingConsumedCostLineageOutcomeV1.Available, Snapshot: { } snapshot }
            || read.CompanyId != source.CompanyId || read.GenerationId != source.GenerationId || read.GenerationVersion != source.GenerationVersion
            || reference.SourceVersion != InventoryManufacturingConsumedCostLineageEvidenceV1.SourceVersion
            || reference.GenerationId != source.GenerationId || reference.GenerationVersion != source.GenerationVersion
            || read.SourceVersion != reference.SourceVersion + "/" + reference.HistorySha256
            || snapshot.CompanyId != source.CompanyId || snapshot.ManufacturingOrderId != source.ManufacturingOrderId
            || snapshot.GenerationId != source.GenerationId || snapshot.GenerationVersion != source.GenerationVersion
            || snapshot.ClosedOrderVersion != source.ClosedOrderVersion || (int)snapshot.State != source.State
            || snapshot.InputFingerprint != source.InputFingerprint || snapshot.CostDifferenceEventId != source.CostDifferenceEventId
            || snapshot.Outputs.Count != source.Outputs.Count) throw Refuse("manufacturing_attribution_lineage_unavailable");
        var outputs = new List<ManufacturingAttributionOutputV1>();
        foreach (var original in source.Outputs)
        {
            var matches = snapshot.Outputs.Where(x => x.OutputId == original.OutputId).ToArray();
            if (matches.Length != 1) throw Refuse("manufacturing_attribution_output_unavailable");
            var output = matches[0];
            var consumed = Ratio(output.ConsumedRootQuantityKg);
            if (output.ReceiptMovementId != original.ReceiptMovementId || output.RootLabelId != original.LabelId
                || output.NetQuantityKg != original.NetQuantityKg
                || !ManufacturingCostDifferenceQuantityV1.TryResolve(original.ConsumedQuantityKg, original.ConsumedQuantityKgRatio, out var amount)
                || amount != consumed || !InventoryManufacturingConsumedCostLineageEvidenceV1.IsValid(source.CompanyId,
                    output.OutputId, output.ReceiptMovementId, output.RootLabelId, output.NetQuantityKg, output.Lineage))
                throw Refuse("manufacturing_attribution_output_unavailable");
            var shipments = new List<ManufacturingAttributionShipmentV1>();
            foreach (var shipment in output.Lineage.Shipments)
            {
                long? sequence = null;
                if (shipment.CreationSequence is { } text)
                {
                    if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < 1
                        || parsed.ToString(CultureInfo.InvariantCulture) != text) throw Refuse("manufacturing_attribution_chronology_unproven");
                    sequence = parsed;
                }
                shipments.Add(new(shipment.ShipmentId, shipment.MovementId, shipment.MovementLineNumber,
                    shipment.LoadingInstructionLineId, shipment.ProductId, shipment.LabelId, shipment.QuantityKg,
                    Ratio(shipment.RootShare), Ratio(shipment.NetRootQuantityKg), shipment.RecordedAtUtc, sequence,
                    shipment.Returns.Count != 0, JsonSerializer.Serialize(shipment, Json)));
            }
            outputs.Add(new(output.OutputId, output.ReceiptMovementId, output.RootLabelId, output.NetQuantityKg,
                consumed, output.Lineage.EvidenceSha256, shipments));
        }
        await Fence();
        return new(source.CompanyId, source.CostDifferenceEventId!.Value, source.GenerationId, source.GenerationVersion,
            reference.HistorySha256, outputs);
    }
    private static AccountingInventoryCostRatioV1 Ratio(InventoryExactCostRatioV1 value) => new(value.Numerator, value.Denominator);
    private static AccountingStoreException Refuse(string code) => new(code, "Kaydedilmiş üretim sevk payı kanıtı bekleniyor.");
}
