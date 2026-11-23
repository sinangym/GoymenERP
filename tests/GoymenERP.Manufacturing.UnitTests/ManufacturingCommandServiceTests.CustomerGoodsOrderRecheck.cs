using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// U5-b / API-36 (URT-D16, URT-D22) at the approval: a linked plan with customer-owned inputs is approved only while its
/// order is still that customer's open, approved subcontract service order carrying the line. Sales is read again at the
/// approval, not trusted from the link time.
/// </summary>
public sealed partial class ManufacturingCommandServiceTests
{
    [Theory]
    [MemberData(nameof(CustomerGoodsRefusals))]
    public async Task A_linked_customer_goods_plan_is_not_approved_once_its_order_no_longer_admits_the_customer_goods(
        string variant)
    {
        var (fixture, plan, orders, service) = CustomerGoodsLinkCase();
        var linked = await service.ChangePlanSalesOrderLinkAsync(plan.Id, LinkRequest(), "link-key",
            "manufacturing.plans.write", CancellationToken.None);
        Assert.Equal(ManufacturingApiStatus.Ok, linked.Status);
        var writes = fixture.Store.Writes.Count;
        var asked = orders.Asked;
        (orders.Outcome, orders.Answered) = variant switch
        {
            "normal-order" => (ManufacturingSubcontractServiceOrderOutcomeV1.NotSubcontractService, null),
            "not-found" => (ManufacturingSubcontractServiceOrderOutcomeV1.NotFound, null),
            "closed" => (ManufacturingSubcontractServiceOrderOutcomeV1.Found, orders.Order with { IsOpen = false }),
            "not-approved" => (ManufacturingSubcontractServiceOrderOutcomeV1.Found, orders.Order with { IsApproved = false }),
            "basis-unusable" => (ManufacturingSubcontractServiceOrderOutcomeV1.Found, orders.Order with { BasisUsable = false }),
            "other-party" => (ManufacturingSubcontractServiceOrderOutcomeV1.Found,
                orders.Order with { CustomerPartyId = Guid.NewGuid() }),
            "other-account" => (ManufacturingSubcontractServiceOrderOutcomeV1.Found,
                orders.Order with { CustomerAccountId = Guid.NewGuid() }),
            _ => (ManufacturingSubcontractServiceOrderOutcomeV1.Found, orders.Order with { BasisLineIds = [Guid.NewGuid()] }),
        };

        var result = await service.ApprovePlanAsync(plan.Id, new ApprovePlanRequestV1(plan.Version), "approve-key",
            "manufacturing.plans.approve", CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Unprocessable, ManufacturingRuleCodes.PlanCustomerGoodsRequireSubcontractOrder),
            (result.Status, result.Code));
        Assert.Equal(asked + 1, orders.Asked);
        Assert.Equal(writes, fixture.Store.Writes.Count);
        Assert.Equal(PlanStatus.Draft, plan.Status);
    }

    [Fact]
    public async Task A_linked_customer_goods_plan_reads_its_order_again_at_the_approval_and_an_unreadable_answer_is_503()
    {
        var (fixture, plan, orders, service) = CustomerGoodsLinkCase();
        var linked = await service.ChangePlanSalesOrderLinkAsync(plan.Id, LinkRequest(), "link-key",
            "manufacturing.plans.write", CancellationToken.None);
        Assert.Equal(ManufacturingApiStatus.Ok, linked.Status);
        var writes = fixture.Store.Writes.Count;

        // The order still admits the goods: the check passes and the approval goes on to its next owner gate (this
        // service has no allocation port, so the approval stops there with 503 and nothing is approved).
        var asked = orders.Asked;
        var admitted = await service.ApprovePlanAsync(plan.Id, new ApprovePlanRequestV1(plan.Version), "approve-ok",
            "manufacturing.plans.approve", CancellationToken.None);
        Assert.Equal((ManufacturingApiStatus.Unavailable, ManufacturingPlanAllocationCodesV1.Incomplete),
            (admitted.Status, admitted.Code));
        Assert.Equal(asked + 1, orders.Asked);

        (orders.Outcome, orders.Answered) = (ManufacturingSubcontractServiceOrderOutcomeV1.Unavailable, null);
        var unreadable = await service.ApprovePlanAsync(plan.Id, new ApprovePlanRequestV1(plan.Version), "approve-503",
            "manufacturing.plans.approve", CancellationToken.None);
        Assert.Equal((ManufacturingApiStatus.Unavailable, ManufacturingRuleCodes.PlanSalesOrderLineUnavailable),
            (unreadable.Status, unreadable.Code));
        Assert.Equal(writes, fixture.Store.Writes.Count);
        Assert.Equal(PlanStatus.Draft, plan.Status);
    }
}
