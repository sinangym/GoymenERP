using System.Globalization;

namespace GoymenERP.Modules.Sales.Domain.Drafts;

/// <summary>
/// DEC-SAL-008 (KILITLI, verbatim): "Sipariş bazında araç/sevkiyat, sipariş tarihli nokta vade, fatura vadesi, taksit veya
/// karma model seçilebilir." — "Her sevkiyat kendi vadesini başlatabilir." The date an instalment's due date counts from:
/// <list type="bullet">
/// <item><see cref="InvoiceDate"/> — "fatura vadesi": N days after the (official) invoice date (SAT-D86).</item>
/// <item><see cref="ShipmentDate"/> — "araç/sevkiyat": N days after the shipment day, so every shipment starts its own due date.</item>
/// <item><see cref="OrderDate"/> — "sipariş tarihli nokta vade": N days after the order's business date — one fixed point,
/// whatever the invoice or shipment date.</item>
/// </list>
/// "Taksit" is more than one instalment; "karma" is instalments of different bases. Nothing here is a default: an order
/// without due terms has none, and its invoicing stays "vade eksik" (SAT-D101, fail-closed).
/// </summary>
public enum SalesDueBasis
{
    InvoiceDate = 1,
    ShipmentDate = 2,
    OrderDate = 3,
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
}

/// <summary>
/// DEC-SAL-008 / SAT-D86: an order's due terms — the "vade modeli" snapshot the order carries (the order owns it; no owner
/// catalogue of payment terms exists on main). One or more instalments, each with its own basis (<see cref="SalesDueBasis"/>),
/// whole calendar days after it and an exact share; the shares add up to exactly 100. Immutable; validated once, here.
/// <para>
/// What is NOT decided and therefore not chosen here (NEEDS-SINAN, fail-closed): whether a due date on a weekend or holiday
/// moves (calendar days are counted, nothing moves); how an instalment's AMOUNT is rounded and which instalment takes the
/// remainder cent (no amount is computed here — that is the invoice's, API-32/36); a default term per customer or company
/// (none: an order without terms has none). The two bounds below are technical guards of the arithmetic, not policies.
/// </para>
/// </summary>
public sealed class SalesDueTerms
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
                throw new SalesDueTermsException(SalesDueTermsCodes.BasisInvalid, "Vade başlangıcı tanımlı değil.", index);
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

    /// <summary>An instalment counts from the invoice date and none was given.</summary>
    InvoiceDateMissing = 2,

    /// <summary>An instalment counts from the shipment day and none was given.</summary>
    ShipmentDateMissing = 3,
}

/// <summary>One instalment's computed due date with its sequence and share.</summary>
public sealed record SalesDueDate(int SequenceNo, DateOnly DueDate, string SharePercent);

/// <summary>The calculator's answer: every instalment's due date, or why none could be computed.</summary>
public sealed record SalesDueDateResult(IReadOnlyList<SalesDueDate> DueDates, SalesDueDateUnresolvedReason? Unresolved)
{
    public bool IsResolved => Unresolved is null;
}

/// <summary>
/// SAT-D86 (EK13): "Vade, siparişin vade modelinden (DEC-SAL-008 anlık görüntüsü) ve resmî fatura tarihinden ERP tarafından
/// hesaplanır." The ONE deterministic backend calculator of an order's due dates: per instalment, its basis date plus its
/// whole calendar days. The invoice date is the official invoice date at matching (SAT-D86) or — for a proposal before any
/// invoice exists (SAT-D101: "vade başlangıcı fatura tarihine bağlı modelde öneri tarihi olarak bugün alınır") — the caller's
/// server business day; the shipment day is the shipment's Europe/Istanbul day (SAT-D10); the order date is the order's
/// business date. A basis date the caller does not have is <see cref="SalesDueDateResult.Unresolved"/>, never a guess. No
/// amount is computed (the invoice's, API-32/36) and nothing moves off a weekend or holiday (not decided; NEEDS-SINAN).
/// </summary>
public static class SalesDueDateCalculator
{
    public static SalesDueDateResult Compute(SalesDueTerms? terms, DateOnly orderDate, DateOnly? invoiceDate, DateOnly? shipmentDate)
    {
        if (terms is null)
        {
            return new SalesDueDateResult([], SalesDueDateUnresolvedReason.NoTerms);
        }

        var dueDates = new List<SalesDueDate>(terms.Instalments.Count);
        foreach (var instalment in terms.Instalments)
        {
            DateOnly basisDate;
            switch (instalment.Basis)
            {
                case SalesDueBasis.InvoiceDate when invoiceDate is { } invoice:
                    basisDate = invoice;
                    break;
                case SalesDueBasis.InvoiceDate:
                    return new SalesDueDateResult([], SalesDueDateUnresolvedReason.InvoiceDateMissing);
                case SalesDueBasis.ShipmentDate when shipmentDate is { } shipment:
                    basisDate = shipment;
                    break;
                case SalesDueBasis.ShipmentDate:
                    return new SalesDueDateResult([], SalesDueDateUnresolvedReason.ShipmentDateMissing);
                case SalesDueBasis.OrderDate:
                    basisDate = orderDate;
                    break;
                default:
                    throw new InvalidOperationException("Vade başlangıcı tanımlı değil.");
            }

            dueDates.Add(new SalesDueDate(instalment.SequenceNo, basisDate.AddDays(instalment.DaysAfter), instalment.SharePercent));
        }

        return new SalesDueDateResult(dueDates, null);
    }
}
