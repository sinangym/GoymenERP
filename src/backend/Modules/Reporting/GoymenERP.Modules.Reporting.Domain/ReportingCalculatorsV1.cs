using System.Globalization;

namespace GoymenERP.Modules.Reporting.Domain;

/// <summary>
/// WP14 aging: the age of a stock position in whole business days and its bucket. The boundaries and the age basis are the
/// caller's parameters (no bucket set is decided by any decision); the business day is the Europe/Istanbul calendar day (P7).
/// The screen never recomputes either value.
/// </summary>
public static class ReportingStockAgingCalculatorV1
{
    /// <summary>The bucket of a position that entered after the measuring date.</summary>
    public const string AfterAsOfBucket = "after-as-of";

    public static int AgeDays(DateOnly entryDate, DateOnly asOf) => asOf.DayNumber - entryDate.DayNumber;

    /// <summary>
    /// Boundaries [30, 60, 90] give "0-30", "31-60", "61-90" and "91+"; the boundary day belongs to the lower bucket. A negative
    /// age is <see cref="AfterAsOfBucket"/>.
    /// </summary>
    public static string Bucket(int ageDays, IReadOnlyList<int> boundaries)
    {
        ArgumentNullException.ThrowIfNull(boundaries);
        if (boundaries.Count == 0) throw new ArgumentException("At least one boundary is required.", nameof(boundaries));
        if (ageDays < 0) return AfterAsOfBucket;
        var lower = 0;
        foreach (var boundary in boundaries)
        {
            if (ageDays <= boundary) return Range(lower, boundary);
            lower = boundary + 1;
        }

        return lower.ToString(CultureInfo.InvariantCulture) + "+";
    }

    /// <summary>Every bucket name of a boundary set, in order (the after-as-of bucket last).</summary>
    public static IReadOnlyList<string> Buckets(IReadOnlyList<int> boundaries)
    {
        var names = new List<string>();
        var lower = 0;
        foreach (var boundary in boundaries)
        {
            names.Add(Range(lower, boundary));
            lower = boundary + 1;
        }

        names.Add(lower.ToString(CultureInfo.InvariantCulture) + "+");
        names.Add(AfterAsOfBucket);
        return names;
    }

    private static string Range(int lower, int upper) =>
        lower.ToString(CultureInfo.InvariantCulture) + "-" + upper.ToString(CultureInfo.InvariantCulture);
}

public sealed record ReportingOtifShipmentV1(DateOnly ShippedOn, decimal Quantity);

public sealed record ReportingOtifLineResultV1(
    decimal ShippedQuantity,
    DateOnly? InFullDate,
    bool OnTime,
    bool InFull,
    bool Otif);

/// <summary>
/// WP14 OTIF over one order line, in the line's own unit (quantities are never converted). The tolerances are required
/// parameters: the decisions name the report (YOL-D4) but not its tolerance (NEEDS-SINAN). The proposed definition, kept as
/// one deterministic function so it can be changed in one place when Sinan decides:
/// <list type="bullet">
/// <item>deadline = requested date + <c>onTimeToleranceDays</c>; target = ordered × (100 − <c>inFullTolerancePercent</c>) / 100;</item>
/// <item><b>on time</b>: the first shipment is on or before the deadline;</item>
/// <item><b>in full</b>: the total shipped reaches the target (the in-full date is the shipment day it is reached);</item>
/// <item><b>OTIF</b>: the quantity shipped on or before the deadline reaches the target.</item>
/// </list>
/// The date is the shipment (dispatch) date: the customer's receipt date is not recorded by any owner.
/// </summary>
public static class ReportingOtifCalculatorV1
{
    public static ReportingOtifLineResultV1 Evaluate(DateOnly requestedDate, decimal orderedQuantity,
        IReadOnlyList<ReportingOtifShipmentV1> shipments, int onTimeToleranceDays, decimal inFullTolerancePercent)
    {
        ArgumentNullException.ThrowIfNull(shipments);
        if (orderedQuantity <= 0) throw new ArgumentOutOfRangeException(nameof(orderedQuantity));
        if (onTimeToleranceDays < 0) throw new ArgumentOutOfRangeException(nameof(onTimeToleranceDays));
        if (inFullTolerancePercent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(inFullTolerancePercent));
        if (shipments.Any(shipment => shipment.Quantity <= 0)) throw new ArgumentException("A shipment quantity is positive.", nameof(shipments));

        var deadline = requestedDate.AddDays(onTimeToleranceDays);
        var target = orderedQuantity * (100 - inFullTolerancePercent) / 100;
        var ordered = shipments.OrderBy(shipment => shipment.ShippedOn).ToArray();
        decimal total = 0, byDeadline = 0;
        DateOnly? inFullDate = null;
        foreach (var shipment in ordered)
        {
            total += shipment.Quantity;
            if (shipment.ShippedOn <= deadline) byDeadline += shipment.Quantity;
            if (inFullDate is null && total >= target) inFullDate = shipment.ShippedOn;
        }

        var onTime = ordered.Length > 0 && ordered[0].ShippedOn <= deadline;
        var inFull = ordered.Length > 0 && total >= target;
        var otif = ordered.Length > 0 && byDeadline >= target;
        return new ReportingOtifLineResultV1(total, inFullDate, onTime, inFull, otif);
    }

    /// <summary>A share as percent text with two decimals (away from zero); "0" lines give null, never a made-up 0 or 100.</summary>
    public static string? Percent(int part, int whole) => whole <= 0
        ? null
        : Math.Round(part * 100m / whole, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);
}
