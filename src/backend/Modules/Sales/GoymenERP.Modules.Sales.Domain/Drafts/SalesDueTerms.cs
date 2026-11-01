using System.Globalization;
using GoymenERP.BuildingBlocks.Monetary;

namespace GoymenERP.Modules.Sales.Domain.Drafts;

/// <summary>
/// The due type ("vade tipi") an instalment counts from — the base date its due date is computed from. SAT-D129 (EK18): there
/// are exactly TWO types; the invoice-date type ("fatura vadesi") of DEC-SAL-008 is removed by Sinan's explicit answer.
/// <list type="bullet">
/// <item><see cref="OrderDate"/> — "Nokta vade": the order's business date + N days, one due date for the order.</item>
/// <item><see cref="ShipmentDate"/> — "Sevk vadesi": each shipment's (Europe/Istanbul) day + N days, every shipment its own due
/// date. Sole exception SAT-D131: in the invoice-first flow (SAT-D94 (2)) it counts from the invoice date
/// (<see cref="SalesDueDateCalculator.ComputeInvoiceFirst"/>); that is a calculation base, never a selectable type.</item>
/// </list>
/// "Taksit" (DEC-SAL-008) is more than one instalment; each instalment carries one of the two types and its own days (SAT-D129),
/// so "karma" is instalments of different types — allowed: Sinan, 2026-10-31 ("Evet, karma olabilsin": every instalment carries
/// its own type and days; DEC-SAL-008's "karma" model is kept; to be recorded in EK19). Nothing here is a default:
/// SAT-D127 — a new order has no due terms. Persisted and sent by name (<c>SalesDueBasisNamesV1</c>), never by number.
/// </summary>
public enum SalesDueBasis
{
    OrderDate = 1,
    ShipmentDate = 2,
}

/// <summary>
/// One instalment of an order's due terms: its position (1..n), the date it counts from, the whole calendar days after that
/// date and its share of the invoice as an exact percent text (kept verbatim; never rounded).
/// </summary>
public sealed record SalesDueInstalment(int SequenceNo, SalesDueBasis Basis, int DaysAfter, string SharePercent);

/// <summary>A due-terms rule refusal; <see cref="InstalmentIndex"/> names the instalment (0-based) a per-instalment rule is about.</summary>
public sealed class SalesDueTermsException(string code, string message, int? instalmentIndex = null) : InvalidOperationException(message)
{
    public string Code { get; } = code;

    public int? InstalmentIndex { get; } = instalmentIndex;
}

/// <summary>The stable refusal codes of the due terms. All prefixed <c>sales_due_terms_</c>.</summary>
public static class SalesDueTermsCodes
{
    /// <summary>No instalment, or more than <see cref="SalesDueTerms.MaximumInstalments"/>.</summary>
    public const string InstalmentCountOutOfRange = "sales_due_terms_instalment_count_out_of_range";

    /// <summary>The instalments are not numbered 1..n in order.</summary>
    public const string SequenceInvalid = "sales_due_terms_sequence_invalid";

    /// <summary>An instalment names no known basis.</summary>
    public const string BasisInvalid = "sales_due_terms_basis_invalid";

    /// <summary>Days after the basis below zero or above <see cref="SalesDueTerms.MaximumDaysAfter"/>.</summary>
    public const string DaysOutOfRange = "sales_due_terms_days_out_of_range";

    /// <summary>A share that is not an exact percent above 0 and at most 100.</summary>
    public const string ShareInvalid = "sales_due_terms_share_invalid";

    /// <summary>The shares do not add up to exactly 100.</summary>
    public const string SharesNotWhole = "sales_due_terms_shares_not_whole";

    /// <summary>SAT-D124 / SAT-D157: the rounded instalments leave a negative remainder for the last one (refused, never repaired; "tutar bu kadar taksite bölünemez").</summary>
    public const string AmountNotSplittable = "sales_due_terms_amount_not_splittable";
}

/// <summary>
/// DEC-SAL-008 / SAT-D86: an order's due terms — the "vade modeli" snapshot the order carries (the order owns it; no owner
/// catalogue of payment terms exists on main). One or more instalments, each with its own basis (<see cref="SalesDueBasis"/>),
/// whole calendar days after it and an exact share; the shares add up to exactly 100. Immutable; validated once, here.
/// <para>
/// Decided (EK18): SAT-D125 — a due date on a weekend or a holiday does NOT move (whole calendar days are counted); SAT-D124 —
/// an instalment's amount is rounded to the money scale and the remainder goes to the LAST instalment
/// (<see cref="SalesDueDateCalculator.SplitAmount"/>); SAT-D127 — no default term per customer or company; SAT-D123 — an order
/// is not approved without due terms. The two bounds below are technical guards of the arithmetic, not policies.
/// </para>
/// </summary>
public sealed class SalesDueTerms : IEquatable<SalesDueTerms>
{
    /// <summary>Technical bound of one order's instalment count (the arithmetic and the wire stay bounded); not a business policy.</summary>
    public const int MaximumInstalments = 120;

    /// <summary>Technical bound of the days after a basis date (keeps the date arithmetic far from <see cref="DateOnly.MaxValue"/>); not a policy.</summary>
    public const int MaximumDaysAfter = 36600;

    private SalesDueTerms(IReadOnlyList<SalesDueInstalment> instalments) => Instalments = instalments;

    /// <summary>The instalments in sequence order (1..n).</summary>
    public IReadOnlyList<SalesDueInstalment> Instalments { get; }

    /// <summary>
    /// Validates and builds the terms: 1..<see cref="MaximumInstalments"/> instalments numbered 1..n in order, each with a
    /// known basis, 0..<see cref="MaximumDaysAfter"/> days and a share 0 &lt; share ≤ 100 (exact decimal text), the shares
    /// adding up to exactly 100. A refusal names the instalment (0-based index) when it is about one.
    /// </summary>
    public static SalesDueTerms Create(IReadOnlyList<SalesDueInstalment> instalments)
    {
        ArgumentNullException.ThrowIfNull(instalments);
        if (instalments.Count is 0 or > MaximumInstalments)
        {
            throw new SalesDueTermsException(SalesDueTermsCodes.InstalmentCountOutOfRange,
                $"Vade en az bir, en fazla {MaximumInstalments} taksit taşır.");
        }

        var total = 0m;
        var accepted = new SalesDueInstalment[instalments.Count];
        for (var index = 0; index < instalments.Count; index++)
        {
            var instalment = instalments[index]
                ?? throw new SalesDueTermsException(SalesDueTermsCodes.SequenceInvalid, "Vade taksiti boş olamaz.", index);
            if (instalment.SequenceNo != index + 1)
            {
                throw new SalesDueTermsException(SalesDueTermsCodes.SequenceInvalid,
                    "Vade taksitleri 1'den başlayarak sırayla numaralanır.", index);
            }

            if (!Enum.IsDefined(instalment.Basis))
            {
                throw new SalesDueTermsException(SalesDueTermsCodes.BasisInvalid, "Vade tipi tanımlı değil.", index);
            }

            if (instalment.DaysAfter is < 0 or > MaximumDaysAfter)
            {
                throw new SalesDueTermsException(SalesDueTermsCodes.DaysOutOfRange,
                    $"Vade günü 0 ile {MaximumDaysAfter} arasında olmalıdır.", index);
            }

            if (string.IsNullOrWhiteSpace(instalment.SharePercent) || !IsPlainDecimal(instalment.SharePercent)
                || !SalesOrderDraft.IsPercent(instalment.SharePercent)
                || !decimal.TryParse(instalment.SharePercent, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var share)
                || share <= 0m)
            {
                throw new SalesDueTermsException(SalesDueTermsCodes.ShareInvalid,
                    "Taksit payı 0'dan büyük, en fazla 100 olan tam bir yüzde metnidir.", index);
            }

            total += share;
            accepted[index] = instalment;
        }

        if (total != 100m)
        {
            throw new SalesDueTermsException(SalesDueTermsCodes.SharesNotWhole, "Taksit paylarının toplamı tam olarak 100 olmalıdır.");
        }

        return new SalesDueTerms(accepted);
    }

    /// <summary>
    /// Whether two terms say the same thing: the same instalments in the same order, each with the same basis, days and the
    /// same share VALUE ("50.0" is "50"). Used to refuse a revision that changes nothing.
    /// </summary>
    public static bool SameTerms(SalesDueTerms? left, SalesDueTerms? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.Instalments.Count == right.Instalments.Count
            && left.Instalments.Zip(right.Instalments).All(pair => pair.First.SequenceNo == pair.Second.SequenceNo
                && pair.First.Basis == pair.Second.Basis && pair.First.DaysAfter == pair.Second.DaysAfter
                && Share(pair.First) == Share(pair.Second));
    }

    /// <summary>Value equality (a value object, so the header record compares by it): <see cref="SameTerms"/>.</summary>
    public bool Equals(SalesDueTerms? other) => other is not null && SameTerms(this, other);

    public override bool Equals(object? obj) => obj is SalesDueTerms other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var instalment in Instalments)
        {
            // decimal hashes by value, so "50.0" and "50" hash alike, as SameTerms compares them.
            hash.Add((instalment.SequenceNo, instalment.Basis, instalment.DaysAfter, Share(instalment)));
        }

        return hash.ToHashCode();
    }

    private static decimal Share(SalesDueInstalment instalment) =>
        decimal.Parse(instalment.SharePercent, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

    /// <summary>Digits with at most one inner decimal point; no sign, exponent, space or group separator.</summary>
    private static bool IsPlainDecimal(string text)
    {
        var point = text.IndexOf('.', StringComparison.Ordinal);
        if (point == 0 || point == text.Length - 1 || text.IndexOf('.', point + 1) >= 0)
        {
            return false;
        }

        return text.All(character => character is >= '0' and <= '9' or '.');
    }
}

/// <summary>Why a due date cannot be computed (fail-closed; no date is guessed).</summary>
public enum SalesDueDateUnresolvedReason
{
    /// <summary>The order carries no due terms (SAT-D101: "vade eksik").</summary>
    NoTerms = 1,

    /// <summary>An instalment is a "sevk vadesi" and no shipment day was given (nothing shipped yet for it): that instalment is "bekliyor" (SAT-D156).</summary>
    ShipmentDateMissing = 2,
}

/// <summary>
/// One instalment's due date with its sequence and share. SAT-D156 (EK21): a mixed plan resolves instalment by instalment — a
/// "nokta vade" instalment always has its date; a "sevk vadesi" instalment without a shipment day has no date and is
/// <see cref="Pending"/> (<see cref="SalesDueDateUnresolvedReason.ShipmentDateMissing"/>, "bekliyor"), without holding back the
/// others. Exactly one of <see cref="DueDate"/> and <see cref="Pending"/> is set.
/// </summary>
public sealed record SalesDueDate(int SequenceNo, DateOnly? DueDate, string SharePercent, SalesDueDateUnresolvedReason? Pending = null)
{
    public bool IsResolved => DueDate is not null;
}

/// <summary>
/// The calculator's answer: every instalment, each with its date or its pending reason (SAT-D156); <see cref="Unresolved"/> is
/// <see cref="SalesDueDateUnresolvedReason.NoTerms"/> when the order has no terms (no instalment at all), else
/// <see cref="SalesDueDateUnresolvedReason.ShipmentDateMissing"/> while ANY instalment is pending, else null.
/// </summary>
public sealed record SalesDueDateResult(IReadOnlyList<SalesDueDate> DueDates, SalesDueDateUnresolvedReason? Unresolved)
{
    /// <summary>Every instalment has its date.</summary>
    public bool IsResolved => Unresolved is null;
}

/// <summary>One instalment's amount (SAT-D124): its sequence and the amount at the money scale.</summary>
public sealed record SalesDueAmount(int SequenceNo, Money Amount);

/// <summary>
/// The ONE deterministic backend calculator of an order's due dates and instalment amounts (SAT-D129, SAT-D131, SAT-D124,
/// SAT-D125). Due date: per instalment its base date — the order's business date ("nokta vade") or the shipment's Europe/Istanbul
/// day ("sevk vadesi", SAT-D10); in the invoice-first flow the invoice date stands in for the shipment day of a "sevk vadesi"
/// (SAT-D131, <see cref="ComputeInvoiceFirst"/>) while a "nokta vade" stays the order's date — plus its whole calendar days;
/// nothing moves off a weekend or holiday (SAT-D125). A base date the caller does not have (nothing shipped yet) is
/// that instalment's pending reason (SAT-D156: per instalment, the others keep their dates), never a guess. Amount: every instalment but the last is the total × its share, rounded half away from zero to the total's money scale (SAT-D11's one
/// rounding, <see cref="Money.Round"/>); the LAST instalment takes the remainder so the instalments add up to the total exactly
/// (SAT-D124). The screen never computes either.
/// </summary>
public static class SalesDueDateCalculator
{
    /// <summary>SAT-D157 (EK21): the user's message for a split whose last instalment would fall below zero (verbatim phrase).</summary>
    public const string AmountNotSplittableMessage =
        "Tutar bu kadar taksite bölünemez; daha az taksit girin (kuruşa yuvarlanan taksitler son taksite eksi tutar bırakıyor).";

    /// <summary>
    /// The shipment-first flow (the default, SAT-D94 (1)): a "nokta vade" counts from <paramref name="orderDate"/>, a "sevk vadesi"
    /// from <paramref name="shipmentDate"/> (the shipment's Europe/Istanbul day; null while nothing is shipped → unresolved).
    /// </summary>
    public static SalesDueDateResult Compute(SalesDueTerms? terms, DateOnly orderDate, DateOnly? shipmentDate) =>
        ComputeFrom(terms, orderDate, shipmentDate);

    /// <summary>
    /// SAT-D131: the invoice-first flow (depot delivery, SAT-D94 (2)), for the invoice draft (API-32). The invoice is cut before
    /// the shipment, so a "sevk vadesi" instalment counts from <paramref name="invoiceDate"/> (the same logic as the rate being
    /// taken at the invoice date in this flow, SAT-D90); a "nokta vade" instalment still counts from <paramref name="orderDate"/>.
    /// </summary>
    public static SalesDueDateResult ComputeInvoiceFirst(SalesDueTerms? terms, DateOnly orderDate, DateOnly invoiceDate) =>
        ComputeFrom(terms, orderDate, invoiceDate);

    private static SalesDueDateResult ComputeFrom(SalesDueTerms? terms, DateOnly orderDate, DateOnly? shipmentBaseDate)
    {
        if (terms is null)
        {
            return new SalesDueDateResult([], SalesDueDateUnresolvedReason.NoTerms);
        }

        // SAT-D156 (EK21): instalment by instalment — one instalment's missing shipment day never holds back another's date.
        var dueDates = new List<SalesDueDate>(terms.Instalments.Count);
        foreach (var instalment in terms.Instalments)
        {
            DateOnly? basisDate = instalment.Basis switch
            {
                SalesDueBasis.OrderDate => orderDate,
                SalesDueBasis.ShipmentDate => shipmentBaseDate,
                _ => throw new InvalidOperationException("Vade tipi tanımlı değil."),
            };
            dueDates.Add(basisDate is { } date
                ? new SalesDueDate(instalment.SequenceNo, date.AddDays(instalment.DaysAfter), instalment.SharePercent)
                : new SalesDueDate(instalment.SequenceNo, null, instalment.SharePercent, SalesDueDateUnresolvedReason.ShipmentDateMissing));
        }

        return new SalesDueDateResult(dueDates,
            dueDates.Any(dueDate => !dueDate.IsResolved) ? SalesDueDateUnresolvedReason.ShipmentDateMissing : null);
    }

    /// <summary>
    /// SAT-D124: the total split over the instalments — each but the last rounded to the total's scale, the last the remainder.
    /// A single instalment is the total itself (no rounding difference arises, EK18). The total must not be negative. Should the
    /// remainder fall below zero (many tiny shares rounded up), the split is refused (<see cref="SalesDueTermsException"/>,
    /// <see cref="SalesDueTermsCodes.AmountNotSplittable"/>), never repaired.
    /// </summary>
    public static IReadOnlyList<SalesDueAmount> SplitAmount(SalesDueTerms terms, Money total)
    {
        ArgumentNullException.ThrowIfNull(terms);
        if (total.IsNegative)
        {
            throw new ArgumentOutOfRangeException(nameof(total), "Bölünecek tutar negatif olamaz.");
        }

        var amounts = new List<SalesDueAmount>(terms.Instalments.Count);
        var allocated = Money.Zero(total.CurrencyId, total.Scale);
        for (var index = 0; index < terms.Instalments.Count - 1; index++)
        {
            var instalment = terms.Instalments[index];
            var share = decimal.Parse(instalment.SharePercent, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
            var amount = Money.Round(total.Amount * share / 100m, total.CurrencyId, total.Scale);
            amounts.Add(new SalesDueAmount(instalment.SequenceNo, amount));
            allocated = allocated.Add(amount);
        }

        var last = total.Subtract(allocated);
        if (last.IsNegative)
        {
            // SAT-D157 (EK21): refused, never repaired (no automatic instalment reduction); the user enters fewer instalments.
            throw new SalesDueTermsException(SalesDueTermsCodes.AmountNotSplittable, AmountNotSplittableMessage);
        }

        amounts.Add(new SalesDueAmount(terms.Instalments[^1].SequenceNo, last));
        return amounts;
    }
}
