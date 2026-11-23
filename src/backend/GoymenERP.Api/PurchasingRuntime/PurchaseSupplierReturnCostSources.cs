using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

namespace GoymenERP.Api.PurchasingRuntime;

/// <summary>Read-only owner bridge. Only identifiers, invoice display labels and exact quantities leave this adapter.</summary>
internal sealed class PurchaseSupplierReturnCostSources(PurchaseSupplierReturnPurchaseSourceReader purchases,
    IInventorySupplierReturnCostSourceReadPortV1 inventory, IPurchasingIncomingInvoiceStoreV1 invoices)
    : IPurchaseSupplierReturnCostSourceReadPortV1
{
    public async Task<PurchaseSupplierReturnCostSourcesV1> GetAsync(Guid companyId, Guid orderId, Guid goodsAcceptanceId,
        CancellationToken cancellationToken = default)
    {
        var purchase = await purchases.ReadAsync(companyId, orderId, goodsAcceptanceId, cancellationToken: cancellationToken);
        InventorySupplierReturnCostSourcesV1 source;
        try { source = await inventory.GetAsync(companyId, goodsAcceptanceId, Map(purchase), cancellationToken); }
        catch (InventorySupplierReturnExitException exception)
        { throw new PurchasingServiceException(exception.Status, exception.Code, innerException: exception); }
        if (source.CompanyId != companyId || source.GoodsAcceptanceId != goodsAcceptanceId || source.LabelId == Guid.Empty)
            throw Unavailable();
        var rows = new List<PurchaseSupplierReturnInvoicedSourceV1>();
        foreach (var row in source.InvoicedSources)
        {
            var invoice = await invoices.FindIncomingInvoiceAsync(row.InvoiceId, cancellationToken) ?? throw Unavailable();
            var line = invoice.Lines.SingleOrDefault(candidate => candidate.Id == row.InvoiceLineId) ?? throw Unavailable();
            if (invoice.CompanyId != companyId || string.IsNullOrWhiteSpace(invoice.Source.OfficialNumber)) throw Unavailable();
            rows.Add(new(row.SourceEventId, row.InvoiceId, row.InvoiceLineId, invoice.Source.OfficialNumber, line.LineNo,
                Quantity(row.AvailableQuantityKg), Quantity(row.AvailableUndoQuantity), row.WholePairAvailable));
        }
        return new(companyId, goodsAcceptanceId, source.LabelId, source.SourceVersion, Quantity(source.PendingQuantityKg),
            Quantity(source.PendingUndoQuantity), rows, source.PendingWholePairAvailable);
    }

    internal static InventorySupplierReturnPurchaseSourceV1 Map(PurchaseSupplierReturnPurchaseSourceV1 source) =>
        new(source.AcceptedQuantity, source.SourceVersion, source.ReceiptAdjustments.Select(row =>
            new InventoryPurchaseInvoiceReceiptAdjustmentV1(row.AdjustmentId, row.UndoQuantity, row.IsSupplierReturn)).ToArray());
    internal static InventorySupplierReturnCostAllocationV1? Map(PurchaseSupplierReturnCostAllocationV1? source) => source is null ? null :
        new(source.ExpectedSourceVersion, new(source.Pending.TakeAll, source.Pending.QuantityKg, source.Pending.UndoQuantity),
            source.InvoicedSources.Select(row => new InventorySupplierReturnInvoicedSourceSelectionV1(row.SourceEventId,
                row.TakeAll, row.QuantityKg, row.UndoQuantity)).ToArray());
    private static PurchaseSupplierReturnExactQuantityV1 Quantity(InventoryExactCostRatioV1 value) => new(value.Numerator, value.Denominator);
    private static PurchasingServiceException Unavailable() => new(503, PurchaseSupplierReturnCodesV1.Unavailable);
}
