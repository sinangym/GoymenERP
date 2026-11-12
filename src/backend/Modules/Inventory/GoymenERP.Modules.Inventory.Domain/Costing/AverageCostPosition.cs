namespace GoymenERP.Modules.Inventory.Domain.Costing;

/// <summary>
/// Moving-average purchase cost (SAT-D262, SAT-D265): the internal precision is exactly 4 decimals and every rounding
/// goes through the existing money rule, <see cref="MidpointRounding.AwayFromZero"/>. Rounding is applied once, to the
/// recalculated average; display rounding (cent, 0.01) belongs to the Sales margin calculator, never to this core.
/// </summary>
public static class AverageCostPrecision
{
    /// <summary>SAT-D265 (2): the internal scale of the moving-average cost.</summary>
    public const int InternalScale = 4;

    public static decimal Round(decimal value) => Math.Round(value, InternalScale, MidpointRounding.AwayFromZero);

    public static bool IsAtInternalScale(decimal value) => Round(value) == value;
}

public enum AverageCostStatus
{
    /// <summary>The ledger tracked the position's purchase receipts: quantity and average mean something.</summary>
    Known = 1,

    /// <summary>
    /// The average cannot be read (see <see cref="AverageCostUnreadableReason"/>). The state is sticky, except
    /// <see cref="AverageCostUnreadableReason.NegativeStock"/>, which a later receipt recovers (SAT-D301 (2), see
    /// <see cref="AverageCostCalculator.Apply"/>).
    /// </summary>
    Unreadable = 2,
}

/// <summary>
/// Why a position is unreadable. Each value is a case that has no figure (SINAN_KARARLARI_2026-11-10_SATIS_EK52 "Sınırlar";
/// SINAN_KARARLARI_2026-11-11_STOK_MALIYET_VE_TRANSFER for SAT-D301…D306); none of them is given a value here. The numbers
/// are persisted: never renumber.
/// </summary>
public enum AverageCostUnreadableReason
{
    /// <summary>
    /// SAT-D301 (Sinan decided, OPEN-INV-AVGCOST-NEGATIVE-STOCK): stock would go below zero, so the cost is "hesaplanamadı".
    /// Not permanent: a later receipt recovers the position (<see cref="AverageCostCalculator.Apply"/>).
    /// </summary>
    NegativeStock = 1,

    /// <summary>OPEN-INV-AVGCOST-UNIT-CONVERSION (NEEDS-SINAN): the cost is not per stock kilogram.</summary>
    UnitConversion = 2,

    /// <summary>OPEN-INV-AVGCOST-FX (NEEDS-SINAN): rate and date for a foreign-currency cost are not decided.</summary>
    ForeignCurrency = 3,

    /// <summary>
    /// SAT-D302 (Sinan decided, OPEN-INV-AVGCOST-LANDED): additional cost does not enter the v1 average, so the v1 core
    /// never produces this reason. The number stays reserved for the later setting (ALS-D80); never reuse it.
    /// </summary>
    LandedOrAdditionalCost = 4,

    /// <summary>OPEN-INV-AVGCOST-RETURN (NEEDS-SINAN): effect of sales and purchase returns.</summary>
    Return = 5,

    /// <summary>OPEN-INV-AVGCOST-MIXED-SOURCE (NEEDS-SINAN, SAT-D258): produced stock is not part of the purchase average.</summary>
    ProductionSource = 6,

    /// <summary>A transfer's source average is unreadable or unknown, so the target cannot be costed from it.</summary>
    TransferSourceUnreadable = 7,

    /// <summary>
    /// The movement's physical stock before it differs from what the ledger tracked (for example opening stock that never
    /// had a cost). The ledger cannot blend a quantity it never costed (OPEN-INV-AVGCOST-UNCOSTED-STOCK, NEEDS-SINAN).
    /// </summary>
    LedgerStockMismatch = 8,

    /// <summary>
    /// A purchase receipt (or a transfer at a zero source average) carries a zero unit cost: Sinan 2026-11-11 decided a
    /// zero-cost receipt means "cost unknown" (Unreadable), never a zero that pulls the average down.
    /// </summary>
    ZeroCostReceipt = 9,
}

/// <summary>
/// The moving-average purchase cost of one (company, location, product) position, per stock kilogram (4 decimals,
/// company base currency). Unknown is never zero: an unreadable position carries a reason and no number.
/// </summary>
public sealed record AverageCostPosition
{
    private AverageCostPosition(
        AverageCostStatus status, decimal? quantityKg, decimal? averageCostPerKg, AverageCostUnreadableReason? reason)
    {
        Status = status;
        QuantityKg = quantityKg;
        AverageCostPerKg = averageCostPerKg;
        UnreadableReason = reason;
    }

    public AverageCostStatus Status { get; }

    /// <summary>The kilograms the average is weighted by; only when <see cref="AverageCostStatus.Known"/>.</summary>
    public decimal? QuantityKg { get; }

    public decimal? AverageCostPerKg { get; }

    public AverageCostUnreadableReason? UnreadableReason { get; }

    public static AverageCostPosition Known(decimal quantityKg, decimal averageCostPerKg)
    {
        if (quantityKg < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(quantityKg), "A known position never holds negative stock.");
        }

        if (averageCostPerKg < 0m || !AverageCostPrecision.IsAtInternalScale(averageCostPerKg))
        {
            throw new ArgumentOutOfRangeException(
                nameof(averageCostPerKg), "The average is non-negative with at most 4 decimals.");
        }

        return new(AverageCostStatus.Known, quantityKg, averageCostPerKg, null);
    }

    public static AverageCostPosition Unreadable(AverageCostUnreadableReason reason)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        return new(AverageCostStatus.Unreadable, null, null, reason);
    }
}
