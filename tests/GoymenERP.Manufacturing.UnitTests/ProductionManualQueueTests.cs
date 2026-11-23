using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

public sealed class ProductionManualQueueTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static ProductionQueueAutomaticKeyV1 Key(Guid id, int day, int score = 0) => new(id, new DateOnly(2027, 1, day), score, DateTimeOffset.UnixEpoch);

    [Fact]
    public void An_arrival_is_inserted_before_the_first_later_key_while_the_reversed_old_pair_stays_reversed()
    {
        Assert.Equal([C, B, A], ProductionWorkCenterQueueOrderV1.Reconcile([B, A], [Key(A, 10), Key(B, 20), Key(C, 15)]));
    }

    [Fact]
    public void Changing_due_dates_and_priority_scores_never_resorts_existing_members()
    {
        Assert.Equal([B, A], ProductionWorkCenterQueueOrderV1.Reconcile([B, A], [Key(A, 1, 100), Key(B, 30, -100)]));
        Assert.Equal([B, A], ProductionWorkCenterQueueOrderV1.Reconcile([B, A], [Key(A, 10, 100), Key(B, 10, -100)]));
    }

    [Fact]
    public void Departures_are_removed_and_multiple_arrivals_use_the_existing_total_automatic_tie_break()
    {
        var same = Key(A, 15);
        Assert.Equal([A, C], ProductionWorkCenterQueueOrderV1.Reconcile([B], [same, same with { PlanId = C }]));
        Assert.Empty(ProductionWorkCenterQueueOrderV1.Reconcile([B, A], []));
    }

    [Fact]
    public void Manual_movement_can_override_every_automatic_key_and_requires_a_real_change()
    {
        Assert.Equal([B, A, C], ProductionWorkCenterQueueOrderV1.Move([A, C, B], B, A));
        Assert.Equal([C, B, A], ProductionWorkCenterQueueOrderV1.Move([A, C, B], A, null));
        Assert.Equal("manufacturing_queue_unchanged", Assert.Throws<ManufacturingRuleException>(() => ProductionWorkCenterQueueOrderV1.Move([A, B], A, B)).Code);
        Assert.Equal("manufacturing_queue_member_invalid", Assert.Throws<ManufacturingRuleException>(() => ProductionWorkCenterQueueOrderV1.Move([A, B], C, B)).Code);
    }

    [Fact]
    public void Termin_suggestion_counts_the_actual_manual_prefix_and_a_draft_target_uses_the_arrival_rule_without_writing()
    {
        var a = new ProductionQueueEntryV1(A, new DateOnly(2027, 1, 10), 0, DateTimeOffset.UnixEpoch, new Kilograms(10m), Kilograms.Zero);
        var b = new ProductionQueueEntryV1(B, new DateOnly(2027, 1, 20), 0, DateTimeOffset.UnixEpoch, new Kilograms(20m), new Kilograms(5m));
        var c = new ProductionQueueEntryV1(C, new DateOnly(2027, 1, 15), 0, DateTimeOffset.UnixEpoch, new Kilograms(5m), Kilograms.Zero);
        Assert.Equal(new ProductionQueueAheadV1(1, new Kilograms(15m)), ProductionWorkCenterQueueOrderV1.AheadRealized([b], a, true, [B, A]));
        Assert.Equal([C, B, A], ProductionWorkCenterQueueOrderV1.Realized([a, b], c, false, [B, A]).Select(entry => entry.PlanId));
        Assert.Equal(new ProductionQueueAheadV1(0, Kilograms.Zero), ProductionWorkCenterQueueOrderV1.AheadRealized([a, b], c, false, [B, A]));
        Assert.Throws<ManufacturingRuleException>(() => ProductionWorkCenterQueueOrderV1.Realized([b], a, true, [A]));
    }
}
