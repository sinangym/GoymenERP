namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>
/// MOD-01 (Satış hazırlık planı v1 §5): the Sales-declared read port the CUSTOMER-FINANCE OWNER answers with the risk inputs of
/// one customer account — its open receivables, its credit limit and its collateral (DEC-FIN-007 / DEC-FIN-009). The owner
/// implements it over its own company-filtered store and registers it in its own module (the
/// <see cref="ISalesOrderDeliveryReadPortV1"/> shape); Sales never reads another module's tables and keeps no copy.
/// <para>
/// The port carries NO authority: the consumer (the Sales risk-check read) measures its own permission before and after asking.
/// It reads under the caller's server-resolved company only — another company's records are invisible — and it CREATES NOTHING.
/// Every figure says whether its owner answered: an input without owner data on main is <see cref="SalesPartyRiskFigureStateV1.NoSource"/>
/// and an owner that cannot answer is <see cref="SalesPartyRiskFigureStateV1.Unavailable"/> — never a zero.
/// </para>
/// </summary>
public interface ISalesPartyRiskReadPortV1
{
    /// <summary>The risk inputs of <paramref name="partyId"/>'s <paramref name="companyAccountId"/> in the context company.</summary>
    Task<SalesPartyRiskReadAnswerV1> ReadPartyRiskAsync(Guid partyId, Guid companyAccountId, CancellationToken cancellationToken = default);
}

/// <summary>Whether the owner answered one figure.</summary>
public enum SalesPartyRiskFigureStateV1
{
    /// <summary>The owner cannot answer (its own failure, a malformed record): fail-closed, never a default.</summary>
    Unavailable = 0,

    /// <summary>No owner record of this figure exists on main yet (no credit-limit or collateral master): never zero.</summary>
    NoSource = 1,

    /// <summary>The owner's recorded figure; an empty amount list is a recorded zero (e.g. no open receivable).</summary>
    Available = 2,
}

/// <summary>
/// The owner's answer for one customer account of the context company. <paramref name="Collateral"/> is reported for the screen
/// only: it is a net-risk input (DEC-FIN-007 note, OPEN-010 haircut) and never enters the gross risk.
/// </summary>
public sealed record SalesPartyRiskReadAnswerV1(
    Guid CompanyId,
    Guid PartyId,
    Guid CompanyAccountId,
    SalesPartyRiskFigureV1 OpenReceivables,
    SalesPartyRiskFigureV1 CreditLimit,
    SalesPartyRiskFigureV1 Collateral);

/// <summary>
/// One figure: its state, the owner's stable source name, the amounts per currency (exact, never converted or rounded), whether
/// those amounts include taxes (null when the notion does not apply, as for a limit) and the owner's stable reason when it is
/// not available.
/// </summary>
public sealed record SalesPartyRiskFigureV1(
    SalesPartyRiskFigureStateV1 State,
    string Source,
    IReadOnlyList<SalesPartyRiskAmountV1> Amounts,
    bool? IncludesTax,
    string? ReasonCode);

/// <summary>One exact amount of a figure in one currency (the Settings currency identity).</summary>
public sealed record SalesPartyRiskAmountV1(Guid CurrencyId, decimal Amount);
