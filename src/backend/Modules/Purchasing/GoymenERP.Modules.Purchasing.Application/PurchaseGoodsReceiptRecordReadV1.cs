using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Modules.Purchasing.Application;

/// <summary>
/// YOL-D42: finds the acceptance's order in Purchasing's own company-filtered rows, then answers with the authorized receipt
/// read. An acceptance that is not found is still sent through that read with an empty order id, so the permission is decided
/// first and an unauthorized caller gets the same 403 whether or not the receipt exists (nothing about the record leaks).
/// </summary>
public sealed class PurchaseGoodsReceiptRecordReadV1(IPurchaseGoodsReceiptServiceV1 receipts, IPurchaseAcceptanceOrderLookupV1 lookup)
    : IPurchaseGoodsReceiptRecordReadPortV1
{
    public async Task<PurchaseGoodsReceiptRecordRefV1> ReadByAcceptanceAsync(Guid acceptanceId, CancellationToken cancellationToken = default)
    {
        var orderId = acceptanceId == Guid.Empty ? null : await lookup.FindOrderIdAsync(acceptanceId, cancellationToken);
        var detail = await receipts.GetGoodsReceiptAsync(orderId ?? Guid.Empty, acceptanceId, cancellationToken);
        var receipt = detail.Receipt;
        if (orderId is null || receipt.AcceptanceId != acceptanceId || receipt.PurchaseOrderId != orderId)
            throw new PurchasingServiceException(404, "not_found");
        return new PurchaseGoodsReceiptRecordRefV1(receipt.CompanyId, receipt.PurchaseOrderId, receipt.AcceptanceId);
    }
}
