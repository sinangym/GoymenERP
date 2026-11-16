using GoymenERP.BuildingBlocks.Monetary;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// BS-1 (SAT-D295): Inventory's own view of the supplier invoice's official rate for one goods receipt line. Inventory does not
/// reference Purchasing; the host adapts the Purchasing-owned invoice cost rate read port to this interface. The goods receipt
/// line is the receipt's source identity (<see cref="InventoryGoodsReceiptContractV1.SourceKind"/> plus the Purchasing acceptance
/// identity). Reading writes nothing.
/// <para>
/// The adapter is fail-closed: an owner failure other than cancellation answers
/// <see cref="InventoryPurchaseInvoiceCostRateStatusV1.Unavailable"/> with
/// <see cref="InventoryPurchaseInvoiceCostRateReasonsV1.Unreadable"/>, never a guessed rate; an unknown receipt stays
/// <see cref="InventoryPurchaseInvoiceCostRateStatusV1.NotFound"/>. Whether a rate is needed at all (the cost is already in the
/// cost currency) is Inventory's decision, made by <see cref="InventoryAverageCostFxRateResolverV1"/>, not the owner's.
/// </para>
/// </summary>
public interface IInventoryPurchaseInvoiceCostRateSourceV1
{
    Task<InventoryPurchaseInvoiceCostRateV1> ReadAsync(Guid companyId, Guid goodsReceiptLineId, CancellationToken cancellationToken = default);
}

/// <summary>The three answers of <see cref="IInventoryPurchaseInvoiceCostRateSourceV1"/>.</summary>
public enum InventoryPurchaseInvoiceCostRateStatusV1
{
    /// <summary>The supplier invoice is recorded and its official rate from the order currency is answered.</summary>
    Rate = 1,

    /// <summary>The receipt is found but no invoiced rate can be answered (BS-1: no provisional rate).</summary>
    Unavailable = 2,

    /// <summary>No such goods receipt line in the asked company.</summary>
    NotFound = 3,
}

/// <summary>Stable reason codes for <see cref="InventoryPurchaseInvoiceCostRateStatusV1.Unavailable"/>.</summary>
public static class InventoryPurchaseInvoiceCostRateReasonsV1
{
    /// <summary>No supplier invoice is recorded for the receipt yet (the owner's own reason, carried unchanged).</summary>
    public const string InvoiceNotRecorded = "purchase_invoice_not_recorded";

    /// <summary>The owner failed or answered something the adapter cannot trust; read as "no rate", fail-closed.</summary>
    public const string Unreadable = "purchase_invoice_rate_unreadable";

    public static IReadOnlyList<string> All { get; } = [InvoiceNotRecorded, Unreadable];
}

/// <summary>
/// One answer of <see cref="IInventoryPurchaseInvoiceCostRateSourceV1"/>. The company and the asked receipt are always echoed. Once
/// found, the purchase order and order line the receipt counts on are named (SAT-D295: the order line is the price input), with
/// the order's recorded currency (null when the order carries no money snapshot). <see cref="Rate"/> is set only for
/// <see cref="InventoryPurchaseInvoiceCostRateStatusV1.Rate"/>; <see cref="UnavailableReason"/> only for
/// <see cref="InventoryPurchaseInvoiceCostRateStatusV1.Unavailable"/>. Built only through its factories.
/// </summary>
public sealed record InventoryPurchaseInvoiceCostRateV1
{
    private InventoryPurchaseInvoiceCostRateV1(
        InventoryPurchaseInvoiceCostRateStatusV1 status,
        Guid companyId,
        Guid goodsReceiptLineId,
        Guid? purchaseOrderId,
        Guid? purchaseOrderLineId,
        Guid? orderCurrencyId,
        ExchangeRate? rate,
        string? unavailableReason)
    {
        Status = status;
        CompanyId = companyId;
        GoodsReceiptLineId = goodsReceiptLineId;
        PurchaseOrderId = purchaseOrderId;
        PurchaseOrderLineId = purchaseOrderLineId;
        OrderCurrencyId = orderCurrencyId;
        Rate = rate;
        UnavailableReason = unavailableReason;
    }

    public InventoryPurchaseInvoiceCostRateStatusV1 Status { get; }

    public Guid CompanyId { get; }

    public Guid GoodsReceiptLineId { get; }

    public Guid? PurchaseOrderId { get; }

    public Guid? PurchaseOrderLineId { get; }

    public Guid? OrderCurrencyId { get; }

    public ExchangeRate? Rate { get; }

    public string? UnavailableReason { get; }

    /// <summary>No such goods receipt line in <paramref name="companyId"/>.</summary>
    public static InventoryPurchaseInvoiceCostRateV1 NotFound(Guid companyId, Guid goodsReceiptLineId)
    {
        RequireCompany(companyId);
        return new(InventoryPurchaseInvoiceCostRateStatusV1.NotFound, companyId, goodsReceiptLineId, null, null, null, null, null);
    }

    /// <summary>
    /// No invoiced rate can be answered for <paramref name="reason"/>. The order and line are named when the owner named them; an
    /// unreadable owner answer may carry neither.
    /// </summary>
    public static InventoryPurchaseInvoiceCostRateV1 Unavailable(Guid companyId, Guid goodsReceiptLineId, Guid? purchaseOrderId,
        Guid? purchaseOrderLineId, Guid? orderCurrencyId, string reason)
    {
        RequireCompany(companyId);
        if (string.IsNullOrEmpty(reason) || !InventoryPurchaseInvoiceCostRateReasonsV1.All.Contains(reason, StringComparer.Ordinal))
        {
            throw new ArgumentException("Alış faturası kuru okunamama nedeni tanımsız.", nameof(reason));
        }

        if (purchaseOrderId == Guid.Empty || purchaseOrderLineId == Guid.Empty || orderCurrencyId == Guid.Empty
            || purchaseOrderId.HasValue != purchaseOrderLineId.HasValue)
        {
            throw new ArgumentException("Alış siparişi ve sipariş satırı kimlikleri birlikte verilir ve boş olamaz.", nameof(purchaseOrderId));
        }

        return new(InventoryPurchaseInvoiceCostRateStatusV1.Unavailable, companyId, goodsReceiptLineId, purchaseOrderId,
            purchaseOrderLineId, orderCurrencyId, null, reason);
    }

    /// <summary>The supplier invoice's official <paramref name="rate"/>, whose source must be the order's recorded currency.</summary>
    public static InventoryPurchaseInvoiceCostRateV1 Rated(Guid companyId, Guid goodsReceiptLineId, Guid purchaseOrderId,
        Guid purchaseOrderLineId, Guid orderCurrencyId, ExchangeRate rate)
    {
        RequireCompany(companyId);
        if (goodsReceiptLineId == Guid.Empty || purchaseOrderId == Guid.Empty || purchaseOrderLineId == Guid.Empty)
        {
            throw new ArgumentException("Mal kabul satırı, alış siparişi ve sipariş satırı kimlikleri boş olamaz.", nameof(goodsReceiptLineId));
        }

        if (orderCurrencyId == Guid.Empty || rate.SourceCurrencyId != orderCurrencyId || rate.TargetCurrencyId == Guid.Empty
            || rate.TargetCurrencyId == rate.SourceCurrencyId || rate.Rate <= 0m)
        {
            throw new ArgumentException("Alış faturası kuru sipariş para biriminden başka bir para birimine pozitif bir kur olmalıdır.",
                nameof(rate));
        }

        return new(InventoryPurchaseInvoiceCostRateStatusV1.Rate, companyId, goodsReceiptLineId, purchaseOrderId, purchaseOrderLineId,
            orderCurrencyId, rate, null);
    }

    private static void RequireCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Şirket kimliği boş olamaz.", nameof(companyId));
        }
    }
}
