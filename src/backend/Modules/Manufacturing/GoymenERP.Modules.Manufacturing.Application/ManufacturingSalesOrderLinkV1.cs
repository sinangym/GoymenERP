using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Modules.Manufacturing.Application;

// Üretim U2-b / API-16 + 1b (SAT-D219): a plan supplies an approved sales order line. Manufacturing records the link on its
// own plan row; Sales reads Manufacturing's open plans and orders for an order through its linked-supply port, which the host
// adapts (Manufacturing does not reference Sales). Manufacturing asks Sales for the line through its own port, also adapted by
// the host. Neither port grants anything or creates anything in the other module.

/// <summary>
/// Üretim U2-b / API-16: the plan's sales order link in one save — the plan row's link columns and link revision, the audit
/// event and the command record — or nothing. A port of its own beside <see cref="IManufacturingPlanLifecycleStore"/>; the one
/// store implements it. The domain decides on the plan rebuilt from storage (with its order's status).
/// </summary>
public interface IManufacturingPlanSalesOrderLinkStore
{
    /// <summary>Links, moves or (with a null <paramref name="link"/>) removes the plan's sales order link.</summary>
    Task<ProductionPlan> ChangeSalesOrderLinkAsync(Guid planId, long expectedVersion, long expectedLinkRevision,
        PlanSalesOrderLink? link, ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default);
}

/// <summary>What Sales answered for one order line, in Manufacturing's own vocabulary.</summary>
public enum ManufacturingSalesOrderLineOutcomeV1
{
    Approved = 1,
    NotApproved = 2,
    NotFound = 3,

    /// <summary>Sales could not answer now (runtime closed, an exception, a malformed answer): nothing is linked.</summary>
    Unavailable = 4,
}

/// <summary>
/// The approved sales order line as Sales answered for it: the company, the order and line, the customer party and its
/// company account, the product and the line's revision. Opaque identities: Manufacturing stores them and parses nothing.
/// </summary>
public sealed record ManufacturingSalesOrderLineV1(
    Guid CompanyId,
    Guid SalesOrderId,
    Guid SalesOrderLineId,
    Guid CustomerPartyId,
    Guid CustomerAccountId,
    Guid ProductId,
    int LineRevision);

/// <summary>The answer: <see cref="Line"/> is set exactly when the outcome is Approved.</summary>
public sealed record ManufacturingSalesOrderLineAnswerV1(
    ManufacturingSalesOrderLineOutcomeV1 Outcome,
    ManufacturingSalesOrderLineV1? Line);

/// <summary>
/// Manufacturing-owned read of one approved sales order line in the caller's company (the host adapts Sales's
/// approved-line port). It carries no authority and creates nothing; any failure is Unavailable, never Approved.
/// </summary>
public interface IManufacturingSalesOrderLineReferencePortV1
{
    Task<ManufacturingSalesOrderLineAnswerV1> GetApprovedLineAsync(Guid salesOrderId, Guid salesOrderLineId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One OPEN Manufacturing supply for a sales order line: a plan that is a Draft or Approved and has no order, or whose order
/// is Open. <see cref="OrderId"/> / <see cref="OrderStatus"/> are set when the plan opened its order. Planned kilograms are the
/// plan's planned input mass; actual kilograms are not stated by this version (always null; the order's realized output
/// stays the order read's own answer).
/// </summary>
public sealed record ManufacturingSalesOrderSupplyItemV1(
    Guid CompanyId,
    Guid PlanId,
    string PlanStatus,
    Guid? OrderId,
    string? OrderStatus,
    Guid SalesOrderLineId,
    decimal PlannedKg,
    decimal? ActualKg);

/// <summary>
/// Manufacturing-owned read for Sales's linked-supply question (SAT-D219, supply kind Production): every OPEN plan or order of
/// the company linked to <c>salesOrderId</c> — all lines, or only the given line ids. Reads only under the caller's resolved
/// company (another company throws), at most <see cref="MaximumLineFilter"/> line ids, and creates nothing.
/// </summary>
public interface IManufacturingSalesOrderSupplyReadPortV1
{
    /// <summary>The most Sales order line ids one query may narrow to (a bound, never an unbounded IN list).</summary>
    const int MaximumLineFilter = 500;

    Task<IReadOnlyList<ManufacturingSalesOrderSupplyItemV1>> ListOpenForSalesOrderAsync(Guid companyId,
        Guid salesOrderId, IReadOnlyList<Guid>? salesOrderLineIds, CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>POST /plans/{planId}/sales-order-link</c>: the plan version and link revision the caller read, and the sales order line
/// to link (both ids), or neither to remove the link.
/// </summary>
public sealed record ChangePlanSalesOrderLinkRequestV1(
    long ExpectedVersion,
    long ExpectedLinkRevision,
    Guid? SalesOrderId,
    Guid? SalesOrderLineId);

/// <summary>The plan's sales order link as Sales answered for it when it was made, and who linked it, when.</summary>
public sealed record PlanSalesOrderLinkViewV1(
    Guid SalesOrderId,
    Guid SalesOrderLineId,
    Guid CustomerPartyId,
    Guid CustomerAccountId,
    Guid ProductId,
    int LineRevision,
    string LinkedBy,
    DateTimeOffset LinkedAtUtc);

public sealed partial class ManufacturingCommandService
{
    /// <summary>
    /// <c>POST /plans/{planId}/sales-order-link</c> (U2-b / API-16) under <c>manufacturing.plans.write</c>: links the plan to
    /// an approved sales order line, moves the link to another line, or removes it. The plan's version does not move; its link
    /// revision does. The line is read from Sales (through the host) before the save: NotApproved / NotFound are 422, an
    /// unavailable Sales is 503 and nothing is linked. A cancelled plan, a closed order, no change and a customer-owned input
    /// of another party are 422; a moved link revision is 409. ADR-011 as the other plan commands.
    /// <para>U5-b / API-36 (URT-D16, URT-D22): a plan with a customer-owned input is linked only to an open, approved
    /// subcontract service order of that customer whose usable basis carries the line (422
    /// <see cref="ManufacturingRuleCodes.PlanCustomerGoodsRequireSubcontractOrder"/>); Sales unreadable is 503. A plan without
    /// customer goods links to any approved line as before.</para>
    /// </summary>
    public Task<ManufacturingApiResult<PlanViewV1>> ChangePlanSalesOrderLinkAsync(Guid planId,
        ChangePlanSalesOrderLinkRequestV1? request, string idempotencyKey, string permission,
        CancellationToken cancellationToken)
    {
        if (planId == Guid.Empty || request is null || request.ExpectedVersion <= 0)
        {
            return Task.FromResult(Invalid<PlanViewV1>("expectedVersion"));
        }

        if (request.ExpectedLinkRevision < 0)
        {
            return Task.FromResult(Invalid<PlanViewV1>("expectedLinkRevision"));
        }

        // Both ids to link, neither to unlink.
        if (request.SalesOrderId is null != request.SalesOrderLineId is null
            || request.SalesOrderId == Guid.Empty || request.SalesOrderLineId == Guid.Empty)
        {
            return Task.FromResult(Invalid<PlanViewV1>("salesOrderLineId"));
        }

        PlanParameterSnapshotV1? live = null;
        return RunAsync<PlanViewV1>(permission, idempotencyKey, ManufacturingCommandNames.ChangePlanSalesOrderLink,
            new
            {
                planId,
                expectedVersion = request.ExpectedVersion,
                expectedLinkRevision = request.ExpectedLinkRevision,
                salesOrderId = request.SalesOrderId,
                salesOrderLineId = request.SalesOrderLineId,
            }, ManufacturingApiStatus.Ok,
            async (metadata, actor, companyId) =>
            {
                if (planSalesOrderLinks is null)
                {
                    return new ManufacturingApiResult<PlanViewV1>(ManufacturingApiStatus.Unavailable, null, false, null);
                }

                PlanSalesOrderLink? link = null;
                if (request is { SalesOrderId: { } salesOrderId, SalesOrderLineId: { } salesOrderLineId })
                {
                    if (salesOrderLines is null)
                    {
                        return new ManufacturingApiResult<PlanViewV1>(ManufacturingApiStatus.Unavailable, null, false,
                            ManufacturingRuleCodes.PlanSalesOrderLineUnavailable);
                    }

                    var answer = await salesOrderLines.GetApprovedLineAsync(salesOrderId, salesOrderLineId,
                        cancellationToken);
                    if (await FenceCapturedAsync<PlanViewV1>(permission, companyId, actor, cancellationToken) is { } fenced)
                    {
                        return fenced;
                    }

                    switch (answer)
                    {
                        case { Outcome: ManufacturingSalesOrderLineOutcomeV1.Approved, Line: { } line }
                            when line.CompanyId == companyId && line.SalesOrderId == salesOrderId
                                 && line.SalesOrderLineId == salesOrderLineId && line.CustomerPartyId != Guid.Empty
                                 && line.CustomerAccountId != Guid.Empty && line.ProductId != Guid.Empty
                                 && line.LineRevision >= 0:
                            link = new PlanSalesOrderLink(salesOrderId, salesOrderLineId, line.CustomerPartyId,
                                line.CustomerAccountId, line.ProductId, line.LineRevision,
                                actor.ToString("D"), timeProvider.GetUtcNow());
                            break;
                        case { Outcome: ManufacturingSalesOrderLineOutcomeV1.NotApproved }:
                            return new ManufacturingApiResult<PlanViewV1>(ManufacturingApiStatus.Unprocessable, null,
                                false, ManufacturingRuleCodes.PlanSalesOrderLineNotApproved);
                        case { Outcome: ManufacturingSalesOrderLineOutcomeV1.NotFound }:
                            return new ManufacturingApiResult<PlanViewV1>(ManufacturingApiStatus.Unprocessable, null,
                                false, ManufacturingRuleCodes.PlanSalesOrderLineNotFound);
                        default:
                            // Unavailable, or an Approved answer that is not this company's very line: fail-closed.
                            return new ManufacturingApiResult<PlanViewV1>(ManufacturingApiStatus.Unavailable, null,
                                false, ManufacturingRuleCodes.PlanSalesOrderLineUnavailable);
                    }

                    if (await RefuseCustomerGoodsLinkAsync(planId, request.ExpectedVersion, link, permission, companyId,
                            actor, cancellationToken) is { } refused)
                    {
                        return refused;
                    }
                }

                // The Draft's answer uses the profile in force today, read once before the save (as the plan read).
                if (await plans.FindPlanAsync(planId, cancellationToken) is { ApprovedAtUtc: null } current)
                {
                    live = await ReadLiveParameterProfileAsync(current, cancellationToken);
                    if (await FenceCapturedAsync<PlanViewV1>(permission, companyId, actor, cancellationToken) is { } fenced)
                    {
                        return fenced;
                    }
                }

                var changed = await planSalesOrderLinks.ChangeSalesOrderLinkAsync(planId, request.ExpectedVersion,
                    request.ExpectedLinkRevision, link, metadata, cancellationToken);
                return new ManufacturingApiResult<PlanViewV1>(ManufacturingApiStatus.Ok, PlanView(changed, live), false,
                    null);
            }, stored => PlanView((ProductionPlan)stored, live), cancellationToken);
    }

    /// <summary>
    /// U5-b / API-36: when the plan (at the caller's expected version) carries a customer-owned input, the order must be an
    /// open, approved subcontract service order of that customer — party and company account — whose usable basis carries the
    /// line. Null when the link may proceed. A plan read at another version is left to the store's version fence; a plan
    /// without customer goods asks nothing. Sales is read before a fresh fence; any unreadable answer is 503.
    /// </summary>
    private async Task<ManufacturingApiResult<PlanViewV1>?> RefuseCustomerGoodsLinkAsync(Guid planId, long expectedVersion,
        PlanSalesOrderLink link, string permission, Guid companyId, Guid actor, CancellationToken cancellationToken)
    {
        if (await plans.FindPlanAsync(planId, cancellationToken) is not { } current || current.Version != expectedVersion
            || current.Inputs.Select(input => input.CommercialContext).FirstOrDefault(context => context is not null)
                is not { } customer)
        {
            return null;
        }

        if (subcontractServiceOrders is null)
        {
            return new ManufacturingApiResult<PlanViewV1>(ManufacturingApiStatus.Unavailable, null, false,
                ManufacturingRuleCodes.PlanSalesOrderLineUnavailable);
        }

        ManufacturingSubcontractServiceOrderAnswerV1? answer;
        try
        {
            answer = await subcontractServiceOrders.GetAsync(link.SalesOrderId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            answer = null;
        }

        if (await FenceCapturedAsync<PlanViewV1>(permission, companyId, actor, cancellationToken) is { } fenced)
        {
            return fenced;
        }

        return answer switch
        {
            { Outcome: ManufacturingSubcontractServiceOrderOutcomeV1.Found, Order: { } order }
                when order.CompanyId == companyId && order.SalesOrderId == link.SalesOrderId =>
                order.Admits(link.SalesOrderLineId, customer.PartyId, customer.CompanyPartyAccountId)
                    ? null
                    : CustomerGoodsRefusal(),
            { Outcome: ManufacturingSubcontractServiceOrderOutcomeV1.NotSubcontractService
                or ManufacturingSubcontractServiceOrderOutcomeV1.NotFound, Order: null } => CustomerGoodsRefusal(),
            _ => new ManufacturingApiResult<PlanViewV1>(ManufacturingApiStatus.Unavailable, null, false,
                ManufacturingRuleCodes.PlanSalesOrderLineUnavailable),
        };

        static ManufacturingApiResult<PlanViewV1> CustomerGoodsRefusal() =>
            new(ManufacturingApiStatus.Unprocessable, null, false,
                ManufacturingRuleCodes.PlanCustomerGoodsRequireSubcontractOrder);
    }
}
