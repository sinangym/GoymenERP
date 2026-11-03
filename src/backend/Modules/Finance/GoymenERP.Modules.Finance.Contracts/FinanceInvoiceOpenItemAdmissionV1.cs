using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Finance.Contracts;

// F2a / MOD-04 (FNS-D2, FNS-D7, DEC-FIN-005, SAT-D85/D86/D172, ALS-D7/D24/D28/D41): the Finance-owned port through which a
// FINALISED invoice of Sales (outgoing) or Purchasing (incoming) becomes Finance open items — one per installment. The
// invoice core (import from Mikro, matching, finalisation) belongs to the producing module (ALS-D44); Finance only admits.
// Before the platform outbox dispatcher (ERP-000F) this is a synchronous, idempotent port (SINAN 10-21 No 55): the producer
// may retry the same call any number of times and gets one set of open items.

/// <summary>Which module finalised the invoice; it fixes the open items' source kind and direction (MOD-15).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FinanceInvoiceProducerV1>))]
public enum FinanceInvoiceProducerV1
{
    /// <summary>A sales invoice: receivable (<c>official.invoice-installment.v1</c>).</summary>
    Sales = 1,

    /// <summary>A purchase invoice: payable (<c>official.incoming-invoice-installment.v1</c>).</summary>
    Purchasing = 2,
}

/// <summary>
/// One installment of the invoice's payable amount (KDV dahil, tevkif edilen KDV düşülmüş — No 15, K8). The producer
/// computes the plan (SAT-D86/D124, ALS-D24/D28); Finance stores it exactly. <paramref name="FunctionalAmount"/> is the
/// installment's functional-currency (TL) equivalent at the invoice's own rate when the invoice is in a foreign currency
/// (SAT-D172; ALS-D41: the supplier invoice's rate), computed by the producer's calculator; null for a TL invoice.
/// </summary>
public sealed record FinanceInvoiceInstallmentV1(
    Guid OpenItemId,
    Guid InstallmentId,
    int Sequence,
    DateOnly DueOn,
    string Amount,
    string? FunctionalAmount);

/// <summary>
/// A finalised invoice to admit. <paramref name="AdmissionId"/> is the producer's id for this admission (the receipt's id);
/// <paramref name="InvoiceVersion"/> the finalised version. <paramref name="ExchangeRate"/> is the invoice's single rate
/// (SAT-D172 / ALS-D41) for a foreign-currency invoice and null for a TL invoice; it is present exactly when every
/// installment carries a <see cref="FinanceInvoiceInstallmentV1.FunctionalAmount"/>.
/// </summary>
public sealed record FinanceInvoiceOpenItemAdmissionV1(
    Guid AdmissionId,
    FinanceInvoiceProducerV1 Producer,
    Guid InvoiceId,
    string InvoiceVersion,
    Guid PartyId,
    Guid CompanyAccountId,
    Guid CurrencyId,
    string? ExchangeRate,
    IReadOnlyList<FinanceInvoiceInstallmentV1> Installments);

/// <summary>
/// The Finance side of MOD-04. It writes open items only. The producing module measures its own finalisation authority
/// (e.g. <c>sales.invoices.finalize</c>); Finance additionally fences every admission with the host-owned
/// <see cref="IFinanceInvoiceOpenItemAdmissionAuthorityV1"/> (fail closed: an unmapped composition is unavailable), and
/// the caller's company and actor must equal the server context. Not an HTTP surface. The producer decides whether the
/// invoice currency is foreign (rate and functional amounts present) or the functional currency (both absent).
/// <list type="bullet">
/// <item>The same admission (same key, same payload, same actor) replays its first answer.</item>
/// <item>An invoice already admitted with the same rows answers as a reuse (no second open item); with other rows or another
/// version it is refused with <c>finance_invoice_already_admitted</c> — a changed invoice is cancelled and re-issued by its
/// owner (SAT-D96, F2b), never silently replaced.</item>
/// </list>
/// </summary>
public interface IFinanceInvoiceOpenItemAdmissionPortV1
{
    Task<FinanceOpenItemAdmissionResult> AdmitAsync(
        FinanceContext context,
        CommandMetadata metadata,
        FinanceInvoiceOpenItemAdmissionV1 invoice,
        CancellationToken cancellationToken);
}

/// <summary>
/// Host-owned fresh authority measurement for one invoice admission (the F-03 <c>IFinanceOpenItemSourceAuthority</c>
/// pattern). No permission string or grant is declared here; which grant answers it is decided when a producer is wired,
/// and an unmapped production composition must remain unavailable.
/// </summary>
public interface IFinanceInvoiceOpenItemAdmissionAuthorityV1
{
    Task<PermissionDecision> CheckAsync(
        FinanceContext context,
        FinanceInvoiceProducerV1 producer,
        Guid invoiceId,
        CancellationToken cancellationToken);
}
