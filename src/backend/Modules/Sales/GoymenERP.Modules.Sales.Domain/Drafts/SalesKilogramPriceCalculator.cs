using GoymenERP.BuildingBlocks.Quantities;

namespace GoymenERP.Modules.Sales.Domain.Drafts;

/// <summary>Whether a kg price (SAT-D436) can be shown, or why not. The wire carries the member name.</summary>
public enum SalesKilogramPriceState
{
    Shown = 1,

    /// <summary>No theoretical kg: the line has no kg/unit pinned (before approval, no card source, an older order) — golden K13.</summary>
    TheoreticalKgUnavailable = 2,

    /// <summary>No weighed kg: nothing invoiced yet, or the invoiced share carries no scale kg — golden K14 / TK6.</summary>
    WeighedKgUnavailable = 3,

    /// <summary>The kg is zero; no division — golden K15.</summary>
    ZeroKg = 4,

    /// <summary>The amount is missing (an unpriced line) — no price is derived.</summary>
    AmountUnavailable = 5,

    /// <summary>
    /// Document level only: a line is not a kg-bearing goods line or lacks that kind of kg (the fail-closed interim rule of the
    /// decision file's "Sınırlar"; question 28 / golden N2 stays open) — golden K17 / K18. Line level is still shown.
    /// </summary>
    DocumentNotAllKilogramLines = 6,

    /// <summary>The value cannot be held exactly; nothing is shown (never a silently rounded figure).</summary>
    OutOfRange = 7,
}

/// <summary>A kg price: the value at the document currency's 0,01 when <see cref="State"/> is Shown, null otherwise.</summary>
public sealed record SalesKilogramPrice(SalesKilogramPriceState State, decimal? Value)
{
    public static SalesKilogramPrice Unavailable(SalesKilogramPriceState state) =>
        state == SalesKilogramPriceState.Shown ? throw new ArgumentOutOfRangeException(nameof(state)) : new(state, null);
}

/// <summary>One line's input: its KDV-excluded net amount after line discount (TK1) and the kg of the requested kind.</summary>
public sealed record SalesKilogramPriceLine(decimal? Amount, decimal? Kilograms);

/// <summary>
/// SAT-D436 / SAT-D439 (AK-5): the ONE kg price calculator — an information figure, never a cost and never an amount (SAT-D434,
/// SAT-D435). Formula (ADET_KG_FORMUL_GOLDEN_V1 §1): line kg price = amount ÷ kg; document kg price = Σ amount ÷ Σ kg (TK4, never an
/// average of line prices); one exact division, rounded once to 0,01 of the document currency, half away from zero (24. cevap,
/// TK3, TK9); no exchange rate (SAT-D439). The document figure is shown only while every line has a kg of that kind (the
/// fail-closed interim rule; N2 open). The kg passed is the document kg as written (TK2: theoretical kg = quantity × the pinned
/// kg/unit at 3 decimals; weighed kg = the scale kg on the invoiced share).
/// </summary>
public static class SalesKilogramPriceCalculator
{
    /// <summary>SAT-D436: shown to 0,01 of the document currency.</summary>
    public const int PriceScale = KilogramPriceArithmetic.PriceScale;

    public static SalesKilogramPrice Line(decimal? amount, decimal? kilograms, SalesKilogramPriceState missingKilograms)
    {
        if (missingKilograms is not (SalesKilogramPriceState.TheoreticalKgUnavailable or SalesKilogramPriceState.WeighedKgUnavailable))
            throw new ArgumentOutOfRangeException(nameof(missingKilograms));
        if (amount is not { } value) return SalesKilogramPrice.Unavailable(SalesKilogramPriceState.AmountUnavailable);
        if (kilograms is not { } kg) return SalesKilogramPrice.Unavailable(missingKilograms);
        return Divide(value, kg);
    }

    public static SalesKilogramPrice Document(IReadOnlyList<SalesKilogramPriceLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0 || lines.Any(line => line is null || line.Amount is null || line.Kilograms is null))
            return SalesKilogramPrice.Unavailable(SalesKilogramPriceState.DocumentNotAllKilogramLines);
        return Divide(lines.Sum(line => line.Amount!.Value), lines.Sum(line => line.Kilograms!.Value));
    }

    /// <summary>
    /// Exact amount ÷ kg rounded once, half away from zero, to <see cref="PriceScale"/> — the shared deterministic division
    /// (<see cref="KilogramPriceArithmetic"/>, the same one Purchasing uses); only the states are Sales'.
    /// </summary>
    private static SalesKilogramPrice Divide(decimal amount, decimal kilograms)
    {
        if (kilograms == 0m) return SalesKilogramPrice.Unavailable(SalesKilogramPriceState.ZeroKg);
        if (kilograms < 0m) return SalesKilogramPrice.Unavailable(SalesKilogramPriceState.OutOfRange);
        try
        {
            return new SalesKilogramPrice(SalesKilogramPriceState.Shown, KilogramPriceArithmetic.RoundedQuotient(amount, kilograms));
        }
        catch (OverflowException)
        {
            return SalesKilogramPrice.Unavailable(SalesKilogramPriceState.OutOfRange);
        }
    }
}
