using System.Globalization;

namespace GoymenERP.Modules.Reporting.Domain;

/// <summary>
/// WP14 stock aging (YOL-D39): the age of a stock position in whole business days, counted from the label root's first entry
/// (the Inventory owner's date; a split takes the root's date, a merge the OLDEST root's), and its bucket: 0-30 / 31-60 / 61-90 /
/// 90+ days (the YOL-D30 buckets). The business day is the Europe/Istanbul calendar day (P7). The screen never recomputes.
/// </summary>
public static class ReportingStockAgingCalculatorV1
{
    /// <summary>The bucket of a position that entered after the measuring date.</summary>
    public const string AfterAsOfBucket = "after-as-of";

    public static IReadOnlyList<string> Buckets { get; } = ["0-30", "31-60", "61-90", "90+", AfterAsOfBucket];

    public static int AgeDays(DateOnly entryDate, DateOnly asOf) => asOf.DayNumber - entryDate.DayNumber;

    public static string Bucket(int ageDays) => ageDays switch
    {
        < 0 => AfterAsOfBucket,
        <= 30 => "0-30",
        <= 60 => "31-60",
        <= 90 => "61-90",
        _ => "90+",
    };
}

public sealed record ReportingOtifShipmentV1(DateOnly ShippedOn, decimal Quantity)
{
    /// <summary>The unit the owner recorded the share in (checked by the host adapter; never converted).</summary>
    public Guid UnitId { get; init; }
}

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
