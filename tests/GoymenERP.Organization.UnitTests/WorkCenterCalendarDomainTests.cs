using GoymenERP.Modules.Organization.Domain.WorkCenters;

namespace GoymenERP.Organization.UnitTests;

public sealed class WorkCenterCalendarDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Urtd11_default_is_one_eight_hour_shift_from_monday_to_friday_without_closed_dates()
    {
        var calendar = WorkCenterCalendarDefinition.Urtd11Default;

        Assert.Equal("Europe/Istanbul", WorkCenterCalendarDefinition.BusinessTimeZoneId);
        Assert.Equal(
            [
                Shift(DayOfWeek.Monday, 8, 0, 16, 0),
                Shift(DayOfWeek.Tuesday, 8, 0, 16, 0),
                Shift(DayOfWeek.Wednesday, 8, 0, 16, 0),
                Shift(DayOfWeek.Thursday, 8, 0, 16, 0),
                Shift(DayOfWeek.Friday, 8, 0, 16, 0),
            ],
            calendar.Shifts);
        Assert.Empty(calendar.ClosedDates);
        Assert.DoesNotContain(calendar.Shifts, shift => shift.Day is DayOfWeek.Saturday or DayOfWeek.Sunday);
    }

    [Fact]
    public void A_definition_is_canonical_shifts_by_iso_weekday_then_start_and_dates_ascending()
    {
        var definition = WorkCenterCalendarDefinition.Create(
            [
                Shift(DayOfWeek.Sunday, 9, 0, 12, 0),
                Shift(DayOfWeek.Monday, 16, 0, 23, 59),
                Shift(DayOfWeek.Monday, 8, 0, 16, 0),
            ],
            [new DateOnly(2026, 12, 31), new DateOnly(2026, 1, 1)]);

        Assert.Equal(
            [
                Shift(DayOfWeek.Monday, 8, 0, 16, 0),
                Shift(DayOfWeek.Monday, 16, 0, 23, 59),
                Shift(DayOfWeek.Sunday, 9, 0, 12, 0),
            ],
            definition.Shifts);
        Assert.Equal([new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)], definition.ClosedDates);
    }

    [Fact]
    public void Two_definitions_with_the_same_content_in_another_order_are_equal()
    {
        var first = WorkCenterCalendarDefinition.Create(
            [Shift(DayOfWeek.Tuesday, 8, 0, 12, 0), Shift(DayOfWeek.Monday, 8, 0, 12, 0)],
            [new DateOnly(2026, 5, 19), new DateOnly(2026, 4, 23)]);
        var second = WorkCenterCalendarDefinition.Create(
            [Shift(DayOfWeek.Monday, 8, 0, 12, 0), Shift(DayOfWeek.Tuesday, 8, 0, 12, 0)],
            [new DateOnly(2026, 4, 23), new DateOnly(2026, 5, 19)]);
        var different = WorkCenterCalendarDefinition.Create(
            [Shift(DayOfWeek.Monday, 8, 0, 12, 0), Shift(DayOfWeek.Tuesday, 8, 0, 12, 0)],
            [new DateOnly(2026, 4, 23)]);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, different);
    }

    [Fact]
    public void The_default_content_built_by_hand_equals_the_default()
    {
        var byHand = WorkCenterCalendarDefinition.Create(
            Enumerable.Reverse(WorkCenterCalendarDefinition.Urtd11Default.Shifts),
            []);

        Assert.Equal(WorkCenterCalendarDefinition.Urtd11Default, byHand);
    }

    [Fact]
    public void Touching_windows_on_one_day_are_allowed()
    {
        var error = WorkCenterCalendarDefinition.TryCreate(
            [Shift(DayOfWeek.Monday, 0, 0, 8, 0), Shift(DayOfWeek.Monday, 8, 0, 16, 0)],
            [],
            out var definition);

        Assert.Equal(WorkCenterCalendarDefinitionError.None, error);
        Assert.NotNull(definition);
    }

    [Fact]
    public void The_same_window_on_two_days_does_not_overlap()
    {
        var error = WorkCenterCalendarDefinition.TryCreate(
            [Shift(DayOfWeek.Monday, 8, 0, 16, 0), Shift(DayOfWeek.Tuesday, 8, 0, 16, 0)],
            [],
            out _);

        Assert.Equal(WorkCenterCalendarDefinitionError.None, error);
    }

    [Fact]
    public void A_calendar_without_a_shift_is_refused()
    {
        Assert.Equal(WorkCenterCalendarDefinitionError.NoShift, TryCreate([], []));
    }

    [Fact]
    public void Overlapping_windows_on_one_day_are_refused()
    {
        Assert.Equal(
            WorkCenterCalendarDefinitionError.ShiftsOverlap,
            TryCreate([Shift(DayOfWeek.Friday, 8, 0, 16, 0), Shift(DayOfWeek.Friday, 15, 59, 20, 0)], []));
        Assert.Equal(
            WorkCenterCalendarDefinitionError.ShiftsOverlap,
            TryCreate([Shift(DayOfWeek.Friday, 8, 0, 16, 0), Shift(DayOfWeek.Friday, 8, 0, 16, 0)], []));
    }

    [Fact]
    public void An_empty_window_is_refused()
    {
        Assert.Equal(
            WorkCenterCalendarDefinitionError.ShiftNotPositive,
            TryCreate([Shift(DayOfWeek.Monday, 8, 0, 8, 0)], []));
        Assert.Equal(
            WorkCenterCalendarDefinitionError.ShiftNotPositive,
            TryCreate([Shift(DayOfWeek.Monday, 0, 0, 0, 0)], []));
    }

    [Fact]
    public void An_end_before_the_start_is_an_overnight_shift_that_ends_on_the_next_day()
    {
        var error = WorkCenterCalendarDefinition.TryCreate(
            [Shift(DayOfWeek.Monday, 22, 0, 6, 0), Shift(DayOfWeek.Monday, 8, 0, 16, 0)],
            [],
            out var definition);

        Assert.Equal(WorkCenterCalendarDefinitionError.None, error);
        Assert.NotNull(definition);
        Assert.Equal(
            [Shift(DayOfWeek.Monday, 8, 0, 16, 0), Shift(DayOfWeek.Monday, 22, 0, 6, 0)],
            definition.Shifts);
        var overnight = definition.Shifts[1];
        Assert.True(overnight.EndsNextDay);
        Assert.Equal(8 * 60, overnight.LengthMinutes);
        Assert.False(definition.Shifts[0].EndsNextDay);
        Assert.Equal(8 * 60, definition.Shifts[0].LengthMinutes);
    }

    [Fact]
    public void A_shift_that_ends_at_midnight_ends_on_the_next_day_without_spilling_into_it()
    {
        var untilMidnight = Shift(DayOfWeek.Monday, 16, 0, 0, 0);

        Assert.True(untilMidnight.EndsNextDay);
        Assert.Equal(8 * 60, untilMidnight.LengthMinutes);
        Assert.Equal(
            WorkCenterCalendarDefinitionError.None,
            TryCreate([untilMidnight, Shift(DayOfWeek.Tuesday, 0, 0, 8, 0)], []));
    }

    [Fact]
    public void The_week_clock_places_a_shift_by_its_iso_weekday_and_start()
    {
        Assert.Equal(7 * 24 * 60, WorkCenterShift.MinutesPerWeek);
        Assert.Equal(24 * 60, WorkCenterShift.MinutesPerDay);
        Assert.Equal(8 * 60, Shift(DayOfWeek.Monday, 8, 0, 16, 0).WeekStartMinute);
        Assert.Equal((6 * 24 * 60) + (22 * 60) + 30, Shift(DayOfWeek.Sunday, 22, 30, 6, 0).WeekStartMinute);
        Assert.Equal((7 * 60) + 30, Shift(DayOfWeek.Sunday, 22, 30, 6, 0).LengthMinutes);
    }

    [Fact]
    public void The_next_day_part_of_an_overnight_shift_may_not_overlap_the_next_days_shift()
    {
        Assert.Equal(
            WorkCenterCalendarDefinitionError.ShiftsOverlap,
            TryCreate([Shift(DayOfWeek.Monday, 22, 0, 6, 0), Shift(DayOfWeek.Tuesday, 5, 59, 14, 0)], []));
        Assert.Equal(
            WorkCenterCalendarDefinitionError.ShiftsOverlap,
            TryCreate([Shift(DayOfWeek.Monday, 22, 0, 6, 0), Shift(DayOfWeek.Monday, 23, 0, 23, 30)], []));
        Assert.Equal(
            WorkCenterCalendarDefinitionError.ShiftsOverlap,
            TryCreate([Shift(DayOfWeek.Monday, 20, 0, 4, 0), Shift(DayOfWeek.Monday, 22, 0, 6, 0)], []));
    }

    [Fact]
    public void A_sunday_overnight_shift_wraps_into_monday_of_the_next_week()
    {
        Assert.Equal(
            WorkCenterCalendarDefinitionError.ShiftsOverlap,
            TryCreate([Shift(DayOfWeek.Sunday, 22, 0, 6, 0), Shift(DayOfWeek.Monday, 5, 0, 13, 0)], []));
        Assert.Equal(
            WorkCenterCalendarDefinitionError.None,
            TryCreate([Shift(DayOfWeek.Sunday, 22, 0, 6, 0), Shift(DayOfWeek.Monday, 6, 0, 14, 0)], []));
    }

    [Fact]
    public void An_overnight_shift_may_touch_the_next_days_shift()
    {
        Assert.Equal(
            WorkCenterCalendarDefinitionError.None,
            TryCreate(
                [
                    Shift(DayOfWeek.Monday, 6, 0, 14, 0),
                    Shift(DayOfWeek.Monday, 14, 0, 22, 0),
                    Shift(DayOfWeek.Monday, 22, 0, 6, 0),
                    Shift(DayOfWeek.Tuesday, 6, 0, 14, 0),
                ],
                []));
    }

    [Fact]
    public void A_window_off_the_whole_minute_is_refused()
    {
        var withSeconds = new WorkCenterShift(DayOfWeek.Monday, new TimeOnly(8, 0, 30), new TimeOnly(16, 0));

        Assert.Equal(WorkCenterCalendarDefinitionError.ShiftNotOnWholeMinute, TryCreate([withSeconds], []));
    }

    [Fact]
    public void An_undefined_weekday_is_refused()
    {
        var undefined = new WorkCenterShift((DayOfWeek)7, new TimeOnly(8, 0), new TimeOnly(16, 0));

        Assert.Equal(WorkCenterCalendarDefinitionError.InvalidDay, TryCreate([undefined], []));
    }

    [Fact]
    public void More_windows_than_the_per_day_bound_are_refused()
    {
        var shifts = Enumerable.Range(0, WorkCenterCalendarDefinition.MaximumShiftsPerDay + 1)
            .Select(hour => Shift(DayOfWeek.Monday, hour, 0, hour, 30))
            .ToArray();

        Assert.Equal(WorkCenterCalendarDefinitionError.TooManyShifts, TryCreate(shifts, []));
    }

    [Fact]
    public void More_windows_than_the_calendar_bound_are_refused()
    {
        var shifts = Enum.GetValues<DayOfWeek>()
            .SelectMany(day => Enumerable.Range(0, WorkCenterCalendarDefinition.MaximumShiftsPerDay)
                .Select(hour => Shift(day, hour, 0, hour, 30)))
            .Append(Shift(DayOfWeek.Monday, 20, 0, 21, 0))
            .ToArray();

        Assert.Equal(WorkCenterCalendarDefinition.MaximumShiftCount + 1, shifts.Length);
        Assert.Equal(WorkCenterCalendarDefinitionError.TooManyShifts, TryCreate(shifts, []));
    }

    [Fact]
    public void A_repeated_closed_date_is_refused()
    {
        Assert.Equal(
            WorkCenterCalendarDefinitionError.DuplicateClosedDate,
            TryCreate([Shift(DayOfWeek.Monday, 8, 0, 16, 0)], [new DateOnly(2026, 10, 29), new DateOnly(2026, 10, 29)]));
    }

    [Fact]
    public void More_closed_dates_than_the_bound_are_refused()
    {
        var first = new DateOnly(2026, 1, 1);
        var dates = Enumerable.Range(0, WorkCenterCalendarDefinition.MaximumClosedDateCount + 1)
            .Select(first.AddDays)
            .ToArray();

        Assert.Equal(
            WorkCenterCalendarDefinitionError.TooManyClosedDates,
            TryCreate([Shift(DayOfWeek.Monday, 8, 0, 16, 0)], dates));
    }

    [Fact]
    public void Create_throws_for_a_refused_definition()
    {
        Assert.Throws<ArgumentException>(() => WorkCenterCalendarDefinition.Create([], []));
    }

    [Fact]
    public void A_new_calendar_starts_at_the_first_version_and_a_revision_increments_it()
    {
        var companyId = Guid.NewGuid();
        var workCenterId = Guid.NewGuid();
        var calendar = WorkCenterCalendar.Create(Guid.NewGuid(), companyId, workCenterId, Now);

        Assert.Equal(WorkCenterCalendar.FirstVersion, calendar.Version);
        Assert.Equal(1, calendar.Version);
        Assert.Equal(companyId, calendar.CompanyId);
        Assert.Equal(workCenterId, calendar.WorkCenterId);
        Assert.Equal(Now, calendar.CreatedAtUtc);

        var later = Now.AddHours(1);
        calendar.Revise(later);

        Assert.Equal(2, calendar.Version);
        Assert.Equal(Now, calendar.CreatedAtUtc);
        Assert.Equal(later, calendar.UpdatedAtUtc);
    }

    [Fact]
    public void A_calendar_needs_its_identity_company_and_work_center()
    {
        Assert.Throws<ArgumentException>(() => WorkCenterCalendar.Create(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), Now));
        Assert.Throws<ArgumentException>(() => WorkCenterCalendar.Create(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), Now));
        Assert.Throws<ArgumentException>(() => WorkCenterCalendar.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, Now));
    }

    [Fact]
    public void Revision_rows_carry_the_company_calendar_and_current_version()
    {
        var companyId = Guid.NewGuid();
        var calendar = WorkCenterCalendar.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), Now);
        calendar.Revise(Now.AddMinutes(5));
        var definition = WorkCenterCalendarDefinition.Create(
            [Shift(DayOfWeek.Saturday, 8, 0, 12, 0), Shift(DayOfWeek.Monday, 6, 0, 14, 0)],
            [new DateOnly(2026, 8, 30)]);

        var rows = calendar.RowsFor(definition);

        Assert.Equal(2, rows.Shifts.Count);
        Assert.All(rows.Shifts, row =>
        {
            Assert.Equal(companyId, row.CompanyId);
            Assert.Equal(calendar.Id, row.CalendarId);
            Assert.Equal(2, row.Revision);
            Assert.NotEqual(Guid.Empty, row.Id);
        });
        Assert.Equal(definition.Shifts, rows.Shifts.Select(row => row.ToShift()).ToArray());
        var closedDate = Assert.Single(rows.ClosedDates);
        Assert.Equal(new DateOnly(2026, 8, 30), closedDate.Date);
        Assert.Equal(2, closedDate.Revision);
        Assert.Equal(companyId, closedDate.CompanyId);
        Assert.Equal(calendar.Id, closedDate.CalendarId);
        Assert.Equal(3, rows.Shifts.Select(row => row.Id).Append(closedDate.Id).Distinct().Count());
    }

    [Fact]
    public void Sunday_sorts_after_saturday_in_the_iso_week()
    {
        Assert.Equal(7, Shift(DayOfWeek.Sunday, 8, 0, 9, 0).IsoDayOrder);
        Assert.Equal(1, Shift(DayOfWeek.Monday, 8, 0, 9, 0).IsoDayOrder);
        Assert.Equal(6, Shift(DayOfWeek.Saturday, 8, 0, 9, 0).IsoDayOrder);
    }

    private static WorkCenterCalendarDefinitionError TryCreate(
        IEnumerable<WorkCenterShift> shifts,
        IEnumerable<DateOnly> closedDates) =>
        WorkCenterCalendarDefinition.TryCreate(shifts, closedDates, out _);

    private static WorkCenterShift Shift(DayOfWeek day, int startHour, int startMinute, int endHour, int endMinute) =>
        new(day, new TimeOnly(startHour, startMinute), new TimeOnly(endHour, endMinute));
}
