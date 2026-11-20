using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>The preparer's explicit attribution of returned kg to pending cost and recorded invoice cost sources.
/// This selects physical shares; it does not select a price or establish a blended-price policy.</summary>
public sealed record PurchaseSupplierReturnCostAllocationV1(string ExpectedSourceVersion, PurchaseSupplierReturnSourceSelectionV1 Pending,
    IReadOnlyList<PurchaseSupplierReturnInvoicedSourceSelectionV1> InvoicedSources);

public sealed record PurchaseSupplierReturnSourceSelectionV1(bool TakeAll, string? QuantityKg, string? UndoQuantity);
public sealed record PurchaseSupplierReturnInvoicedSourceSelectionV1(Guid SourceEventId, bool TakeAll, string? QuantityKg, string? UndoQuantity);

/// <summary>Exact available kg; the numerator/denominator are not money and must not be rounded into an available share.</summary>
public sealed record PurchaseSupplierReturnExactQuantityV1(string Numerator, string Denominator);

public sealed record PurchaseSupplierReturnInvoicedSourceV1(Guid SourceEventId, Guid InvoiceId, Guid InvoiceLineId,
    string InvoiceNumber, int InvoiceLineNo, PurchaseSupplierReturnExactQuantityV1 AvailableQuantityKg,
    PurchaseSupplierReturnExactQuantityV1 AvailableUndoQuantity, bool WholePairAvailable = false);

/// <summary>Safe invoice labels frozen with the preparer's selected sources, also readable by the exception approver.</summary>
public sealed record PurchaseSupplierReturnInvoiceSourceLabelV1(Guid SourceEventId, Guid InvoiceId, Guid InvoiceLineId,
    string InvoiceNumber, int InvoiceLineNo);

public sealed record PurchaseSupplierReturnCostSourcesV1(Guid CompanyId, Guid GoodsAcceptanceId, Guid LabelId,
    string SourceVersion, PurchaseSupplierReturnExactQuantityV1 PendingQuantityKg, PurchaseSupplierReturnExactQuantityV1 PendingUndoQuantity,
    IReadOnlyList<PurchaseSupplierReturnInvoicedSourceV1> InvoicedSources, bool PendingWholePairAvailable = false);

public interface IPurchaseSupplierReturnCostSourceReadPortV1
{
    Task<PurchaseSupplierReturnCostSourcesV1> GetAsync(Guid companyId, Guid orderId, Guid goodsAcceptanceId,
        CancellationToken cancellationToken = default);
}

public static class PurchaseSupplierReturnCostAllocationContractV1
{
    public const string Required = "purchasing_supplier_return_cost_allocation_required";
    public const string Stale = "purchasing_supplier_return_cost_allocation_stale";
    public const string ExceedsSource = "purchasing_supplier_return_cost_allocation_exceeds_source";

    public static void Validate(PurchaseSupplierReturnCostAllocationV1 allocation, string quantityKg, string? undoQuantity)
    {
        if (allocation is null || string.IsNullOrWhiteSpace(allocation.ExpectedSourceVersion)
            || allocation.ExpectedSourceVersion.Length > 128 || allocation.ExpectedSourceVersion != allocation.ExpectedSourceVersion.Trim()
            || allocation.Pending is null || !ValidSelection(allocation.Pending.TakeAll, allocation.Pending.QuantityKg, allocation.Pending.UndoQuantity, true)
            || allocation.InvoicedSources is null
            || allocation.InvoicedSources.Count > 1000 || allocation.InvoicedSources.Any(row => row is null
                || row.SourceEventId == Guid.Empty || !ValidSelection(row.TakeAll, row.QuantityKg, row.UndoQuantity, false))
            || allocation.InvoicedSources.Select(row => row.SourceEventId).Distinct().Count() != allocation.InvoicedSources.Count
            || !PurchaseSupplierReturnJsonV1.IsPositiveQuantity(quantityKg)
            || !PurchaseSupplierReturnJsonV1.IsPositiveQuantity(undoQuantity)) throw new JsonException("Invalid return cost allocation.");
        if (!allocation.Pending.TakeAll && allocation.InvoicedSources.All(row => !row.TakeAll))
        {
            var kg = Decimal(allocation.Pending.QuantityKg!);
            var quantity = Decimal(allocation.Pending.UndoQuantity!);
            foreach (var row in allocation.InvoicedSources)
            { kg = checked(kg + Decimal(row.QuantityKg!)); quantity = checked(quantity + Decimal(row.UndoQuantity!)); }
            if (kg != Decimal(quantityKg) || quantity != Decimal(undoQuantity!))
                throw new JsonException("Return source shares must equal both independently measured return quantities.");
        }
    }

    public static bool HasPositiveQuantity(PurchaseSupplierReturnExactQuantityV1 quantity) =>
        ReadRatio(quantity).Numerator > BigInteger.Zero;

    public static void ValidateAvailability(PurchaseSupplierReturnCostAllocationV1 allocation, string kg, string undo,
        PurchaseSupplierReturnCostSourcesV1 source)
    {
        if (allocation.ExpectedSourceVersion != source.SourceVersion) throw Refused(Stale);
        var selectedKg = Select(allocation.Pending.TakeAll, allocation.Pending.QuantityKg, source.PendingQuantityKg);
        var selectedUndo = SelectUndo(allocation.Pending.TakeAll, allocation.Pending.UndoQuantity, source.PendingUndoQuantity,
            source.PendingWholePairAvailable);
        RequireNoOrphan(selectedKg, selectedUndo, source.PendingQuantityKg, source.PendingUndoQuantity, source.PendingWholePairAvailable);
        foreach (var selected in allocation.InvoicedSources)
        {
            var available = source.InvoicedSources.SingleOrDefault(row => row.SourceEventId == selected.SourceEventId)
                ?? throw Refused(ExceedsSource);
            if (!HasPositiveQuantity(available.AvailableQuantityKg) || !HasPositiveQuantity(available.AvailableUndoQuantity))
                throw Refused(ExceedsSource);
            var invoiceKg = Select(selected.TakeAll, selected.QuantityKg, available.AvailableQuantityKg);
            var invoiceUndo = SelectUndo(selected.TakeAll, selected.UndoQuantity, available.AvailableUndoQuantity, available.WholePairAvailable);
            RequireNoOrphan(invoiceKg, invoiceUndo, available.AvailableQuantityKg, available.AvailableUndoQuantity, available.WholePairAvailable);
            selectedKg = Add(selectedKg, invoiceKg);
            selectedUndo = Add(selectedUndo, invoiceUndo);
        }
        if (Compare(selectedKg, Ratio(kg)) != 0 || Compare(selectedUndo, Ratio(undo)) != 0) throw Refused(ExceedsSource);
    }

    private static (BigInteger Numerator, BigInteger Denominator) Select(bool all, string? requested, PurchaseSupplierReturnExactQuantityV1 available)
    {
        var remaining = ReadRatio(available);
        var selected = all ? remaining : Ratio(requested!);
        if (Compare(selected, remaining) > 0) throw Refused(ExceedsSource);
        return selected;
    }

    private static (BigInteger Numerator, BigInteger Denominator) SelectUndo(bool all, string? requested,
        PurchaseSupplierReturnExactQuantityV1 available, bool wholePairAvailable)
    {
        if (all && requested is null && !wholePairAvailable) throw Refused(ExceedsSource);
        return Select(all && requested is null, requested, available);
    }

    private static void RequireNoOrphan((BigInteger Numerator, BigInteger Denominator) selectedKg,
        (BigInteger Numerator, BigInteger Denominator) selectedUndo, PurchaseSupplierReturnExactQuantityV1 availableKg,
        PurchaseSupplierReturnExactQuantityV1 availableUndo, bool wholePairAvailable)
    {
        var exhaustsKg = Compare(selectedKg, ReadRatio(availableKg)) == 0;
        var exhaustsUndo = Compare(selectedUndo, ReadRatio(availableUndo)) == 0;
        // After shipment root kg exceeds available stock kg. Its independent purchase quantity cannot be exhausted by
        // returning only the still-physical share. No surviving purchase quantity is inferred from a kg fraction.
        if (selectedKg.Numerator.IsZero != selectedUndo.Numerator.IsZero
            || (wholePairAvailable ? exhaustsKg != exhaustsUndo : exhaustsUndo)) throw Refused(ExceedsSource);
    }

    private static (BigInteger Numerator, BigInteger Denominator) Ratio(string value)
    {
        var bits = decimal.GetBits(Decimal(value));
        return ((new BigInteger((uint)bits[2]) << 64) | (new BigInteger((uint)bits[1]) << 32) | (uint)bits[0],
            BigInteger.Pow(10, (bits[3] >> 16) & 0xff));
    }

    private static (BigInteger Numerator, BigInteger Denominator) Add((BigInteger Numerator, BigInteger Denominator) left,
        (BigInteger Numerator, BigInteger Denominator) right)
    {
        var numerator = left.Numerator * right.Denominator + right.Numerator * left.Denominator;
        var denominator = left.Denominator * right.Denominator;
        var divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        return (numerator / divisor, denominator / divisor);
    }
    private static int Compare((BigInteger Numerator, BigInteger Denominator) left, (BigInteger Numerator, BigInteger Denominator) right) =>
        (left.Numerator * right.Denominator).CompareTo(right.Numerator * left.Denominator);

    private static (BigInteger Numerator, BigInteger Denominator) ReadRatio(PurchaseSupplierReturnExactQuantityV1 quantity)
    {
        if (quantity is null || quantity.Numerator is null || quantity.Denominator is null
            || quantity.Numerator.Length > 512 || quantity.Denominator.Length > 512
            || !BigInteger.TryParse(quantity.Numerator, NumberStyles.None, CultureInfo.InvariantCulture, out var numerator)
            || !BigInteger.TryParse(quantity.Denominator, NumberStyles.None, CultureInfo.InvariantCulture, out var denominator)
            || numerator < 0 || denominator <= 0
            || numerator.ToString(CultureInfo.InvariantCulture) != quantity.Numerator
            || denominator.ToString(CultureInfo.InvariantCulture) != quantity.Denominator)
            throw new PurchasingServiceException(503, PurchaseSupplierReturnCodesV1.Unavailable);
        return (numerator, denominator);
    }

    private static bool NonnegativeKg(string? quantity) => quantity == "0" || PurchaseSupplierReturnJsonV1.IsPositiveQuantity(quantity);
    private static bool ValidSelection(bool all, string? kg, string? undo, bool allowZero) => all
        ? kg is null && (undo is null || PurchaseSupplierReturnJsonV1.IsPositiveQuantity(undo))
        : allowZero ? NonnegativeKg(kg) && NonnegativeKg(undo) && (kg == "0") == (undo == "0")
        : PurchaseSupplierReturnJsonV1.IsPositiveQuantity(kg) && PurchaseSupplierReturnJsonV1.IsPositiveQuantity(undo);
    private static decimal Decimal(string value) => decimal.Parse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    private static PurchasingServiceException Refused(string code) => new(409, code, [new("costAllocation", code)]);
}
