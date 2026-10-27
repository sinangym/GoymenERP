using GoymenERP.BuildingBlocks.Monetary;

namespace GoymenERP.Modules.Sales.Domain.Drafts;

/// <summary>
/// TEK-03: a line's money fields as the shared <see cref="Money"/> value (TEK-01) in the draft's currency at
/// the approved money profile's scale — the form the domain compares and, later, the totals calculator
/// multiplies (API-03). The wire and the stored row keep the exact text the person gave
/// (<see cref="CommercialDraftFields"/>): no API contract or column changes, and "42000.50" still reads back
/// as "42000.50". Null stays null: an un-priced line is not free and not zero.
/// <para>
/// What is NOT a <see cref="Money"/> here, on purpose: percentages (a discount or surcharge percent is a
/// ratio, not an amount); the net unit price (<see cref="SalesLinePriceCalculator"/>), which is exact and
/// unrounded because the net-price rounding is still a NEEDS-SINAN under SAT-D11; and the Product price list
/// reference and floor, which are the Product owner's snapshot in its own currency code.
/// </para>
/// </summary>
public sealed record SalesLineMoney(Money? UnitPrice, Money? DiscountAmount, Money? SurchargeAmount)
{
    /// <summary>
    /// The commercial texts as money. Callers validate the texts first (exact decimal text within the scale,
    /// <see cref="SalesOrderDraft"/>'s rules); a text that is not would be refused here, never rounded.
    /// </summary>
    public static SalesLineMoney From(CommercialDraftFields commercial, Guid currencyId, int profileScale)
    {
        ArgumentNullException.ThrowIfNull(commercial);
        // A profile scale beyond System.Decimal's own 28 decimals allows nothing more than 28 does.
        var scale = Math.Min(profileScale, Money.MaximumScale);
        return new SalesLineMoney(
            ToMoney(commercial.UnitPrice, currencyId, scale),
            ToMoney(commercial.DiscountAmount, currencyId, scale),
            ToMoney(commercial.SurchargeAmount, currencyId, scale));
    }

    /// <summary>One of <paramref name="draft"/>'s lines as money in the draft currency at the draft's money profile scale.</summary>
    public static SalesLineMoney ForLine(SalesOrderDraft draft, SalesOrderDraftLine line)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(line);
        if (!draft.Lines.Contains(line))
        {
            throw new ArgumentException("Satır bu taslağa ait değil.", nameof(line));
        }

        return From(line.Commercial, draft.Header.CurrencyId, draft.Money.Scale);
    }

    private static Money? ToMoney(string? text, Guid currencyId, int scale) =>
        text is null ? null : Money.Of(SalesLinePriceCalculator.Parse(text), currencyId, scale);
}
