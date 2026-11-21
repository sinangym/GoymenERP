namespace GoymenERP.Modules.Sales.Domain.Invoices;

/// <summary>
/// One order line a shared delivery weighing served, as the split reads it: the Sales order and order line, and the line's
/// theoretical kilograms (the instruction's loaded kilograms of that line when it was weighed; WGT-002 puts the loaded kg on the
/// theoretical side of the weighbridge comparison). Facts only.
/// </summary>
/// <param name="SalesOrderId">The Sales order the line belongs to.</param>
/// <param name="SalesOrderLineId">The Sales order line.</param>
/// <param name="TheoreticalKg">The line's theoretical kilograms; null when the owner did not answer a readable value.</param>
public sealed record SalesSubcontractWeighingSplitLine(Guid SalesOrderId, Guid SalesOrderLineId, decimal? TheoreticalKg);

/// <summary>One order line's billed whole kilograms of the shared weighing (SAT-D376/D377/D402).</summary>
public sealed record SalesSubcontractWeighingSplitShare(Guid SalesOrderId, Guid SalesOrderLineId, Quantity BilledKg);

/// <summary>
/// SAT-D376 + SAT-D377 + SAT-D402: the one deterministic calculator that splits a delivery weighing's net kilograms over the
/// subcontract order lines it served. The screen never recomputes it.
/// <list type="number">
/// <item><b>Raw share</b> (SAT-D376, updates SAT-D312 (1)): each line's raw share is <c>net × theoretical / Σ theoretical</c>,
/// unrounded.</item>
/// <item><b>Whole kg per line</b> (SAT-D377, SAT-D402): every line but the last is rounded to whole kilograms with
/// <see cref="MidpointRounding.AwayFromZero"/>.</item>
/// <item><b>Residue to the last line</b> (SAT-D376, SAT-D402): the whole-kg total is the weighing's net kg rounded
/// AwayFromZero; the last line takes that total minus the other lines, so the lines always add up to the rounded total. "Last" is
/// the line order the weighing answered (the document order; the SAT-D402 reading).</item>
/// </list>
/// The weighbridge's own record keeps its three-decimal value; rounding is only in the billed quantity (SAT-D377). Each line is
/// priced on its own billed kilograms by the one invoice calculator, so no money is split here.
/// <para>
/// Fail closed (SAT-D376 author note): a line whose theoretical kg is missing or not positive stops the split
/// (<see cref="SalesRuleCodes.SubcontractTheoreticalKgUnavailable"/>); a line whose whole-kg share would be zero or negative is
/// never billed as such (<see cref="SalesRuleCodes.SubcontractShareNotPositive"/>). Several instruction lines of the same order
/// line are summed into that order line at its first appearance. A weighing that serves a single order line needs no theoretical
/// kg: that line takes the whole rounded net kg.
/// </para>
/// </summary>
public static class SalesSubcontractWeighingSplit
{
    /// <summary>Billed kilograms carry no decimals (SAT-D377: whole kg).</summary>
    public const int BilledKgDecimals = 0;

    private const string TheoreticalKgMessage =
        "Ortak tartıdaki bir satırın teorik kg'ı okunamadı; net kg paylaştırılmadı (SAT-D376).";

    private const string ShareNotPositiveMessage =
        "Ortak tartının tam kg'a yuvarlanmış payı bu satır için sıfır ya da negatif çıkıyor; fason faturası hazırlanmadı.";

    /// <summary>Splits <paramref name="netKg"/> over <paramref name="lines"/> (in the weighing's line order).</summary>
    /// <exception cref="SalesRuleException">
    /// <see cref="SalesRuleCodes.SubcontractNetKgUnavailable"/> for a net kg that is not a positive three-decimal kilogram value;
    /// <see cref="SalesRuleCodes.SubcontractTheoreticalKgUnavailable"/> and <see cref="SalesRuleCodes.SubcontractShareNotPositive"/>
    /// naming the order line.
    /// </exception>
    public static IReadOnlyList<SalesSubcontractWeighingSplitShare> Split(decimal netKg,
        IReadOnlyList<SalesSubcontractWeighingSplitLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0)
        {
            throw new ArgumentException("Ortak tartı paylaştırması en az bir satır ister.", nameof(lines));
        }

        if (netKg <= 0m || decimal.Round(netKg, Quantity.KilogramScale) != netKg)
        {
            throw new SalesRuleException(SalesRuleCodes.SubcontractNetKgUnavailable,
                "Teslim tartımının net kg'ı pozitif bir kilogram değeri değil; paylaştırma yapılmadı.");
        }

        // Aggregate per order line, keeping the first appearance's position (deterministic "last line").
        var order = new List<(Guid OrderId, Guid LineId)>();
        var theoretical = new Dictionary<Guid, decimal?>();
        foreach (var line in lines)
        {
            ArgumentNullException.ThrowIfNull(line);
            var kg = line.TheoreticalKg is { } value && value > 0m && decimal.Round(value, Quantity.KilogramScale) == value
                ? value
                : (decimal?)null;
            if (theoretical.TryGetValue(line.SalesOrderLineId, out var sum))
            {
                if (order.Single(entry => entry.LineId == line.SalesOrderLineId).OrderId != line.SalesOrderId)
                {
                    throw new SalesRuleException(SalesRuleCodes.InternalInvariant,
                        "Aynı sipariş satırı iki ayrı siparişe bağlı görünüyor.", line.SalesOrderLineId);
                }

                theoretical[line.SalesOrderLineId] = sum is { } known && kg is { } more ? known + more : null;
            }
            else
            {
                theoretical.Add(line.SalesOrderLineId, kg);
                order.Add((line.SalesOrderId, line.SalesOrderLineId));
            }
        }

        var roundedTotal = decimal.Round(netKg, BilledKgDecimals, MidpointRounding.AwayFromZero);
        if (order.Count == 1)
        {
            // An unshared weighing needs no weight: the one order line takes the whole rounded net kg (SAT-D377).
            if (roundedTotal <= 0m)
            {
                throw new SalesRuleException(SalesRuleCodes.SubcontractShareNotPositive, ShareNotPositiveMessage, order[0].LineId);
            }

            return [new SalesSubcontractWeighingSplitShare(order[0].OrderId, order[0].LineId,
                Quantity.Positive(roundedTotal, QuantityUnit.Kilogram))];
        }

        foreach (var (_, lineId) in order)
        {
            if (theoretical[lineId] is null)
            {
                throw new SalesRuleException(SalesRuleCodes.SubcontractTheoreticalKgUnavailable, TheoreticalKgMessage, lineId);
            }
        }

        var denominator = order.Sum(entry => theoretical[entry.LineId]!.Value);
        var shares = new List<SalesSubcontractWeighingSplitShare>(order.Count);
        var allocated = 0m;
        for (var index = 0; index < order.Count; index++)
        {
            var (orderId, lineId) = order[index];
            decimal billed;
            if (index == order.Count - 1)
            {
                billed = roundedTotal - allocated;
            }
            else
            {
                // Multiply before dividing so the raw share keeps the most precision decimal allows.
                var raw = netKg * theoretical[lineId]!.Value / denominator;
                billed = decimal.Round(raw, BilledKgDecimals, MidpointRounding.AwayFromZero);
            }

            if (billed <= 0m)
            {
                throw new SalesRuleException(SalesRuleCodes.SubcontractShareNotPositive, ShareNotPositiveMessage, lineId);
            }

            allocated += billed;
            shares.Add(new SalesSubcontractWeighingSplitShare(orderId, lineId, Quantity.Positive(billed, QuantityUnit.Kilogram)));
        }

        if (allocated != roundedTotal)
        {
            throw new SalesRuleException(SalesRuleCodes.InternalInvariant, "Paylaştırılan kg toplamı yuvarlanmış net kg'a eşit değil.");
        }

        return shares;
    }
}
