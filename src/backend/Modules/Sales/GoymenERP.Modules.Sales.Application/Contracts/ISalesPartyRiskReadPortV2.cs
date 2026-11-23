using System.Diagnostics.CodeAnalysis;

namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>
/// MOD-01, plan S0 (b) (Satış risk/sevk kapısı planı v1 §S0; SAT-D311; FNS-D53): the second version of the Sales-declared customer
/// risk read port. It answers everything <see cref="ISalesPartyRiskReadPortV1"/> answers and, in the SAME owner answer and the SAME
/// read snapshot as <see cref="SalesPartyRiskReadAnswerV2.OpenReceivables"/>, which of the Sales invoices Sales asks about the
/// customer-finance owner has PROCESSED into those open receivables (admitted; a collected invoice stays processed).
/// <para>
/// Why one answer: a finalized Sales invoice leaves "shipped, not invoiced" only once Finance has processed it into the open
/// receivables (SAT-D311: until then it stays inside shipped-unbilled as the <c>InvoicedAwaitingFinance</c> sub-row). Two separate
/// reads could see the invoice in neither component or in both. With this answer every finalized share is in exactly one: the
/// receivables (processed) or shipped-unbilled (not processed). FNS-D53: an invoice whose reversal Finance has processed is NOT
/// processed — it has left the open receivables in the same answer — so Sales counts its shipment as shipped-unbilled again.
/// </para>
/// <para>
/// The same owner rules as V1: the owner implements it over its own company-filtered store and registers it in its own module;
/// Sales never reads another module's tables and keeps no copy. The port carries NO authority (the consumer measures its own
/// permission before and after asking), reads under the caller's server-resolved company only and CREATES NOTHING. The set is a
/// membership answer about the asked ids only: it is bounded by the query, never the customer's whole invoice history.
/// </para>
/// <para>
/// Opened by Sales (SAT-S3); the customer-finance owner fills it (FIN-S20). Until the owner registers it and the Sales risk check
/// is switched to it, the risk check keeps V1 and answers a customer with a finalized share fail-closed
/// (<c>finance_processed_set_unavailable</c>).
/// </para>
/// </summary>
public interface ISalesPartyRiskReadPortV2
{
    /// <summary>
    /// The risk inputs of the query's customer account in the context company, with the processed membership of the query's
    /// Sales invoice ids read in the same snapshot as the open receivables.
    /// </summary>
    Task<SalesPartyRiskReadAnswerV2> ReadPartyRiskAsync(SalesPartyRiskReadQueryV2 query, CancellationToken cancellationToken = default);
}

/// <summary>
/// The V2 read request: the customer (party and company account) and the Sales invoice ids whose processed state Sales needs —
/// the finalized invoices of the shares Sales counts (none, when no share is on a finalized invoice). An invoice Sales has
/// cancelled after finalization may be named too, since Finance may not have processed its reversal yet (FNS-D53: never in two
/// components at once; how the consumer counts it is the consumer's rule, not this contract's). Each id appears once; at most
/// <see cref="SalesPartyRiskReadQueryV2.MaximumSalesInvoiceIds"/> ids (one read's share bound).
/// </summary>
public sealed record SalesPartyRiskReadQueryV2(Guid PartyId, Guid CompanyAccountId, IReadOnlyList<Guid> SalesInvoiceIds)
{
    /// <summary>
    /// The most Sales invoice ids one V2 read names — the risk read's own share bound (a technical response bound, not a
    /// business policy).
    /// </summary>
    public const int MaximumSalesInvoiceIds = SalesDispatchApiV1.MaximumCandidateShares;

    /// <summary>
    /// Whether <paramref name="query"/> is a request the owner may answer: a party and an account, at most
    /// <see cref="MaximumSalesInvoiceIds"/> invoice ids, none empty, none repeated. The owner answers anything else
    /// <see cref="SalesPartyRiskProcessedSetStateV2.Unavailable"/>.
    /// </summary>
    public static bool IsValid([NotNullWhen(true)] SalesPartyRiskReadQueryV2? query) =>
        query is not null
        && query.PartyId != Guid.Empty
        && query.CompanyAccountId != Guid.Empty
        && query.SalesInvoiceIds is not null
        && query.SalesInvoiceIds.Count <= MaximumSalesInvoiceIds
        && query.SalesInvoiceIds.All(id => id != Guid.Empty)
        && query.SalesInvoiceIds.Distinct().Count() == query.SalesInvoiceIds.Count;
}

/// <summary>Whether the owner answered the processed set.</summary>
public enum SalesPartyRiskProcessedSetStateV2
{
    /// <summary>
    /// The owner cannot answer (its own failure, an invalid query, open receivables not read): fail-closed — the consumer never
    /// reads it as "nothing processed".
    /// </summary>
    Unavailable = 0,

    /// <summary>The owner's answer in the open receivables' snapshot; an empty list is a recorded "none of the asked ids processed".</summary>
    Available = 1,
}

/// <summary>
/// The processed membership of the asked Sales invoice ids: its state, the owner's stable source name, the asked ids the owner
/// has processed into <see cref="SalesPartyRiskReadAnswerV2.OpenReceivables"/> (each once, only asked ids; an asked id missing
/// here is NOT processed — never admitted, or reversed per FNS-D53) and the owner's stable reason when it is not available.
/// </summary>
public sealed record SalesPartyRiskProcessedInvoicesV2(
    SalesPartyRiskProcessedSetStateV2 State,
    string Source,
    IReadOnlyList<Guid> ProcessedSalesInvoiceIds,
    string? ReasonCode);

/// <summary>
/// The owner's V2 answer for one customer account of the context company: the V1 figures (same meaning, same states) and the
/// processed set read in the same snapshot as <see cref="SalesPartyRiskReadAnswerV2.OpenReceivables"/>.
/// </summary>
public sealed record SalesPartyRiskReadAnswerV2(
    Guid CompanyId,
    Guid PartyId,
    Guid CompanyAccountId,
    SalesPartyRiskFigureV1 OpenReceivables,
    SalesPartyRiskFigureV1 CreditLimit,
    SalesPartyRiskFigureV1 Collateral,
    SalesPartyRiskProcessedInvoicesV2 ProcessedSalesInvoices)
{
    /// <summary>The V1 figures of this answer, so the consumer validates them with the V1 rules unchanged.</summary>
    public SalesPartyRiskReadAnswerV1 ToV1() => new(CompanyId, PartyId, CompanyAccountId, OpenReceivables, CreditLimit, Collateral);

    /// <summary>
    /// Whether <paramref name="answer"/>'s processed set is well formed for <paramref name="query"/> (the consumer reads anything
    /// else as Unavailable, never as "none processed"): the same party and account; a non-blank source; when available, no
    /// reason, only asked ids, each once, and an AVAILABLE open receivables figure (plan S0 (b): the set means nothing without
    /// the receivables it was read with); when unavailable, no ids and a non-blank reason. The V1 figures are validated by the
    /// consumer's own V1 rules through <see cref="ToV1"/>.
    /// </summary>
    public static bool IsProcessedSetWellFormed(SalesPartyRiskReadAnswerV2? answer, SalesPartyRiskReadQueryV2? query)
    {
        if (answer is null || !SalesPartyRiskReadQueryV2.IsValid(query) || answer.PartyId != query.PartyId
            || answer.CompanyAccountId != query.CompanyAccountId)
        {
            return false;
        }

        var processed = answer.ProcessedSalesInvoices;
        if (processed is null || string.IsNullOrWhiteSpace(processed.Source) || processed.ProcessedSalesInvoiceIds is null)
        {
            return false;
        }

        var asked = query.SalesInvoiceIds.ToHashSet();
        return processed.State switch
        {
            SalesPartyRiskProcessedSetStateV2.Available =>
                processed.ReasonCode is null
                && answer.OpenReceivables is { State: SalesPartyRiskFigureStateV1.Available }
                && processed.ProcessedSalesInvoiceIds.Distinct().Count() == processed.ProcessedSalesInvoiceIds.Count
                && processed.ProcessedSalesInvoiceIds.All(id => asked.Contains(id)),
            SalesPartyRiskProcessedSetStateV2.Unavailable =>
                processed.ProcessedSalesInvoiceIds.Count == 0 && !string.IsNullOrWhiteSpace(processed.ReasonCode),
            _ => false,
        };
    }
}
