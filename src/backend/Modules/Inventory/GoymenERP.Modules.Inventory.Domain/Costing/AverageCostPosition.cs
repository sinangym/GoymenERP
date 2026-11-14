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
    /// The average cannot be read (see <see cref="AverageCostUnreadableReason"/>). The state is sticky: the core never
    /// reopens it by itself (that would be a policy choice).
    /// </summary>
    Unreadable = 2,
}

/// <summary>
/// Why a position is unreadable. Each value is a case that has no figure (SINAN_KARARLARI_2026-11-10_SATIS_EK52 "Sınırlar";
/// SINAN_KARARLARI_2026-11-11_STOK_MALIYET_VE_TRANSFER for SAT-D301…D306); none of them is given a value here. SAT-D301 (2)
/// ("giriş gelince düzelir") is superseded by SAT-D323 (1) (SINAN_KARARLARI_2026-11-13_URETIM_MALIYETI_VE_EKSI_STOK); see
/// <see cref="AverageCostUnreadableReason.NegativeStock"/>. The numbers are persisted: never renumber.
/// </summary>
public enum AverageCostUnreadableReason
{
    /// <summary>
    /// OPEN-INV-AVGCOST-NEGATIVE-STOCK (Sinan decided: SAT-D318, SAT-D323): stock never goes below zero in any module
    /// (SAT-D318 (1)), so an issue beyond the physical stock before it is an invariant breach (SAT-D318 (4)). In this core
    /// the reason arises only then: <see cref="AverageCostUnreadableReason.LedgerStockMismatch"/> is checked first. No
    /// recovery-from-negative formula is written for any case (SAT-D323 (1)-(2)). Staying fail-closed (the cost is
    /// "hesaplanamadı": no value is made up, it is not counted as zero) and sticky (a receipt never reopens it) is the
    /// decision file's author reading (SAT-D318 (4), SAT-D323 (2)); the safety behaviour for a violated record is
    /// NEEDS-SINAN (that file's "Sınırlar").
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
    /// had a cost). The ledger cannot blend a quantity it never costed (SAT-D304: cost is "yok" until an approved opening
    /// cost exists; that command is a later slice).
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
