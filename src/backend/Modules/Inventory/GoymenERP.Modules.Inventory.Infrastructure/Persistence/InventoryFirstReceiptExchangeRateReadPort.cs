using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Movements;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// FNS-D77/D78 (STK-SC36-b): the Inventory-owned answer to <see cref="IInventoryFirstReceiptExchangeRateReadPortV1"/>. It reads
/// only under the server-resolved company (a different <c>companyId</c> argument reads as NotFound, never as another company's
/// label), untracked, and writes nothing.
/// <para>
/// No Inventory or Purchasing receipt records an approved exchange rate today, so a goods-acceptance first entry answers
/// <see cref="InventoryFirstReceiptExchangeRateReasonsV1.RateNotRecorded"/>: the port is dry (fail-closed "hesaplanamadı") until
/// the recorded rate and its recorder are decided. It never borrows the invoice rate, the bulletin rate or the current rate.
/// </para>
/// </summary>
internal sealed class InventoryFirstReceiptExchangeRateReadPort(InventoryDbContext dbContext, ICompanyContext companyContext)
    : IInventoryFirstReceiptExchangeRateReadPortV1
{
    /// <summary>
    /// The longest split chain followed before the answer is read as unreadable. A bound keeps a corrupt cyclic lineage from
    /// looping; real split chains are far shorter.
    /// </summary>
    internal const int MaximumLineageDepth = 64;

    public async Task<InventoryFirstReceiptExchangeRateV1> GetAsync(
        Guid companyId, Guid labelId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            throw new InvalidOperationException("İlk mal kabul kuru yalnız çözümlenmiş şirket bağlamında okunur.");
        }

        if (companyId == Guid.Empty || labelId == Guid.Empty)
        {
            throw new ArgumentException("Şirket ve etiket kimlikleri boş olamaz.", companyId == Guid.Empty ? nameof(companyId) : nameof(labelId));
        }

        if (companyId != companyContext.CompanyId
            || !await dbContext.Labels.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == labelId, cancellationToken))
        {
            return InventoryFirstReceiptExchangeRateV1.NotFound(companyId, labelId);
        }

        var visited = new HashSet<Guid>();
        var current = labelId;
        for (var depth = 0; depth <= MaximumLineageDepth; depth++)
        {
            if (!visited.Add(current))
            {
                return InventoryFirstReceiptExchangeRateV1.Unavailable(companyId, labelId, null,
                    InventoryFirstReceiptExchangeRateReasonsV1.Unreadable);
            }

            var entry = await FirstReceiptAsync(companyId, current, cancellationToken);
            if (entry is not null)
            {
                var isGoodsAcceptance = string.Equals(entry.SourceKind, InventoryGoodsReceiptContractV1.SourceKind, StringComparison.Ordinal);
                var answered = new InventoryFirstReceiptEntryV1(current, entry.MovementId, entry.OccurredAtUtc.ToUniversalTime(),
                    isGoodsAcceptance ? entry.SourceId : null, entry.CostCurrencyId);
                return InventoryFirstReceiptExchangeRateV1.Unavailable(companyId, labelId, answered,
                    isGoodsAcceptance
                        ? InventoryFirstReceiptExchangeRateReasonsV1.RateNotRecorded
                        : InventoryFirstReceiptExchangeRateReasonsV1.FirstEntryNotGoodsReceipt);
            }

            var parents = await ParentLabelsAsync(companyId, current, cancellationToken);
            if (parents.Count == 0)
            {
                return InventoryFirstReceiptExchangeRateV1.Unavailable(companyId, labelId, null,
                    InventoryFirstReceiptExchangeRateReasonsV1.NoStockEntry);
            }

            if (parents.Count > 1)
            {
                return InventoryFirstReceiptExchangeRateV1.Unavailable(companyId, labelId, null,
                    InventoryFirstReceiptExchangeRateReasonsV1.LineageAmbiguous);
            }

            current = parents[0];
        }

        return InventoryFirstReceiptExchangeRateV1.Unavailable(companyId, labelId, null,
            InventoryFirstReceiptExchangeRateReasonsV1.Unreadable);
    }

    private async Task<FirstReceipt?> FirstReceiptAsync(Guid companyId, Guid labelId, CancellationToken cancellationToken)
    {
        var receiptKind = (int)StockMovementKind.Receipt;
        var inbound = (int)StockDirection.In;
        return await (
                from line in dbContext.MovementLines.AsNoTracking()
                join movement in dbContext.Movements.AsNoTracking()
                    on new { line.CompanyId, Id = line.MovementId } equals new { movement.CompanyId, movement.Id }
                where line.CompanyId == companyId && line.LabelId == labelId && line.Direction == inbound
                    && movement.Kind == receiptKind
                orderby movement.OccurredAtUtc, movement.RecordedAtUtc, movement.Id
                select new FirstReceipt(movement.Id, movement.OccurredAtUtc, movement.SourceKind, movement.SourceId,
                    movement.CostCurrencyId))
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// The distinct input labels of every transformation that created <paramref name="labelId"/>. A split answers one parent; a
    /// merge or manufacturing edge answers several (ambiguous for a single first receipt).
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ParentLabelsAsync(Guid companyId, Guid labelId, CancellationToken cancellationToken)
    {
        var transformationIds = dbContext.LabelTransformationOutputs.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.LabelId == labelId)
            .Select(x => x.TransformationId);
        var manufacturingKind = (int)InventoryLabelTransformationKind.Manufacturing;
        var manufacturing = await dbContext.LabelTransformations.AsNoTracking()
            .AnyAsync(x => x.CompanyId == companyId && transformationIds.Contains(x.Id) && x.Kind == manufacturingKind,
                cancellationToken);
        var parents = await dbContext.LabelTransformationInputs.AsNoTracking()
            .Where(x => x.CompanyId == companyId && transformationIds.Contains(x.TransformationId))
            .Select(x => x.LabelId)
            .Distinct()
            .Take(2)
            .ToListAsync(cancellationToken);

        // A manufacturing output is new material, not a relabelled receipt: with any number of inputs it never inherits a single
        // input's first receipt (FNS-D78 asks each input separately), so it reads as ambiguous rather than as a parent.
        return manufacturing && parents.Count > 0 ? [parents[0], Guid.Empty] : parents;
    }

    private sealed record FirstReceipt(Guid MovementId, DateTimeOffset OccurredAtUtc, string SourceKind, Guid SourceId,
        Guid? CostCurrencyId);
}
