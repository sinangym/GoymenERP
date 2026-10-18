using GoymenERP.Modules.Manufacturing.Domain.Scheduling;

namespace GoymenERP.Manufacturing.UnitTests.Scheduling;

public sealed class ManufacturingScheduleCalculatorV1Tests
{
    private static readonly Guid Company = Guid.Parse("91000000-0000-4000-8000-000000000001");
    private static readonly Guid WorkCenter = Guid.Parse("92000000-0000-4000-8000-000000000001");
    private static readonly Guid Order = Guid.Parse("93000000-0000-4000-8000-000000000001");
    private static readonly Guid Calendar = Guid.Parse("94000000-0000-4000-8000-000000000001");
    private static readonly Guid SetupSource = Guid.Parse("95000000-0000-4000-8000-000000000001");
    private static readonly Guid ChangeoverSource = Guid.Parse("95000000-0000-4000-8000-000000000002");
    private static readonly Guid RunSource = Guid.Parse("95000000-0000-4000-8000-000000000003");
    private static readonly Guid CleanupSource = Guid.Parse("95000000-0000-4000-8000-000000000004");
    private static readonly DateTimeOffset Day = Utc(2026, 10, 20, 0, 0);

    [Fact]
    public void Golden_latest_readiness_plus_150_working_minutes_finishes_at_1630()
    {
        var input = Input(
            ready: Utc(2026, 10, 20, 14, 0),
            intervals:
            [
                Interval(8, 0, 10, 0),
                Interval(10, 45, 12, 0),
                Interval(13, 0, 17, 0),
            ],
            setup: Duration(15, SetupSource),
            changeover: Duration(10, ChangeoverSource),
            run: Duration(120, RunSource),
            cleanup: Duration(5, CleanupSource));

        var result = ManufacturingScheduleCalculatorV1.Calculate(input);

        Assert.Equal(ManufacturingScheduleOutcomeV1.Ready, result.Outcome);
        Assert.Null(result.Code);
        Assert.Equal(Utc(2026, 10, 20, 14, 0), result.EffectiveReadyAtUtc);
        Assert.Equal(Utc(2026, 10, 20, 14, 0), result.PlannedStartUtc);
        Assert.Equal(Utc(2026, 10, 20, 16, 30), result.PlannedFinishUtc);
        Assert.Equal(150L, result.TotalWorkingMinutes);
    }

    [Fact]
    public void Overlapping_availability_is_unioned_and_never_double_counted()
    {
        var input = Input(
            ready: Utc(2026, 10, 20, 8, 0),
            intervals:
            [
                Interval(8, 0, 12, 0),
                Interval(10, 0, 14, 0),
            ],
            setup: Duration(0, SetupSource),
            changeover: Duration(0, ChangeoverSource),
            run: Duration(300, RunSource),
            cleanup: Duration(0, CleanupSource));

        var result = ManufacturingScheduleCalculatorV1.Calculate(input);

        Assert.Equal(ManufacturingScheduleOutcomeV1.Ready, result.Outcome);
        Assert.Equal(Utc(2026, 10, 20, 13, 0), result.PlannedFinishUtc);
    }

    [Fact]
    public void Ready_at_a_half_open_interval_end_starts_at_the_next_interval()
    {
        var input = Input(
            ready: Utc(2026, 10, 20, 10, 0),
            intervals: [Interval(8, 0, 10, 0), Interval(10, 45, 12, 0)],
            setup: Duration(0, SetupSource),
            changeover: Duration(0, ChangeoverSource),
            run: Duration(30, RunSource),
            cleanup: Duration(0, CleanupSource));

        var result = ManufacturingScheduleCalculatorV1.Calculate(input);

        Assert.Equal(ManufacturingScheduleOutcomeV1.Ready, result.Outcome);
        Assert.Equal(Utc(2026, 10, 20, 10, 45), result.PlannedStartUtc);
        Assert.Equal(Utc(2026, 10, 20, 11, 15), result.PlannedFinishUtc);
    }

    [Fact]
    public void Zero_or_insufficient_availability_is_unavailable_not_an_invented_shift()
    {
        var none = ManufacturingScheduleCalculatorV1.Calculate(Input(intervals: []));
        var insufficient = ManufacturingScheduleCalculatorV1.Calculate(Input(
            intervals: [Interval(8, 0, 8, 30)],
            run: Duration(60, RunSource)));

        Assert.Equal(ManufacturingScheduleOutcomeV1.Unavailable, none.Outcome);
        Assert.Equal(ManufacturingScheduleCodesV1.CapacityUnavailable, none.Code);
        Assert.Equal(ManufacturingScheduleOutcomeV1.Unavailable, insufficient.Outcome);
        Assert.Equal(ManufacturingScheduleCodesV1.CapacityUnavailable, insufficient.Code);
    }

    [Fact]
    public void Missing_run_or_provenance_is_unavailable_and_never_zero()
    {
        var missingRun = ManufacturingScheduleCalculatorV1.Calculate(Input(run: new(
            null,
            RunSource,
            4,
            "RUN-REV-4")));
        var missingProvenance = ManufacturingScheduleCalculatorV1.Calculate(Input(setup: new(
            15,
            null,
            null,
            null)));

        Assert.Equal(ManufacturingScheduleOutcomeV1.Unavailable, missingRun.Outcome);
        Assert.Equal(ManufacturingScheduleCodesV1.InputUnavailable, missingRun.Code);
        Assert.Equal(ManufacturingScheduleOutcomeV1.Unavailable, missingProvenance.Outcome);
        Assert.Equal(ManufacturingScheduleCodesV1.InputUnavailable, missingProvenance.Code);
    }

    [Fact]
    public void Missing_readiness_is_unavailable_while_local_time_is_a_conflict()
    {
        var missing = BaseItem(Utc(2026, 10, 20, 9, 0)) with { MaterialReadyAtUtc = null };
        var local = BaseItem(Utc(2026, 10, 20, 9, 0)) with
        {
            ApprovalReadyAtUtc = new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.FromHours(3)),
        };

        var missingResult = ManufacturingScheduleCalculatorV1.Calculate(Input(item: missing));
        var localResult = ManufacturingScheduleCalculatorV1.Calculate(Input(item: local));

        Assert.Equal(ManufacturingScheduleOutcomeV1.Unavailable, missingResult.Outcome);
        Assert.Equal(ManufacturingScheduleOutcomeV1.Conflict, localResult.Outcome);
    }

    [Fact]
    public void Negative_duration_and_arithmetic_overflow_are_conflicts()
    {
        var negative = ManufacturingScheduleCalculatorV1.Calculate(Input(
            setup: Duration(-1, SetupSource)));
        var overflow = ManufacturingScheduleCalculatorV1.Calculate(Input(
            setup: Duration(long.MaxValue, SetupSource),
            changeover: Duration(long.MaxValue, ChangeoverSource)));

        Assert.Equal(ManufacturingScheduleOutcomeV1.Conflict, negative.Outcome);
        Assert.Equal(ManufacturingScheduleCodesV1.InputConflict, negative.Code);
        Assert.Equal(ManufacturingScheduleOutcomeV1.Conflict, overflow.Outcome);
        Assert.Equal(ManufacturingScheduleCodesV1.InputConflict, overflow.Code);
    }

    [Fact]
    public void Invalid_interval_or_identity_scope_is_a_conflict()
    {
        var inverted = ManufacturingScheduleCalculatorV1.Calculate(Input(
            intervals: [Interval(10, 0, 9, 0)]));
        var wrongWorkCenter = ManufacturingScheduleCalculatorV1.Calculate(Input(
            availabilityWorkCenter: Guid.NewGuid()));

        Assert.Equal(ManufacturingScheduleOutcomeV1.Conflict, inverted.Outcome);
        Assert.Equal(ManufacturingScheduleOutcomeV1.Conflict, wrongWorkCenter.Outcome);
    }

    [Fact]
    public void Same_input_produces_the_same_result_without_mutating_the_source_intervals()
    {
        var mutable = new List<ManufacturingScheduleAvailabilityIntervalV1>
        {
            Interval(13, 0, 17, 0),
            Interval(8, 0, 12, 0),
        };
        var input = Input(intervals: mutable);
        var snapshotIntervals = input.Availability!.Intervals.ToArray();

        var first = ManufacturingScheduleCalculatorV1.Calculate(input);
        var second = ManufacturingScheduleCalculatorV1.Calculate(input);

        Assert.Equal(first, second);
        Assert.Equal(snapshotIntervals, input.Availability.Intervals.ToArray());
        Assert.Equal(2, mutable.Count);
    }

    private static ManufacturingScheduleInputV1 Input(
        ManufacturingScheduleQueueItemV1? item = null,
        DateTimeOffset? ready = null,
        IEnumerable<ManufacturingScheduleAvailabilityIntervalV1>? intervals = null,
        ManufacturingScheduleDurationSnapshotV1? setup = null,
        ManufacturingScheduleDurationSnapshotV1? changeover = null,
        ManufacturingScheduleDurationSnapshotV1? run = null,
        ManufacturingScheduleDurationSnapshotV1? cleanup = null,
        Guid? availabilityWorkCenter = null) =>
        new(
            item ?? BaseItem(ready ?? Utc(2026, 10, 20, 9, 0)),
            new ManufacturingScheduleAvailabilitySnapshotV1(
                Company,
                availabilityWorkCenter ?? WorkCenter,
                Calendar,
                7,
                "CAL-REV-7",
                "Europe/Istanbul",
                3,
                "TZ-REV-3",
                Day,
                Day.AddDays(1),
                intervals ?? new[] { Interval(8, 0, 17, 0) }),
            setup ?? Duration(15, SetupSource),
            changeover ?? Duration(10, ChangeoverSource),
            run ?? Duration(120, RunSource),
            cleanup ?? Duration(5, CleanupSource));

    private static ManufacturingScheduleQueueItemV1 BaseItem(DateTimeOffset ready) =>
        new(
            Company,
            WorkCenter,
            Order,
            11,
            ready.AddHours(-1),
            ready.AddMinutes(-45),
            ready.AddMinutes(-30),
            ready);

    private static ManufacturingScheduleDurationSnapshotV1 Duration(long minutes, Guid sourceId) =>
        new(minutes, sourceId, 4, $"SOURCE-{sourceId:N}-V4");

    private static ManufacturingScheduleAvailabilityIntervalV1 Interval(
        int startHour,
        int startMinute,
        int endHour,
        int endMinute) =>
        new(
            Utc(2026, 10, 20, startHour, startMinute),
            Utc(2026, 10, 20, endHour, endMinute));

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);
}
