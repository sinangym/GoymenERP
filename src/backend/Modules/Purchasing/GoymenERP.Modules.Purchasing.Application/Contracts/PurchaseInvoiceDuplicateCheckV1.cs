using GoymenERP.Modules.Purchasing.Domain.Invoices;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>
/// WP15 (DALGA_PLANI_v2 1.16): what the duplicate check does with a match. Only <see cref="Warn"/> exists: no decision says
/// a rule match blocks recording (NEEDS-SINAN WP15-1), so the answer is a warning and recording stays possible. The existing
/// database refusals (same ETTN; same supplier and exact official number) are not this check's and are unchanged.
/// </summary>
public static class PurchaseInvoiceDuplicateBehaviorV1
{
    public const string Warn = "warn";
}

/// <summary>One recorded invoice that looks like the checked one, with the rule code (<c>purchasing.invoice-duplicate.*</c>).</summary>
public sealed record PurchaseInvoiceDuplicateWarningV1(Guid InvoiceId, string Code, string OfficialNumber, DateOnly? InvoiceDate,
    string Link);

/// <summary><see cref="Truncated"/> is true when more matches exist than were returned.</summary>
public sealed record PurchaseInvoiceDuplicateCheckResponseV1(string Behavior, IReadOnlyList<PurchaseInvoiceDuplicateWarningV1> Warnings,
    bool Truncated);

/// <summary>The facts of an invoice that is about to be recorded. Payable is invariant-culture decimal text.</summary>
public sealed record PurchaseInvoiceDuplicateCheckRequestV1(Guid SupplierPartyId, string OfficialNumber, Guid? Ettn,
    DateOnly InvoiceDate, Guid CurrencyId, string Payable);

public interface IPurchaseInvoiceDuplicateCheckV1
{
    /// <summary>Checks facts before recording (import permission).</summary>
    Task<PurchaseInvoiceDuplicateCheckResponseV1> CheckAsync(PurchaseInvoiceDuplicateCheckRequestV1 request,
        CancellationToken cancellationToken = default);

    /// <summary>Checks an already recorded invoice against the others (read permission); 404 when it does not exist.</summary>
    Task<PurchaseInvoiceDuplicateCheckResponseV1> CheckRecordedAsync(Guid invoiceId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Purchasing-owned read of duplicate candidates in the resolved company: every recorded invoice with the ETTN, every invoice of
/// the supplier whose stored number spelling could normalize to the probed one, and every invoice of the supplier on the
/// invoice date. Untracked; writes nothing; the caller fences before and after.
/// </summary>
public interface IPurchasingInvoiceDuplicateCandidateSourceV1
{
    Task<IReadOnlyList<PurchaseInvoiceDuplicateCandidate>> ListDuplicateCandidatesAsync(PurchaseInvoiceDuplicateProbe probe,
        CancellationToken cancellationToken = default);
}
