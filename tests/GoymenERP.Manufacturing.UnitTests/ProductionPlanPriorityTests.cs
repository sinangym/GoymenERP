using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

public sealed class ProductionPlanPriorityTests
{
    [Fact]
    public void Signed_priority_scores_are_technical_values_and_every_change_appends_an_independent_revision()
    {
        var first = ProductionPlanPriorityV1.Default.Change(0, int.MinValue, "Synthetic low score");
        var second = first.Change(1, int.MaxValue, "Synthetic high score");
        Assert.Equal(new ProductionPlanPriorityV1(int.MaxValue, 2), second);
        Assert.Equal(new ProductionPlanPriorityV1(int.MinValue, 1), first);
        Assert.Equal(new ProductionPlanPriorityV1(0, 0), ProductionPlanPriorityV1.Default);
    }

    [Fact]
    public void A_stale_priority_revision_does_not_change_the_score()
    {
        var current = new ProductionPlanPriorityV1(9, 4);
        var conflict = Assert.Throws<ManufacturingVersionConflictException>(() => current.Change(3, 2, "Synthetic"));
        Assert.Equal((3L, 4L), (conflict.ExpectedVersion, conflict.CurrentVersion));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_priority_change_requires_a_reason(string? reason) =>
        Assert.Throws<ManufacturingRuleException>(() => ProductionPlanPriorityV1.Default.Change(0, 1, reason));

    [Fact]
    public void The_same_priority_is_not_a_new_version() =>
        Assert.Equal("manufacturing_plan_priority_unchanged", Assert.Throws<ManufacturingRuleException>(() =>
            ProductionPlanPriorityV1.Default.Change(0, 0, "Synthetic unchanged")).Code);
}
