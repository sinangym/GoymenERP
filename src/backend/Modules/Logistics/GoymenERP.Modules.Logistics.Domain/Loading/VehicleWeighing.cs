using GoymenERP.Modules.Logistics.Domain.Quantities;

namespace GoymenERP.Modules.Logistics.Domain.Loading;

/// <summary>
/// Which of the two manual weighings is the empty vehicle (tare) — the "brüt/dara yönü" metadata of
/// master v3 §28.12 WGT-001. A vehicle arriving empty and leaving loaded is weighed tare first; one
/// arriving loaded is weighed gross first.
/// </summary>
public enum WeighingDirection
{
    /// <summary>The first weighing is the tare (empty), the second the gross (loaded).</summary>
    TareFirst = 1,

    /// <summary>The first weighing is the gross (loaded), the second the tare (empty).</summary>
    GrossFirst = 2,
}

/// <summary>
/// One manual weighbridge record of a loading instruction's vehicle (AT-053; OPEN-003 closed: the weight
/// is entered by hand). Facts only: the two weighings as entered, the direction, the net the backend
/// calculated (WGT-001), the kilograms loaded on the instruction when it was weighed, and the signed
/// difference net − loaded (WGT-002). No tolerance verdict: the warn/approve/block thresholds are
/// OPEN-004 (DEC-STK-009), so nothing here says whether the difference is acceptable. Not a stock
/// movement; Inventory is never written.
/// </summary>
/// <param name="SharesBefore">How many load/shipment/delivery shares the instruction had when it was weighed — the rebuild replays the weighing at that point, so the loaded snapshot is reproduced, never recomputed later.</param>
/// <param name="RecordedBy">The authenticated actor who entered it (the operator).</param>
public sealed record VehicleWeighing(
    Guid WeighingId,
    WeighingDirection Direction,
    Quantity FirstWeighing,
    Quantity SecondWeighing,
    Quantity Net,
    Quantity LoadedAtWeighing,
    decimal DifferenceKg,
    int SharesBefore,
    string RecordedBy,
    DateTimeOffset RecordedAtUtc)
{
    public Quantity Tare => Direction == WeighingDirection.TareFirst ? FirstWeighing : SecondWeighing;

    public Quantity Gross => Direction == WeighingDirection.TareFirst ? SecondWeighing : FirstWeighing;
}

/// <summary>
/// The deterministic weighbridge calculator (master v3 §28.12; AGENTS "formula/UI/report use the same
/// backend calculator"): the screen shows what this answers and never recomputes it.
/// </summary>
public static class WeighbridgeCalculator
{
    /// <summary>
    /// WGT-001: net = |second − first|, with the direction deciding which weighing is the tare. AT-053: the
    /// net is consistent with the direction — the gross must be strictly heavier than the tare, so a pair
    /// that contradicts the declared direction (or two equal weighings) is refused, never turned around.
    /// </summary>
    public static Quantity Net(WeighingDirection direction, Quantity firstWeighing, Quantity secondWeighing)
    {
        if (!Enum.IsDefined(direction))
        {
            throw new LogisticsRuleException(LogisticsRuleCodes.WeighingDirectionRequired, "Tartım yönü (önce dara / önce brüt) belirtilmelidir.");
        }

        if (firstWeighing.Unit != QuantityUnit.Kilogram || secondWeighing.Unit != QuantityUnit.Kilogram)
        {
            throw new LogisticsRuleException(LogisticsRuleCodes.WeighingUnitUnsupported, "Kantar tartımı kilogramdır.");
        }

        if (firstWeighing.IsZero || secondWeighing.IsZero)
        {
            throw new ArgumentOutOfRangeException(nameof(firstWeighing), "Tartım sıfırdan büyük olmalıdır.");
        }

        var (tare, gross) = direction == WeighingDirection.TareFirst ? (firstWeighing, secondWeighing) : (secondWeighing, firstWeighing);
        if (!gross.IsGreaterThan(tare))
        {
            throw new LogisticsRuleException(LogisticsRuleCodes.WeighingDirectionInconsistent,
                "Brüt tartım daradan ağır olmalıdır; tartımlar belirtilen yönle tutarsız.");
        }

        return Quantity.Of(gross.Value - tare.Value, QuantityUnit.Kilogram);
    }

    /// <summary>WGT-002: the signed difference weighed − expected (here: net − loaded kilograms). Positive = heavier than loaded.</summary>
    public static decimal Difference(Quantity net, Quantity expected)
    {
        net.RequireSameUnit(expected);
        return (net.Value - expected.Value) / 1.0000000000000000000000000000m;
    }
}
