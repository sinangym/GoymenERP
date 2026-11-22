using System.Text;

namespace GoymenERP.Modules.Purchasing.Domain.Invoices;

/// <summary>Why an already recorded incoming invoice looks like the probed one (WP15, DALGA_PLANI_v2 1.16, YOL-D1).</summary>
public enum PurchaseInvoiceDuplicateReason
{
    /// <summary>The same ETTN. The database already refuses a second record with it; the check reports it before recording.</summary>
    SameEttn = 1,

    /// <summary>The same supplier and the same official number after <see cref="PurchaseInvoiceDuplicateRule.NormalizeNumber"/>
    /// (letter case, spaces and dashes ignored), the stored spelling being different from the probed one.</summary>
    SameSupplierAndNumber = 2,

    /// <summary>The same supplier, invoice date, currency and payable amount under a different official number.</summary>
    SameSupplierDateAndAmount = 3,
}

/// <summary>The facts of the invoice being checked. <see cref="Ettn"/> <see cref="Guid.Empty"/> means "no ETTN".</summary>
public sealed record PurchaseInvoiceDuplicateProbe(Guid SupplierPartyId, string OfficialNumber, Guid Ettn, DateOnly InvoiceDate,
    Guid CurrencyId, decimal Payable);

/// <summary>An already recorded incoming invoice as the duplicate rule sees it. A legacy record without stored amount facts
/// carries null date/currency/payable and then only takes part in the ETTN and number rules.</summary>
public sealed record PurchaseInvoiceDuplicateCandidate(Guid InvoiceId, Guid SupplierPartyId, string OfficialNumber, Guid Ettn,
    DateOnly? InvoiceDate, Guid? CurrencyId, decimal? Payable);

public sealed record PurchaseInvoiceDuplicateMatch(Guid InvoiceId, PurchaseInvoiceDuplicateReason Reason);

/// <summary>
/// WP15 (1.16): the deterministic, rule-based incoming invoice duplicate check. It decides only whether a recorded invoice
/// looks like the probed one and why; it never blocks. Whether a match should block recording is not decided
/// (ALS-D125; the record command asks for a reason): the caller shows a warning. The hard refusals that already exist (database uniqueness on
/// company + ETTN and on company + supplier + exact official number) are unchanged.
/// Rules, strongest first; one match per candidate:
/// (1) both ETTNs present and equal; (2) same supplier and equal normalized number; (3) same supplier, same invoice date,
/// same currency and an exactly equal payable amount (no tolerance: a tolerance is a policy that is not decided).
/// The probed invoice itself (<c>excludeInvoiceId</c>) is never a match.
/// </summary>
public static class PurchaseInvoiceDuplicateRule
{
    public static IReadOnlyList<PurchaseInvoiceDuplicateMatch> Evaluate(PurchaseInvoiceDuplicateProbe probe,
        IEnumerable<PurchaseInvoiceDuplicateCandidate> candidates, Guid? excludeInvoiceId = null)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(candidates);
        var number = NormalizeNumber(probe.OfficialNumber);
        var matches = new List<PurchaseInvoiceDuplicateMatch>();
        foreach (var candidate in candidates.DistinctBy(item => item.InvoiceId).OrderBy(item => item.InvoiceId))
        {
            if (candidate.InvoiceId == Guid.Empty || candidate.InvoiceId == excludeInvoiceId) continue;
            if (Reason(probe, number, candidate) is { } reason) matches.Add(new(candidate.InvoiceId, reason));
        }

        return matches.OrderBy(match => match.Reason).ThenBy(match => match.InvoiceId).ToArray();
    }

    /// <summary>Upper-cased (invariant), with every whitespace character and dash removed. Empty when nothing is left.</summary>
    public static string NormalizeNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character) || character is '-' or '‐' or '‑' or '‒' or '–' or '—') continue;
            builder.Append(char.ToUpperInvariant(character));
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static PurchaseInvoiceDuplicateReason? Reason(PurchaseInvoiceDuplicateProbe probe, string number,
        PurchaseInvoiceDuplicateCandidate candidate)
    {
        if (probe.Ettn != Guid.Empty && candidate.Ettn == probe.Ettn) return PurchaseInvoiceDuplicateReason.SameEttn;
        if (probe.SupplierPartyId == Guid.Empty || candidate.SupplierPartyId != probe.SupplierPartyId) return null;
        if (number.Length > 0 && NormalizeNumber(candidate.OfficialNumber) == number)
            return PurchaseInvoiceDuplicateReason.SameSupplierAndNumber;
        if (probe.InvoiceDate != default && candidate.InvoiceDate == probe.InvoiceDate
            && probe.CurrencyId != Guid.Empty && candidate.CurrencyId == probe.CurrencyId
            && candidate.Payable is { } payable && payable == probe.Payable)
            return PurchaseInvoiceDuplicateReason.SameSupplierDateAndAmount;
        return null;
    }

    /// <summary>The wire code of a reason (<c>purchasing.invoice-duplicate.*</c>).</summary>
    public static string Code(PurchaseInvoiceDuplicateReason reason) => reason switch
    {
        PurchaseInvoiceDuplicateReason.SameEttn => "purchasing.invoice-duplicate.same-ettn",
        PurchaseInvoiceDuplicateReason.SameSupplierAndNumber => "purchasing.invoice-duplicate.same-supplier-number",
        PurchaseInvoiceDuplicateReason.SameSupplierDateAndAmount => "purchasing.invoice-duplicate.same-supplier-date-amount",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason.ToString(), null),
    };
}
