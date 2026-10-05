using System.Globalization;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Quantities;

namespace GoymenERP.Modules.Purchasing.Application;

public sealed partial class PurchasingService
{
    public async Task<PurchaseGoodsReceiptQuantityComparisonV1> GetGoodsReceiptQuantityComparisonAsync(
        Guid orderId,
        Guid acceptanceId,
        CancellationToken cancellationToken = default)
    {
        var permission = bindings.ReadPermission;
        var scope = await RequireAsync(permission, cancellationToken);
        if (orderId == Guid.Empty || acceptanceId == Guid.Empty)
        {
            throw new PurchasingServiceException(404, "not_found");
        }

        var stored = await GuardedAsync(scope, permission, () => store.FindOrderAsync(orderId, cancellationToken), cancellationToken)
            ?? throw new PurchasingServiceException(404, "not_found");
        var acceptance = stored.Order.Acceptances.SingleOrDefault(candidate => candidate.AcceptanceId == acceptanceId)
            ?? throw new PurchasingServiceException(404, "not_found");
        var line = stored.Order.Lines.SingleOrDefault(candidate => candidate.Id == acceptance.LineId)
            ?? throw ReceiptSourceInvalid();
        var reads = receiptReads ?? throw ReceiptUnavailable();
        var receipt = await GuardedAsync(scope, permission,
            () => reads.GetReceiptAsync(orderId, acceptanceId, cancellationToken), cancellationToken)
            ?? throw ReceiptSourceInvalid();

        try
        {
            return PurchaseGoodsReceiptQuantityComparisonFactoryV1.Create(
                receipt,
                scope.CompanyId,
                orderId,
                acceptanceId,
                acceptance.LineId,
                line.ProductId,
                VersionOf(stored.ProductVersionsByLineId, line.Id),
                line.UnitId,
                Text(acceptance.Quantity),
                line.Quantity.Unit == QuantityUnit.Kilogram,
                stored.Order.Version.ToString(CultureInfo.InvariantCulture));
        }
        catch (ArgumentException)
        {
            throw ReceiptSourceInvalid();
        }
    }
}