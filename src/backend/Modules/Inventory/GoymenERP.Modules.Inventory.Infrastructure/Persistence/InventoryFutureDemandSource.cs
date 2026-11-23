using System.Globalization;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// F-017 STOK §3.2 steps 1 and 5 (F017-b): the Sales demand line as the future allocation needs it, read through Sales-declared
/// ports only (Inventory never reads Sales tables). The approved line comes from <see cref="ISalesOrderLineReferencePortV1"/>; its
/// open quantity is the line's shippable quantity (the approved quantity, or the re-approval shipping cap when smaller, SAT-D103/
/// D104/D111) minus what the delivery owner recorded as definitely shipped for the line (<see cref="ISalesOrderDeliveryReadPortV1"/>),
/// never below zero. A cancelled remainder is open 0. This reading of "açık miktar" is the author's (spec §3.2 step 5 "yazarın
/// okuması, geri alınabilir") and is reported as NEEDS-SINAN; it lives in this one adapter so it can change without touching the
/// writer. Fail-closed: a missing port, an unreadable answer, a shipment recorded in another unit or any exception is
/// <see cref="InventoryFutureDemandSourceOutcomeV1.Unavailable"/>, never an assumed zero. No authority; nothing is written.
/// </summary>
public sealed class InventoryFutureDemandSource(
    ISalesOrderLineReferencePortV1? salesLines = null,
    ISalesOrderDeliveryReadPortV1? deliveries = null) : IInventoryFutureDemandSourceV1
{
    private static readonly InventoryFutureDemandSourceAnswerV1 Unavailable =
        new(InventoryFutureDemandSourceOutcomeV1.Unavailable, null);

    public async Task<InventoryFutureDemandSourceAnswerV1> GetDemandLineAsync(Guid salesOrderId, Guid salesOrderLineId,
        CancellationToken cancellationToken = default)
    {
        if (salesLines is null || deliveries is null || salesOrderId == Guid.Empty || salesOrderLineId == Guid.Empty)
        {
            return Unavailable;
        }

        try
        {
            var answer = await salesLines.GetApprovedLineAsync(salesOrderId, salesOrderLineId, cancellationToken);
            switch (answer.Outcome)
            {
                case SalesOrderLineReferenceOutcomeV1.NotFound:
                    return new InventoryFutureDemandSourceAnswerV1(InventoryFutureDemandSourceOutcomeV1.NotFound, null);
                case SalesOrderLineReferenceOutcomeV1.NotApproved:
                    return new InventoryFutureDemandSourceAnswerV1(InventoryFutureDemandSourceOutcomeV1.NotApproved, null);
                case SalesOrderLineReferenceOutcomeV1.Approved:
                    break;
                default:
                    return Unavailable;
            }

            if (answer.Line is not { } line || line.OrderId != salesOrderId || line.LineId != salesOrderLineId
                || line.ProductId == Guid.Empty || string.IsNullOrEmpty(line.ProductVersion) || line.OrderUnitId == Guid.Empty)
            {
                return Unavailable;
            }

            decimal open;
            if (string.Equals(line.RemainderState, SalesRemainderChangeNamesV1.Cancelled, StringComparison.Ordinal))
            {
                open = 0m;
            }
            else if (string.Equals(line.RemainderState, SalesRemainderChangeNamesV1.Open, StringComparison.Ordinal)
                || string.Equals(line.RemainderState, SalesRemainderChangeNamesV1.Revised, StringComparison.Ordinal))
            {
                if (!TryQuantity(line.QuantityText, out var ordered))
                {
                    return Unavailable;
                }

                if (line.ShippingCapQuantityText is not null)
                {
                    if (!TryQuantity(line.ShippingCapQuantityText, out var cap))
                    {
                        return Unavailable;
                    }

                    ordered = Math.Min(ordered, cap);
                }

                var delivery = await deliveries.ReadOrderDeliveriesAsync(salesOrderId, cancellationToken);
                if (delivery is null || delivery.OrderId != salesOrderId || delivery.CompanyId != line.CompanyId
                    || delivery.Lines is null)
                {
                    return Unavailable;
                }

                var shipped = 0m;
                foreach (var entry in delivery.Lines.Where(item => item.OrderLineId == salesOrderLineId))
                {
                    if (entry.UnitId != line.OrderUnitId || entry.ShippedQuantity < 0m)
                    {
                        return Unavailable;
                    }

                    shipped += entry.ShippedQuantity;
                }

                open = Math.Max(0m, ordered - shipped);
            }
            else
            {
                return Unavailable;
            }

            return new InventoryFutureDemandSourceAnswerV1(InventoryFutureDemandSourceOutcomeV1.Open,
                new InventoryFutureDemandLineV1(line.OrderId, line.LineId, line.ProductId, line.ProductVersion, line.OrderUnitId,
                    open.ToString(CultureInfo.InvariantCulture)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Fail-closed: an owner that cannot answer reliably is "unavailable" (503 at the writer), never an assumed zero.
            return Unavailable;
        }
    }

    private static bool TryQuantity(string? text, out decimal value)
    {
        value = 0m;
        return !string.IsNullOrEmpty(text)
            && decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)
            && value >= 0m;
    }
}
