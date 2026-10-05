using System.Globalization;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>
/// Read-only comparison of the immutable commercial acceptance and Inventory weighbridge measurement.
/// A difference is exposed only when the persisted Purchasing line proves that its unit is kilograms.
/// This contract does not define a tolerance, approval, conversion, stock effect or quality decision.
/// </summary>
public sealed record PurchaseGoodsReceiptQuantityComparisonV1(
    Guid CompanyId,
    Guid AcceptanceId,
    Guid PurchaseOrderId,
    Guid PurchaseOrderLineId,
    string PurchaseOrderVersion,
    Guid ProductId,
    string ProductVersion,
    Guid UnitId,
    string AcceptedQuantity,
    string MeasuredQuantityKg,
    string ComparisonStatus,
    string? AcceptedQuantityKg,
    string? MeasuredMinusAcceptedKg);

public interface IPurchaseGoodsReceiptQuantityComparisonServiceV1
{
    Task<PurchaseGoodsReceiptQuantityComparisonV1> GetGoodsReceiptQuantityComparisonAsync(
        Guid orderId,
        Guid acceptanceId,
        CancellationToken cancellationToken = default);
}

public static class PurchaseGoodsReceiptQuantityComparisonStatusesV1
{
    public const string SameKilogramUnit = "SameKilogramUnit";
    public const string ApprovedConversion = "ApprovedConversion";
    public const string Unavailable = "Unavailable";
}

/// <summary>
/// Builds the projection only after matching the Inventory receipt to immutable Purchasing owner facts.
/// ApprovedConversion is intentionally not emitted until an accepted, versioned conversion owner exists.
/// </summary>
public static class PurchaseGoodsReceiptQuantityComparisonFactoryV1
{
    public static PurchaseGoodsReceiptQuantityComparisonV1 Create(
        PurchaseGoodsReceiptViewV1 receipt,
        Guid storedCompanyId,
        Guid storedOrderId,
        Guid storedAcceptanceId,
        Guid storedLineId,
        Guid storedProductId,
        string storedProductVersion,
        Guid storedUnitId,
        string storedAcceptedQuantity,
        bool storedUnitIsKilogram,
        string currentPurchaseOrderVersion)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        if (storedCompanyId == Guid.Empty || storedOrderId == Guid.Empty || storedAcceptanceId == Guid.Empty
            || storedLineId == Guid.Empty || storedProductId == Guid.Empty || storedUnitId == Guid.Empty
            || receipt.CompanyId != storedCompanyId || receipt.PurchaseOrderId != storedOrderId
            || receipt.AcceptanceId != storedAcceptanceId || receipt.PurchaseOrderLineId != storedLineId
            || receipt.ProductId != storedProductId || receipt.ProductVersion != storedProductVersion
            || receipt.UnitId != storedUnitId || receipt.AcceptedQuantity != storedAcceptedQuantity
            || !TryCanonicalPositive(receipt.AcceptedQuantity, out var accepted)
            || !TryCanonicalPositive(receipt.MeasuredQuantityKg, out var measured)
            || !TryVersion(receipt.PurchaseOrderVersion, out var receiptVersion)
            || !TryVersion(currentPurchaseOrderVersion, out var currentVersion)
            || receiptVersion > currentVersion)
        {
            throw new ArgumentException("Receipt comparison source facts are inconsistent.", nameof(receipt));
        }

        if (!storedUnitIsKilogram)
        {
            return View(receipt, PurchaseGoodsReceiptQuantityComparisonStatusesV1.Unavailable, null, null);
        }

        return View(
            receipt,
            PurchaseGoodsReceiptQuantityComparisonStatusesV1.SameKilogramUnit,
            Canonical(accepted),
            Canonical(measured - accepted));
    }

    private static PurchaseGoodsReceiptQuantityComparisonV1 View(
        PurchaseGoodsReceiptViewV1 receipt,
        string status,
        string? acceptedQuantityKg,
        string? measuredMinusAcceptedKg) =>
        new(
            receipt.CompanyId,
            receipt.AcceptanceId,
            receipt.PurchaseOrderId,
            receipt.PurchaseOrderLineId,
            receipt.PurchaseOrderVersion,
            receipt.ProductId,
            receipt.ProductVersion,
            receipt.UnitId,
            receipt.AcceptedQuantity,
            receipt.MeasuredQuantityKg,
            status,
            acceptedQuantityKg,
            measuredMinusAcceptedKg);

    private static bool TryCanonicalPositive(string value, out decimal parsed) =>
        decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out parsed)
        && parsed > 0m
        && string.Equals(value, Canonical(parsed), StringComparison.Ordinal);

    private static bool TryVersion(string value, out long parsed) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed)
        && parsed > 0
        && string.Equals(value, parsed.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private static string Canonical(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);
}