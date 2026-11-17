using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;

namespace GoymenERP.Manufacturing.UnitTests;

public sealed partial class ManufacturingCommandServiceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Completion_V2_preserves_real_plan_link_revision_output_identity_and_exact_remaining_kg(bool completed)
    {
        var (fixture, plan, _, service) = CustomerGoodsLinkCase();
        Assert.Equal(ManufacturingApiStatus.Ok, (await service.ChangePlanSalesOrderLinkAsync(plan.Id,
            LinkRequest(), "completion-v2-link", "manufacturing.plans.write", CancellationToken.None)).Status);
        plan.WithDueDate().Approve(plan.Version, Guid.NewGuid(), At);
        var order = ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);
        order.IssueInput(order.Version, Guid.NewGuid(), Roll, new Kilograms(1000m), At);
        order.DeclareOutput(order.Version, LineageOutputOne, Roll, OutputClass.GoodProduct, new Kilograms(1000m), At);
        if (completed) order.Close(order.Version, Actor, At);
        fixture.Store.Orders[order.Id] = order;
        var lineage = new FakeLineagePort { Answer = LineageAnswer(order.Id) };
        var owner = new ManufacturingSubcontractCompletionReaderV2(fixture.Store, fixture.Store, lineage, fixture.CompanyContext);
        var answer = await owner.GetAsync(Company, order.Id);
        Assert.Equal(ManufacturingSubcontractCompletionOutcomeV1.Available, answer.Outcome);
        var proof = Assert.IsType<ManufacturingSubcontractCompletionProofV2>(answer.Value);
        Assert.Equal((Company, order.Id), (answer.CompanyId, answer.ManufacturingOrderId));
        Assert.Equal((order.Version, plan.Id, plan.Version, plan.SalesOrderLinkRevision),
            (proof.OrderVersion, proof.PlanId, proof.PlanVersion, proof.SalesLinkRevision));
        Assert.Equal((plan.SalesOrderLink!.SalesOrderId, plan.SalesOrderLink.SalesOrderLineId, plan.SalesOrderLink.LineRevision,
                plan.SalesOrderLink.CustomerPartyId, plan.SalesOrderLink.CustomerAccountId, plan.SalesOrderLink.ProductId),
            (proof.SalesOrderId, proof.SalesOrderLineId, proof.SalesLineRevision, proof.CustomerPartyId, proof.CustomerAccountId, proof.ProductId));
        Assert.Equal(completed, proof.IsCompleted);
        var output = Assert.Single(proof.Outputs);
        Assert.Equal((LineageOutputOne, LineageLabelOne, "1000"), (output.OutputFactId, output.LabelId, output.RemainingKg));
        Assert.Equal(64, proof.SourceVersion.Length);
        Assert.Equal(proof.SourceVersion, (await owner.GetAsync(Company, order.Id)).Value!.SourceVersion);
        var foreign = await owner.GetAsync(Guid.NewGuid(), order.Id);
        Assert.Equal(ManufacturingSubcontractCompletionOutcomeV1.Unavailable, foreign.Outcome);
        Assert.Null(foreign.Value);
        var missing = await owner.GetAsync(Company, Guid.NewGuid());
        Assert.Equal(ManufacturingSubcontractCompletionOutcomeV1.NotFound, missing.Outcome);
        Assert.Null(missing.Value);
        lineage.Answer = LineageAnswer(order.Id, truncated: true);
        var truncated = await owner.GetAsync(Company, order.Id);
        Assert.Equal(ManufacturingSubcontractCompletionOutcomeV1.Unavailable, truncated.Outcome);
        Assert.Null(truncated.Value);
    }

    [Fact]
    public async Task Completion_V2_refuses_ambiguous_labels_instead_of_selecting_a_convenient_source()
    {
        var (fixture, plan, _, service) = CustomerGoodsLinkCase();
        await service.ChangePlanSalesOrderLinkAsync(plan.Id, LinkRequest(), "completion-v2-ambiguous", "manufacturing.plans.write", CancellationToken.None);
        plan.WithDueDate().Approve(plan.Version, Guid.NewGuid(), At);
        var order = ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);
        order.IssueInput(order.Version, Guid.NewGuid(), Roll, new Kilograms(1000m), At);
        order.DeclareOutput(order.Version, LineageOutputOne, Roll, OutputClass.GoodProduct, new Kilograms(1000m), At);
        fixture.Store.Orders[order.Id] = order;
        var trace = LineageAnswer(order.Id);
        var duplicate = trace with { Outputs = trace.Outputs.Concat(trace.Outputs.Where(output => output.OutputId == LineageOutputOne)).ToArray() };
        var owner = new ManufacturingSubcontractCompletionReaderV2(fixture.Store, fixture.Store,
            new FakeLineagePort { Answer = duplicate }, fixture.CompanyContext);
        var answer = await owner.GetAsync(Company, order.Id);
        Assert.Equal(ManufacturingSubcontractCompletionOutcomeV1.Unavailable, answer.Outcome);
        Assert.Null(answer.Value);
    }

    [Fact]
    public async Task Completion_V2_excludes_explicit_non_stock_process_loss_but_never_infers_a_missing_stockable_output_label()
    {
        var (fixture, plan, _, service) = CustomerGoodsLinkCase();
        await service.ChangePlanSalesOrderLinkAsync(plan.Id, LinkRequest(), "completion-v2-process-loss", "manufacturing.plans.write", CancellationToken.None);
        plan.WithDueDate().Approve(plan.Version, Guid.NewGuid(), At);
        var order = ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);
        order.IssueInput(order.Version, Guid.NewGuid(), Roll, new Kilograms(1000m), At);
        order.DeclareOutput(order.Version, LineageOutputOne, Roll, OutputClass.GoodProduct, new Kilograms(900m), At);
        var lossId = Guid.NewGuid();
        order.DeclareOutput(order.Version, lossId, Roll, OutputClass.ProcessLoss, new Kilograms(100m), At);
        fixture.Store.Orders[order.Id] = order;
        var trace = LineageAnswer(order.Id);
        var lineage = new FakeLineagePort { Answer = trace };
        var owner = new ManufacturingSubcontractCompletionReaderV2(fixture.Store, fixture.Store, lineage, fixture.CompanyContext);
        var available = await owner.GetAsync(Company, order.Id);
        Assert.Equal(ManufacturingSubcontractCompletionOutcomeV1.Available, available.Outcome);
        var output = Assert.Single(available.Value!.Outputs);
        Assert.Equal((LineageOutputOne, LineageLabelOne, "900"), (output.OutputFactId, output.LabelId, output.RemainingKg));
        Assert.DoesNotContain(available.Value.Outputs, item => item.OutputFactId == lossId);
        lineage.Answer = trace with { Outputs = [] };
        var missing = await owner.GetAsync(Company, order.Id);
        Assert.Equal(ManufacturingSubcontractCompletionOutcomeV1.Unavailable, missing.Outcome);
        Assert.Null(missing.Value);
    }
}
