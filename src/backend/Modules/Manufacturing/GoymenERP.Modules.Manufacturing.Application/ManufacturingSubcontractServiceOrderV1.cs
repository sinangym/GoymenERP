namespace GoymenERP.Modules.Manufacturing.Application;

// Üretim U5-b1 / API-36 (URT-D16, URT-D22; Sinan karar 5, #66: first version is incoming subcontracting only): a plan that
// processes a customer's goods is linked to that customer's subcontract service order. Manufacturing asks Sales for the order
// through its own port, adapted by the host (Manufacturing does not reference Sales); Sales asks Manufacturing whether any plan
// binds an order (SAT-D175) through Sales's binding port, also adapted by the host. Neither port grants or creates anything.

/// <summary>What Sales answered for one subcontract service order, in Manufacturing's own vocabulary.</summary>
public enum ManufacturingSubcontractServiceOrderOutcomeV1
{
    /// <summary>The order is a subcontract service order; <see cref="ManufacturingSubcontractServiceOrderAnswerV1.Order"/> carries it.</summary>
    Found = 1,

    /// <summary>The order exists but is a normal order, not a subcontract service order.</summary>
    NotSubcontractService = 2,

    /// <summary>No such order in the caller's company.</summary>
    NotFound = 3,

    /// <summary>Sales could not answer now (runtime closed, an exception, a malformed answer, no usable basis): nothing is decided.</summary>
    Unavailable = 4,
}

/// <summary>
/// One subcontract service order as Sales answered for it: the company and order, the customer (party and company account — the
/// owner of the processed goods), whether it is open, whether its CURRENT revision is internally approved, whether the revision it
/// continues on is a usable basis (approved and customer-accepted, acceptance not required, or the revision the customer last
/// accepted after a rejection — SAT-D115), and that basis' service line ids. Opaque identities; Manufacturing parses nothing.
/// </summary>
public sealed record ManufacturingSubcontractServiceOrderV1(
    Guid CompanyId,
    Guid SalesOrderId,
    Guid CustomerPartyId,
    Guid CustomerAccountId,
    bool IsOpen,
    bool IsApproved,
    bool BasisUsable,
    IReadOnlyList<Guid> BasisLineIds)
{
    /// <summary>
    /// True when a customer-goods plan may be linked to <paramref name="salesOrderLineId"/> of this order for the given customer:
    /// open, approved, a usable basis, the same customer party and account, and the line among the basis lines.
    /// </summary>
    public bool Admits(Guid salesOrderLineId, Guid customerPartyId, Guid customerAccountId) =>
        IsOpen && IsApproved && BasisUsable && CustomerPartyId != Guid.Empty && CustomerPartyId == customerPartyId
        && CustomerAccountId != Guid.Empty && CustomerAccountId == customerAccountId
        && BasisLineIds is not null && BasisLineIds.Contains(salesOrderLineId);
}

/// <summary>The answer: <see cref="Order"/> is set exactly when the outcome is Found.</summary>
public sealed record ManufacturingSubcontractServiceOrderAnswerV1(
    ManufacturingSubcontractServiceOrderOutcomeV1 Outcome,
    ManufacturingSubcontractServiceOrderV1? Order);

/// <summary>
/// Manufacturing-owned read of one subcontract service order in the caller's company (the host adapts Sales's
/// <c>sales.subcontract-service-order.v1</c> port). It carries no authority and creates nothing; any failure is Unavailable,
/// never Found.
/// </summary>
public interface IManufacturingSubcontractServiceOrderPortV1
{
    Task<ManufacturingSubcontractServiceOrderAnswerV1> GetAsync(Guid salesOrderId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Manufacturing-owned read for Sales's binding question (SAT-D175): whether any plan of the company that is not cancelled is
/// linked to <c>salesOrderId</c> — a Draft, an Approved plan, or one whose order is open or closed (a closed order still
/// produced the customer's goods). Reads only under the caller's resolved company (another company throws) and creates nothing.
/// </summary>
public interface IManufacturingSalesOrderBindingReadPortV1
{
    Task<bool> HasBindingPlanAsync(Guid companyId, Guid salesOrderId, CancellationToken cancellationToken = default);
}
