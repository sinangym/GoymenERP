using System.Collections.ObjectModel;
using GoymenERP.Modules.Inventory.Domain.Labels;

namespace GoymenERP.Modules.Inventory.Domain.Costing;

public sealed record InventoryCostLineageQuantity(Guid LabelId, decimal QuantityKg);

/// <summary>A bounded immutable copy of one whole-label transformation, without persistence or cost data.</summary>
public sealed class InventoryCostLineageTransformation
{
    public InventoryCostLineageTransformation(
        Guid transformationId,
        InventoryLabelTransformationKind kind,
        IEnumerable<InventoryCostLineageQuantity> inputs,
        IEnumerable<InventoryCostLineageQuantity> outputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(outputs);
        TransformationId = transformationId;
        Kind = kind;
        Inputs = Array.AsReadOnly(inputs.Take(InventoryCostLineageShares.MaximumQuantityRows + 1).ToArray());
        Outputs = Array.AsReadOnly(outputs.Take(InventoryCostLineageShares.MaximumQuantityRows + 1).ToArray());
    }

    public Guid TransformationId { get; }
    public InventoryLabelTransformationKind Kind { get; }
    public IReadOnlyList<InventoryCostLineageQuantity> Inputs { get; }
    public IReadOnlyList<InventoryCostLineageQuantity> Outputs { get; }
}

/// <summary>
/// Resolves the fraction of each descendant label's kilograms belonging to one root label's material.
/// This is neither receipt-cohort identification nor a monetary valuation: the caller must prove the
/// relevant root receipt and each input's whole remaining physical quantity independently. Manufacturing
/// creates new material and never inherits a share. Physical exits between transformations reduce the
/// quantity without changing the remaining label's material fraction.
/// All arithmetic stays exact; no quantity, cost, currency or rounding policy is introduced here.
/// </summary>
public static class InventoryCostLineageShares
{
    public const int MaximumDepth = 64;
    public const int MaximumTransformations = 4096;
    public const int MaximumQuantityRows = 16384;
    public const string InvalidGraph = "inventory_cost_lineage_invalid";
    public const string GraphLimitExceeded = "inventory_cost_lineage_graph_limit_exceeded";
    public const string CycleDetected = "inventory_cost_lineage_cycle";
    public const string DepthExceeded = "inventory_cost_lineage_depth_exceeded";

    /// <summary>
    /// Returns the root (share one) and every positive-share descendant. Unrelated labels have share
    /// zero and are omitted. Invalid, incomplete whole-label or excessive graphs return no shares.
    /// </summary>
    public static bool TryResolve(
        Guid rootLabelId,
        IEnumerable<InventoryCostLineageTransformation>? transformations,
        out IReadOnlyDictionary<Guid, ExactCostRatio> shares,
        out string? reason)
    {
        shares = ReadOnly(new Dictionary<Guid, ExactCostRatio>());
        reason = InvalidGraph;
        if (rootLabelId == Guid.Empty || transformations is null) return false;

        var events = transformations.Take(MaximumTransformations + 1).ToArray();
        if (events.Length > MaximumTransformations)
        {
            reason = GraphLimitExceeded;
            return false;
        }

        var eventIds = new HashSet<Guid>();
        var producers = new Dictionary<Guid, InventoryCostLineageTransformation>();
        var consumers = new HashSet<Guid>();
        var labels = new HashSet<Guid>();
        var inputQuantities = new Dictionary<Guid, decimal>();
        var outputQuantities = new Dictionary<Guid, decimal>();
        var successors = new Dictionary<Guid, List<Guid>>();
        var indegrees = new Dictionary<Guid, int>();
        var rowCount = 0;

        foreach (var transformation in events)
        {
            if (transformation is null || transformation.TransformationId == Guid.Empty
                || !eventIds.Add(transformation.TransformationId)
                || transformation.Kind is not (InventoryLabelTransformationKind.Split
                    or InventoryLabelTransformationKind.Merge or InventoryLabelTransformationKind.Manufacturing)) return false;

            rowCount += transformation.Inputs.Count + transformation.Outputs.Count;
            if (rowCount > MaximumQuantityRows)
            {
                reason = GraphLimitExceeded;
                return false;
            }

            // Production quantities do not describe a mass-preserving repack. Do not traverse either
            // side of that edge, including a later split of a manufactured output label.
            if (transformation.Kind == InventoryLabelTransformationKind.Manufacturing) continue;
            if (transformation.Kind == InventoryLabelTransformationKind.Split
                ? transformation.Inputs.Count != 1 || transformation.Outputs.Count < 2
                : transformation.Inputs.Count < 2 || transformation.Outputs.Count != 1) return false;

            var inputIds = new HashSet<Guid>();
            var outputIds = new HashSet<Guid>();
            var inputTotal = ExactCostRatio.Of(0m);
            var outputTotal = ExactCostRatio.Of(0m);
            foreach (var input in transformation.Inputs)
            {
                if (!ValidQuantity(input) || !inputIds.Add(input.LabelId)
                    || !consumers.Add(input.LabelId)) return false;
                RememberLabel(input.LabelId);
                inputQuantities.Add(input.LabelId, input.QuantityKg);
                inputTotal = inputTotal.Add(ExactCostRatio.Of(input.QuantityKg));
            }

            foreach (var output in transformation.Outputs)
            {
                if (!ValidQuantity(output) || inputIds.Contains(output.LabelId) || !outputIds.Add(output.LabelId)
                    || !producers.TryAdd(output.LabelId, transformation)) return false;
                RememberLabel(output.LabelId);
                outputQuantities.Add(output.LabelId, output.QuantityKg);
                outputTotal = outputTotal.Add(ExactCostRatio.Of(output.QuantityKg));
            }

            if (inputTotal.CompareTo(outputTotal) != 0) return false;
            foreach (var input in transformation.Inputs)
            {
                foreach (var output in transformation.Outputs)
                {
                    successors[input.LabelId].Add(output.LabelId);
                    indegrees[output.LabelId]++;
                }
            }
        }

        foreach (var (labelId, producedQuantity) in outputQuantities)
        {
            // The caller proves that the input is the whole balance remaining after physical exits.
            // An exit can decrease it; a repack label cannot acquire additional kilograms by lineage.
            if (inputQuantities.TryGetValue(labelId, out var remainingQuantity)
                && remainingQuantity > producedQuantity) return false;
        }

        // Whole-label consumption makes a diamond legal (one split, one later merge), while a second
        // consumption or a second producer above is corruption. Topological order detects longer cycles
        // without recursive stack growth and computes the longest, rather than first-seen, path.
        var ready = new Queue<Guid>(indegrees.Where(pair => pair.Value == 0).Select(pair => pair.Key));
        var depths = labels.ToDictionary(labelId => labelId, _ => 0);
        var resolved = new Dictionary<Guid, ExactCostRatio> { [rootLabelId] = ExactCostRatio.Of(1m) };
        var visited = 0;
        while (ready.TryDequeue(out var labelId))
        {
            visited++;
            if (labelId != rootLabelId && producers.TryGetValue(labelId, out var producer))
            {
                var contribution = ExactCostRatio.Of(0m);
                foreach (var input in producer.Inputs)
                {
                    if (resolved.TryGetValue(input.LabelId, out var inputShare))
                        contribution = contribution.Add(inputShare.Multiply(input.QuantityKg));
                }

                // Split has one input whose whole mass equals the sum of the outputs. Every child
                // therefore keeps the parent's share; merge divides total contributed mass by its output.
                var share = producer.Kind == InventoryLabelTransformationKind.Split
                    ? contribution.Divide(producer.Inputs[0].QuantityKg)
                    : contribution.Divide(producer.Outputs[0].QuantityKg);
                if (share.Sign > 0) resolved.Add(labelId, share);
            }

            foreach (var successor in successors[labelId])
            {
                depths[successor] = Math.Max(depths[successor], depths[labelId] + 1);
                if (depths[successor] > MaximumDepth)
                {
                    reason = DepthExceeded;
                    return false;
                }

                if (--indegrees[successor] == 0) ready.Enqueue(successor);
            }
        }

        if (visited != labels.Count)
        {
            reason = CycleDetected;
            return false;
        }

        shares = ReadOnly(resolved);
        reason = null;
        return true;

        void RememberLabel(Guid labelId)
        {
            if (!labels.Add(labelId)) return;
            successors.Add(labelId, []);
            indegrees.Add(labelId, 0);
        }
    }

    private static bool ValidQuantity(InventoryCostLineageQuantity? quantity) =>
        quantity is { LabelId: var labelId, QuantityKg: > 0m } && labelId != Guid.Empty;

    private static IReadOnlyDictionary<Guid, ExactCostRatio> ReadOnly(Dictionary<Guid, ExactCostRatio> values) =>
        new ReadOnlyDictionary<Guid, ExactCostRatio>(values);
}
