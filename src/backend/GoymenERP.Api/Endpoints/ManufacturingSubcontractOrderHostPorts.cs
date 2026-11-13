using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// Üretim U5-b / API-36 (URT-D16, URT-D22, SAT-D175): the host's translation of Sales's
/// <see cref="ISalesSubcontractServiceOrderReadPortV1"/> to Manufacturing's <see cref="IManufacturingSubcontractServiceOrderPortV1"/>
/// (Manufacturing does not reference Sales). Found only when Sales answers Found for that very order in the resolved company with
/// a customer party and account; the basis is usable when it is the current revision and the customer accepted it or no
/// acceptance is required, or the revision the customer last accepted (SAT-D115). NotSubcontractService and NotFound pass through;
/// Unreadable, an unknown outcome, a malformed answer or any exception is Unavailable (fail-closed), never Found. Reading creates
/// nothing and grants nothing.
/// </summary>
public sealed class ManufacturingSubcontractServiceOrderPort(
    ISalesSubcontractServiceOrderReadPortV1 salesOrders,
    ICompanyContext companyContext) : IManufacturingSubcontractServiceOrderPortV1
{
    private static readonly ManufacturingSubcontractServiceOrderAnswerV1 Unavailable =
        new(ManufacturingSubcontractServiceOrderOutcomeV1.Unavailable, null);

    public async Task<ManufacturingSubcontractServiceOrderAnswerV1> GetAsync(Guid salesOrderId,
        CancellationToken cancellationToken = default)
    {
        if (salesOrderId == Guid.Empty || !companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            return Unavailable;
        }

        var companyId = companyContext.CompanyId;
        try
        {
            var answer = await salesOrders.GetAsync(companyId, salesOrderId, cancellationToken);
            return answer switch
            {
                { Outcome: SalesSubcontractServiceOrderOutcomeV1.Found, Order: { } order }
                    when order.CompanyId == companyId && order.OrderId == salesOrderId && order.PartyId != Guid.Empty
                         && order.CompanyAccountId != Guid.Empty && order.Lines is not null
                         && order.Lines.All(line => line is not null && line.LineId != Guid.Empty)
                    => new ManufacturingSubcontractServiceOrderAnswerV1(ManufacturingSubcontractServiceOrderOutcomeV1.Found,
                        new ManufacturingSubcontractServiceOrderV1(order.CompanyId, order.OrderId, order.PartyId,
                            order.CompanyAccountId, order.IsOpen, order.IsApproved, BasisUsable(order),
                            [.. order.Lines.Select(line => line.LineId)])),
                { Outcome: SalesSubcontractServiceOrderOutcomeV1.NotSubcontractService, Order: null } =>
                    new ManufacturingSubcontractServiceOrderAnswerV1(
                        ManufacturingSubcontractServiceOrderOutcomeV1.NotSubcontractService, null),
                { Outcome: SalesSubcontractServiceOrderOutcomeV1.NotFound, Order: null } =>
                    new ManufacturingSubcontractServiceOrderAnswerV1(ManufacturingSubcontractServiceOrderOutcomeV1.NotFound,
                        null),
                _ => Unavailable,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Unavailable;
        }
    }

    /// <summary>
    /// The revision the order continues on carries goods for this customer: the current revision when the customer accepted it
    /// or no acceptance is required, or the revision the customer last accepted after rejecting a later one. Any other basis or
    /// state (pending or rejected acceptance on the current revision, an unknown string) is not usable.
    /// </summary>
    internal static bool BasisUsable(SalesSubcontractServiceOrderV1 order) =>
        order.Basis switch
        {
            SalesSubcontractServiceOrderBasesV1.CurrentRevision => order.CustomerAcceptanceState is
                SalesSubcontractCustomerAcceptanceStatesV1.Accepted or SalesSubcontractCustomerAcceptanceStatesV1.NotRequired,
            SalesSubcontractServiceOrderBasesV1.CustomerAcceptedRevision => true,
            _ => false,
        };
}

/// <summary>
/// Üretim U5-b / SAT-D175: the host's answer to Sales's <see cref="ISalesSubcontractOrderBindingReadPortV1"/> from the
/// Manufacturing owner alone. Bound when a plan that is not cancelled is linked to the order, NotBound when none is; any failure,
/// a company other than the resolved one, or an empty order id is Unreadable. Customer goods are allocated only through a plan's
/// approval (Üretim U3-a; Inventory keeps no order-bound customer-goods allocation of its own — coordinator S2), so a plan that
/// binds the order covers every allocation the contract names.
/// </summary>
public sealed class ManufacturingSalesSubcontractOrderBindingReadPort(
    IManufacturingSalesOrderBindingReadPortV1 manufacturing,
    ICompanyContext companyContext) : ISalesSubcontractOrderBindingReadPortV1
{
    private static readonly SalesSubcontractOrderBindingAnswerV1 Unreadable =
        new(SalesSubcontractOrderBindingOutcomeV1.Unreadable);

    public async Task<SalesSubcontractOrderBindingAnswerV1> GetAsync(Guid companyId, Guid orderId,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty || orderId == Guid.Empty || !companyContext.IsResolved
            || companyContext.CompanyId != companyId)
        {
            return Unreadable;
        }

        try
        {
            return await manufacturing.HasBindingPlanAsync(companyId, orderId, cancellationToken)
                ? new SalesSubcontractOrderBindingAnswerV1(SalesSubcontractOrderBindingOutcomeV1.Bound)
                : new SalesSubcontractOrderBindingAnswerV1(SalesSubcontractOrderBindingOutcomeV1.NotBound);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Unreadable;
        }
    }
}
