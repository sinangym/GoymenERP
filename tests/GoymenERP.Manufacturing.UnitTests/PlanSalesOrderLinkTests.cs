using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// Üretim U2-b / API-16 (SAT-D219): a plan supplies an approved sales order line. Linking, moving and removing the link moves the
/// plan's link revision and never its version; a cancelled plan, a closed order, a stale link revision, no change and a
/// customer-owned input of another party are refused with nothing changed. The stored link is restored and checked.
/// </summary>
public sealed class PlanSalesOrderLinkTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid WorkCenter = Guid.Parse("20000000-0000-0000-0000-00000000e001");
    private static readonly Guid Roll = Guid.Parse("30000000-0000-0000-0000-00000000e001");
    private static readonly Guid Party = Guid.Parse("50000000-0000-0000-0000-00000000e001");
    private static readonly Guid OtherParty = Guid.Parse("50000000-0000-0000-0000-00000000e002");
    private static readonly Guid Account = Guid.Parse("60000000-0000-0000-0000-00000000e001");
    private static readonly Guid SalesOrder = Guid.Parse("70000000-0000-0000-0000-00000000e001");
    private static readonly Guid LineA = Guid.Parse("71000000-0000-0000-0000-00000000e001");
    private static readonly Guid LineB = Guid.Parse("71000000-0000-0000-0000-00000000e002");
    private static readonly Guid Product = Guid.Parse("80000000-0000-0000-0000-00000000e001");
    private static readonly DateTimeOffset At = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);

    private static PlannedInput Input(Guid? customer = null) => new(Roll, new Kilograms(1_000m), 1, null,
        customer is { } party ? new PlanCommercialContextSnapshotV1(party, Account, "party-v1", "account-v1") : null);

    private static ProductionPlan Draft(Guid? customer = null) =>
        new(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.ColdRolling, [Input(customer)]);

    private static PlanSalesOrderLink Link(Guid line, Guid? party = null) =>
        new(SalesOrder, line, party ?? Party, Account, Product, 3, "40000000-0000-0000-0000-00000000e001", At);

    [Fact]
    public void Linking_moving_and_removing_move_the_link_revision_and_not_the_plan_version()
    {
        var plan = Draft(Party);

        plan.ChangeSalesOrderLink(1, 0, Link(LineA), orderClosed: false);
        Assert.Equal((LineA, 1L, 1L), (plan.SalesOrderLink!.SalesOrderLineId, plan.SalesOrderLinkRevision, plan.Version));

        plan.ChangeSalesOrderLink(1, 1, Link(LineB), orderClosed: false);
        Assert.Equal((LineB, 2L), (plan.SalesOrderLink!.SalesOrderLineId, plan.SalesOrderLinkRevision));

        plan.ChangeSalesOrderLink(1, 2, null, orderClosed: false);
        Assert.Null(plan.SalesOrderLink);
        Assert.Equal((3L, 1L), (plan.SalesOrderLinkRevision, plan.Version));
    }

    [Fact]
    public void An_approved_plan_may_be_linked_while_its_order_is_open()
    {
        var plan = Draft();
        plan.Approve(1, Guid.NewGuid(), At);

        plan.ChangeSalesOrderLink(2, 0, Link(LineA), orderClosed: false);

        Assert.Equal((PlanStatus.Approved, 1L), (plan.Status, plan.SalesOrderLinkRevision));
    }

    [Fact]
    public void A_cancelled_plan_is_refused_and_nothing_changes()
    {
        var plan = Draft();
        plan.Cancel(1, Guid.NewGuid(), At, "gerek kalmadı");

        AssertRefused(plan, plan.Version, 0, Link(LineA), false, ManufacturingRuleCodes.PlanCancelled);
    }

    [Fact]
    public void A_stale_link_revision_is_refused_and_nothing_changes()
    {
        var plan = Draft();
        plan.ChangeSalesOrderLink(1, 0, Link(LineA), orderClosed: false);

        var refusal = Assert.Throws<ManufacturingRuleException>(
            () => plan.ChangeSalesOrderLink(1, 0, Link(LineB), orderClosed: false));

        Assert.Equal(ManufacturingRuleCodes.PlanSalesOrderLinkChanged, refusal.Code);
        Assert.Equal((LineA, 1L), (plan.SalesOrderLink!.SalesOrderLineId, plan.SalesOrderLinkRevision));
    }

    [Fact]
    public void A_closed_order_is_refused_and_nothing_changes() =>
        AssertRefused(Draft(), 1, 0, Link(LineA), true, ManufacturingRuleCodes.PlanSalesOrderLinkOrderClosed);

    [Fact]
    public void Linking_the_same_line_again_or_removing_no_link_is_refused_as_no_change()
    {
        AssertRefused(Draft(), 1, 0, null, false, ManufacturingRuleCodes.PlanSalesOrderLinkNoChange);

        var plan = Draft();
        plan.ChangeSalesOrderLink(1, 0, Link(LineA), orderClosed: false);
        var refusal = Assert.Throws<ManufacturingRuleException>(
            () => plan.ChangeSalesOrderLink(1, 1, Link(LineA), orderClosed: false));
        Assert.Equal(ManufacturingRuleCodes.PlanSalesOrderLinkNoChange, refusal.Code);
        Assert.Equal(1L, plan.SalesOrderLinkRevision);
    }

    [Fact]
    public void A_customer_owned_input_of_another_party_is_refused_and_nothing_changes() =>
        AssertRefused(Draft(OtherParty), 1, 0, Link(LineA), false, ManufacturingRuleCodes.PlanSalesOrderPartyMismatch);

    [Fact]
    public void A_stale_plan_version_is_refused_as_a_version_conflict()
    {
        var plan = Draft();

        Assert.Throws<ManufacturingVersionConflictException>(
            () => plan.ChangeSalesOrderLink(2, 0, Link(LineA), orderClosed: false));

        Assert.Equal(0L, plan.SalesOrderLinkRevision);
    }

    [Fact]
    public void A_malformed_link_is_an_argument_error()
    {
        var plan = Draft();
        var malformed = Link(LineA) with { LinkedBy = " " };

        Assert.Throws<ArgumentException>(() => plan.ChangeSalesOrderLink(1, 0, malformed, orderClosed: false));
        Assert.Equal(0L, plan.SalesOrderLinkRevision);
    }

    [Fact]
    public void A_linked_plan_is_not_revised_to_a_customer_owned_input_of_another_party()
    {
        var plan = Draft(Party);
        plan.ChangeSalesOrderLink(1, 0, Link(LineA), orderClosed: false);

        var refusal = Assert.Throws<ManufacturingRuleException>(() =>
            plan.Revise(1, WorkCenter, ManufacturingProcess.ColdRolling, [Input(OtherParty)], null));

        Assert.Equal(ManufacturingRuleCodes.PlanSalesOrderPartyMismatch, refusal.Code);
        Assert.Equal(1L, plan.Version);
        plan.Revise(1, WorkCenter, ManufacturingProcess.ColdRolling, [Input()], null);
        Assert.Equal(2L, plan.Version);
    }

    [Fact]
    public void The_stored_link_is_restored_and_an_inconsistent_one_is_refused()
    {
        var id = Guid.NewGuid();
        var restored = Restore(id, Link(LineA), 4);
        Assert.Equal((LineA, 4L), (restored.SalesOrderLink!.SalesOrderLineId, restored.SalesOrderLinkRevision));
        Assert.Equal(2L, Restore(id, null, 2).SalesOrderLinkRevision);

        foreach (var (link, revision) in new (PlanSalesOrderLink?, long)[]
                 {
                     (Link(LineA), 0), (null, -1), (Link(LineA) with { ProductId = Guid.Empty }, 1),
                     (Link(LineA, OtherParty), 1),
                 })
        {
            var refusal = Assert.Throws<ManufacturingRuleException>(() => Restore(id, link, revision));
            Assert.Equal(ManufacturingRuleCodes.StoredRecordInvalid, refusal.Code);
        }
    }

    private static ProductionPlan Restore(Guid id, PlanSalesOrderLink? link, long revision) =>
        ProductionPlan.Restore(id, Company, WorkCenter, ManufacturingProcess.ColdRolling, [Input(Party)], null, null, null, 1,
            salesOrderLink: link, salesOrderLinkRevision: revision);

    private static void AssertRefused(ProductionPlan plan, long version, long linkRevision, PlanSalesOrderLink? link,
        bool orderClosed, string code)
    {
        var before = (plan.SalesOrderLink, plan.SalesOrderLinkRevision, plan.Version);

        var refusal = Assert.Throws<ManufacturingRuleException>(
            () => plan.ChangeSalesOrderLink(version, linkRevision, link, orderClosed));

        Assert.Equal(code, refusal.Code);
        Assert.Equal(before, (plan.SalesOrderLink, plan.SalesOrderLinkRevision, plan.Version));
    }
}
