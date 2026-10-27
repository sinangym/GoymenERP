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
        var passive = Groups(Group(Seller, false, Fixed(Seller, 1, 35m, Day)), Group(Manager, true, Linked(Manager, 1, Seller, 3m, Day)));
        var unknown = Groups(Group(Manager, true, Linked(Manager, 1, Seller, 3m, Day)));
        var later = Groups(Group(Seller, true, Fixed(Seller, 1, 35m, Day.AddDays(1))), Group(Manager, true, Linked(Manager, 1, Seller, 3m, Day)));

        foreach (var groups in new[] { noRecord, passive, unknown, later })
        {
            Assert.Equal(SalesDiscountLimitResolutionOutcomeV1.None, SalesDiscountLimitCalculatorV1.Resolve(groups, Manager, Day).Outcome);
        }

        // A passive group contributes nothing itself, even with a record.
        Assert.Equal(SalesDiscountLimitResolutionOutcomeV1.None, SalesDiscountLimitCalculatorV1.Resolve(passive, Seller, Day).Outcome);
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
        var backwards = Groups(Group(Seller, true, Fixed(Seller, 1, 10m, Day), Fixed(Seller, 2, 20m, Day.AddDays(-1))));

        Assert.Equal(SalesDiscountLimitResolutionOutcomeV1.Invalid, SalesDiscountLimitCalculatorV1.Resolve(cycle, Manager, Day).Outcome);
        Assert.Equal(SalesDiscountLimitResolutionOutcomeV1.Invalid, SalesDiscountLimitCalculatorV1.Resolve(malformed, Seller, Day).Outcome);
        Assert.Equal(SalesDiscountLimitResolutionOutcomeV1.Invalid, SalesDiscountLimitCalculatorV1.Resolve(gap, Seller, Day).Outcome);
        Assert.Equal(SalesDiscountLimitResolutionOutcomeV1.Invalid, SalesDiscountLimitCalculatorV1.Resolve(backwards, Seller, Day).Outcome);
    }

    [Fact]
    public void A_link_that_closes_a_cycle_over_the_current_records_is_detected_including_a_self_link()
    {
        var links = new Dictionary<Guid, Guid?> { [Seller] = null, [Manager] = Seller, [Senior] = Manager };

        Assert.True(SalesDiscountLimitCalculatorV1.WouldCreateCycle(links, Seller, Senior));
        Assert.True(SalesDiscountLimitCalculatorV1.WouldCreateCycle(links, Seller, Seller));
        Assert.True(SalesDiscountLimitCalculatorV1.WouldCreateCycle(links, Seller, Manager));
        Assert.False(SalesDiscountLimitCalculatorV1.WouldCreateCycle(links, Senior, Seller));
        Assert.False(SalesDiscountLimitCalculatorV1.WouldCreateCycle(links, Senior, Guid.NewGuid()));
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
