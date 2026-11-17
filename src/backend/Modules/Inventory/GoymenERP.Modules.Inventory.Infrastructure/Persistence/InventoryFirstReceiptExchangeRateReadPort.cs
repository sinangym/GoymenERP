using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>FNS-D77/D78/D103/D104. Reads immutable receipt-time owner evidence and repack input kg,
/// never today's rate or invoice stock-cost FX. Manufacturing is a new-material boundary.</summary>
internal sealed class InventoryFirstReceiptExchangeRateReadPort(InventoryDbContext dbContext, ICompanyContext companyContext)
    : IInventoryFirstReceiptExchangeRateReadPortV1
{
    internal const int MaximumLineageDepth = 64;
    private const int MaximumLabels = 4096;
    private const int MaximumRows = WeightedFirstReceiptExchangeRateCalculator.MaximumInputs;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<InventoryFirstReceiptExchangeRateV1> GetAsync(Guid companyId, Guid labelId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
            throw new InvalidOperationException("İlk mal kabul kuru yalnız çözümlenmiş şirket bağlamında okunur.");
        if (companyId == Guid.Empty || labelId == Guid.Empty) throw new ArgumentException("Şirket ve etiket kimlikleri boş olamaz.");
        if (companyId != companyContext.CompanyId) return InventoryFirstReceiptExchangeRateV1.NotFound(companyId, labelId);
        var memo = new Dictionary<Guid, InventoryFirstReceiptExchangeRateV1>();
        var heights = new Dictionary<Guid, int>();
        var path = new HashSet<Guid>();
        var rowCount = 0;
        try
        {
            var result = await ReadAsync(labelId, 0);
            RequireCompany(); cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            // A failed or malformed owner read must not masquerade as a genuine empty material history.
            RequireCompany();
            return Unreadable(labelId);
        }

        void RequireCompany()
        {
            if (!companyContext.IsResolved || companyContext.CompanyId != companyId) throw new UnauthorizedAccessException();
        }
        InventoryFirstReceiptExchangeRateV1 Unreadable(Guid asked) => InventoryFirstReceiptExchangeRateV1.Unavailable(
            companyId, asked, null, InventoryFirstReceiptExchangeRateReasonsV1.Unreadable);

        async Task<InventoryFirstReceiptExchangeRateV1> ReadAsync(Guid current, int depth)
        {
            RequireCompany(); cancellationToken.ThrowIfCancellationRequested();
            if (depth > MaximumLineageDepth) return Unreadable(current);
            // A cached subtree can be reached later through a deeper DAG path. Its full height still counts.
            if (memo.TryGetValue(current, out var cached))
                return depth + heights[current] <= MaximumLineageDepth ? cached : Unreadable(current);
            if (memo.Count + path.Count >= MaximumLabels || !path.Add(current))
                return Unreadable(current);
            try
            {
                var label = await dbContext.Labels.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == current, cancellationToken);
                RequireCompany();
                if (label is null) return InventoryFirstReceiptExchangeRateV1.NotFound(companyId, current);
                if (label.Version < 1 || label.ProductId == Guid.Empty || label.LotId == Guid.Empty) return Unreadable(current);
                var entry = await FirstReceiptAsync(companyId, current, cancellationToken);
                RequireCompany();
                InventoryFirstReceiptExchangeRateV1 result;
                if (entry is not null)
                {
                    if (entry.QuantityKg <= 0 || entry.OccurredAtUtc.Offset != TimeSpan.Zero) return Unreadable(current);
                    var goods = entry.SourceKind == InventoryGoodsReceiptContractV1.SourceKind;
                    var factual = new InventoryFirstReceiptEntryV1(current, entry.MovementId, entry.OccurredAtUtc,
                        goods ? entry.SourceId : null, entry.CostCurrencyId);
                    if (!goods) result = InventoryFirstReceiptExchangeRateV1.Unavailable(companyId, current, factual,
                        InventoryFirstReceiptExchangeRateReasonsV1.FirstEntryNotGoodsReceipt);
                    else
                    {
                        var json = await dbContext.MovementCostStamps.AsNoTracking().Where(x => x.CompanyId == companyId
                            && x.MovementId == entry.MovementId && x.LineNumber == entry.LineNumber
                            && x.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira).Select(x => x.FirstEntryRateJson)
                            .SingleOrDefaultAsync(cancellationToken);
                        RequireCompany();
                        var snapshot = InventoryFirstEntryRateProof.Read(json, companyId, entry.SourceId);
                        if (snapshot?.State != InventoryFirstEntryRateStateV1.Rated)
                            result = InventoryFirstReceiptExchangeRateV1.Unavailable(companyId, current, factual,
                                snapshot?.State == InventoryFirstEntryRateStateV1.NotRequired
                                    ? InventoryFirstReceiptExchangeRateReasonsV1.RateNotRequired
                                    : json is null ? InventoryFirstReceiptExchangeRateReasonsV1.RateNotRecorded
                                    : InventoryFirstReceiptExchangeRateReasonsV1.Unreadable);
                        else result = InventoryFirstReceiptExchangeRateV1.RatedExact(companyId, current,
                            snapshot.SourceCurrencyId!.Value, snapshot.TargetCurrencyId!.Value,
                            InventoryExactCostRatioV1.From(InventoryExactCostRatioV1.Resolve(snapshot.Rate, snapshot.RateRatio)),
                            snapshot.SourceVersion, [new(factual, InventoryExactCostRatioV1.From(ExactCostRatio.Of(1m)), snapshot)]);
                    }
                }
                else
                {
                    var creators = await dbContext.LabelTransformationOutputs.AsNoTracking().Where(x => x.CompanyId == companyId
                        && x.LabelId == current).Take(2).ToListAsync(cancellationToken);
                    RequireCompany();
                    if (creators.Count == 0) result = InventoryFirstReceiptExchangeRateV1.Unavailable(companyId, current, null,
                        InventoryFirstReceiptExchangeRateReasonsV1.NoStockEntry);
                    else if (creators.Count != 1) result = Unreadable(current);
                    else result = await ReadTransformationAsync(label, creators[0], depth);
                }
                heights.TryAdd(current, 0);
                memo[current] = result;
                return result;
            }
            finally { path.Remove(current); }
        }

        async Task<InventoryFirstReceiptExchangeRateV1> ReadTransformationAsync(LabelRow label,
            LabelTransformationOutputRow creator, int depth)
        {
            var current = label.Id;
            var header = await dbContext.LabelTransformations.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId
                && x.Id == creator.TransformationId, cancellationToken);
            RequireCompany();
            if (header is null || header.OccurredAtUtc.Offset != TimeSpan.Zero
                || creator.LabelVersion < 1 || label.Version < creator.LabelVersion || creator.QuantityKg <= 0)
                return Unreadable(current);
            // A Manufacturing output's own Receipt normally answered above. A missing Receipt is not a repack inheritance.
            if (header.Kind == (int)InventoryLabelTransformationKind.Manufacturing) return Unreadable(current);
            var inputs = await dbContext.LabelTransformationInputs.AsNoTracking().Where(x => x.CompanyId == companyId
                && x.TransformationId == header.Id).OrderBy(x => x.Sequence).Take(MaximumRows + 1).ToListAsync(cancellationToken);
            var outputs = await dbContext.LabelTransformationOutputs.AsNoTracking().Where(x => x.CompanyId == companyId
                && x.TransformationId == header.Id).OrderBy(x => x.Sequence).Take(MaximumRows + 1).ToListAsync(cancellationToken);
            RequireCompany();
            rowCount += inputs.Count + outputs.Count;
            if (rowCount > MaximumRows || inputs.Count == 0 || outputs.Count == 0
                || inputs.Any(x => x.QuantityKg <= 0 || x.LabelVersion < 1 || x.LabelId == Guid.Empty)
                || outputs.Any(x => x.QuantityKg <= 0 || x.LabelVersion < 1 || x.LabelId == Guid.Empty)
                || !inputs.Select(x => x.Sequence).SequenceEqual(Enumerable.Range(1, inputs.Count))
                || !outputs.Select(x => x.Sequence).SequenceEqual(Enumerable.Range(1, outputs.Count))
                || inputs.Select(x => x.LabelId).Distinct().Count() != inputs.Count
                || outputs.Select(x => x.LabelId).Distinct().Count() != outputs.Count
                || inputs.Select(x => x.LabelId).Intersect(outputs.Select(x => x.LabelId)).Any()
                || inputs.Sum(x => x.QuantityKg) != outputs.Sum(x => x.QuantityKg)
                || header.Kind == (int)InventoryLabelTransformationKind.Split && (inputs.Count != 1 || outputs.Count < 2)
                || header.Kind == (int)InventoryLabelTransformationKind.Merge && (inputs.Count < 2 || outputs.Count != 1)
                || header.Kind != (int)InventoryLabelTransformationKind.Split && header.Kind != (int)InventoryLabelTransformationKind.Merge)
                return Unreadable(current);
            var ids = inputs.Select(x => x.LabelId).Concat(outputs.Select(x => x.LabelId)).ToArray();
            var labels = await dbContext.Labels.AsNoTracking().Where(x => x.CompanyId == companyId && ids.Contains(x.Id)).ToListAsync(cancellationToken);
            RequireCompany();
            if (labels.Count != ids.Length || labels.Any(x => x.ProductId != label.ProductId || x.LotId != label.LotId
                || x.HeatNumber != label.HeatNumber || x.CoilNumber != label.CoilNumber)
                || inputs.Any(x => labels.Single(l => l.Id == x.LabelId).Version < x.LabelVersion)
                || outputs.Any(x => labels.Single(l => l.Id == x.LabelId).Version < x.LabelVersion)) return Unreadable(current);
            var parents = new List<(LabelTransformationInputRow Input, InventoryFirstReceiptExchangeRateV1 Rate)>();
            foreach (var input in inputs)
            {
                var rate = await ReadAsync(input.LabelId, depth + 1);
                if (rate.Status != InventoryFirstReceiptExchangeRateStatusV1.Rated || rate.RateRatio is null || rate.Sources.Count == 0)
                    return InventoryFirstReceiptExchangeRateV1.Unavailable(companyId, current, null,
                        InventoryFirstReceiptExchangeRateReasonsV1.Unreadable);
                parents.Add((input, rate));
            }
            heights[current] = checked(1 + parents.Max(x => heights[x.Input.LabelId]));
            if (depth + heights[current] > MaximumLineageDepth) return Unreadable(current);
            if (!WeightedFirstReceiptExchangeRateCalculator.TryCalculate(parents.Select(x => new WeightedFirstReceiptExchangeRateInput(
                x.Input.LabelId, ExactCostRatio.Of(x.Input.QuantityKg), x.Rate.RateRatio!.Value(), x.Rate.SourceCurrencyId!.Value,
                x.Rate.TargetCurrencyId!.Value)), out var weighted, out var reason))
                return InventoryFirstReceiptExchangeRateV1.Unavailable(companyId, current, null,
                    reason == WeightedFirstReceiptExchangeRateCalculator.CurrencyPairMismatch
                        ? InventoryFirstReceiptExchangeRateReasonsV1.CurrencyPairMismatch : InventoryFirstReceiptExchangeRateReasonsV1.Unreadable);
            var sources = new Dictionary<Guid, InventoryFirstReceiptRateSourceV1>();
            foreach (var parent in parents)
            {
                var share = ExactCostRatio.Of(parent.Input.QuantityKg).Divide(weighted!.QuantityKg);
                foreach (var source in parent.Rate.Sources)
                {
                    var actualShare = source.MaterialShare.Value().Multiply(share);
                    if (sources.TryGetValue(source.Entry.MovementId, out var existing))
                    {
                        if (existing.Entry != source.Entry || existing.Snapshot != source.Snapshot) return Unreadable(current);
                        actualShare = actualShare.Add(existing.MaterialShare.Value());
                    }
                    sources[source.Entry.MovementId] = source with { MaterialShare = InventoryExactCostRatioV1.From(actualShare) };
                    if (sources.Count > MaximumRows) return Unreadable(current);
                }
            }
            var versionEvidence = JsonSerializer.Serialize(new { transformation = header, inputs, outputs,
                parents = parents.Select(x => new { x.Input.LabelId, x.Rate.SourceVersion }), labelId = current }, Json);
            var version = "inventory.first-entry-lineage.v1/" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(versionEvidence))).ToLowerInvariant();
            return InventoryFirstReceiptExchangeRateV1.RatedExact(companyId, current, weighted!.SourceCurrencyId,
                weighted.TargetCurrencyId, InventoryExactCostRatioV1.From(weighted.Rate), version,
                sources.Values.OrderBy(x => x.Entry.MovementId).ToArray());
        }
    }

    private Task<FirstReceipt?> FirstReceiptAsync(Guid companyId, Guid labelId, CancellationToken token) =>
        (from line in dbContext.MovementLines.AsNoTracking()
         join movement in dbContext.Movements.AsNoTracking()
             on new { line.CompanyId, Id = line.MovementId } equals new { movement.CompanyId, movement.Id }
         where line.CompanyId == companyId && line.LabelId == labelId && line.Direction == (int)StockDirection.In
             && movement.Kind == (int)StockMovementKind.Receipt
         orderby movement.OccurredAtUtc, movement.RecordedAtUtc, movement.Id
         select new FirstReceipt(movement.Id, movement.OccurredAtUtc, movement.SourceKind, movement.SourceId,
             movement.CostCurrencyId, line.LineNumber, line.QuantityKg)).FirstOrDefaultAsync(token);

    private sealed record FirstReceipt(Guid MovementId, DateTimeOffset OccurredAtUtc, string SourceKind, Guid SourceId,
        Guid? CostCurrencyId, int LineNumber, decimal QuantityKg);
}
