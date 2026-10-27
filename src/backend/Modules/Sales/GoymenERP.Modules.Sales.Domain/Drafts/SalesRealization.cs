namespace GoymenERP.Modules.Sales.Domain.Drafts;

/// <summary>
/// SAT-D5 / SAT-D15: the delivery tolerance percents a realization is judged with, relative to the ordered quantity, with
/// 0 ≤ warn ≤ approval ≤ block ≤ 100. The values are never a code constant: the caller takes them from the Settings Sales
/// policy record (<c>ISettingsSalesPolicyProviderV1</c>) and this type only refuses a set that is not a valid ladder.
/// </summary>
public sealed record SalesDeliveryTolerance
{
    /// <summary>The percent base the ladder is expressed in (a deviation of the whole ordered quantity); not a policy value.</summary>
    public const decimal PercentBase = 100m;

    public SalesDeliveryTolerance(decimal warnPercent, decimal approvalPercent, decimal blockPercent)
    {
        if (warnPercent < 0m || warnPercent > approvalPercent || approvalPercent > blockPercent || blockPercent > PercentBase)
        {
            throw new ArgumentException("Teslim toleransı 0 ≤ uyarı ≤ onay ≤ blok ≤ 100 olmalıdır.");
        }

        WarnPercent = warnPercent;
        ApprovalPercent = approvalPercent;
        BlockPercent = blockPercent;
    }

    public decimal WarnPercent { get; }
    public decimal ApprovalPercent { get; }
    public decimal BlockPercent { get; }
}

/// <summary>SAT-D15 bands: a bound value falls into the band it starts (the K4 <c>AtOrAbove</c> rule).</summary>
public enum DeliveryToleranceBand
{
    /// <summary>deviation &lt; warn: within tolerance.</summary>
    Within = 1,

    /// <summary>warn ≤ deviation &lt; approval.</summary>
    Warn = 2,

    /// <summary>approval ≤ deviation &lt; block.</summary>
    Approval = 3,

    /// <summary>deviation ≥ block.</summary>
    Block = 4,
}

/// <summary>Whether the line's shipped total is below, equal to or above its ordered quantity.</summary>
public enum DeliveryDeviationDirection
{
    None = 1,
    Under = 2,
    Over = 3,
}

/// <summary>
/// One order line on the realization axis (DEC-SAL-001; DURUM_MAKINELERI "Gerçekleşme: Başlamadı / Kısmi / Tamamlandı").
/// <see cref="OutsideTolerance"/> and <see cref="ClosedWithRemainder"/> are the two cases the three-word vocabulary does not
/// name: they are shown as what they are, never folded into "Tamamlandı".
/// </summary>
public enum LineRealizationState
{
    /// <summary>Nothing shipped on an open line.</summary>
    NotStarted = 1,

    /// <summary>Shipped below the ordered quantity, outside the tolerance, line still open: the open remainder is not a shortfall (SAT-D16).</summary>
    Partial = 2,

    /// <summary>The shipped total is within the tolerance of the ordered quantity, over or under (SAT-D5: "Tamamlandı").</summary>
    Completed = 3,

    /// <summary>Shipped ABOVE the ordered quantity in the warn, approval or block band (over-delivery is measured on every shipment).</summary>
    OutsideTolerance = 4,

    /// <summary>
    /// The order was force-closed (No 30 / SAT-D12) and this line's shipped total is below the ordered quantity outside the
    /// tolerance: the rest is the closed ("iptal edilen") remainder, not a completion.
    /// </summary>
    ClosedWithRemainder = 5,
}

/// <summary>One line's realization: its state, the direction and exact size of the deviation, and the band (null when not measured).</summary>
public sealed record LineRealization(
    LineRealizationState State,
    DeliveryDeviationDirection Direction,
    Quantity Deviation,
    DeliveryToleranceBand? Band);

/// <summary>
/// The deterministic backend calculator of the realization axis (SAT-D5, SAT-D15, SAT-D16, SAT-D17; DEC-SAL-001). UI and
/// reports reuse its answer; no percent lives in code.
/// <list type="bullet">
/// <item><b>Measure</b> (SAT-D16): per order line, in the line's order unit; the delivered amount is the sum of the line's
/// shipments ("bütün sevklerin toplamı"). Deviation = |shipped − ordered|; deviation % = deviation / ordered × 100.</item>
/// <item><b>Exact</b>: the band is decided by comparing deviation × 100 with percent × ordered in <see cref="decimal"/> — no
/// division, no rounding, no floating point. A zero deviation is no deviation and always <see cref="DeliveryToleranceBand.Within"/>.</item>
/// <item><b>Over-delivery</b> is measured on every shipment: within → Completed; any other band → OutsideTolerance.</item>
/// <item><b>Under-delivery</b> on an open line: within → Completed (SAT-D5); otherwise Partial and NOT banded — the open
/// remainder is not a shortfall (SAT-D16). On a force-closed order the line is closed, so the shortfall is measured: within →
/// Completed (SAT-D17), otherwise ClosedWithRemainder with its band.</item>
/// </list>
/// </summary>
public static class SalesRealizationCalculator
{
    /// <summary>Whether the line's state depends on the tolerance: anything shipped, or a closed line (its shortfall is measured).</summary>
    public static bool NeedsTolerance(Quantity shipped, bool lineClosed) => lineClosed || !shipped.IsZero;

    /// <summary>
    /// The line's realization. <paramref name="ordered"/> must be positive and in the same unit as <paramref name="shipped"/>;
    /// <paramref name="tolerance"/> is required whenever <see cref="NeedsTolerance"/> is true. An arithmetic overflow of a
    /// malformed input escapes as <see cref="OverflowException"/>; the caller shows no state rather than a wrong one.
    /// </summary>
    public static LineRealization Line(Quantity ordered, Quantity shipped, bool lineClosed, SalesDeliveryTolerance? tolerance)
    {
        ordered.RequireSameUnit(shipped);
        if (ordered.IsZero)
        {
            throw new ArgumentException("Sipariş miktarı sıfır olan satırın gerçekleşmesi ölçülemez.", nameof(ordered));
        }

        var direction = shipped.CompareTo(ordered) switch
        {
            < 0 => DeliveryDeviationDirection.Under,
            0 => DeliveryDeviationDirection.None,
            _ => DeliveryDeviationDirection.Over,
        };
        var deviation = direction == DeliveryDeviationDirection.Over ? shipped.Subtract(ordered) : ordered.Subtract(shipped);
        if (!NeedsTolerance(shipped, lineClosed))
        {
            return new LineRealization(LineRealizationState.NotStarted, direction, deviation, null);
        }

        ArgumentNullException.ThrowIfNull(tolerance);
        var band = Band(ordered, deviation, tolerance);
        if (band == DeliveryToleranceBand.Within)
        {
            return new LineRealization(LineRealizationState.Completed, direction, deviation, band);
        }

        return direction switch
        {
            DeliveryDeviationDirection.Over => new LineRealization(LineRealizationState.OutsideTolerance, direction, deviation, band),
            _ when lineClosed => new LineRealization(LineRealizationState.ClosedWithRemainder, direction, deviation, band),
            // SAT-D16: the open remainder is not a shortfall, so an open under-delivered line is not banded.
            _ => new LineRealization(LineRealizationState.Partial, direction, deviation, null),
        };
    }

    /// <summary>
    /// The SAT-D15 band of <paramref name="deviation"/> relative to <paramref name="ordered"/>: every bound is inclusive and
    /// starts its band. Decided exactly as deviation × 100 ≥ percent × ordered.
    /// </summary>
    public static DeliveryToleranceBand Band(Quantity ordered, Quantity deviation, SalesDeliveryTolerance tolerance)
    {
        ArgumentNullException.ThrowIfNull(tolerance);
        ordered.RequireSameUnit(deviation);
        if (deviation.IsZero)
        {
            return DeliveryToleranceBand.Within;
        }

        var scaled = deviation.Value * SalesDeliveryTolerance.PercentBase;
        if (scaled >= tolerance.BlockPercent * ordered.Value)
        {
            return DeliveryToleranceBand.Block;
        }

        if (scaled >= tolerance.ApprovalPercent * ordered.Value)
        {
            return DeliveryToleranceBand.Approval;
        }

        return scaled >= tolerance.WarnPercent * ordered.Value ? DeliveryToleranceBand.Warn : DeliveryToleranceBand.Within;
    }

    /// <summary>
    /// The axis over every line of the order: any OutsideTolerance → OutsideTolerance; all Completed → Completed; all
    /// NotStarted → NotStarted; all Completed or ClosedWithRemainder → ClosedWithRemainder; otherwise Partial. An order
    /// without lines (never stored) is NotStarted.
    /// </summary>
    public static LineRealizationState Axis(IReadOnlyCollection<LineRealizationState> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Contains(LineRealizationState.OutsideTolerance))
        {
            return LineRealizationState.OutsideTolerance;
        }

        if (lines.Count == 0 || lines.All(state => state == LineRealizationState.NotStarted))
        {
            return LineRealizationState.NotStarted;
        }

        if (lines.All(state => state == LineRealizationState.Completed))
        {
            return LineRealizationState.Completed;
        }

        return lines.All(state => state is LineRealizationState.Completed or LineRealizationState.ClosedWithRemainder)
            ? LineRealizationState.ClosedWithRemainder
            : LineRealizationState.Partial;
    }
}
