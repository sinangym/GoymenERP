using System.Globalization;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Quantities;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

/// <summary>
/// PUR-4a: the Purchasing-owned implementation of <see cref="IPurchaseSupplyReferencePortV1"/> over the
/// company-filtered store (the mirror of Sales's <c>SalesOrderLineReferencePort</c>). It reads under the
/// caller's server context — the consumer (INV-AL-1) measures its own authority before asking — and creates
/// nothing. An APPROVED order's line answers its remaining commitment (committed − accepted) as expected
/// supply, never as stock; a Draft order's line is <see cref="PurchaseSupplyReferenceOutcomeV1.NotApproved"/>
/// with the status name (Draft orders reserve nothing, ASSUMPTION-PUR4-2); another company's order is
/// invisible under the tenant filter and answers <see cref="PurchaseSupplyReferenceOutcomeV1.NotFound"/>, never 403.
/// </summary>
public sealed class PurchaseSupplyReferencePort(IPurchasingStore store) : IPurchaseSupplyReferencePortV1
{
    private static readonly PurchaseSupplyReferenceAnswerV1 NotFound = new PurchaseSupplyReferenceAnswerV1(PurchaseSupplyReferenceOutcomeV1.NotFound, null, null);

    public async Task<PurchaseSupplyReferenceAnswerV1> GetApprovedLineAsync(Guid orderId, Guid lineId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (orderId == Guid.Empty || lineId == Guid.Empty)
        {
            return NotFound;
        }

        var stored = await store.FindOrderAsync(orderId, cancellationToken);
        if (stored is null)
        {
            return NotFound;
        }

        var order = stored.Order;
        var index = -1;
        for (var candidate = 0; candidate < order.Lines.Count; candidate++)
        {
            if (order.Lines[candidate].Id == lineId)
            {
                index = candidate;
                break;
            }
        }

        if (index < 0)
        {
            return NotFound;
        }

        if (order.Status != PurchaseOrderStatus.Approved)
        {
            return new PurchaseSupplyReferenceAnswerV1(PurchaseSupplyReferenceOutcomeV1.NotApproved, null, order.Status.ToString());
        }

        var line = order.Lines[index];
        // Fail closed (FİNANS #352 INFO-1): a stored line without its answered product version is a record that cannot be restored, never an empty version on the wire.
        var productVersion = stored.ProductVersionsByLineId.TryGetValue(line.Id, out var version) && !string.IsNullOrWhiteSpace(version)
            ? version
            : throw new InvalidOperationException("Kayıtlı sipariş satırının ürün sürümü okunamadı.");
        return new PurchaseSupplyReferenceAnswerV1(PurchaseSupplyReferenceOutcomeV1.Approved, new PurchaseSupplyLineReferenceV1(
            order.CompanyId, order.Id, line.Id, index + 1, order.SupplierPartyId, stored.CompanyAccountId, order.SourceRequestId, line.SourceRequestLineId,
            line.ProductId, productVersion, line.UnitId, line.Kind.ToString(), Text(line.CommittedQuantity), Text(line.AcceptedQuantity),
            Text(line.RemainingQuantity), order.Version), null);
    }

    private static string Text(Quantity quantity) => quantity.Value.ToString(CultureInfo.InvariantCulture);
}
