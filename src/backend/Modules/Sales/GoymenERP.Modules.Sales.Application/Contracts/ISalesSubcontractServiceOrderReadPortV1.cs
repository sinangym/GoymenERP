namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>
/// S-3 (<c>docs/handoffs/URETIM_SATIS_DEVIR_v1.md</c>) / SAT-D175…D176 (EK25) / URT-D16 / URT-D22: the Sales-owned read port the
/// Production owner asks before it records or approves a plan that processes a customer's goods ("gelen fason"). Input: the company
/// and the order id. It answers the order's facts as the Sales owner stored them — never a decision for the consumer:
/// <list type="bullet">
/// <item>whether the order is a subcontract service order at all (<see cref="SalesSubcontractServiceOrderOutcomeV1.NotSubcontractService"/>
/// otherwise — a normal order never feeds a customer-goods plan);</item>
/// <item>its internal approval (<see cref="SalesSubcontractServiceOrderV1.IsApproved"/>: the CURRENT revision is internally approved)
/// and, separately, its customer acceptance (<see cref="SalesSubcontractServiceOrderV1.CustomerAcceptanceState"/>);</item>
/// <item>the customer (party + company account) — the owner of the processed goods (SAT-D175: no separate owner field; URT-D22
/// matches the customer goods by this customer);</item>
/// <item>the service lines (product, quantity as exact text and value, order unit) of the revision the order CONTINUES ON
/// (<see cref="SalesSubcontractServiceOrderV1.BasisRevision"/>, <see cref="SalesSubcontractServiceOrderV1.Basis"/>): the current
/// revision, or — SAT-D115 (EK14), the line port's rule — when the customer REJECTED the last approved revision, the revision the
/// customer last accepted (never the rejected quantities);</item>
/// <item>whether the order is open (lifecycle Draft) or closed (cancelled / force-closed).</item>
/// </list>
/// Fail-closed: a company other than the server's resolved company, a missing company context, or any read fault answers
/// <see cref="SalesSubcontractServiceOrderOutcomeV1.Unreadable"/> explicitly (never "not found", never an empty order). Another
/// company's order is invisible under the tenant filter (<see cref="SalesSubcontractServiceOrderOutcomeV1.NotFound"/>). No price,
/// discount, currency, tax or payment member exists on the answer (the consumer is not a price reader). Reading creates nothing;
/// the consumer measures its own authority before asking. Versioned by <see cref="ContractVersion"/> and pinned by a contract test.
/// The host adapter that binds this port to the Production owner is the Production lane's (S-3 note: "Üretim tarafı portları Satış
/// beklemeden yazılır ve Satış portu gelene kadar fail-closed çalışır").
/// </summary>
public interface ISalesSubcontractServiceOrderReadPortV1
{
    /// <summary>The contract version of this port's answer shape.</summary>
    public const string ContractVersion = "sales.subcontract-service-order.v1";

    Task<SalesSubcontractServiceOrderAnswerV1> GetAsync(Guid companyId, Guid orderId, CancellationToken cancellationToken = default);
}

public enum SalesSubcontractServiceOrderOutcomeV1
{
    /// <summary>Fail-closed: the company is not the server's, or the owner could not read; nothing is answered.</summary>
    Unreadable = 0,

    /// <summary>No such order in the company (another company's order is invisible).</summary>
    NotFound = 1,

    /// <summary>The order exists but is a normal order, not a subcontract service order.</summary>
    NotSubcontractService = 2,

    /// <summary>The order is a subcontract service order; <see cref="SalesSubcontractServiceOrderAnswerV1.Order"/> carries it.</summary>
    Found = 3,
}

/// <summary>Exactly one of: the order (Found), or neither (the other outcomes); <see cref="Reason"/> names an Unreadable cause.</summary>
public sealed record SalesSubcontractServiceOrderAnswerV1(
    SalesSubcontractServiceOrderOutcomeV1 Outcome,
    SalesSubcontractServiceOrderV1? Order,
    string? Reason = null)
{
    public string ContractVersion { get; init; } = ISalesSubcontractServiceOrderReadPortV1.ContractVersion;
}

/// <summary>
/// One subcontract service order: identity, the approval axis (<see cref="ApprovalState"/> as the Sales name;
/// <see cref="IsApproved"/> = the CURRENT revision <see cref="Revision"/> is internally approved), the customer acceptance of the
/// current revision (<see cref="SalesSubcontractCustomerAcceptanceStatesV1"/>), the lifecycle (<see cref="LifecycleState"/>;
/// <see cref="IsOpen"/> = neither cancelled nor force-closed), the customer, the concurrency version, and the service
/// <see cref="Lines"/> of revision <see cref="BasisRevision"/> (<see cref="Basis"/>: <see cref="SalesSubcontractServiceOrderBasesV1"/>).
/// A consumer plans on the basis lines only when the basis is usable: the current revision approved and accepted (or acceptance not
/// required), or the customer-accepted revision the order continues on after a rejection (SAT-D115).
/// </summary>
public sealed record SalesSubcontractServiceOrderV1(
    Guid CompanyId,
    Guid OrderId,
    string DocumentNo,
    int Revision,
    long Version,
    string ApprovalState,
    bool IsApproved,
    string CustomerAcceptanceState,
    string LifecycleState,
    bool IsOpen,
    Guid PartyId,
    Guid CompanyAccountId,
    int BasisRevision,
    string Basis,
    IReadOnlyList<SalesSubcontractServiceLineV1> Lines);

/// <summary>Which revision <see cref="SalesSubcontractServiceOrderV1.Lines"/> describe.</summary>
public static class SalesSubcontractServiceOrderBasesV1
{
    /// <summary>The order's current revision (<see cref="SalesSubcontractServiceOrderV1.Revision"/>).</summary>
    public const string CurrentRevision = "CurrentRevision";

    /// <summary>
    /// SAT-D115 (EK14): the customer rejected the last approved revision; the order continues on the revision the customer last
    /// accepted (an archived revision under the same customer and currency), as the shipping line port does.
    /// </summary>
    public const string CustomerAcceptedRevision = "CustomerAcceptedRevision";
}

/// <summary>One service line: identity, the product as the Product owner answered it, the exact quantity (text and value) and the order unit.</summary>
public sealed record SalesSubcontractServiceLineV1(
    Guid LineId,
    int LineNo,
    Guid ProductId,
    string ProductVersion,
    string ProductCode,
    string ProductName,
    string QuantityText,
    decimal Quantity,
    Guid OrderUnitId,
    string OrderUnitCode,
    string ItemType);

/// <summary>The customer acceptance of the CURRENT revision (Q9), as names.</summary>
public static class SalesSubcontractCustomerAcceptanceStatesV1
{
    /// <summary>An order written before the acceptance cut-over: no acceptance is required.</summary>
    public const string NotRequired = "NotRequired";

    /// <summary>The customer accepted the current revision.</summary>
    public const string Accepted = "Accepted";

    /// <summary>The current revision waits for the customer's acceptance (neither accepted nor rejected).</summary>
    public const string Pending = "Pending";

    /// <summary>
    /// SAT-D115 (EK14): the customer rejected the current revision (the order status axis' "Rejected"); the order continues on the
    /// revision the customer last accepted, when one exists (<see cref="SalesSubcontractServiceOrderBasesV1.CustomerAcceptedRevision"/>).
    /// </summary>
    public const string Rejected = "Rejected";
}

/// <summary>Why a read answered <see cref="SalesSubcontractServiceOrderOutcomeV1.Unreadable"/>.</summary>
public static class SalesSubcontractServiceOrderUnreadableReasonsV1
{
    /// <summary>The asked company is not the server's resolved company context.</summary>
    public const string CompanyMismatch = "company_mismatch";

    /// <summary>The Sales store could not read the order.</summary>
    public const string StoreUnavailable = "store_unavailable";

    /// <summary>
    /// K3 review A P3-1: the customer rejected the last approved revision and the revision it last accepted names another customer or
    /// currency than the order now does — the order continues on no revision (nothing ships, SAT-D115), so no lines are a basis.
    /// </summary>
    public const string NoBasis = "no_basis";
}

/// <summary>
/// SAT-D175 (EK25): the consumer hook Sales asks before the customer of a subcontract service order changes — whether a production
/// plan or a customer-goods allocation is linked to the order. Implemented by the host from the Production (and, for customer-goods
/// allocations, Inventory) owners; until it is composed, Sales treats the answer as unreadable and refuses the customer change
/// (fail-closed, <see cref="SalesDraftProblemCodesV1.SubcontractBindingUnavailable"/>). Company is a copy of the server context.
/// </summary>
public interface ISalesSubcontractOrderBindingReadPortV1
{
    Task<SalesSubcontractOrderBindingAnswerV1> GetAsync(Guid companyId, Guid orderId, CancellationToken cancellationToken = default);
}

public enum SalesSubcontractOrderBindingOutcomeV1
{
    /// <summary>Fail-closed: the owners could not be read.</summary>
    Unreadable = 0,

    /// <summary>No production plan and no customer-goods allocation is linked to the order.</summary>
    NotBound = 1,

    /// <summary>A production plan or a customer-goods allocation is linked to the order: its customer is locked.</summary>
    Bound = 2,
}

public sealed record SalesSubcontractOrderBindingAnswerV1(SalesSubcontractOrderBindingOutcomeV1 Outcome);
