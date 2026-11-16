using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;

namespace GoymenERP.Manufacturing.UnitTests;

public sealed partial class ManufacturingCommandServiceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Subcontract_completion_reads_the_customer_and_own_output_labels_only(bool completed)
    {
        var (fixture, plan, _, service) = CustomerGoodsLinkCase();
        var linked = await service.ChangePlanSalesOrderLinkAsync(plan.Id, LinkRequest(), "completion-link",
            "manufacturing.plans.write", CancellationToken.None);
        Assert.Equal(ManufacturingApiStatus.Ok, linked.Status);
        plan.WithDueDate().Approve(plan.Version, Guid.NewGuid(), At);
        var order = ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);
        order.IssueInput(order.Version, Guid.NewGuid(), Roll, new Kilograms(1000m), At);
        order.DeclareOutput(order.Version, LineageOutputOne, Roll, OutputClass.GoodProduct, new Kilograms(1000m), At);
        if (completed) order.Close(order.Version, Actor, At);
        fixture.Store.Orders[order.Id] = order;
        var lineage = new FakeLineagePort { Answer = LineageAnswer(order.Id) };
        var owner = new ManufacturingSubcontractCompletionReadPort(fixture.Store, fixture.Store, lineage,
            fixture.CompanyContext);
        var answer = await owner.GetAsync(Company, order.Id);
        Assert.Equal((ManufacturingSubcontractCompletionOutcomeV1.Available, Party, completed),
            (answer.Outcome, answer.CustomerPartyId, answer.IsCompleted));
        // LineageOutputTwo is not an output of this order and can never become an eligible delivery label.
        Assert.Equal([LineageLabelOne], answer.OutputLabelIds);
        Assert.Equal(ManufacturingSubcontractCompletionOutcomeV1.Unavailable,
            (await owner.GetAsync(Guid.NewGuid(), order.Id)).Outcome);
        lineage.Answer = LineageAnswer(order.Id, truncated: true);
        Assert.Equal(ManufacturingSubcontractCompletionOutcomeV1.Unavailable,
            (await owner.GetAsync(Company, order.Id)).Outcome);
    }
}
