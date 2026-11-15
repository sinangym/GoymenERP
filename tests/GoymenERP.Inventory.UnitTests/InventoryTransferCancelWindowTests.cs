using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Transfers;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>The pure KLN-D25 (6)/(7) and KLN-D27 cancellation-window rule (B-22b).</summary>
public sealed class InventoryTransferCancelWindowTests
{
    private static readonly DateTimeOffset DispatchedAt = DateTimeOffset.Parse("2026-09-30T13:00:00Z");
    private const InventoryTransferSourceScaleSetting Off = InventoryTransferSourceScaleSetting.Off;
    private const InventoryTransferSourceScaleSetting On = InventoryTransferSourceScaleSetting.On;
    private const InventoryTransferSourceScaleSetting Unreadable = InventoryTransferSourceScaleSetting.Unreadable;

    [Theory]
    [InlineData(Off, Off)]
    [InlineData(Off, On)]
    [InlineData(On, Off)]
    [InlineData(Unreadable, On)]
    [InlineData(Unreadable, Unreadable)]
    public void Unlinked_dispatch_closes_at_dispatch_when_the_stored_or_the_current_setting_is_off(
        InventoryTransferSourceScaleSetting stored,
        InventoryTransferSourceScaleSetting current)
    {
        var decision = InventoryTransferCancelWindow.Evaluate(stored, current, Facts(), DispatchedAt);

        Assert.Equal(InventoryTransferCancelWindowDecision.Closed(
            InventoryTransferCancelClosureBasis.DispatchRecorded, DispatchedAt), decision);
    }

    [Fact]
    public void Unlinked_dispatch_on_the_scale_path_with_an_unreadable_current_setting_is_unreadable()
    {
        Assert.Equal(InventoryTransferCancelWindowDecision.Unreadable,
            InventoryTransferCancelWindow.Evaluate(On, Unreadable, Facts(), DispatchedAt));
    }

    [Fact]
    public void Unlinked_dispatch_on_the_scale_path_stays_open_and_needs_only_the_logistics_confirmation()
    {
        Assert.Equal(InventoryTransferCancelWindowDecision.Open(true, false),
            InventoryTransferCancelWindow.Evaluate(On, On, Facts(), DispatchedAt));
    }

    [Fact]
    public void Unlinked_scale_path_closes_at_the_earliest_exit_and_ignores_a_gib_send_it_does_not_watch()
    {
        var facts = Facts(
            gibSend: InventoryTransferDepartureFact.At(DispatchedAt.AddMinutes(5)),
            exitWeighing: InventoryTransferDepartureFact.At(DispatchedAt.AddMinutes(30)),
            vehicleExit: InventoryTransferDepartureFact.At(DispatchedAt.AddMinutes(20)));

        Assert.Equal(InventoryTransferCancelWindowDecision.Closed(
                InventoryTransferCancelClosureBasis.VehicleExit, DispatchedAt.AddMinutes(20)),
            InventoryTransferCancelWindow.Evaluate(On, On, facts, DispatchedAt));
    }

    [Fact]
    public void Simultaneous_departure_facts_close_with_the_lowest_basis_so_the_record_is_deterministic()
    {
        var same = InventoryTransferDepartureFact.At(DispatchedAt.AddMinutes(10));

        Assert.Equal(InventoryTransferCancelWindowDecision.Closed(
                InventoryTransferCancelClosureBasis.ExitWeighing, DispatchedAt.AddMinutes(10)),
            InventoryTransferCancelWindow.Evaluate(On, On, Facts(exitWeighing: same, vehicleExit: same), DispatchedAt));
    }

    [Fact]
    public void An_occurred_fact_closes_the_window_even_when_another_fact_is_unreadable()
    {
        var facts = Facts(
            exitWeighing: InventoryTransferDepartureFact.Unreadable,
            vehicleExit: InventoryTransferDepartureFact.At(DispatchedAt.AddMinutes(25)));

        Assert.Equal(InventoryTransferCancelWindowDecision.Closed(
                InventoryTransferCancelClosureBasis.VehicleExit, DispatchedAt.AddMinutes(25)),
            InventoryTransferCancelWindow.Evaluate(On, On, facts, DispatchedAt));
    }

    [Fact]
    public void An_unreadable_watched_fact_with_nothing_closed_is_unreadable()
    {
        Assert.Equal(InventoryTransferCancelWindowDecision.Unreadable,
            InventoryTransferCancelWindow.Evaluate(On, On,
                Facts(vehicleExit: InventoryTransferDepartureFact.Unreadable), DispatchedAt));
    }

    [Fact]
    public void Linked_dispatch_off_the_scale_path_stays_open_until_the_gib_send_and_needs_the_e_dispatch_confirmation()
    {
        var notSent = Facts(InventoryTransferEDispatchLink.Linked, DispatchedAt,
            exitWeighing: InventoryTransferDepartureFact.At(DispatchedAt.AddMinutes(5)));
        var sent = Facts(InventoryTransferEDispatchLink.Linked, DispatchedAt,
            gibSend: InventoryTransferDepartureFact.At(DispatchedAt.AddMinutes(40)));

        Assert.Equal(InventoryTransferCancelWindowDecision.Open(false, true),
            InventoryTransferCancelWindow.Evaluate(Off, Off, notSent, DispatchedAt));
        Assert.Equal(InventoryTransferCancelWindowDecision.Closed(
                InventoryTransferCancelClosureBasis.EDispatchSentToGib, DispatchedAt.AddMinutes(40)),
            InventoryTransferCancelWindow.Evaluate(Off, Off, sent, DispatchedAt));
    }

    [Theory]
    [InlineData(On, Off)]
    [InlineData(Off, On)]
    [InlineData(Unreadable, Off)]
    [InlineData(Off, Unreadable)]
    public void Linked_dispatch_on_the_scale_path_closes_at_the_earliest_departure_and_needs_both_confirmations(
        InventoryTransferSourceScaleSetting stored,
        InventoryTransferSourceScaleSetting current)
    {
        var open = InventoryTransferCancelWindow.Evaluate(stored, current,
            Facts(InventoryTransferEDispatchLink.Linked, DispatchedAt), DispatchedAt);
        var closed = InventoryTransferCancelWindow.Evaluate(stored, current,
            Facts(InventoryTransferEDispatchLink.Linked, DispatchedAt,
                gibSend: InventoryTransferDepartureFact.At(DispatchedAt.AddMinutes(30)),
                exitWeighing: InventoryTransferDepartureFact.At(DispatchedAt.AddMinutes(15))),
            DispatchedAt);

        Assert.Equal(InventoryTransferCancelWindowDecision.Open(true, true), open);
        Assert.Equal(InventoryTransferCancelWindowDecision.Closed(
            InventoryTransferCancelClosureBasis.ExitWeighing, DispatchedAt.AddMinutes(15)), closed);
    }

    [Fact]
    public void A_link_created_after_an_unlinked_closure_never_reopens_the_window()
    {
        var lateLink = Facts(InventoryTransferEDispatchLink.Linked, DispatchedAt.AddMinutes(10));

        Assert.Equal(InventoryTransferCancelWindowDecision.Closed(
                InventoryTransferCancelClosureBasis.DispatchRecorded, DispatchedAt),
            InventoryTransferCancelWindow.Evaluate(Off, Off, lateLink, DispatchedAt));
    }

    [Fact]
    public void A_link_created_after_an_open_unlinked_window_needs_both_confirmations()
    {
        var lateLink = Facts(InventoryTransferEDispatchLink.Linked, DispatchedAt.AddMinutes(10));

        Assert.Equal(InventoryTransferCancelWindowDecision.Open(true, true),
            InventoryTransferCancelWindow.Evaluate(On, On, lateLink, DispatchedAt));
    }

    [Theory]
    [InlineData("undefined-stored")]
    [InlineData("undefined-current")]
    [InlineData("undefined-link")]
    [InlineData("unreadable-link")]
    [InlineData("not-linked-with-moment")]
    [InlineData("linked-without-moment")]
    [InlineData("linked-with-local-moment")]
    [InlineData("local-dispatch-moment")]
    [InlineData("occurred-without-moment")]
    [InlineData("occurred-with-local-moment")]
    [InlineData("not-occurred-with-moment")]
    [InlineData("unreadable-with-moment")]
    [InlineData("undefined-fact-status")]
    [InlineData("null-facts")]
    [InlineData("null-fact")]
    public void Inconsistent_or_malformed_inputs_fail_closed_as_unreadable(string fault)
    {
        var stored = On;
        var current = On;
        var dispatchedAt = DispatchedAt;
        var local = DispatchedAt.AddMinutes(5).ToOffset(TimeSpan.FromHours(3));
        InventoryTransferDepartureFacts? facts = Facts();
        switch (fault)
        {
            case "undefined-stored": stored = (InventoryTransferSourceScaleSetting)99; break;
            case "undefined-current": current = (InventoryTransferSourceScaleSetting)99; break;
            case "undefined-link": facts = Facts((InventoryTransferEDispatchLink)99); break;
            case "unreadable-link": facts = Facts(InventoryTransferEDispatchLink.Unreadable); break;
            case "not-linked-with-moment":
                facts = Facts(InventoryTransferEDispatchLink.NotLinked, DispatchedAt);
                break;
            case "linked-without-moment": facts = Facts(InventoryTransferEDispatchLink.Linked); break;
            case "linked-with-local-moment": facts = Facts(InventoryTransferEDispatchLink.Linked, local); break;
            case "local-dispatch-moment": dispatchedAt = DispatchedAt.ToOffset(TimeSpan.FromHours(3)); break;
            case "occurred-without-moment":
                facts = Facts(vehicleExit: new(InventoryTransferDepartureFactStatus.Occurred, null));
                break;
            case "occurred-with-local-moment":
                facts = Facts(vehicleExit: new(InventoryTransferDepartureFactStatus.Occurred, local));
                break;
            case "not-occurred-with-moment":
                facts = Facts(exitWeighing: new(InventoryTransferDepartureFactStatus.NotOccurred, DispatchedAt));
                break;
            case "unreadable-with-moment":
                facts = Facts(gibSend: new(InventoryTransferDepartureFactStatus.Unreadable, DispatchedAt));
                break;
            case "undefined-fact-status":
                facts = Facts(exitWeighing: new((InventoryTransferDepartureFactStatus)99, null));
                break;
            case "null-facts": facts = null; break;
            default: facts = Facts() with { VehicleExit = null! }; break;
        }

        Assert.Equal(InventoryTransferCancelWindowDecision.Unreadable,
            InventoryTransferCancelWindow.Evaluate(stored, current, facts!, dispatchedAt));
    }

    [Fact]
    public void A_malformed_unwatched_fact_still_fails_closed_because_the_owner_answer_is_not_trustworthy()
    {
        var facts = Facts(gibSend: new(InventoryTransferDepartureFactStatus.Occurred, null));

        Assert.Equal(InventoryTransferCancelWindowDecision.Unreadable,
            InventoryTransferCancelWindow.Evaluate(Off, Off, facts, DispatchedAt));
    }

    [Fact]
    public void Owner_readings_map_unknown_values_to_unreadable_and_a_missing_answer_to_all_unreadable()
    {
        Assert.Equal(InventoryTransferSourceScaleSetting.Unreadable,
            InventoryTransferDispatchCancelMapping.ToDomain((InventoryTransferScaleSettingV1)99));
        Assert.Equal(InventoryTransferSourceScaleSetting.Unreadable,
            InventoryTransferDispatchCancelMapping.ToDomain(InventoryTransferScaleSettingV1.Unreadable));
        Assert.Equal((InventoryTransferSourceScaleSetting.Off, InventoryTransferSourceScaleSetting.On),
            (InventoryTransferDispatchCancelMapping.ToDomain(InventoryTransferScaleSettingV1.Off),
                InventoryTransferDispatchCancelMapping.ToDomain(InventoryTransferScaleSettingV1.On)));

        var missing = InventoryTransferDispatchCancelMapping.ToDomain((InventoryTransferDispatchCancelFactsV1?)null);
        Assert.Equal(InventoryTransferEDispatchLink.Unreadable, missing.EDispatchLink);
        Assert.Equal(InventoryTransferCancelWindowDecision.Unreadable,
            InventoryTransferCancelWindow.Evaluate(On, On, missing, DispatchedAt));

        var mapped = InventoryTransferDispatchCancelMapping.ToDomain(new InventoryTransferDispatchCancelFactsV1(
            (InventoryTransferEDispatchLinkV1)99, null,
            new(InventoryTransferDepartureFactStatusV1.NotOccurred, null),
            new((InventoryTransferDepartureFactStatusV1)99, null),
            new(InventoryTransferDepartureFactStatusV1.Occurred, DispatchedAt)));
        Assert.Equal(InventoryTransferEDispatchLink.Unreadable, mapped.EDispatchLink);
        Assert.Equal(InventoryTransferDepartureFact.NotOccurred, mapped.EDispatchSentToGib);
        Assert.Equal(InventoryTransferDepartureFact.Unreadable, mapped.ExitWeighing);
        Assert.Equal(InventoryTransferDepartureFact.At(DispatchedAt), mapped.VehicleExit);
    }

    private static InventoryTransferDepartureFacts Facts(
        InventoryTransferEDispatchLink link = InventoryTransferEDispatchLink.NotLinked,
        DateTimeOffset? linkedAtUtc = null,
        InventoryTransferDepartureFact? gibSend = null,
        InventoryTransferDepartureFact? exitWeighing = null,
        InventoryTransferDepartureFact? vehicleExit = null) =>
        new(link, linkedAtUtc, gibSend ?? InventoryTransferDepartureFact.NotOccurred,
            exitWeighing ?? InventoryTransferDepartureFact.NotOccurred,
            vehicleExit ?? InventoryTransferDepartureFact.NotOccurred);
}
