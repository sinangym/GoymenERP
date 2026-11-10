using System.Globalization;
using System.Numerics;
using GoymenERP.BuildingBlocks.Monetary;

namespace GoymenERP.Modules.Sales.Domain.Drafts;

/// <summary>Whether a line's margin (SAT-D247/SAT-D259) could be computed.</summary>
public enum SalesLineMarginState
{
    /// <summary>The margin amount is computed; the percent is computed too unless the net sale is zero or negative.</summary>
    Computed = 1,

    /// <summary>The margin is "kullanılamıyor" (SAT-D247 fail-closed): no amount and no percent — never zero, never an estimate.</summary>
    Unavailable = 2,
}

/// <summary>
/// Why a line's margin, or only its percent, is "kullanılamıyor". Every reason is fail-closed (SAT-D247, SAT-D262 (5)): the
/// margin is never zero and never estimated. The wire carries the member name.
/// </summary>
public enum SalesLineMarginUnavailableReason
{
    /// <summary>No cost source answered: the Inventory cost read port (SAT-D262) is not wired to Sales.</summary>
    CostSourceMissing = 1,

    /// <summary>The cost port answered <c>Unreadable</c> (SAT-D262 (2)/(5)).</summary>
    CostUnreadable = 2,

    /// <summary>The cost port answered <c>NoSource</c>: Stok has no purchase cost for the item to give (SAT-D262 (2)/(5)).</summary>
    CostNoSource = 3,

    /// <summary>The item is produced: production cost is not part of the average (SAT-D258 (2); <c>OPEN-008</c>).</summary>
    ProducedItem = 5,

    /// <summary>The item is both purchased and produced; its margin is not decided (SAT-D258, EK52 "Sınırlar" <c>NEEDS-SINAN</c>).</summary>
    MixedSourceItem = 6,

    /// <summary>The cost unit differs from the order unit; the conversion rule is not decided (EK52 "Sınırlar" <c>NEEDS-SINAN</c>).</summary>
    UnitConversionUndecided = 7,

    /// <summary>
    /// The sale or the cost is not in the functional currency (TL); converting either is not decided (EK52 "Sınırlar"
    /// <c>NEEDS-SINAN</c> for foreign-currency cost; the margin is a TL amount, SAT-D259 (2)).
    /// </summary>
    CurrencyConversionUndecided = 8,

    /// <summary>The line has no net sale amount (unpriced, priced in another unit or out of range — the totals calculator's state).</summary>
    SalesAmountUnavailable = 9,

    /// <summary>The cost answered is negative or finer than its 4-decimal internal precision (SAT-D265 (2)); it is not used.</summary>
    CostInvalid = 10,

    /// <summary>A margin value cannot be held exactly by System.Decimal; nothing is shown (never a silently rounded amount).</summary>
    AmountOutOfRange = 11,

    /// <summary>Only the percent: the net sale is zero or negative, so there is no ratio (SAT-D259 detail; no division by zero).</summary>
    NetSaleNotPositive = 12,
}

/// <summary>Why no cost value is available for a line — the cost side's own fail-closed states (SAT-D258, SAT-D262; no warehouse state — SAT-D272).</summary>
public enum SalesLineCostUnavailability
{
    /// <summary>No cost source is wired (SAT-D262: the Inventory port does not exist yet).</summary>
    SourceMissing = 1,

    /// <summary>The cost port answered <c>Unreadable</c>.</summary>
    Unreadable = 2,

    /// <summary>The cost port answered <c>NoSource</c>.</summary>
    NoSource = 3,

    /// <summary>A produced item (SAT-D258 (2)).</summary>
    ProducedItem = 5,

    /// <summary>A purchased-and-produced item (SAT-D258, <c>NEEDS-SINAN</c>).</summary>
    MixedSourceItem = 6,
}

/// <summary>
/// One line's purchase-only cost (SAT-D263 (1)) as the margin calculator takes it: either a unit cost in a currency and a unit,
/// or the reason there is none. This is the CALCULATOR's input, not the Inventory port's contract (SAT-D262: that port is the
/// Inventory owner's and is not defined here); a future adapter maps the port's answer onto it. The unit cost is the
/// COMPANY-WIDE (all warehouses) weighted moving-average purchase cost that Stok supplies through its versioned read port, for
/// the preview and the later snapshot alike — orders carry no warehouse (Sinan 2026-11-10, EK52 SAT-D272; supersedes the
/// purchase-order-line-price wording of Issue #66 comment 6761125860). There is no manual cost entry. Value 4 is unused: a
/// warehouse-undetermined state was dropped because orders carry no warehouse.
/// </summary>
public sealed record SalesLineMarginCost
{
    private SalesLineMarginCost(decimal? unitCost, Guid? currencyId, Guid? unitId, SalesLineCostUnavailability? unavailability)
    {
        UnitCost = unitCost;
        CurrencyId = currencyId;
        UnitId = unitId;
        Unavailability = unavailability;
    }

    /// <summary>The purchase cost per <see cref="UnitId"/> (the company-wide weighted moving-average purchase cost from Stok's read port); null exactly when <see cref="Unavailability"/> is set.</summary>
    public decimal? UnitCost { get; }

    public Guid? CurrencyId { get; }

    public Guid? UnitId { get; }

    public SalesLineCostUnavailability? Unavailability { get; }

    /// <summary>The cost before any source exists (SAT-D262: until the Inventory port is in main the margin stays unavailable).</summary>
    public static SalesLineMarginCost SourceMissing { get; } = new(null, null, null, SalesLineCostUnavailability.SourceMissing);

    /// <summary>A unit cost answered by the cost source. Its validity (sign, 4-decimal precision) is the calculator's fail-closed check.</summary>
    public static SalesLineMarginCost Of(decimal unitCost, Guid currencyId, Guid unitId)
    {
        if (currencyId == Guid.Empty)
        {
            throw new ArgumentException("Maliyet para birimi kimliği boş olamaz.", nameof(currencyId));
        }

        if (unitId == Guid.Empty)
        {
            throw new ArgumentException("Maliyet birimi kimliği boş olamaz.", nameof(unitId));
        }

        return new SalesLineMarginCost(unitCost, currencyId, unitId, null);
    }

    /// <summary>No cost value, for <paramref name="unavailability"/>.</summary>
    public static SalesLineMarginCost Unavailable(SalesLineCostUnavailability unavailability) =>
        Enum.IsDefined(unavailability)
            ? new SalesLineMarginCost(null, null, null, unavailability)
            : throw new ArgumentOutOfRangeException(nameof(unavailability));
}

/// <summary>
/// One line's input to <see cref="SalesLineMarginCalculator"/>.
/// </summary>
/// <param name="NetSale">
/// The line's KDV hariç net sale (SAT-D259 (2)): <see cref="SalesLineTotal.NetAmount"/> from <see cref="SalesOrderTotalsCalculator"/>
/// — quantity × net unit price after discount and maturity surcharge, rounded once (SAT-D11); null when the line has none.
/// </param>
/// <param name="OrderUnitId">The line's order unit; the quantity is in it.</param>
/// <param name="Quantity">The line quantity in the order unit (positive).</param>
/// <param name="FunctionalCurrencyId">The company's functional currency (TL): the margin is a TL amount (SAT-D259 (2)). Data, not a code constant.</param>
/// <param name="Cost">The line's purchase-only cost (SAT-D263) or the reason there is none.</param>
public sealed record SalesLineMarginInput(Money? NetSale, Guid OrderUnitId, decimal Quantity, Guid FunctionalCurrencyId,
    SalesLineMarginCost Cost);

/// <summary>
/// One line's margin. <see cref="Amount"/> is in the functional currency at <see cref="SalesLineMarginCalculator.AmountScale"/>;
/// <see cref="Percent"/> at <see cref="SalesLineMarginCalculator.PercentScale"/>. When <see cref="State"/> is Unavailable both are
/// null and <see cref="UnavailableReason"/> says why; when Computed the amount is set and the percent is null exactly when
/// <see cref="PercentUnavailableReason"/> is <see cref="SalesLineMarginUnavailableReason.NetSaleNotPositive"/>.
/// </summary>
public sealed record SalesLineMargin(
    SalesLineMarginState State,
    SalesLineMarginUnavailableReason? UnavailableReason,
    Money? Amount,
    decimal? Percent,
    SalesLineMarginUnavailableReason? PercentUnavailableReason)
{
    public static SalesLineMargin Unavailable(SalesLineMarginUnavailableReason reason) => new(SalesLineMarginState.Unavailable, reason,
        null, null, null);
}

/// <summary>
/// SAT-D247 / SAT-D259 / SAT-D261 / SAT-D265: the ONE deterministic margin calculator; the line-pricing read, the S-05 screen and
/// any report use it and never recompute (AGENTS.md "Formula/UI/report aynı deterministic backend calculator'ı kullanır";
/// SAT-D247 (3): the frontend never computes a margin).
/// <list type="number">
/// <item><b>Cost</b> = unit cost × quantity, exact. The unit cost is the purchase-only cost (SAT-D263 (1)) — the company-wide
/// (all warehouses) weighted moving-average purchase cost Stok supplies through its versioned read port (SAT-D272; no manual cost entry) — with
/// an internal precision of 4 decimals (SAT-D265 (2)); a finer or negative cost is refused (<c>CostInvalid</c>), never rounded.</item>
/// <item><b>Margin amount</b> = KDV hariç net sale − cost (SAT-D259 (2)), exact (no intermediate rounding, SAT-D265 (1)).</item>
/// <item><b>Margin percent</b> = exact margin amount × 100 / net sale (SAT-D259 (3)) — from the UNROUNDED amount (SAT-D265 (1));
/// a zero or negative net sale has no percent (<c>NetSaleNotPositive</c>, SAT-D259 detail).</item>
/// <item><b>Display</b>: the amount to 0,01 TL and the percent to 0,01 (SAT-D261 (1), SAT-D265 (3)), half away from zero — the
/// existing <see cref="Money.Round"/> rule (SAT-D261 (2), SAT-D265 (4)); negative margins use the same rule.</item>
/// </list>
/// Fail-closed (SAT-D247, SAT-D262 (5)): any missing cost state, a sale or cost outside the functional currency, a cost unit other
/// than the order unit (EK52 <c>NEEDS-SINAN</c> conversions) or a value System.Decimal cannot hold exactly is "kullanılamıyor",
/// never zero. Checks run in a fixed order: sale, sale currency, cost state, cost currency, cost unit, cost validity, range.
/// SAT-D265 (5): the formula is not activated until its golden is in main; until the Inventory port exists the line-pricing read
/// passes <see cref="SalesLineMarginCost.SourceMissing"/> and every margin is unavailable.
/// </summary>
public static class SalesLineMarginCalculator
{
    /// <summary>SAT-D261 (1) / SAT-D265 (3): the margin amount is shown to the kuruş (0,01 TL).</summary>
    public const int AmountScale = 2;

    /// <summary>SAT-D261 (1) / SAT-D265 (3): the margin percent is shown to 0,01.</summary>
    public const int PercentScale = 2;

    /// <summary>SAT-D265 (2): the unit cost's internal precision.</summary>
    public const int CostScale = 4;

    private static readonly BigInteger Hundred = new(100);

    public static SalesLineMargin Calculate(SalesLineMarginInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Cost);
        if (input.FunctionalCurrencyId == Guid.Empty)
        {
            throw new ArgumentException("Fonksiyonel para birimi kimliği boş olamaz.", nameof(input));
        }

        if (input.Quantity <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Satır miktarı pozitif olmalıdır.");
        }

        if (input.NetSale is not { } netSale)
        {
            return SalesLineMargin.Unavailable(SalesLineMarginUnavailableReason.SalesAmountUnavailable);
        }

        if (netSale.CurrencyId != input.FunctionalCurrencyId)
        {
            return SalesLineMargin.Unavailable(SalesLineMarginUnavailableReason.CurrencyConversionUndecided);
        }

        var cost = input.Cost;
        if (cost.Unavailability is { } unavailability)
        {
            return SalesLineMargin.Unavailable(Reason(unavailability));
        }

        if (cost.CurrencyId != input.FunctionalCurrencyId)
        {
            return SalesLineMargin.Unavailable(SalesLineMarginUnavailableReason.CurrencyConversionUndecided);
        }

        if (cost.UnitId != input.OrderUnitId)
        {
            return SalesLineMargin.Unavailable(SalesLineMarginUnavailableReason.UnitConversionUndecided);
        }

        var unitCost = cost.UnitCost!.Value;
        if (unitCost < 0m || Money.SignificantDecimals(unitCost) > CostScale)
        {
            return SalesLineMargin.Unavailable(SalesLineMarginUnavailableReason.CostInvalid);
        }

        var net = Exact.Of(netSale.Amount);
        var margin = net.Subtract(Exact.Of(unitCost).Multiply(Exact.Of(input.Quantity)));
        if (!margin.RoundAwayFromZero(AmountScale).TryToDecimal(out var amount))
        {
            return SalesLineMargin.Unavailable(SalesLineMarginUnavailableReason.AmountOutOfRange);
        }

        var money = Money.Of(amount, input.FunctionalCurrencyId, AmountScale);
        if (net.Mantissa.Sign <= 0)
        {
            return new SalesLineMargin(SalesLineMarginState.Computed, null, money, null,
                SalesLineMarginUnavailableReason.NetSaleNotPositive);
        }

        if (!margin.PercentOf(net, PercentScale).TryToDecimal(out var percent))
        {
            return SalesLineMargin.Unavailable(SalesLineMarginUnavailableReason.AmountOutOfRange);
        }

        return new SalesLineMargin(SalesLineMarginState.Computed, null, money, percent, null);
    }

    private static SalesLineMarginUnavailableReason Reason(SalesLineCostUnavailability unavailability) => unavailability switch
    {
        SalesLineCostUnavailability.SourceMissing => SalesLineMarginUnavailableReason.CostSourceMissing,
        SalesLineCostUnavailability.Unreadable => SalesLineMarginUnavailableReason.CostUnreadable,
        SalesLineCostUnavailability.NoSource => SalesLineMarginUnavailableReason.CostNoSource,
        SalesLineCostUnavailability.ProducedItem => SalesLineMarginUnavailableReason.ProducedItem,
        SalesLineCostUnavailability.MixedSourceItem => SalesLineMarginUnavailableReason.MixedSourceItem,
        _ => throw new ArgumentOutOfRangeException(nameof(unavailability)),
    };

    /// <summary>Exact decimal arithmetic (mantissa × 10^-scale) so no intermediate value is ever silently rounded.</summary>
    private readonly record struct Exact(BigInteger Mantissa, int Scale)
    {
        public static Exact Of(decimal value)
        {
            var text = value.ToString("0.############################", CultureInfo.InvariantCulture);
            var dot = text.IndexOf('.', StringComparison.Ordinal);
            var scale = dot < 0 ? 0 : text.Length - dot - 1;
            var digits = dot < 0 ? text : text.Remove(dot, 1);
            return new Exact(BigInteger.Parse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture), scale);
        }

        public Exact Subtract(Exact other)
        {
            var scale = Math.Max(Scale, other.Scale);
            return new Exact(To(scale) - other.To(scale), scale);
        }

        public Exact Multiply(Exact other) => new(Mantissa * other.Mantissa, checked(Scale + other.Scale));

        /// <summary>Half away from zero to <paramref name="scale"/> decimals on the exact value (SAT-D11 / <see cref="Money.Round"/>).</summary>
        public Exact RoundAwayFromZero(int scale) => Scale <= scale
            ? this
            : new Exact(Divide(Mantissa, BigInteger.Pow(10, Scale - scale)), scale);

        /// <summary>This × 100 / <paramref name="denominator"/> (positive), rounded half away from zero to <paramref name="scale"/> decimals.</summary>
        public Exact PercentOf(Exact denominator, int scale)
        {
            // (m / 10^s) × 100 / (d / 10^t) at 10^-scale = m × 100 × 10^(t + scale) / (d × 10^s)
            var numerator = Mantissa * Hundred * BigInteger.Pow(10, denominator.Scale + scale);
            return new Exact(Divide(numerator, denominator.Mantissa * BigInteger.Pow(10, Scale)), scale);
        }

        public bool TryToDecimal(out decimal value)
        {
            var negative = Mantissa.Sign < 0;
            var digits = BigInteger.Abs(Mantissa).ToString(CultureInfo.InvariantCulture);
            if (Scale > 0)
            {
                digits = digits.Length <= Scale
                    ? $"0.{new string('0', Scale - digits.Length)}{digits}"
                    : $"{digits[..^Scale]}.{digits[^Scale..]}";
            }

            // The exact parser takes unsigned text only; the sign is applied after the exact, round-trip-checked parse.
            if (!SalesLinePriceCalculator.TryParseExact(digits, out var magnitude))
            {
                value = default;
                return false;
            }

            value = negative ? -magnitude : magnitude;
            return true;
        }

        private BigInteger To(int scale) => scale == Scale ? Mantissa : Mantissa * BigInteger.Pow(10, scale - Scale);

        /// <summary>numerator / denominator (denominator positive), half away from zero.</summary>
        private static BigInteger Divide(BigInteger numerator, BigInteger denominator)
        {
            var quotient = BigInteger.DivRem(BigInteger.Abs(numerator), denominator, out var remainder);
            if (remainder * 2 >= denominator)
            {
                quotient += 1;
            }

            return numerator.Sign < 0 ? -quotient : quotient;
        }
    }
}
