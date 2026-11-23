using System.Globalization;
using System.Numerics;
using GoymenERP.Modules.Accounting.Domain.Posting;

namespace GoymenERP.Modules.Accounting.Application;

/// <summary>Exact constraints of an approved source; no rounding, exchange rate or account policy is selected here.</summary>
public static class NormalJournalExactSourceAmountsV1
{
    public static bool Valid(IReadOnlyList<NormalJournalExactSourceLineV1> lines)
    {
        if (lines.Count < 2 || lines.Any(x => x is null || string.IsNullOrWhiteSpace(x.MappingKey)
            || x.MappingKey != x.MappingKey.Trim() || x.Side is not ("Debit" or "Credit")
            || !NormalJournalDraftSourcePreparationV1.TryExactAmount(x.Amount, out _))
            || lines.Select(x => (x.MappingKey, x.Side)).Distinct().Count() != lines.Count) return false;
        return Sum(lines.Where(x => x.Side == "Debit").Select(x => decimal.Parse(x.Amount, CultureInfo.InvariantCulture)))
            == Sum(lines.Where(x => x.Side == "Credit").Select(x => decimal.Parse(x.Amount, CultureInfo.InvariantCulture)));
    }

    public static bool Matches(IReadOnlyList<NormalJournalExactSourceLineV1> expected, IReadOnlyList<ProposedLine> proposed) =>
        Valid(expected) && expected.Count == proposed.Count && proposed.All(line => line.Amount is { } amount
            && expected.Count(x => x.MappingKey == line.MappingKey && x.Side == line.Side.ToString()
                && decimal.Parse(x.Amount, CultureInfo.InvariantCulture) == amount) == 1)
        && proposed.Select(x => (x.MappingKey, x.Side)).Distinct().Count() == proposed.Count;

    public static bool TotalMatches(IEnumerable<decimal> amounts, decimal sourceAmount) => Sum(amounts) == Units(sourceAmount);

    public static bool TryDifference(decimal target, decimal source, out decimal difference)
    {
        var units = Units(target) - Units(source);
        var text = BigInteger.Abs(units).ToString(CultureInfo.InvariantCulture).PadLeft(29, '0');
        text = (units.Sign < 0 ? "-" : "") + text[..^28] + "." + text[^28..];
        return decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out difference) && Units(difference) == units;
    }

    private static BigInteger Sum(IEnumerable<decimal> amounts) => amounts.Aggregate(BigInteger.Zero, (sum, amount) => sum + Units(amount));
    private static BigInteger Units(decimal amount)
    {
        var bits = decimal.GetBits(amount);
        var value = (new BigInteger((uint)bits[2]) << 64) + (new BigInteger((uint)bits[1]) << 32) + (uint)bits[0];
        if ((bits[3] & int.MinValue) != 0) value = -value;
        return value * BigInteger.Pow(10, 28 - ((bits[3] >> 16) & 0xff));
    }
}
