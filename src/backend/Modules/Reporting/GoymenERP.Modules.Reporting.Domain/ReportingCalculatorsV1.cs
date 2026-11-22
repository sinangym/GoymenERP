using System.Globalization;

namespace GoymenERP.Modules.Reporting.Domain;

/// <summary>
/// WP14 stock aging: the age of a stock position in whole business days and its bucket. The boundaries are the caller's
/// parameter (no stock bucket set is decided, NEEDS-SINAN); the entry date is the Inventory owner's (label entry, inherited on
/// split); the business day is the Europe/Istanbul calendar day (P7).
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
    decimal ShippedByPromisedDate,
    DateOnly? InFullDate,
    bool Otif);

/// <summary>
/// WP14 OTIF over one order line, in the line's own unit (quantities are never converted). YOL-D29: the line is OTIF when the
/// WHOLE ordered quantity is shipped on or before the promised day; a partial shipment is not OTIF; there is no tolerance. The
/// date is the shipment (dispatch) date. The in-full date is the shipment day the whole quantity was reached (late or not).
/// </summary>
public static class ReportingOtifCalculatorV1
{
    public static ReportingOtifLineResultV1 Evaluate(DateOnly promisedDate, decimal orderedQuantity, IReadOnlyList<ReportingOtifShipmentV1> shipments)
    {
        ArgumentNullException.ThrowIfNull(shipments);
        if (orderedQuantity <= 0) throw new ArgumentOutOfRangeException(nameof(orderedQuantity));
        if (shipments.Any(shipment => shipment.Quantity <= 0)) throw new ArgumentException("A shipment quantity is positive.", nameof(shipments));

        decimal total = 0, byPromise = 0;
        DateOnly? inFullDate = null;
        foreach (var shipment in shipments.OrderBy(shipment => shipment.ShippedOn))
        {
            total += shipment.Quantity;
            if (shipment.ShippedOn <= promisedDate) byPromise += shipment.Quantity;
            if (inFullDate is null && total >= orderedQuantity) inFullDate = shipment.ShippedOn;
        }

        return new ReportingOtifLineResultV1(total, byPromise, inFullDate, byPromise >= orderedQuantity);
    }

    /// <summary>A share as percent text with two decimals (away from zero); "0" lines give null, never a made-up 0 or 100.</summary>
    public static string? Percent(int part, int whole) => whole <= 0
        ? null
        : Math.Round(part * 100m / whole, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);
}

/// <summary>
/// WP14 receivable/payable aging (YOL-D30): the delay is counted from the due date; buckets 0-30 / 31-60 / 61-90 / 90+ days
/// overdue; an item not yet due is its own column; an item without a due date is "due unknown" (never guessed).
/// </summary>
public static class ReportingPartyAgingCalculatorV1
{
    public const string NotDue = "notDue";
    public const string Overdue0To30 = "overdue0To30";
    public const string Overdue31To60 = "overdue31To60";
    public const string Overdue61To90 = "overdue61To90";
    public const string Overdue90Plus = "overdue90Plus";
    public const string DueUnknown = "dueUnknown";

    public static IReadOnlyList<string> Columns { get; } = [NotDue, Overdue0To30, Overdue31To60, Overdue61To90, Overdue90Plus, DueUnknown];

    public static string Bucket(DateOnly? dueOn, DateOnly asOf)
    {
        if (dueOn is not { } due) return DueUnknown;
        var overdue = asOf.DayNumber - due.DayNumber;
        return overdue switch
        {
            < 0 => NotDue,
            <= 30 => Overdue0To30,
            <= 60 => Overdue31To60,
            <= 90 => Overdue61To90,
            _ => Overdue90Plus,
        };
    }
}
