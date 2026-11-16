using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// URT-SC33 W5 follow-up: the work center queue order (URT-D97: termin, priority score, creation) and the remaining kg
/// of each plan ahead (URT-D99, formula golden). Every kg, score and moment here is a synthetic test value.
/// </summary>
public sealed class ProductionWorkCenterQueueTests
{
    private static readonly DateTimeOffset Created = new(2026, 9, 18, 10, 0, 0, TimeSpan.Zero);

    private static ProductionQueueEntryV1 Entry(string id, DateOnly? dueDate, int priority = 0, int minutes = 0,
        decimal plannedKg = 100m, decimal processedKg = 0m) =>
        new(Guid.Parse(id), dueDate, priority, Created.AddMinutes(minutes), new Kilograms(plannedKg),
            new Kilograms(processedKg));

    [Theory]
    [InlineData("500", "0", "500")]
    [InlineData("500", "120.250", "379.75")]
    [InlineData("500", "500", "0")]
    [InlineData("500", "650.5", "0")]
    [InlineData("0", "0", "0")]
    public void The_remaining_kg_is_planned_minus_processed_and_never_below_zero(string planned, string processed,
        string expected)
    {
        var remaining = ProductionQueueRemainingKgCalculatorV1.Remaining(Kg(planned), Kg(processed));

        Assert.Equal(Kg(expected), remaining);
    }

    [Fact]
    public void The_queue_is_ordered_by_termin_then_higher_priority_then_earlier_creation_then_plan_id()
    {
        var undated = Entry("00000000-0000-0000-0000-000000000001", null, priority: 9, minutes: -60);
        var late = Entry("00000000-0000-0000-0000-000000000002", new DateOnly(2026, 10, 1), priority: 9);
        var earlyLowPriority = Entry("00000000-0000-0000-0000-000000000003", new DateOnly(2026, 9, 25), priority: 1);
        var earlyHighPriorityNewer = Entry("00000000-0000-0000-0000-000000000004", new DateOnly(2026, 9, 25),
            priority: 5, minutes: 10);
        var earlyHighPriorityOlder = Entry("00000000-0000-0000-0000-000000000006", new DateOnly(2026, 9, 25),
            priority: 5);
        var exactTieByIdentity = Entry("00000000-0000-0000-0000-000000000005", new DateOnly(2026, 9, 25), priority: 5);

        var ordered = ProductionWorkCenterQueueOrderV1.Order(
            [undated, late, earlyLowPriority, earlyHighPriorityNewer, earlyHighPriorityOlder, exactTieByIdentity]);

        Assert.Equal(
            [exactTieByIdentity, earlyHighPriorityOlder, earlyHighPriorityNewer, earlyLowPriority, late, undated],
            ordered);
    }

    [Fact]
    public void The_work_ahead_counts_only_plans_ordered_before_the_target_with_their_remaining_kg()
    {
        var target = Entry("00000000-0000-0000-0000-0000000000aa", new DateOnly(2026, 9, 25), priority: 2,
            plannedKg: 0m);
        var queued = new[]
        {
            Entry("00000000-0000-0000-0000-000000000001", new DateOnly(2026, 9, 24), plannedKg: 300m,
                processedKg: 120m),
            Entry("00000000-0000-0000-0000-000000000002", new DateOnly(2026, 9, 25), priority: 3, plannedKg: 200m,
                processedKg: 250m),
            Entry("00000000-0000-0000-0000-000000000003", new DateOnly(2026, 9, 25), priority: 2, minutes: -5,
                plannedKg: 80.5m),
            Entry("00000000-0000-0000-0000-000000000004", new DateOnly(2026, 9, 25), priority: 2, minutes: 5,
                plannedKg: 999m),
            Entry("00000000-0000-0000-0000-000000000005", new DateOnly(2026, 9, 25), priority: 1, plannedKg: 999m),
            Entry("00000000-0000-0000-0000-000000000006", null, priority: 9, plannedKg: 999m),
            // The target itself is never counted, even when the store returns it in the queue.
            target with { DueDate = new DateOnly(2026, 9, 1), PlannedInputKg = new Kilograms(999m) },
        };

        var ahead = ProductionWorkCenterQueueOrderV1.Ahead(queued, target);

        // 180 (300 - 120) + 0 (over-processed) + 80.5 = 260.5 kg in three plans.
        Assert.Equal(new ProductionQueueAheadV1(3, Kg("260.5")), ahead);
    }

    [Fact]
    public void An_empty_queue_has_nothing_ahead_and_a_null_entry_is_refused()
    {
        var target = Entry("00000000-0000-0000-0000-0000000000aa", null);

        Assert.Equal(new ProductionQueueAheadV1(0, Kilograms.Zero), ProductionWorkCenterQueueOrderV1.Ahead([], target));
        Assert.Throws<ArgumentException>(() => ProductionWorkCenterQueueOrderV1.Order([target, null!]));
    }

    private static Kilograms Kg(string value) =>
        new(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
}
