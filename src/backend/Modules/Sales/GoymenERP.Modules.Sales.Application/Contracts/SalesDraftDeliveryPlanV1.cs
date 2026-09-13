using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace GoymenERP.Modules.Sales.Application.Contracts;

// Exact same-unit planning only; no delivery, conversion or stock event.
// Real unit profile/scale and authority validation remain owner prerequisites.
public static class SalesDraftDeliveryPlanV1
{
    public static string GetUnscheduledQuantity(SalesOrderDraftLineRequest line)
    {
        var ordered = Parse(line.Quantity);
        var slices = line.RequestedDeliverySlices;
        if (slices?.Count > 500) throw new JsonException("Too many delivery slices.");
        var values = new List<(BigInteger Coefficient, int Scale)>();
        var ids = new HashSet<Guid>();
        foreach (var slice in slices ?? [])
        {
            if (slice is null || slice.SliceId == Guid.Empty || !ids.Add(slice.SliceId)
                || slice.OrderUnitId != line.OrderUnitId)
                throw new JsonException("Invalid delivery slice identity or unit.");
            values.Add(Parse(slice.Quantity));
        }

        var scale = values.Aggregate(ordered.Scale, (current, value) => Math.Max(current, value.Scale));
        var remainder = ordered.Coefficient * BigInteger.Pow(10, scale - ordered.Scale);
        foreach (var value in values)
            remainder -= value.Coefficient * BigInteger.Pow(10, scale - value.Scale);
        if (remainder.Sign < 0) throw new JsonException("Delivery slices exceed order quantity.");
        var digits = remainder.ToString(CultureInfo.InvariantCulture);
        if (scale == 0) return digits;
        digits = digits.PadLeft(scale + 1, '0');
        return digits.Insert(digits.Length - scale, ".").TrimEnd('0').TrimEnd('.');
    }

    private static (BigInteger Coefficient, int Scale) Parse(string quantity)
    {
        SalesDraftJson.RequireDecimalShape(quantity, positive: true);
        var dot = quantity.IndexOf('.');
        return (BigInteger.Parse(quantity.Replace(".", ""), CultureInfo.InvariantCulture),
            dot < 0 ? 0 : quantity.Length - dot - 1);
    }
}
