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
/// invoice's business date.
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
    DateOnly BusinessDate);

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
/// advance, recorded on the credit. When more than one of the invoice's items still has an outstanding amount and the
/// return covers only part of them, the order in which the items are reduced is not decided: the acceptance is refused
/// with <c>finance_customer_return_installment_order_not_decided</c> and nothing is written.</item>
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
