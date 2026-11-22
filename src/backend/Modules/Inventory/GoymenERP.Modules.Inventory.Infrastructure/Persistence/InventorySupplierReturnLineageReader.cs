using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// AK-8 (SAT-D433 TI1, SAT-D441, SAT-D445): reads one label's lineage back to its roots — company-fenced, read-only, price-free.
/// It walks only the producing split/merge transformations above the label (never a sibling's later history). A manufacturing
/// output starts new material and is an "other" root; a root brought in by exactly one whole goods receipt is a purchase root;
/// anything else is "other". Every refusal is the closed 503 receipt-trace code: a root is never guessed.
/// </summary>
public sealed class InventorySupplierReturnLineageReader(InventoryDbContext db, ICompanyContext company)
    : IInventorySupplierReturnLineageReadPortV1
{
    private const int QueryBatchSize = 256;

    public async Task<InventorySupplierReturnLineageV1> ReadAsync(Guid companyId, Guid labelId,
        CancellationToken cancellationToken = default)
    {
        if (!company.IsResolved || companyId == Guid.Empty || company.CompanyId != companyId || labelId == Guid.Empty)
            throw Unreadable();
        try
        {
            return await ReadCoreAsync(companyId, labelId, cancellationToken);
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException or ArgumentException
            or OverflowException or DivideByZeroException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw Unreadable();
        }
    }

    private async Task<InventorySupplierReturnLineageV1> ReadCoreAsync(Guid companyId, Guid labelId, CancellationToken token)
    {
        var target = await db.Labels.AsNoTracking().Where(row => row.CompanyId == companyId && row.Id == labelId)
            .Select(row => new { row.Id, row.ProductId }).SingleOrDefaultAsync(token) ?? throw Unreadable();
        var labels = new HashSet<Guid> { labelId };
        var headers = new Dictionary<Guid, int>();
        var inputs = new List<InventorySupplierReturnLineageEdge>();
        var outputs = new List<InventorySupplierReturnLineageEdge>();
        var manufactured = new HashSet<Guid>();
        var frontier = new HashSet<Guid> { labelId };
        var depth = 0;
        while (frontier.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            if (++depth > InventoryCostLineageShares.MaximumDepth + 1) throw Unreadable();
            var producing = new List<(Guid Label, Guid Transformation)>();
            foreach (var batch in frontier.Order().Chunk(QueryBatchSize))
                producing.AddRange((await db.LabelTransformationOutputs.AsNoTracking()
                    .Where(row => row.CompanyId == companyId && batch.Contains(row.LabelId))
                    .Select(row => new { row.LabelId, row.TransformationId }).ToListAsync(token))
                    .Select(row => (row.LabelId, row.TransformationId)));
            if (producing.GroupBy(row => row.Label).Any(group => group.Count() > 1)) throw Unreadable();
            var newIds = producing.Select(row => row.Transformation).Distinct().Where(id => !headers.ContainsKey(id)).ToArray();
            if (headers.Count + newIds.Length > InventoryCostLineageShares.MaximumTransformations) throw Unreadable();
            var next = new HashSet<Guid>();
            foreach (var batch in newIds.Order().Chunk(QueryBatchSize))
            {
                var found = await db.LabelTransformations.AsNoTracking()
                    .Where(row => row.CompanyId == companyId && batch.Contains(row.Id))
                    .Select(row => new { row.Id, row.Kind }).ToListAsync(token);
                if (found.Count != batch.Length) throw Unreadable();
                foreach (var header in found) headers.Add(header.Id, header.Kind);
                var repack = found.Where(row => row.Kind != (int)InventoryLabelTransformationKind.Manufacturing).Select(row => row.Id).ToArray();
                foreach (var made in found.Where(row => row.Kind == (int)InventoryLabelTransformationKind.Manufacturing))
                    manufactured.UnionWith(producing.Where(row => row.Transformation == made.Id).Select(row => row.Label));
                if (repack.Length == 0) continue;
                var loadedInputs = await db.LabelTransformationInputs.AsNoTracking()
                    .Where(row => row.CompanyId == companyId && repack.Contains(row.TransformationId))
                    .Select(row => new InventorySupplierReturnLineageEdge(row.TransformationId, row.Sequence, row.LabelId, row.QuantityKg))
                    .ToListAsync(token);
                var loadedOutputs = await db.LabelTransformationOutputs.AsNoTracking()
                    .Where(row => row.CompanyId == companyId && repack.Contains(row.TransformationId))
                    .Select(row => new InventorySupplierReturnLineageEdge(row.TransformationId, row.Sequence, row.LabelId, row.QuantityKg))
                    .ToListAsync(token);
                inputs.AddRange(loadedInputs);
                outputs.AddRange(loadedOutputs);
                if (inputs.Count + outputs.Count > InventoryCostLineageShares.MaximumQuantityRows) throw Unreadable();
                foreach (var input in loadedInputs) if (labels.Add(input.LabelId)) next.Add(input.LabelId);
            }

            frontier = next;
        }

        // Every repack label in the walked ancestry must be the target's product (a repack never changes the product).
        var ancestry = labels.ToArray();
        var products = new List<Guid>();
        foreach (var batch in ancestry.Order().Chunk(QueryBatchSize))
            products.AddRange(await db.Labels.AsNoTracking().Where(row => row.CompanyId == companyId && batch.Contains(row.Id))
                .Select(row => row.ProductId).ToListAsync(token));
        if (products.Count != ancestry.Length || products.Any(product => product != target.ProductId)) throw Unreadable();

        var produced = outputs.Select(row => row.LabelId).ToHashSet();
        var roots = ancestry.Where(id => !produced.Contains(id) || manufactured.Contains(id)).ToArray();
        var receipts = new List<InventorySupplierReturnLineageReceiptFact>();
        var rootList = roots.Where(id => !manufactured.Contains(id)).ToArray();
        foreach (var batch in rootList.Order().Chunk(QueryBatchSize))
            receipts.AddRange(await (from line in db.MovementLines.AsNoTracking()
                join movement in db.Movements.AsNoTracking() on line.MovementId equals movement.Id
                where line.CompanyId == companyId && movement.CompanyId == companyId && batch.Contains(line.LabelId)
                    && movement.Kind == (int)StockMovementKind.Receipt && line.Direction == (int)StockDirection.In
                    && movement.SourceKind == InventoryGoodsReceiptContractV1.SourceKind
                select new InventorySupplierReturnLineageReceiptFact(line.LabelId, movement.Id, movement.SourceId,
                    movement.SourceGrain, movement.SourceLineId, movement.RecordedAtUtc)).ToListAsync(token));
        return Compose(companyId, labelId, target.ProductId, roots, manufactured,
            headers.Where(pair => pair.Value != (int)InventoryLabelTransformationKind.Manufacturing)
                .Select(pair => (pair.Key, (InventoryLabelTransformationKind)pair.Value)).ToArray(),
            inputs, outputs, receipts);
    }

    /// <summary>Pure composition of the loaded facts; used directly by model tests.</summary>
    internal static InventorySupplierReturnLineageV1 Compose(Guid companyId, Guid labelId, Guid productId,
        IReadOnlyCollection<Guid> roots, IReadOnlySet<Guid> manufactured,
        IReadOnlyList<(Guid Id, InventoryLabelTransformationKind Kind)> repacks,
        IReadOnlyList<InventorySupplierReturnLineageEdge> inputs, IReadOnlyList<InventorySupplierReturnLineageEdge> outputs,
        IReadOnlyList<InventorySupplierReturnLineageReceiptFact> receipts)
    {
        var transformations = repacks.Select(header => new InventoryCostLineageTransformation(header.Id, header.Kind,
            inputs.Where(row => row.TransformationId == header.Id).OrderBy(row => row.Sequence)
                .Select(row => new InventoryCostLineageQuantity(row.LabelId, row.QuantityKg)),
            outputs.Where(row => row.TransformationId == header.Id).OrderBy(row => row.Sequence)
                .Select(row => new InventoryCostLineageQuantity(row.LabelId, row.QuantityKg)))).ToArray();
        var shares = SupplierReturnLineageCalculator.ResolveShares(labelId, roots, transformations) ?? throw Unreadable();
        var result = new List<InventorySupplierReturnLineageRootV1>();
        foreach (var (root, share) in shares)
        {
            var facts = manufactured.Contains(root) ? [] : receipts.Where(row => row.LabelId == root).ToArray();
            if (facts.Length > 1) throw Unreadable();
            if (facts.Length == 1)
            {
                var fact = facts[0];
                if (fact.SourceGrain != (int)StockSourceGrain.Whole || fact.SourceLineId is not null || fact.AcceptanceId == Guid.Empty)
                    throw Unreadable();
                result.Add(new InventorySupplierReturnLineageRootV1(root, InventorySupplierReturnLineageRootKindV1.PurchaseReceipt,
                    fact.AcceptanceId, fact.MovementId, fact.RecordedAtUtc, InventoryExactCostRatioV1.From(share)));
            }
            else
                result.Add(new InventorySupplierReturnLineageRootV1(root, InventorySupplierReturnLineageRootKindV1.Other,
                    null, null, null, InventoryExactCostRatioV1.From(share)));
        }

        return new InventorySupplierReturnLineageV1(companyId, labelId, productId, result
            .OrderBy(row => row.Kind).ThenBy(row => row.ReceivedAtUtc).ThenBy(row => row.ReceiptMovementId).ThenBy(row => row.RootLabelId)
            .ToArray());
    }

    private static InventorySupplierReturnExitException Unreadable() =>
        InventorySupplierReturnExitException.Refused(503, InventorySupplierReturnExitCodesV1.ReceiptTraceInvalid);
}

internal sealed record InventorySupplierReturnLineageEdge(Guid TransformationId, int Sequence, Guid LabelId, decimal QuantityKg);

internal sealed record InventorySupplierReturnLineageReceiptFact(Guid LabelId, Guid MovementId, Guid AcceptanceId, int SourceGrain,
    Guid? SourceLineId, DateTimeOffset RecordedAtUtc);
