using System.Globalization;
using System.Numerics;
using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Finance.Contracts;

// DEC-FIN-014 (KILITLI, K1): "Müşteri POS vardır, tedarikçi POS yoktur; müşteri borcu brüt tutarla kapanır, banka
// neti ve komisyon ayrı izlenir. Komisyon şirket gideri veya müşteriye yansıtma politikası işlem bazlıdır."
//
// A customer POS collection is a manual receipt whose source amount is the GROSS card amount: the customer's debt is
// distributed (F-03) from that gross, exactly as for a bank transfer. The bank's commission and the net the bank
// settles are two separate facts of the same source and of its one money event — never a second source, a second
// money event or a smaller customer credit. There is no supplier POS: the outgoing payment contract has no POS member.
//
// What this does NOT do, deliberately:
//  - no journal: the posting (bank net / commission expense / customer gross) waits for the chart of accounts
//    (NEEDS-BUSINESS 5740501573) and the voucher approval (5740501655), as every Finance source does (ACC-V1);
//  - no customer charge for a "Customer" bearer: a charge is an open item, and in the first release an open item comes
//    only from an approved official invoice installment (OPEN-016, DEC-FIN-005). The choice is recorded, not applied;
//  - no commission rate, POS terminal or bank contract: none is decided anywhere.

/// <summary>Who bears the bank's POS commission, decided per transaction (DEC-FIN-014).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PosCommissionBearer>))]
public enum PosCommissionBearer
{
    /// <summary>The company's own expense; the customer's debt still closes at the gross amount.</summary>
    Company = 1,

    /// <summary>To be reflected to the customer by a separate document; recorded here, not applied (OPEN-016).</summary>
    Customer = 2,
}

/// <summary>
/// The POS facts of a receipt preparation. The gross is the preparation's own <c>money.amount</c>; the commission is
/// exact text in the same currency and within the same Settings scale (zero allowed, never rounded). The bank net is
/// never supplied: the server derives it as gross − commission, so the three can never disagree.
/// </summary>
public sealed record PosCollectionInput(string CommissionAmount, PosCommissionBearer CommissionBearer);

/// <summary>
/// The POS facts of a source as read. Both amounts follow the source amount's disclosure: an actor who may not read
/// the amount sees neither the commission nor the net. The gross is <c>source.amount</c>.
/// </summary>
public sealed record PosCollectionView(PosCommissionBearer CommissionBearer, AmountView Commission, AmountView BankNet);

/// <summary>DEC-FIN-014's arithmetic: exact decimal text, no floating point, no rounding, no local digit limit.</summary>
public static class PosCollectionRules
{
    public const string CommissionField = "pos.commissionAmount";
    public const string CommissionBearerField = "pos.commissionBearer";
    public const string CommissionInvalid = "pos_commission_invalid";
    public const string CommissionNotBelowGross = "pos_commission_not_below_gross";
    public const string CommissionBearerInvalid = "pos_commission_bearer_invalid";
    public const string ScaleExceeded = "settings_decimal_scale_exceeded";

    /// <summary>
    /// The commission against the gross under the approved scale: lexically a non-negative decimal, within the
    /// scale, and strictly below the gross (the bank net is positive). A known bearer is required.
    /// </summary>
    public static IReadOnlyList<ValidationIssue> Validate(string gross, PosCollectionInput pos, int approvedScale)
    {
        ArgumentNullException.ThrowIfNull(pos);
        var issues = new List<ValidationIssue>();
        if (!Enum.IsDefined(pos.CommissionBearer))
        {
            issues.Add(new ValidationIssue(CommissionBearerField, CommissionBearerInvalid));
        }

        if (!TryParse(pos.CommissionAmount, out var commission))
        {
            issues.Add(new ValidationIssue(CommissionField, CommissionInvalid));
        }
        else if (commission.Scale > approvedScale)
        {
            issues.Add(new ValidationIssue(CommissionField, ScaleExceeded));
        }
        else if (!TryBankNet(gross, pos.CommissionAmount, out _))
        {
            issues.Add(new ValidationIssue(CommissionField, CommissionNotBelowGross));
        }

        return issues;
    }

    /// <summary>
    /// Bank net = gross − commission, as exact text at the larger of the two scales. False when either text is not a
    /// decimal or the net would not be positive.
    /// </summary>
    public static bool TryBankNet(string? gross, string? commission, out string bankNet)
    {
        bankNet = string.Empty;
        if (!TryParse(gross, out var g) || !TryParse(commission, out var c))
        {
            return false;
        }

        var scale = Math.Max(g.Scale, c.Scale);
        var net = Align(g, scale) - Align(c, scale);
        if (net.Sign <= 0)
        {
            return false;
        }

        bankNet = Format(net, scale);
        return true;
    }

    private readonly record struct Exact(BigInteger Unscaled, int Scale);

    /// <summary>Digits with at most one inner '.', nothing else (no sign, exponent, separator or whitespace).</summary>
    private static bool TryParse(string? text, out Exact value)
    {
        value = default;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var point = -1;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '.' && point < 0 && index > 0 && index < text.Length - 1)
            {
                point = index;
                continue;
            }

            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        var digits = point < 0 ? text : text.Remove(point, 1);
        value = new Exact(BigInteger.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture),
            point < 0 ? 0 : text.Length - point - 1);
        return true;
    }

    private static BigInteger Align(Exact value, int scale) => value.Unscaled * BigInteger.Pow(10, scale - value.Scale);

    private static string Format(BigInteger value, int scale)
    {
        var digits = value.ToString(CultureInfo.InvariantCulture);
        if (scale == 0)
        {
            return digits;
        }

        if (digits.Length <= scale)
        {
            digits = digits.PadLeft(scale + 1, '0');
        }

        return digits.Insert(digits.Length - scale, ".");
    }
}
