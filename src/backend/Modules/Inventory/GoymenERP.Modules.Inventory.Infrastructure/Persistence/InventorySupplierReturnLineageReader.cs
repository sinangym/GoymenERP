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

    public async Task<InventorySupplierReturnLineagePlanV1> PlanAsync(Guid companyId, Guid labelId, string quantityKg, int? pieceCount,
        CancellationToken cancellationToken = default)
    {
        if (!decimal.TryParse(quantityKg, System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture, out var kg) || kg <= 0m
            || InventorySupplierReturnReceiptStates.Kg(kg) != quantityKg || pieceCount is < 1)
            throw InventorySupplierReturnExitException.Refused(400, InventorySupplierReturnExitCodesV1.InvalidQuantity);
        var lineage = await ReadAsync(companyId, labelId, cancellationToken);
        try
        {
            var balances = await db.Balances.AsNoTracking().Where(row => row.CompanyId == companyId && row.LabelId == labelId)
                .Select(row => new { row.Block, row.QuantityKg, row.PieceCount, row.LocationId, row.OwnerKind, row.OwnerId })
                .ToListAsync(cancellationToken);
            var stocked = balances.Where(row => row.QuantityKg > 0m).ToArray();
            var tracks = stocked.Any(row => row.PieceCount is not null);
            var free = stocked.Where(row => row.Block == (int)BlockState.Free).ToArray();
            var blocked = stocked.Where(row => row.Block == (int)BlockState.Blocked).ToArray();
            var locations = stocked.Where(row => row.OwnerKind == (int)StockPartyKind.Company && row.OwnerId == companyId)
                .Select(row => row.LocationId).Distinct().ToArray();
            var averageReadable = locations.Length == 1 && stocked.All(row => row.OwnerKind == (int)StockPartyKind.Company)
                && await InventorySupplierReturnReceiptStates.ReadAverageAsync(db, companyId, lineage.ProductId, locations[0],
                    cancellationToken) is not null;
            var purchase = lineage.Roots.Where(root => root.Kind == InventorySupplierReturnLineageRootKindV1.PurchaseReceipt).ToArray();
            IReadOnlyList<SupplierReturnLineagePart>? parts = null;
            if (purchase.Length == lineage.Roots.Count)
            {
                // SAT-D442 behind its own gate: with it closed a piece return over several receipts is not distributed.
                if (pieceCount is not null && purchase.Length > 1 && !InventorySupplierReturnPieceDistributionV1.IsAccepted())
                    throw Unreadable();
                parts = SupplierReturnLineageCalculator.DistributeByShare(Shares(lineage), kg, tracks ? pieceCount : null);
            }

            var receipts = new List<InventorySupplierReturnReceiptStateV1>();
            foreach (var root in purchase)
            {
                var facts = await InventorySupplierReturnReceiptStates.ReadAsync(db, companyId, root.GoodsAcceptanceId!.Value,
                    cancellationToken) ?? throw Unreadable();
                if (facts.ReceiptMovementId != root.ReceiptMovementId || facts.RootLabelId != root.RootLabelId) throw Unreadable();
                var part = parts?.Single(item => item.RootId == root.RootLabelId);
                receipts.Add(State(companyId, facts) with
                {
                    DistributedKg = part is null ? null : InventorySupplierReturnReceiptStates.Kg(part.QuantityKg),
                    DistributedPieces = part?.Pieces,
                });
            }

            return new InventorySupplierReturnLineagePlanV1(lineage, tracks,
                InventorySupplierReturnReceiptStates.Kg(free.Sum(row => row.QuantityKg)),
                InventorySupplierReturnReceiptStates.Kg(blocked.Sum(row => row.QuantityKg)),
                tracks ? free.Sum(row => row.PieceCount ?? 0) : null, tracks ? blocked.Sum(row => row.PieceCount ?? 0) : null,
                averageReadable, receipts);
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException or ArgumentException
            or OverflowException or DivideByZeroException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw Unreadable();
        }
    }

    public async Task<InventorySupplierReturnReceiptStateV1> ReadReceiptAsync(Guid companyId, Guid goodsAcceptanceId,
        CancellationToken cancellationToken = default)
    {
        if (!company.IsResolved || companyId == Guid.Empty || company.CompanyId != companyId || goodsAcceptanceId == Guid.Empty)
            throw Unreadable();
        try
        {
            var facts = await InventorySupplierReturnReceiptStates.ReadAsync(db, companyId, goodsAcceptanceId, cancellationToken)
                ?? throw Unreadable();
            return State(companyId, facts);
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException or ArgumentException
            or OverflowException or DivideByZeroException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw Unreadable();
        }
    }

    /// <summary>The lineage's shares in receipt order (the order the roots are answered in), for SAT-D440/SAT-D442 ties.</summary>
    internal static IReadOnlyList<SupplierReturnLineageShare> Shares(InventorySupplierReturnLineageV1 lineage) =>
        lineage.Roots.Select((root, index) => new SupplierReturnLineageShare(root.RootLabelId, index + 1, root.Share.Value())).ToArray();

    internal static InventorySupplierReturnReceiptStateV1 State(Guid companyId, InventorySupplierReturnReceiptStates.Facts facts) =>
        new(companyId, facts.GoodsAcceptanceId, facts.ReceiptMovementId, facts.RootLabelId, facts.ReceivedAtUtc,
            InventorySupplierReturnReceiptStates.Kg(facts.ReceivedKg),
            InventorySupplierReturnReceiptStates.Kg(facts.ReceivedKg - facts.ReturnedKg), facts.Cohorts is not null,
            facts.Cohorts is null ? null : InventoryExactCostRatioV1.From(facts.Cohorts.PendingKg),
            facts.Cohorts is null ? [] : facts.Cohorts.InvoicedKg.OrderBy(pair => pair.Key)
                .Select(pair => new InventorySupplierReturnInvoicedPartV1(pair.Key, facts.Invoices[pair.Key].InvoiceId,
                    facts.Invoices[pair.Key].InvoiceLineId, InventoryExactCostRatioV1.From(pair.Value))).ToArray());

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
