using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Application.WorkingTime;

namespace GoymenERP.Organization.UnitTests;

public sealed class WorkCenterWorkingTimeAvailabilityContractTests
{
    private static readonly Guid Company = Guid.Parse("17000000-0000-4000-8000-000000000001");
    private static readonly Guid WorkCenter = Guid.Parse("17000000-0000-4000-8000-000000000002");
    private static readonly Guid Calendar = Guid.Parse("17000000-0000-4000-8000-000000000003");
    private static readonly DateTimeOffset From = new(2026, 10, 6, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Through = From.AddDays(1);

    [Fact]
    public void Complete_zero_time_is_available_only_with_full_identity_version_provenance_and_coverage()
    {
        var query = Query();

        var snapshot = Snapshot(query, []);
        var answer = OrganizationWorkCenterWorkingTimeAvailabilityAnswerV1.Available(snapshot);

        Assert.Equal(OrganizationWorkCenterWorkingTimeAvailabilityOutcomeV1.Available, answer.Outcome);
        Assert.Same(snapshot, answer.Snapshot);
        Assert.Empty(snapshot.Intervals);
        Assert.Equal((Company, WorkCenter, Calendar, 7L),
            (snapshot.CompanyId, snapshot.WorkCenterId, snapshot.CalendarId, snapshot.CalendarVersion));
        Assert.Equal((" calendar:v7/α ", " zone:factory-a/β ", 11L, "tz-bind:v11/β"),
            (snapshot.CalendarSourceRevision, snapshot.TimeZoneId, snapshot.TimeZoneBindingVersion,
                snapshot.TimeZoneSourceRevision));
        Assert.Equal((From, Through), (snapshot.FromUtc, snapshot.ThroughUtc));
    }

    [Fact]
    public void Ordered_adjacent_utc_intervals_are_retained_losslessly_without_double_counting()
    {
        var first = new WorkCenterWorkingTimeIntervalV1(From.AddHours(6), From.AddHours(10));
        var second = new WorkCenterWorkingTimeIntervalV1(first.EndsAtUtc, From.AddHours(14));

        var snapshot = Snapshot(Query(), [first, second]);

        Assert.Equal([first, second], snapshot.Intervals);
        Assert.Equal(snapshot.Intervals[0].EndsAtUtc, snapshot.Intervals[1].StartsAtUtc);
    }

    [Fact]
    public void An_interval_may_cover_the_exact_half_open_query_boundaries()
    {
        var interval = new WorkCenterWorkingTimeIntervalV1(From, Through);

        var snapshot = Snapshot(Query(), [interval]);

        Assert.Equal(interval, Assert.Single(snapshot.Intervals));
    }

    [Fact]
    public void The_snapshot_defensively_owns_the_callers_interval_sequence()
    {
        var original = new WorkCenterWorkingTimeIntervalV1(From.AddHours(6), From.AddHours(10));
        var replacement = new WorkCenterWorkingTimeIntervalV1(From.AddHours(12), From.AddHours(14));
        var supplied = new[] { original };

        var snapshot = Snapshot(Query(), supplied);
        supplied[0] = replacement;

        Assert.Equal(original, Assert.Single(snapshot.Intervals));
        Assert.Throws<NotSupportedException>(() =>
            ((System.Collections.IList)snapshot.Intervals)[0] = replacement);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("workCenter")]
    public void Empty_query_identity_is_rejected(string fault)
    {
        Assert.Throws<ArgumentException>(() => new OrganizationWorkCenterWorkingTimeAvailabilityQueryV1(
            fault == "company" ? Guid.Empty : Company,
            fault == "workCenter" ? Guid.Empty : WorkCenter,
            From,
            Through));
    }

    [Theory]
    [InlineData("fromOffset")]
    [InlineData("throughOffset")]
    [InlineData("equal")]
    [InlineData("reversed")]
    public void Query_requires_a_positive_utc_half_open_window(string fault)
    {
        var from = fault == "fromOffset" ? From.ToOffset(TimeSpan.FromHours(3)) : From;
        var through = fault switch
        {
            "throughOffset" => Through.ToOffset(TimeSpan.FromHours(3)),
            "equal" => From,
            "reversed" => From.AddTicks(-1),
            _ => Through,
        };

        Assert.Throws<ArgumentException>(() =>
            new OrganizationWorkCenterWorkingTimeAvailabilityQueryV1(Company, WorkCenter, from, through));
    }

    [Theory]
    [InlineData("companyMismatch")]
    [InlineData("workCenterMismatch")]
    [InlineData("companyEmpty")]
    [InlineData("workCenterEmpty")]
    public void Available_source_identity_must_match_the_query(string fault)
    {
        var query = Query();

        Assert.Throws<ArgumentException>(() => WorkCenterWorkingTimeAvailabilitySnapshotV1.Create(
            query,
            fault switch
            {
                "companyMismatch" => Guid.NewGuid(),
                "companyEmpty" => Guid.Empty,
                _ => Company,
            },
            fault switch
            {
                "workCenterMismatch" => Guid.NewGuid(),
                "workCenterEmpty" => Guid.Empty,
                _ => WorkCenter,
            },
            Calendar,
            7,
            "calendar:v7",
            " zone:factory-a/β ",
            11,
            "tz-bind:v11",
            From,
            Through,
            []));
    }

    [Theory]
    [InlineData("calendarId")]
    [InlineData("calendarSource")]
    [InlineData("timeZoneId")]
    [InlineData("timeZoneSource")]
    public void Zero_time_cannot_hide_a_missing_calendar_or_timezone_source(string fault)
    {
        var query = Query();

        Assert.Throws<ArgumentException>(() => WorkCenterWorkingTimeAvailabilitySnapshotV1.Create(
            query,
            Company,
            WorkCenter,
            fault == "calendarId" ? Guid.Empty : Calendar,
            fault == "calendarVersion" ? 0 : 7,
            fault == "calendarSource" ? " " : "calendar:v7",
            fault == "timeZoneId" ? "" : " zone:factory-a/β ",
            fault == "timeZoneVersion" ? 0 : 11,
            fault == "timeZoneSource" ? "\t" : "tz-bind:v11",
            From,
            Through,
            []));
    }

    [Theory]
    [InlineData("calendarVersion")]
    [InlineData("timeZoneVersion")]
    public void Calendar_and_timezone_versions_must_be_positive(string fault)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkCenterWorkingTimeAvailabilitySnapshotV1.Create(
            Query(),
            Company,
            WorkCenter,
            Calendar,
            fault == "calendarVersion" ? 0 : 7,
            "calendar:v7",
            " zone:factory-a/β ",
            fault == "timeZoneVersion" ? 0 : 11,
            "tz-bind:v11",
            From,
            Through,
            []));
    }

    [Theory]
    [InlineData("fromOffset")]
    [InlineData("throughOffset")]
    [InlineData("partialFrom")]
    [InlineData("partialThrough")]
    public void Available_requires_exact_complete_utc_source_coverage(string fault)
    {
        var coveredFrom = fault switch
        {
            "fromOffset" => From.ToOffset(TimeSpan.FromHours(3)),
            "partialFrom" => From.AddMinutes(1),
            _ => From,
        };
        var coveredThrough = fault switch
        {
            "throughOffset" => Through.ToOffset(TimeSpan.FromHours(3)),
            "partialThrough" => Through.AddMinutes(-1),
            _ => Through,
        };

        Assert.Throws<ArgumentException>(() => WorkCenterWorkingTimeAvailabilitySnapshotV1.Create(
            Query(), Company, WorkCenter, Calendar, 7, "calendar:v7", " zone:factory-a/β ", 11, "tz-bind:v11",
            coveredFrom, coveredThrough, []));
    }

    [Theory]
    [MemberData(nameof(InvalidIntervals))]
    public void Intervals_must_be_utc_positive_bounded_ordered_and_nonoverlapping(
        IReadOnlyList<WorkCenterWorkingTimeIntervalV1> intervals)
    {
        Assert.Throws<ArgumentException>(() => Snapshot(Query(), intervals));
    }

    [Fact]
    public void Outcomes_are_distinct_and_only_available_carries_a_snapshot()
    {
        var notFound = OrganizationWorkCenterWorkingTimeAvailabilityAnswerV1.NotFound();
        var unavailable = OrganizationWorkCenterWorkingTimeAvailabilityAnswerV1.Unavailable();

        Assert.Equal(OrganizationWorkCenterWorkingTimeAvailabilityOutcomeV1.NotFound, notFound.Outcome);
        Assert.Null(notFound.Snapshot);
        Assert.Equal(OrganizationWorkCenterWorkingTimeAvailabilityOutcomeV1.Unavailable, unavailable.Outcome);
        Assert.Null(unavailable.Snapshot);
        Assert.Throws<ArgumentNullException>(() =>
            OrganizationWorkCenterWorkingTimeAvailabilityAnswerV1.Available(null!));
    }

    [Fact]
    public void The_internal_read_has_one_typed_query_and_no_authority_or_runtime_parameter()
    {
        var method = typeof(IOrganizationWorkCenterWorkingTimeAvailabilityV1).GetMethod("ReadAsync")!;
        var parameters = method.GetParameters();

        Assert.Equal(typeof(Task<OrganizationWorkCenterWorkingTimeAvailabilityAnswerV1>), method.ReturnType);
        Assert.Equal(typeof(OrganizationWorkCenterWorkingTimeAvailabilityQueryV1), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
        Assert.Equal(2, parameters.Length);
    }

    public static TheoryData<IReadOnlyList<WorkCenterWorkingTimeIntervalV1>> InvalidIntervals => new()
    {
        new[] { new WorkCenterWorkingTimeIntervalV1(From.ToOffset(TimeSpan.FromHours(3)), From.AddHours(1)) },
        new[] { new WorkCenterWorkingTimeIntervalV1(From, Through.ToOffset(TimeSpan.FromHours(3))) },
        new[] { new WorkCenterWorkingTimeIntervalV1(From.AddHours(2), From.AddHours(2)) },
        new[] { new WorkCenterWorkingTimeIntervalV1(From.AddHours(3), From.AddHours(2)) },
        new[] { new WorkCenterWorkingTimeIntervalV1(From.AddTicks(-1), From.AddHours(1)) },
        new[] { new WorkCenterWorkingTimeIntervalV1(From.AddHours(1), Through.AddTicks(1)) },
        new[]
        {
            new WorkCenterWorkingTimeIntervalV1(From.AddHours(6), From.AddHours(10)),
            new WorkCenterWorkingTimeIntervalV1(From.AddHours(6), From.AddHours(10)),
        },
        new[]
        {
            new WorkCenterWorkingTimeIntervalV1(From.AddHours(6), From.AddHours(10)),
            new WorkCenterWorkingTimeIntervalV1(From.AddHours(9), From.AddHours(12)),
        },
        new[]
        {
            new WorkCenterWorkingTimeIntervalV1(From.AddHours(12), From.AddHours(14)),
            new WorkCenterWorkingTimeIntervalV1(From.AddHours(6), From.AddHours(10)),
        },
    };

    private static OrganizationWorkCenterWorkingTimeAvailabilityQueryV1 Query() =>
        new(Company, WorkCenter, From, Through);

    private static WorkCenterWorkingTimeAvailabilitySnapshotV1 Snapshot(
        OrganizationWorkCenterWorkingTimeAvailabilityQueryV1 query,
        IEnumerable<WorkCenterWorkingTimeIntervalV1> intervals) =>
        WorkCenterWorkingTimeAvailabilitySnapshotV1.Create(
            query,
            Company,
            WorkCenter,
            Calendar,
            7,
            " calendar:v7/α ",
            " zone:factory-a/β ",
            11,
            "tz-bind:v11/β",
            From,
            Through,
            intervals);
}
