using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

/// <summary>F17-1b / ALS-D81 / KLN-D30: read the owner's validated aggregate, without a second connection or write.</summary>
internal sealed class PurchaseFutureSupplyScheduleReadPort(IPurchasingStore store, ICompanyContext companyContext)
    : IPurchaseFutureSupplyScheduleReadPortV1
{
    public async Task<PurchaseFutureSupplyScheduleV1> GetLineScheduleAsync(Guid orderId, Guid lineId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var companyId = companyContext.IsResolved ? companyContext.CompanyId : Guid.Empty;
        PurchaseFutureSupplyScheduleV1 Missing(PurchaseFutureSupplyScheduleOutcomeV1 outcome) =>
            new(outcome, companyId, orderId, lineId, Guid.Empty, null, []);
        if (companyId == Guid.Empty || orderId == Guid.Empty || lineId == Guid.Empty)
        {
            return Missing(PurchaseFutureSupplyScheduleOutcomeV1.NotFound);
        }

        // FindOrderAsync restores the append-only revisions and validates schedule totals and ALS-D81 again.
        var stored = await store.FindOrderAsync(orderId, cancellationToken);
        if (!companyContext.IsResolved || companyContext.CompanyId != companyId)
        {
            return Missing(PurchaseFutureSupplyScheduleOutcomeV1.Unreadable);
        }

        if (stored is null)
        {
            return Missing(PurchaseFutureSupplyScheduleOutcomeV1.NotFound);
        }

        if (stored.Order.CompanyId != companyId || stored.Order.Id != orderId)
        {
            return Missing(PurchaseFutureSupplyScheduleOutcomeV1.Unreadable);
        }

        var line = stored.Order.Lines.SingleOrDefault(candidate => candidate.Id == lineId);
        if (line is null)
        {
            return Missing(PurchaseFutureSupplyScheduleOutcomeV1.NotFound);
        }

        // A technical line does not own a schedule; treating it as an undated general line would change release order.
        if (line.Kind != PurchaseOrderLineKind.General)
        {
            return Missing(PurchaseFutureSupplyScheduleOutcomeV1.Unreadable);
        }

        var accepted = line.AcceptedQuantity.Value + stored.Order.Lines
            .Where(candidate => candidate.ParentLineId == lineId && candidate.Kind == PurchaseOrderLineKind.Technical)
            .Sum(candidate => candidate.AcceptedQuantity.Value);
        if (accepted < 0m || accepted > line.Quantity.Value)
        {
            return Missing(PurchaseFutureSupplyScheduleOutcomeV1.Unreadable);
        }

        var slices = new List<PurchaseFutureSupplyScheduleSliceV1>();
        foreach (var slice in line.DeliverySchedule)
        {
            var received = Math.Min(accepted, slice.Quantity.Value);
            accepted -= received;
            slices.Add(new(slice.SliceId, slice.SliceNo, slice.DeliveryDate, Text(slice.Quantity.Value),
                Text(slice.Quantity.Value - received)));
        }

        return new(PurchaseFutureSupplyScheduleOutcomeV1.Found, companyId, orderId, lineId, line.UnitId,
            stored.Order.Version, slices.AsReadOnly());
    }

    private static string Text(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
}
