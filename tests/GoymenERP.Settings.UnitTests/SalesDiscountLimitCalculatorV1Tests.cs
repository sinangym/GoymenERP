using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Settings.UnitTests;

/// <summary>SAT-D36 / SAT-D62 and the EK10 implementation notes: the one calculator of a group's discount limit.</summary>
public sealed class SalesDiscountLimitCalculatorV1Tests
{
    private static readonly Guid Seller = Guid.Parse("6a4e0c51-6f5b-4d3e-9f0a-000000000001");
    private static readonly Guid Manager = Guid.Parse("6a4e0c51-6f5b-4d3e-9f0a-000000000002");
    private static readonly Guid Senior = Guid.Parse("6a4e0c51-6f5b-4d3e-9f0a-000000000003");
    private static readonly DateOnly Day = new(2026, 10, 27);

    [Fact]
    public void A_fixed_limit_is_in_force_from_its_date_and_a_linked_limit_adds_its_points_to_the_linked_groups_limit()
    {
        var groups = Groups(
            Group(Seller, true, Fixed(Seller, 1, 35m, Day)),
            Group(Manager, true, Linked(Manager, 1, Seller, 3m, Day)));

        var seller = SalesDiscountLimitCalculatorV1.Resolve(groups, Seller, Day);
        var manager = SalesDiscountLimitCalculatorV1.Resolve(groups, Manager, Day.AddDays(10));

        Assert.Equal((SalesDiscountLimitResolutionOutcomeV1.Defined, 35m, Day, (DateOnly?)null),
            (seller.Outcome, seller.Percent, seller.EffectiveFrom, seller.EffectiveThrough));
        Assert.Equal((SalesDiscountLimitResolutionOutcomeV1.Defined, 38m, 1L),
            (manager.Outcome, manager.Percent, manager.Record!.Version));
        Assert.Equal(SalesDiscountLimitResolutionOutcomeV1.None,
            SalesDiscountLimitCalculatorV1.Resolve(groups, Seller, Day.AddDays(-1)).Outcome);
    }

    [Fact]
    public void A_linked_limit_follows_the_linked_groups_versions_and_reports_the_window_it_holds_over()
    {
        var groups = Groups(
            Group(Seller, true, Fixed(Seller, 1, 35m, Day), Fixed(Seller, 2, 40m, Day.AddDays(5))),
            Group(Manager, true, Linked(Manager, 1, Seller, 3m, Day.AddDays(2))));

        var early = SalesDiscountLimitCalculatorV1.Resolve(groups, Manager, Day.AddDays(3));
        var late = SalesDiscountLimitCalculatorV1.Resolve(groups, Manager, Day.AddDays(6));

        Assert.Equal((38m, Day.AddDays(2), (DateOnly?)Day.AddDays(4)), (early.Percent, early.EffectiveFrom, early.EffectiveThrough));
        Assert.Equal((43m, Day.AddDays(5), (DateOnly?)null), (late.Percent, late.EffectiveFrom, late.EffectiveThrough));
    }

    [Fact]
    public void A_same_day_correction_supersedes_the_earlier_record_of_that_day()
    {
        var groups = Groups(Group(Seller, true,
            Fixed(Seller, 1, 10m, Day), Fixed(Seller, 2, 0m, Day.AddDays(1)), Fixed(Seller, 3, 5m, Day.AddDays(1))));

        var first = SalesDiscountLimitCalculatorV1.Resolve(groups, Seller, Day);
        var corrected = SalesDiscountLimitCalculatorV1.Resolve(groups, Seller, Day.AddDays(1));

        Assert.Equal((10m, (DateOnly?)Day), (first.Percent, first.EffectiveThrough));
        Assert.Equal((5m, 3L, (DateOnly?)null), (corrected.Percent, corrected.Record!.Version, corrected.EffectiveThrough));
    }

    [Theory]
    [InlineData("98", "3", "100")]
    [InlineData("100", "0", "100")]
    [InlineData("0", "0", "0")]
    [InlineData("35.25", "2.5", "37.75")]
    public void The_linked_limit_is_capped_at_one_hundred(string sellerPercent, string extra, string expected)
    {
        var groups = Groups(
            Group(Seller, true, Fixed(Seller, 1, decimal.Parse(sellerPercent, System.Globalization.CultureInfo.InvariantCulture), Day)),
            Group(Manager, true, Linked(Manager, 1, Seller, decimal.Parse(extra, System.Globalization.CultureInfo.InvariantCulture), Day)));

        var resolution = SalesDiscountLimitCalculatorV1.Resolve(groups, Manager, Day);

        Assert.Equal(expected, SalesDiscountLimitCalculatorV1.Format(resolution.Percent!.Value));
    }

    [Fact]
    public void Without_an_approved_linked_limit_the_linked_limit_is_none_and_never_zero_plus_points()
    {
        var noRecord = Groups(Group(Seller, true), Group(Manager, true, Linked(Manager, 1, Seller, 3m, Day)));
        var unknown = Groups(Group(Manager, true, Linked(Manager, 1, Seller, 3m, Day)));
        var later = Groups(Group(Seller, true, Fixed(Seller, 1, 35m, Day.AddDays(1))), Group(Manager, true, Linked(Manager, 1, Seller, 3m, Day)));

        foreach (var groups in new[] { noRecord, unknown, later })
        {
            Assert.Equal(SalesDiscountLimitResolutionOutcomeV1.None, SalesDiscountLimitCalculatorV1.Resolve(groups, Manager, Day).Outcome);
        }
    }

    [Fact]
    public void Sat_d117_a_passive_group_keeps_counting_for_itself_and_for_the_limits_linked_to_it()
    {
        var passive = Groups(Group(Seller, false, Fixed(Seller, 1, 35m, Day)), Group(Manager, true, Linked(Manager, 1, Seller, 3m, Day)));

        Assert.Equal(35m, SalesDiscountLimitCalculatorV1.Resolve(passive, Seller, Day).Percent);
        Assert.Equal(38m, SalesDiscountLimitCalculatorV1.Resolve(passive, Manager, Day).Percent);
    }

    [Fact]
    public void A_cycle_or_a_malformed_record_is_invalid_and_never_a_guessed_limit()
    {
        var cycle = Groups(
            Group(Seller, true, Linked(Seller, 1, Senior, 1m, Day)),
            Group(Senior, true, Linked(Senior, 1, Manager, 1m, Day)),
            Group(Manager, true, Linked(Manager, 1, Seller, 1m, Day)));
        var malformed = Groups(Group(Seller, true, new SalesDiscountLimitVersionDataV1(Seller, 1, Guid.NewGuid(),
            SalesDiscountLimitKindsV1.Fixed, null, null, 3m, Day)));
        var gap = Groups(Group(Seller, true, Fixed(Seller, 2, 10m, Day)));

        Assert.Equal(SalesDiscountLimitResolutionOutcomeV1.Invalid, SalesDiscountLimitCalculatorV1.Resolve(cycle, Manager, Day).Outcome);
        Assert.Equal(SalesDiscountLimitResolutionOutcomeV1.Invalid, SalesDiscountLimitCalculatorV1.Resolve(malformed, Seller, Day).Outcome);
        Assert.Equal(SalesDiscountLimitResolutionOutcomeV1.Invalid, SalesDiscountLimitCalculatorV1.Resolve(gap, Seller, Day).Outcome);
    }

    [Fact]
    public void A_link_that_closes_a_cycle_over_the_current_records_is_detected_including_a_self_link()
    {
        var groups = Groups(
            Group(Seller, true, Fixed(Seller, 1, 35m, Day)),
            Group(Manager, true, Linked(Manager, 1, Seller, 3m, Day)),
            Group(Senior, true, Linked(Senior, 1, Manager, 2m, Day)));

        Assert.True(SalesDiscountLimitCalculatorV1.CheckLink(groups, Seller, Senior, Day).CreatesCycle);
        Assert.True(SalesDiscountLimitCalculatorV1.CheckLink(groups, Seller, Seller, Day).CreatesCycle);
        Assert.True(SalesDiscountLimitCalculatorV1.CheckLink(groups, Seller, Manager, Day).CreatesCycle);
        var fine = SalesDiscountLimitCalculatorV1.CheckLink(groups, Senior, Seller, Day);
        Assert.False(fine.CreatesCycle);
        Assert.Equal([Seller], fine.Chain);
        Assert.False(SalesDiscountLimitCalculatorV1.CheckLink(groups, Senior, Guid.NewGuid(), Day).CreatesCycle);
        Assert.Equal([Manager, Seller], SalesDiscountLimitCalculatorV1.CheckLink(groups, Senior, Manager, Day).Chain);
    }

    [Fact]
    public void Sat_d78_a_future_record_is_not_in_force_before_its_date_and_applies_by_itself_from_it()
    {
        var groups = Groups(Group(Seller, true, Fixed(Seller, 1, 30m, Day), Fixed(Seller, 2, 35m, Day.AddDays(66))));

        var before = SalesDiscountLimitCalculatorV1.Resolve(groups, Seller, Day.AddDays(65));
        var on = SalesDiscountLimitCalculatorV1.Resolve(groups, Seller, Day.AddDays(66));
        var after = SalesDiscountLimitCalculatorV1.Resolve(groups, Seller, Day.AddDays(400));

        Assert.Equal((30m, 1L, (DateOnly?)Day.AddDays(65)), (before.Percent, before.Record!.Version, before.EffectiveThrough));
        Assert.Equal((35m, 2L, (DateOnly?)Day.AddDays(66), (DateOnly?)null),
            (on.Percent, on.Record!.Version, on.EffectiveFrom, on.EffectiveThrough));
        Assert.Equal(35m, after.Percent);
        Assert.Equal(1L, SalesDiscountLimitCalculatorV1.RecordInForce(groups[Seller], Day.AddDays(65))!.Version);
        Assert.Equal(2L, SalesDiscountLimitCalculatorV1.RecordInForce(groups[Seller], Day.AddDays(66))!.Version);
        Assert.Null(SalesDiscountLimitCalculatorV1.RecordInForce(groups[Seller], Day.AddDays(-1)));
    }

    [Fact]
    public void Sat_d78_only_the_first_record_is_future_and_nothing_is_in_force_before_it()
    {
        var groups = Groups(Group(Seller, true, Fixed(Seller, 1, 30m, Day.AddDays(5))));

        Assert.Equal(SalesDiscountLimitResolutionOutcomeV1.None, SalesDiscountLimitCalculatorV1.Resolve(groups, Seller, Day).Outcome);
        Assert.Equal(30m, SalesDiscountLimitCalculatorV1.Resolve(groups, Seller, Day.AddDays(5)).Percent);
    }

    [Fact]
    public void Sat_d78_the_linked_chain_is_resolved_on_the_asked_date_with_each_groups_record_of_that_date()
    {
        // Seller 30 % today and 35 % from day +10; the manager is seller + 3 from today and seller + 5 from day +20.
        var groups = Groups(
            Group(Seller, true, Fixed(Seller, 1, 30m, Day), Fixed(Seller, 2, 35m, Day.AddDays(10))),
            Group(Manager, true, Linked(Manager, 1, Seller, 3m, Day), Linked(Manager, 2, Seller, 5m, Day.AddDays(20))),
            Group(Senior, true, Linked(Senior, 1, Manager, 1m, Day)));

        var today = SalesDiscountLimitCalculatorV1.Resolve(groups, Senior, Day);
        var sellerChanged = SalesDiscountLimitCalculatorV1.Resolve(groups, Senior, Day.AddDays(10));
        var managerChanged = SalesDiscountLimitCalculatorV1.Resolve(groups, Senior, Day.AddDays(20));

        Assert.Equal((34m, Day, (DateOnly?)Day.AddDays(9)), (today.Percent, today.EffectiveFrom, today.EffectiveThrough));
        Assert.Equal((39m, Day.AddDays(10), (DateOnly?)Day.AddDays(19)),
            (sellerChanged.Percent, sellerChanged.EffectiveFrom, sellerChanged.EffectiveThrough));
        Assert.Equal((41m, Day.AddDays(20), (DateOnly?)null),
            (managerChanged.Percent, managerChanged.EffectiveFrom, managerChanged.EffectiveThrough));
    }

    [Fact]
    public void Sat_d79_a_linked_group_whose_linked_group_has_no_limit_until_a_future_date_is_none_until_then()
    {
        var groups = Groups(
            Group(Seller, true, Fixed(Seller, 1, 30m, Day.AddDays(7))),
            Group(Manager, true, Linked(Manager, 1, Seller, 3m, Day)));

        Assert.Equal(SalesDiscountLimitResolutionOutcomeV1.None, SalesDiscountLimitCalculatorV1.Resolve(groups, Manager, Day.AddDays(6)).Outcome);
        Assert.Equal(33m, SalesDiscountLimitCalculatorV1.Resolve(groups, Manager, Day.AddDays(7)).Percent);
    }

    [Fact]
    public void Sat_d78_a_link_is_checked_on_every_later_date_so_a_cycle_that_only_exists_in_the_future_is_detected()
    {
        // Today the manager is fixed; from day +30 it is scheduled to follow the seller. Linking the seller to the manager
        // from today is fine until day +29 but closes seller → manager → seller from day +30.
        var groups = Groups(
            Group(Seller, true, Fixed(Seller, 1, 30m, Day)),
            Group(Manager, true, Fixed(Manager, 1, 40m, Day), Linked(Manager, 2, Seller, 3m, Day.AddDays(30))));

        var check = SalesDiscountLimitCalculatorV1.CheckLink(groups, Seller, Manager, Day);
        Assert.True(check.CreatesCycle);

        // Starting after the manager's scheduled change it is the same cycle; a link of another group is fine.
        Assert.True(SalesDiscountLimitCalculatorV1.CheckLink(groups, Seller, Manager, Day.AddDays(40)).CreatesCycle);
        var senior = SalesDiscountLimitCalculatorV1.CheckLink(groups, Senior, Manager, Day);
        Assert.False(senior.CreatesCycle);
        Assert.Equal([Manager, Seller], senior.Chain);
    }

    [Fact]
    public void Sat_d78_the_scheduled_records_are_the_later_ones_with_a_same_day_correction_superseding()
    {
        var group = Group(Seller, true,
            Fixed(Seller, 1, 30m, Day), Fixed(Seller, 2, 35m, Day.AddDays(10)), Fixed(Seller, 3, 36m, Day.AddDays(10)),
            Fixed(Seller, 4, 40m, Day.AddDays(20)));

        var scheduled = SalesDiscountLimitCalculatorV1.ScheduledAfter(group, Day);

        Assert.Equal([3L, 4L], scheduled.Select(record => record.Version));
        Assert.Equal([4L], SalesDiscountLimitCalculatorV1.ScheduledAfter(group, Day.AddDays(10)).Select(record => record.Version));
        Assert.Empty(SalesDiscountLimitCalculatorV1.ScheduledAfter(group, Day.AddDays(20)));
    }

    [Fact]
    public void Sat_d84_a_withdrawn_record_is_ignored_and_the_linked_chain_is_recalculated_without_it()
    {
        var withdrawn = Fixed(Seller, 2, 35m, Day.AddDays(10)) with { IsWithdrawn = true };
        var groups = Groups(
            Group(Seller, true, Fixed(Seller, 1, 30m, Day), withdrawn),
            Group(Manager, true, Linked(Manager, 1, Seller, 3m, Day)));

        var seller = SalesDiscountLimitCalculatorV1.Resolve(groups, Seller, Day.AddDays(10));
        var manager = SalesDiscountLimitCalculatorV1.Resolve(groups, Manager, Day.AddDays(10));

        Assert.Equal((30m, 1L, (DateOnly?)null), (seller.Percent, seller.Record!.Version, seller.EffectiveThrough));
        Assert.Equal((33m, (DateOnly?)null), (manager.Percent, manager.EffectiveThrough));
        Assert.Empty(SalesDiscountLimitCalculatorV1.ScheduledAfter(groups[Seller], Day));
        Assert.Equal(1L, SalesDiscountLimitCalculatorV1.LatestRecord(groups[Seller])!.Version);
    }

    [Fact]
    public void Review_a_p2_1_the_timeline_is_ordered_by_date_then_version_so_a_later_version_may_start_earlier()
    {
        // 30 % today and 40 % scheduled for day +20 (v2); a new record v3 = 33 % from day +5 holds until day +19 and the
        // scheduled 40 % still takes effect on day +20.
        var groups = Groups(Group(Seller, true,
            Fixed(Seller, 1, 30m, Day), Fixed(Seller, 2, 40m, Day.AddDays(20)), Fixed(Seller, 3, 33m, Day.AddDays(5))));

        var early = SalesDiscountLimitCalculatorV1.Resolve(groups, Seller, Day.AddDays(4));
        var middle = SalesDiscountLimitCalculatorV1.Resolve(groups, Seller, Day.AddDays(5));
        var late = SalesDiscountLimitCalculatorV1.Resolve(groups, Seller, Day.AddDays(20));

        Assert.Equal((30m, 1L, (DateOnly?)Day.AddDays(4)), (early.Percent, early.Record!.Version, early.EffectiveThrough));
        Assert.Equal((33m, 3L, (DateOnly?)Day.AddDays(19)), (middle.Percent, middle.Record!.Version, middle.EffectiveThrough));
        Assert.Equal((40m, 2L, (DateOnly?)null), (late.Percent, late.Record!.Version, late.EffectiveThrough));
        Assert.Equal([3L, 2L], SalesDiscountLimitCalculatorV1.ScheduledAfter(groups[Seller], Day).Select(record => record.Version));
        Assert.Equal(2L, SalesDiscountLimitCalculatorV1.LatestRecord(groups[Seller])!.Version);
    }

    [Fact]
    public void Review_a_p2_1_correcting_the_earlier_of_two_scheduled_records_keeps_its_date_and_the_later_one()
    {
        // v2 35 % @+10 and v3 40 % @+20 scheduled; v2 corrected to 36 % on its own date (v2 withdrawn, v4 written).
        var groups = Groups(Group(Seller, true,
            Fixed(Seller, 1, 30m, Day), Fixed(Seller, 2, 35m, Day.AddDays(10)) with { IsWithdrawn = true },
            Fixed(Seller, 3, 40m, Day.AddDays(20)), Fixed(Seller, 4, 36m, Day.AddDays(10))));

        var corrected = SalesDiscountLimitCalculatorV1.Resolve(groups, Seller, Day.AddDays(10));

        Assert.Equal((36m, 4L, (DateOnly?)Day.AddDays(19)), (corrected.Percent, corrected.Record!.Version, corrected.EffectiveThrough));
        Assert.Equal(40m, SalesDiscountLimitCalculatorV1.Resolve(groups, Seller, Day.AddDays(20)).Percent);
        Assert.Equal(30m, SalesDiscountLimitCalculatorV1.Resolve(groups, Seller, Day.AddDays(9)).Percent);
        Assert.Equal([4L, 3L], SalesDiscountLimitCalculatorV1.ScheduledAfter(groups[Seller], Day).Select(record => record.Version));
    }

    [Fact]
    public void Review_a_p2_1_a_link_inserted_before_a_scheduled_record_is_checked_on_every_date()
    {
        // The manager is fixed and scheduled to follow the seller from day +30; the seller is linked to the manager from day
        // +5, before its own scheduled fixed record at day +40: days +30…+39 would cycle.
        var groups = Groups(
            Group(Seller, true, Fixed(Seller, 1, 30m, Day), Fixed(Seller, 2, 31m, Day.AddDays(40))),
            Group(Manager, true, Fixed(Manager, 1, 40m, Day), Linked(Manager, 2, Seller, 3m, Day.AddDays(30))));

        Assert.True(SalesDiscountLimitCalculatorV1.CheckLink(groups, Seller, Manager, Day.AddDays(5)).CreatesCycle);
        // Until day +29 the manager is fixed: a link that ends before then (the seller's own fixed record from day +20)
        // would be fine, which the date-by-date walk sees.
        var bounded = SalesDiscountLimitCalculatorV1.With(groups, groups[Seller] with
        {
            Versions = [.. groups[Seller].Versions, Fixed(Seller, 3, 30m, Day.AddDays(20))],
        });
        Assert.False(SalesDiscountLimitCalculatorV1.CheckLink(bounded, Seller, Manager, Day.AddDays(5)).CreatesCycle);
    }

    [Fact]
    public void Review_p2_1_a_replaced_same_date_record_and_its_withdrawn_replacement_leave_the_earlier_limit_in_force()
    {
        // 5 % today; 50 % scheduled for day +10 (v2), replaced on the same date by 35 % (v3, so v2 is withdrawn with it);
        // then v3 is withdrawn too. Nothing scheduled remains and 5 % stays in force: the replaced 50 % never comes back.
        var groups = Groups(Group(Seller, true,
            Fixed(Seller, 1, 5m, Day),
            Fixed(Seller, 2, 50m, Day.AddDays(10)) with { IsWithdrawn = true },
            Fixed(Seller, 3, 35m, Day.AddDays(10)) with { IsWithdrawn = true }));

        var resolution = SalesDiscountLimitCalculatorV1.Resolve(groups, Seller, Day.AddDays(10));

        Assert.Equal((5m, 1L, (DateOnly?)null), (resolution.Percent, resolution.Record!.Version, resolution.EffectiveThrough));
        Assert.Empty(SalesDiscountLimitCalculatorV1.ScheduledAfter(groups[Seller], Day));
    }

    [Fact]
    public void Review_p2_1_a_same_date_record_left_in_place_would_be_brought_back_by_withdrawing_its_replacement()
    {
        // Why the store withdraws the replaced record: if it stayed active (only shadowed by a higher version on its date),
        // withdrawing the replacement would bring the hidden 50 % back. This pins the calculator behaviour the store guards.
        var groups = Groups(Group(Seller, true,
            Fixed(Seller, 1, 5m, Day),
            Fixed(Seller, 2, 50m, Day.AddDays(10)),
            Fixed(Seller, 3, 35m, Day.AddDays(10)) with { IsWithdrawn = true }));

        Assert.Equal(50m, SalesDiscountLimitCalculatorV1.Resolve(groups, Seller, Day.AddDays(10)).Percent);
    }

    [Fact]
    public void Sat_d84_after_a_withdrawal_a_later_record_may_start_before_the_withdrawn_ones_date()
    {
        var groups = Groups(Group(Seller, true,
            Fixed(Seller, 1, 30m, Day), Fixed(Seller, 2, 35m, Day.AddDays(10)) with { IsWithdrawn = true },
            Fixed(Seller, 3, 32m, Day.AddDays(5))));

        Assert.Equal((32m, 3L), (SalesDiscountLimitCalculatorV1.Resolve(groups, Seller, Day.AddDays(20)).Percent,
            SalesDiscountLimitCalculatorV1.Resolve(groups, Seller, Day.AddDays(20)).Record!.Version));
        Assert.Equal(30m, SalesDiscountLimitCalculatorV1.Resolve(groups, Seller, Day.AddDays(4)).Percent);
    }

    [Fact]
    public void Sat_d84_a_withdrawal_that_extends_an_earlier_link_into_a_cycle_is_detected()
    {
        // The manager follows the seller today and is fixed from day +10; the seller follows the manager from day +20.
        // Withdrawing the manager's day +10 record would keep manager → seller → manager from day +20 on.
        var groups = Groups(
            Group(Manager, true, Linked(Manager, 1, Seller, 2m, Day), Fixed(Manager, 2, 40m, Day.AddDays(10))),
            Group(Seller, true, Fixed(Seller, 1, 30m, Day), Linked(Seller, 2, Manager, 1m, Day.AddDays(20))));
        Assert.False(SalesDiscountLimitCalculatorV1.CheckGroupLinks(groups, Manager, Day).CreatesCycle);

        var manager = groups[Manager];
        var afterWithdrawal = SalesDiscountLimitCalculatorV1.With(groups, manager with
        {
            Versions = [manager.Versions[0], manager.Versions[1] with { IsWithdrawn = true }],
        });

        Assert.True(SalesDiscountLimitCalculatorV1.CheckGroupLinks(afterWithdrawal, Manager, Day.AddDays(10)).CreatesCycle);
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("35", true)]
    [InlineData("37.5", true)]
    [InlineData("100", true)]
    [InlineData("100.0000", true)]
    [InlineData("0.0001", true)]
    [InlineData("100.0001", false)]
    [InlineData("101", false)]
    [InlineData("-1", false)]
    [InlineData("1.23456", false)]
    [InlineData("1,5", false)]
    [InlineData(" 5", false)]
    [InlineData("5.", false)]
    [InlineData(".5", false)]
    [InlineData("1e2", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_percent_is_lossless_decimal_text_between_zero_and_one_hundred_never_rounded(string? text, bool valid)
    {
        Assert.Equal(valid, SalesDiscountLimitCalculatorV1.TryParsePercent(text, out _));
    }

    private static Dictionary<Guid, SalesDiscountLimitGroupDataV1> Groups(params SalesDiscountLimitGroupDataV1[] groups) =>
        groups.ToDictionary(group => group.Id);

    private static SalesDiscountLimitGroupDataV1 Group(Guid id, bool active, params SalesDiscountLimitVersionDataV1[] versions) =>
        new(id, active, versions);

    private static SalesDiscountLimitVersionDataV1 Fixed(Guid group, long version, decimal percent, DateOnly from) =>
        new(group, version, Guid.NewGuid(), SalesDiscountLimitKindsV1.Fixed, percent, null, null, from);

    private static SalesDiscountLimitVersionDataV1 Linked(Guid group, long version, Guid linked, decimal extra, DateOnly from) =>
        new(group, version, Guid.NewGuid(), SalesDiscountLimitKindsV1.Linked, null, linked, extra, from);
}
