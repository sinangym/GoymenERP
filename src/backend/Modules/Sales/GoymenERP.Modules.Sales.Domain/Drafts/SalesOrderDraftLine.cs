using GoymenERP.Modules.Sales.Domain.Quantities;

namespace GoymenERP.Modules.Sales.Domain.Drafts;

/// <summary>
/// One requested delivery slice of a line: a client-owned stable identity, the line's unit, a
/// positive quantity and a date-only requested delivery date. A slice is a plan, never a delivery.
/// </summary>
public sealed record DeliverySlice(Guid SliceId, Quantity Quantity, string QuantityText, DateOnly RequestedDeliveryDate);

/// <summary>
/// One line of a sales order draft: the product as the Product owner answered it, the order unit
/// as the Settings owner answered it, the exact ordered quantity, and the optional requested
/// delivery plan. Built only through <see cref="SalesOrderDraft.AddLine"/>, which is the single
/// authority for every rule; the checks kept here are a second guard that reports
/// <see cref="SalesRuleCodes.InternalInvariant"/>.
/// </summary>
public sealed class SalesOrderDraftLine
{
    private readonly List<DeliverySlice> slices;

    public SalesOrderDraftLine(Guid id, int lineNo, ProductSnapshot product, UnitProfileSnapshot orderUnit,
        Quantity quantity, string quantityText, DateOnly? requestedDeliveryDate, string? note,
        CommercialDraftFields commercial, IReadOnlyList<DeliverySlice> slices)
    {
        Id = Identity.Require(id, nameof(id));
        LineNo = lineNo >= 1 ? lineNo : throw new ArgumentOutOfRangeException(nameof(lineNo));
        Product = product ?? throw new ArgumentNullException(nameof(product));
        OrderUnit = orderUnit ?? throw new ArgumentNullException(nameof(orderUnit));
        if (quantity.IsZero)
        {
            throw new SalesRuleException(SalesRuleCodes.InternalInvariant, "Satır miktarı sıfır olamaz (iç değişmez).");
        }

        Quantity = quantity;
        QuantityText = string.IsNullOrWhiteSpace(quantityText)
            ? throw new ArgumentException("Miktar metni boş olamaz.", nameof(quantityText))
            : quantityText;
        RequestedDeliveryDate = requestedDeliveryDate;
        Note = note;
        Commercial = commercial ?? throw new ArgumentNullException(nameof(commercial));
        ArgumentNullException.ThrowIfNull(slices);
        this.slices = [.. slices];
        var planned = this.slices.Aggregate(Quantity.Zero(quantity.Unit), (sum, slice) => sum.Add(slice.Quantity));
        if (planned.IsGreaterThan(quantity))
        {
            throw new SalesRuleException(SalesRuleCodes.InternalInvariant, "Dilimler satır miktarını aşıyor (iç değişmez).");
        }
    }

    /// <summary>The client-owned line identity, generated once when the line was added and kept across retries (#257, R3).</summary>
    public Guid Id { get; }

    public int LineNo { get; }

    public ProductSnapshot Product { get; }

    public UnitProfileSnapshot OrderUnit { get; }

    /// <summary>The exact ordered quantity in the order unit's dimension; extra decimals were refused, never rounded.</summary>
    public Quantity Quantity { get; }

    /// <summary>The quantity text exactly as the client sent it (#257: lossless text retained verbatim).</summary>
    public string QuantityText { get; }

    /// <summary>The line's requested delivery date; DEC-SAL-003 lets a delivery-schedule update move it (dates only).</summary>
    public DateOnly? RequestedDeliveryDate { get; private set; }

    public string? Note { get; }

    public CommercialDraftFields Commercial { get; private set; }

    /// <summary>The aggregate's commercial revision replaces the fields; the rules were applied by the aggregate first.</summary>
    internal void ReplaceCommercial(CommercialDraftFields commercial) =>
        Commercial = commercial ?? throw new ArgumentNullException(nameof(commercial));

    /// <summary>The requested delivery plan; empty means no slices planned, which is not a delivery.</summary>
    public IReadOnlyList<DeliverySlice> RequestedDeliverySlices => slices.AsReadOnly();

    /// <summary>
    /// DEC-SAL-003 (dates only): the line's date and the dates of its CURRENT slices, keyed by slice id. The caller (the
    /// aggregate) has already checked that <paramref name="sliceDates"/> names exactly this line's slices; the slice ids,
    /// order, quantities and texts stay as they were, so the plan's quantities are not re-split.
    /// </summary>
    internal void Reschedule(DateOnly? requestedDeliveryDate, IReadOnlyDictionary<Guid, DateOnly> sliceDates)
    {
        ArgumentNullException.ThrowIfNull(sliceDates);
        if (sliceDates.Count != slices.Count || slices.Any(slice => !sliceDates.ContainsKey(slice.SliceId)))
        {
            throw new SalesRuleException(SalesRuleCodes.InternalInvariant, "Termin dilimleri satırın dilimleriyle uyuşmuyor (iç değişmez).");
        }

        RequestedDeliveryDate = requestedDeliveryDate;
        for (var index = 0; index < slices.Count; index++)
        {
            slices[index] = slices[index] with { RequestedDeliveryDate = sliceDates[slices[index].SliceId] };
        }
    }

    /// <summary>What the plan does not cover: the ordered quantity minus the slices (never negative, never "delivered").</summary>
    public Quantity UnplannedQuantity =>
        slices.Aggregate(Quantity, (left, slice) => left.Subtract(slice.Quantity));
}
