using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Finance.Contracts;

// F-019 / MOD-04 / API-40 (SAT-D288, SAT-D291, SAT-D294, DEC-FIN-003): the second version of Finance's invoice acceptance
// port adds a document kind the first version never knew — the customer's return invoice. Version 1
// (IFinanceInvoiceOpenItemAdmissionPortV1, IFinanceInvoiceOpenItemCancellationPortV1) is kept unchanged; a producer moves
// to version 2 only for the new kind.
//
// A finalised customer return invoice is its own document (DEC-FIN-003, SAT-D288): it never edits or deletes the original
// invoice's open items. Finance records one separate reversing credit for it (SAT-D291 (1)); the credit first reduces the
// outstanding amount of the original invoice's receivable items and the rest — the part the customer had already paid —
// stays as the customer's advance (SAT-D294 (1)-(2)). Finance makes that split (SAT-D294 (4)); the producer only reports
// the return amount and the original invoice link. No party-card movement is written (SAT-D291 (3)); the producer never
// writes Finance tables (SAT-D291 (4)).
//
// FIN-S10 (FNS-D44, FNS-D81): a partial return over several outstanding installments is no longer refused. The user chooses
// the installments (FNS-D44): the screen is prefilled from the nearest due date by Finance's one deterministic calculator,
// read through IFinanceCustomerReturnInstallmentReadPortV2, and the producer reports the user's selection as Allocations.
// The selection must total the return amount exactly and no installment may take more than its open amount, otherwise the
// return is refused; the server never replaces a submitted selection with the prefill. FNS-D81: the installment choice is
// the user's; when no selection is reported over several outstanding installments the return is refused
// (finance_customer_return_allocation_required) and no automatic nearest-due order (FNS-D54) is applied. Only an invoice
// with exactly one outstanding installment may omit the selection.

/// <summary>The document kinds version 2 accepts. Only the customer return invoice exists today.</summary>
public enum FinanceInvoiceAcceptanceKindV2
{
    /// <summary>The customer's return invoice against one of our finalised sales invoices (API-40).</summary>
    CustomerReturn = 1,
}

/// <summary>
/// One document reported for acceptance. <paramref name="AcceptanceId"/> is the producer's id for this acceptance (the
/// receipt's and the reversing credit's id); <paramref name="DocumentId"/> and <paramref name="DocumentVersion"/> identify
/// the return invoice at the producer. <paramref name="OriginalInvoiceId"/> and <paramref name="OriginalInvoiceVersion"/>
/// are the admitted sales invoice being returned against (SAT-D288: the link is mandatory) and act as the expected version:
/// another admitted version is refused. <paramref name="Amount"/> is the return amount in the original invoice's currency,
/// exact text, already rounded by the producer (SAT-D292); <paramref name="FunctionalAmount"/> is its TL equivalent and is
/// present exactly when the original invoice is in a foreign currency. <paramref name="BusinessDate"/> is the return
/// invoice's business date. <paramref name="Allocations"/> is the user's installment selection for a partial return
/// (FNS-D44): absent (null, and then left out of the payload so earlier payloads keep their hash) when the user made no
/// selection or the return covers the whole outstanding amount.
/// </summary>
public sealed record FinanceInvoiceAcceptanceV2(
    Guid AcceptanceId,
    FinanceInvoiceProducerV1 Producer,
    FinanceInvoiceAcceptanceKindV2 Kind,
    Guid DocumentId,
    string DocumentVersion,
    Guid OriginalInvoiceId,
    string OriginalInvoiceVersion,
    Guid PartyId,
    Guid CompanyAccountId,
    Guid CurrencyId,
    string Amount,
    string? FunctionalAmount,
    DateOnly BusinessDate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<FinanceInvoiceAcceptanceAllocationV2>? Allocations = null);

/// <summary>
/// One installment the user chose for a partial return (FNS-D44): <paramref name="OpenItemId"/> is an outstanding open item
/// of the original invoice and <paramref name="Amount"/> the exact part of the return it takes, never above the item's open
/// amount and never finer than the return amount's own scale. The selection's total equals the return amount exactly.
/// </summary>
public sealed record FinanceInvoiceAcceptanceAllocationV2(Guid OpenItemId, string Amount);

/// <summary>
/// The Finance side of the customer return acceptance (API-11 v2). Synchronous, idempotent and in-process (no HTTP
/// surface), like version 1; the producer commits its own finalisation first and then calls this with the same key until
/// it is answered (SAT-D208 "ayrı adım").
/// <list type="bullet">
/// <item>The same key, payload and actor replay the first answer; the same <c>AcceptanceId</c> under a new key replays when
/// the payload is the same and is refused otherwise. One return document is accepted once.</item>
/// <item>An original invoice Finance never admitted, another version of it, a cancelled one, or one whose party, company
/// account or currency differs from the report is refused; so is a total of returns above the invoice's original total.</item>
/// <item>The credit reduces the original invoice's items' outstanding amounts first; the remainder is the customer's
/// advance, recorded on the credit. A return that covers the whole outstanding amount closes every outstanding item and
/// takes no selection (<c>finance_customer_return_allocation_not_applicable</c>).</item>
/// <item>A partial return reduces exactly the installments the user selected (FNS-D44): an item that is not an outstanding
/// item of the invoice (<c>finance_customer_return_allocation_item_unknown</c>), a part above an item's open amount
/// (<c>finance_customer_return_allocation_exceeds_open_amount</c>) or a selection whose total is not exactly the return
/// amount (<c>finance_customer_return_allocation_total_mismatch</c>) is refused and nothing is written. The selection is
/// never replaced by the prefill. Without a selection (FNS-D81) a return over several outstanding installments is refused
/// (<c>finance_customer_return_allocation_required</c>); only the invoice's single outstanding installment is reduced
/// without one. The nearest-due prefill is a suggestion the user confirms, never applied on its own.</item>
/// </list>
/// Fenced by the host-owned <see cref="IFinanceInvoiceAcceptanceAuthorityV2"/> before any read and right before the save;
/// the caller's company and actor must equal the server context.
/// </summary>
public interface IFinanceInvoiceAcceptancePortV2
{
    Task<FinanceInvoiceAcceptanceResultV2> AcceptAsync(
        FinanceContext context,
        CommandMetadata metadata,
        FinanceInvoiceAcceptanceV2 acceptance,
        CancellationToken cancellationToken);
}

/// <summary>One original open item reduced by the reversing credit, and by how much.</summary>
public sealed record FinanceInvoiceAcceptanceReductionV2(Guid OpenItemId, string Amount);

/// <summary>
/// The answer. On success <paramref name="CreditId"/> is the reversing credit (the acceptance id),
/// <paramref name="Reductions"/> the items it reduced and <paramref name="AdvanceAmount"/> the part left as the customer's
/// advance ("0" when none); on a refusal they are null and empty.
/// </summary>
public sealed record FinanceInvoiceAcceptanceResultV2(
    CommandOutcome Outcome,
    Guid? CreditId,
    IReadOnlyList<FinanceInvoiceAcceptanceReductionV2> Reductions,
    string? AdvanceAmount,
    bool IsReplay,
    string OriginalCorrelationId,
    IReadOnlyList<ValidationIssue> Issues);

/// <summary>
/// Host-owned fresh authority measurement for one acceptance (the F-03 pattern). No permission string or grant is declared
/// here; which grant answers it is decided by the host, and an unmapped composition must remain unavailable.
/// </summary>
public interface IFinanceInvoiceAcceptanceAuthorityV2
{
    Task<PermissionDecision> CheckAsync(
        FinanceContext context,
        FinanceInvoiceProducerV1 producer,
        FinanceInvoiceAcceptanceKindV2 kind,
        Guid documentId,
        CancellationToken cancellationToken);
}

/// <summary>
/// The prefill question of the return screen (FNS-D44): the return document being finalised (<paramref name="Producer"/>,
/// <paramref name="Kind"/>, <paramref name="DocumentId"/> — the authority is measured for it), the admitted original invoice
/// and its expected version, and the return amount as exact text.
/// </summary>
public sealed record FinanceCustomerReturnInstallmentQueryV2(
    FinanceInvoiceProducerV1 Producer,
    FinanceInvoiceAcceptanceKindV2 Kind,
    Guid DocumentId,
    Guid OriginalInvoiceId,
    string OriginalInvoiceVersion,
    string Amount);

/// <summary>
/// One outstanding installment of the original invoice, nearest due first: its due date, original and open amounts, and the
/// part of the return the deterministic calculator puts on it (<paramref name="PrefillAmount"/>, "0" when none).
/// </summary>
public sealed record FinanceCustomerReturnInstallmentV2(
    Guid OpenItemId,
    DateOnly DueOn,
    string OriginalAmount,
    string OutstandingAmount,
    string PrefillAmount);

/// <summary>
/// The prefill answer. <paramref name="Outcome"/> is <see cref="CommandOutcome.Succeeded"/> when the prefill was computed;
/// <see cref="CommandOutcome.ValidationFailed"/> with <paramref name="Issues"/> (the same codes the acceptance would answer)
/// when the question cannot be answered; <see cref="CommandOutcome.Denied"/> or <see cref="CommandOutcome.Unavailable"/>
/// from the authority fence. <paramref name="SelectionAllowed"/> is true exactly when the return is partial, i.e. the user
/// may change the prefill and the producer reports the chosen installments as allocations; a whole return takes none.
/// <paramref name="AppliedAmount"/> is the part that reduces outstanding amounts and <paramref name="AdvanceAmount"/> the part
/// left as the customer's advance ("0" when none); both are null on a refusal.
/// </summary>
public sealed record FinanceCustomerReturnInstallmentPrefillV2(
    CommandOutcome Outcome,
    IReadOnlyList<FinanceCustomerReturnInstallmentV2> Installments,
    bool SelectionAllowed,
    string? AppliedAmount,
    string? AdvanceAmount,
    IReadOnlyList<ValidationIssue> Issues);

/// <summary>
/// The read side of FNS-D44: the outstanding installments of an admitted original invoice and the nearest-due prefill of a
/// return amount, computed by the same deterministic Finance calculator the acceptance uses when no selection is reported.
/// In-process (no HTTP surface), writes nothing, fenced by the same host-owned
/// <see cref="IFinanceInvoiceAcceptanceAuthorityV2"/> as the acceptance; the caller's company and actor must equal the
/// server context.
/// </summary>
public interface IFinanceCustomerReturnInstallmentReadPortV2
{
    Task<FinanceCustomerReturnInstallmentPrefillV2> ReadAsync(
        FinanceContext context,
        FinanceCustomerReturnInstallmentQueryV2 query,
        CancellationToken cancellationToken);
}
