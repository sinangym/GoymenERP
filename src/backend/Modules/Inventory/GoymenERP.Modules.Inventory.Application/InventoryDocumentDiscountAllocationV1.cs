using GoymenERP.BuildingBlocks.Monetary;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>One line's share of a document discount (<see cref="InventoryDocumentDiscountAllocationV1"/>).</summary>
/// <param name="LineAmount">The line amount after its own line discount, VAT excluded.</param>
/// <param name="DocumentDiscountShare">The line's share of the document discount, at the money scale.</param>
/// <param name="NetLineAmount">The line amount after line AND document discount, VAT excluded (the cost base).</param>
public sealed record InventoryDocumentDiscountLineShareV1(Money LineAmount, Money DocumentDiscountShare, Money NetLineAmount);

/// <summary>
/// SAT-D386 / SAT-D403 / SAT-D407: the moving-average cost of a purchase line is its net unit price after the line discount
/// AND the document (belge altı) discount, VAT excluded. The document discount is distributed over the lines by line
/// amount (after line discount); each share is rounded to the money scale (kuruş for TL) with the house rule
/// (<see cref="MidpointRounding.AwayFromZero"/>); the rounding remainder goes to the line with the largest amount, or the
/// first such line on a tie. The shares always sum to the document discount exactly. Deterministic and pure: the money
/// scale is the caller's approved currency profile, never chosen here. Purchasing does not carry a document discount yet
/// (OPEN wiring); this is the one calculator the cost path, screens and reports read once it does.
/// </summary>
public static class InventoryDocumentDiscountAllocationV1
{
    public static IReadOnlyList<InventoryDocumentDiscountLineShareV1> Allocate(
        IReadOnlyList<Money> lineAmounts, Money documentDiscount)
    {
        ArgumentNullException.ThrowIfNull(lineAmounts);
        if (lineAmounts.Count == 0)
        {
            throw new ArgumentException("Belge altı iskonto en az bir satıra dağıtılır.", nameof(lineAmounts));
        }

        var currencyId = documentDiscount.CurrencyId;
        var scale = documentDiscount.Scale;
        if (lineAmounts.Any(line => line.CurrencyId != currencyId || line.Scale != scale))
        {
            throw new MoneyRuleException(MoneyRuleCodes.CurrencyMismatch,
                "Satır tutarları ve belge altı iskonto aynı para birimi ve ölçekte olmalıdır.");
        }

        if (lineAmounts.Any(line => line.IsNegative) || documentDiscount.IsNegative)
        {
            throw new ArgumentOutOfRangeException(nameof(lineAmounts), "Satır tutarları ve belge altı iskonto negatif olamaz.");
        }

        var total = lineAmounts.Sum(line => line.Amount);
        if (documentDiscount.IsZero)
        {
            return [.. lineAmounts.Select(line => new InventoryDocumentDiscountLineShareV1(line, Money.Zero(currencyId, scale), line))];
        }

        if (total <= 0m || documentDiscount.Amount > total)
        {
            throw new ArgumentOutOfRangeException(nameof(documentDiscount),
                "Belge altı iskonto pozitif bir satır toplamını aşamaz.");
        }

        var shares = lineAmounts
            .Select(line => Money.Round(documentDiscount.Amount * line.Amount / total, currencyId, scale).Amount)
            .ToArray();
        var remainder = documentDiscount.Amount - shares.Sum();
        if (remainder != 0m)
        {
            var largest = 0;
            for (var index = 1; index < lineAmounts.Count; index++)
            {
                if (lineAmounts[index].Amount > lineAmounts[largest].Amount)
                {
                    largest = index;
                }
            }

            shares[largest] += remainder;
        }

        return [.. lineAmounts.Select((line, index) => new InventoryDocumentDiscountLineShareV1(
            line, Money.Of(shares[index], currencyId, scale), Money.Of(line.Amount - shares[index], currencyId, scale)))];
    }
}
