using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// DEC-SAL-001 / API-08 over SYNTHETIC owners: the seven axes are answered separately; lifecycle, internal approval and
/// customer acceptance come from the aggregate; realization from the delivery owner's shipped totals judged with the Settings
/// SAT-D5 tolerance (asked only when needed, for the order's business date); a missing or invalid policy is
/// "PolicyUnavailable", an unreadable owner "Unreadable", an owner module that does not exist "NoSource" — never a default,
/// a zero or "complete". The read writes nothing and is fenced before, after the draft and after each owner.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    private static readonly Guid TolerancePolicyProfile = Guid.Parse("5a1e5000-0000-4000-8000-000000000001");

    [Fact]
    public async Task The_seven_axes_are_answered_separately_and_the_realization_is_judged_with_the_settings_tolerance()
    {
        var world = new World();
        var approved = await ApprovedTwoLineAsync(world);
        var (first, second) = (approved.Lines[0].LineId, approved.Lines[1].LineId);
        // Line 1 (1.25 kg) shipped 1.25 / delivered 1 in two entries: complete. Line 2 (2.25 kg) shipped 1: partial (SAT-D16).
        var deliveries = new FakeDeliveries(order => new(Company, order,
            [new(first, KgUnit, 1m, 1m), new(first, KgUnit, 0.25m, 0m), new(second, KgUnit, 1m, 0.5m)]));
        world.Deliveries = deliveries;
        var policies = new FakeSalesPolicies(Ladder123());
        world.SalesPolicies = policies;
        var attempts = world.Store.Attempts;
        var envelopes = world.Store.Envelopes.Count;
        var transitions = world.Store.Transitions.Count;
        world.Authority.Measured.Clear();

        var status = await world.Service().GetDraftStatusAsync(approved.Draft.Id);

        Assert.Equal(("5", SalesDraftApiV1.Rev0), (status.DraftVersion, status.Revision));
        Assert.Equal(("Draft", "Approved"), (status.Lifecycle.State, status.InternalApproval.State));
        // A new order requires the customer acceptance (Q9) and none is recorded yet.
        Assert.Equal(new SalesDraftCustomerAcceptanceAxisV1("Pending", null), status.CustomerAcceptance);
        // Owner modules that do not exist on main: "no source", never complete or zero.
        Assert.Equal(("NoSource", "NoSource", "NoSource"), (status.Source.State, status.Invoicing.State, status.FinancialClosure.State));

        Assert.Equal("Partial", status.Realization.State);
        Assert.Equal(new SalesDraftDeliveryToleranceV1(TolerancePolicyProfile, 3, "1", "2", "3", new DateOnly(2026, 9, 1), null, Day),
            status.Realization.Tolerance);
        Assert.Equal(
            [
                new SalesDraftLineRealizationV1(first, 1, KgUnit, "1.25", "1.25", "1", "Completed", "None", "0", "Within", null),
                new SalesDraftLineRealizationV1(second, 2, KgUnit, "2.25", "1", "0.5", "Partial", "Under", "1.25", null, null),
            ],
            status.Realization.Lines);
        // The tolerance is asked once, for the order's business date; each owner once, for this draft only.
        Assert.Equal([Day], policies.Asked);
        Assert.Equal([approved.Draft.Id], deliveries.Asked);
        // The draft read before, after the draft, after the delivery owner and after Settings.
        Assert.Equal([ReadPermission, ReadPermission, ReadPermission, ReadPermission], world.Authority.Measured);
        // Read-only: no attempt, envelope, transition or refusal anywhere in Sales.
        Assert.Equal((attempts, envelopes, transitions), (world.Store.Attempts, world.Store.Envelopes.Count, world.Store.Transitions.Count));
        Assert.Empty(world.Store.Refusals);

        // The rest of line 2 shipped within the 1 % of SAT-D5 (2.24 of 2.25 is 0.44 % short): the whole order is complete.
        deliveries.Answer = order => new(Company, order, [new(first, KgUnit, 1.25m, 1.25m), new(second, KgUnit, 2.24m, 2.24m)]);
        var reopened = await world.Service().GetDraftStatusAsync(approved.Draft.Id);
        Assert.Equal("Completed", reopened.Realization.State);
        Assert.Equal(("Completed", "Under", "0.01", "Within"), (reopened.Realization.Lines[1].State, reopened.Realization.Lines[1].Direction,
            reopened.Realization.Lines[1].Deviation, reopened.Realization.Lines[1].Band));

        // The customer accepts the current revision: only that axis moves.
        await world.Service().RecordCustomerAcceptanceAsync(approved.Draft.Id, Command("5"), Metadata("k-accept"));
        var accepted = await world.Service().GetDraftStatusAsync(approved.Draft.Id);
        Assert.Equal(new SalesDraftCustomerAcceptanceAxisV1("Accepted", SalesDraftApiV1.Rev0), accepted.CustomerAcceptance);
        Assert.Equal(("6", "Draft", "Approved", "Completed"), (accepted.DraftVersion, accepted.Lifecycle.State,
            accepted.InternalApproval.State, accepted.Realization.State));
    }

    [Fact]
    public async Task Over_delivery_outside_the_tolerance_is_outside_tolerance_and_never_folded_into_completed()
    {
        var world = new World();
        var approved = await ApprovedTwoLineAsync(world);
        var (first, second) = (approved.Lines[0].LineId, approved.Lines[1].LineId);
        // Line 1: 1.25 → 1.262 is 0.96 % over (within); line 2: 2.25 → 2.295 is exactly 2 % over (approval band, inclusive).
        world.Deliveries = new FakeDeliveries(order => new(Company, order,
            [new(first, KgUnit, 1.262m, 1.262m), new(second, KgUnit, 2.295m, 0m)]));
        world.SalesPolicies = new FakeSalesPolicies(Ladder123());

        var status = await world.Service().GetDraftStatusAsync(approved.Draft.Id);

        Assert.Equal("OutsideTolerance", status.Realization.State);
        Assert.Equal(
            [("Completed", "Over", "0.012", "Within"), ("OutsideTolerance", "Over", "0.045", "Approval")],
            status.Realization.Lines.Select(line => (line.State, line.Direction!, line.Deviation!, line.Band!)));
    }

    [Fact]
    public async Task Nothing_shipped_is_not_started_and_the_tolerance_is_not_asked()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(lines: 2), Metadata("k-1"))).Detail;
        world.Deliveries = new FakeDeliveries(order => new(Company, order, []));
        var policies = new FakeSalesPolicies(_ => throw new SettingsPrerequisiteException("settings_provider_unavailable"));
        world.SalesPolicies = policies;
        world.Authority.Measured.Clear();

        var status = await world.Service().GetDraftStatusAsync(created.Draft.Id);

        Assert.Equal(("Draft", "Preparing", "NotStarted"), (status.Lifecycle.State, status.InternalApproval.State, status.Realization.State));
        Assert.Null(status.Realization.Tolerance);
        Assert.All(status.Realization.Lines, line => Assert.Equal(("NotStarted", "0", "0", "Under", (string?)null),
            (line.State, line.Shipped, line.Delivered, line.Direction!, line.Band)));
        Assert.Empty(policies.Asked);
        Assert.Equal([ReadPermission, ReadPermission, ReadPermission], world.Authority.Measured);
    }

    public static TheoryData<string> UnavailablePolicies => new()
    {
        "not-composed", "provider-unavailable", "policy-not-found", "access-denied", "unexpected-failure", "null-answer",
        "another-company", "inactive", "not-yet-in-force", "ended", "not-a-ladder", "empty-profile",
    };

    [Theory]
    [MemberData(nameof(UnavailablePolicies))]
    public async Task Without_an_approved_tolerance_in_force_the_realization_is_policy_unavailable_and_never_a_default(string fault)
    {
        var world = new World();
        var approved = await ApprovedTwoLineAsync(world);
        var (first, second) = (approved.Lines[0].LineId, approved.Lines[1].LineId);
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(first, KgUnit, 1.25m, 1.25m)]));
        var valid = Ladder123()(Day);
        world.SalesPolicies = fault == "not-composed" ? null : new FakeSalesPolicies(on => fault switch
        {
            "provider-unavailable" => throw new SettingsPrerequisiteException("settings_provider_unavailable"),
            "policy-not-found" => throw new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.SalesPolicyNotFound),
            "access-denied" => throw new SettingsPrerequisiteException("settings_access_denied"),
            "unexpected-failure" => throw new InvalidOperationException("SENTETIK Settings outage"),
            "null-answer" => null!,
            "another-company" => valid with { CompanyId = Guid.NewGuid() },
            "inactive" => valid with { IsActive = false },
            // The provider answers the nearest version with its REAL window: the consumer's own check refuses it.
            "not-yet-in-force" => valid with { EffectiveFrom = on.AddDays(1) },
            "ended" => valid with { EffectiveThrough = on.AddDays(-1) },
            "not-a-ladder" => valid with { WarnPercent = 3m, BlockPercent = 1m },
            "empty-profile" => valid with { Profile = new ProfileVersionV1(Guid.Empty, 1) },
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        });

        var status = await world.Service().GetDraftStatusAsync(approved.Draft.Id);

        Assert.Equal("PolicyUnavailable", status.Realization.State);
        Assert.Null(status.Realization.Tolerance);
        // The facts stay visible; a line that needs the tolerance carries no judged state, one with nothing shipped is not started.
        Assert.Equal(
            [
                new SalesDraftLineRealizationV1(first, 1, KgUnit, "1.25", "1.25", "1.25", "PolicyUnavailable", null, null, null, null),
                new SalesDraftLineRealizationV1(second, 2, KgUnit, "2.25", "0", "0", "NotStarted", null, null, null, null),
            ],
            status.Realization.Lines);
        // The other axes are still answered (the realization's policy does not take them down).
        Assert.Equal(("Draft", "Approved", "NoSource"), (status.Lifecycle.State, status.InternalApproval.State, status.Invoicing.State));
    }

    public static TheoryData<string> UnreadableDeliveries => new()
    {
        "not-composed", "failure", "another-company", "another-order", "unknown-line", "another-unit", "delivered-above-shipped",
        "above-scale", "sum-overflow",
    };

    [Theory]
    [MemberData(nameof(UnreadableDeliveries))]
    public async Task An_unreadable_delivery_owner_shows_no_number_and_no_state_and_settings_is_not_asked(string fault)
    {
        var world = new World();
        var approved = await ApprovedTwoLineAsync(world);
        var line = approved.Lines[0].LineId;
        world.Deliveries = fault == "not-composed" ? null : new FakeDeliveries(order => fault switch
        {
            "failure" => throw new InvalidOperationException("SENTETIK owner outage"),
            "another-company" => new(Guid.NewGuid(), order, []),
            "another-order" => new(Company, Guid.NewGuid(), []),
            "unknown-line" => new(Company, order, [new(Guid.NewGuid(), KgUnit, 1m, 1m)]),
            "another-unit" => new(Company, order, [new(line, Guid.NewGuid(), 1m, 1m)]),
            "delivered-above-shipped" => new(Company, order, [new(line, KgUnit, 1m, 2m)]),
            "above-scale" => new(Company, order, [new(line, KgUnit, 1.0001m, 0m)]),
            "sum-overflow" => new(Company, order, [new(line, KgUnit, decimal.MaxValue, 0m), new(line, KgUnit, decimal.MaxValue, 0m)]),
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        });
        var policies = new FakeSalesPolicies(Ladder123());
        world.SalesPolicies = policies;

        var status = await world.Service().GetDraftStatusAsync(approved.Draft.Id);

        Assert.Equal("Unreadable", status.Realization.State);
        Assert.Null(status.Realization.Tolerance);
        Assert.All(status.Realization.Lines, item => Assert.Equal(("Unreadable", (string?)null, (string?)null, (string?)null),
            (item.State, item.Shipped, item.Delivered, item.Band)));
        Assert.Empty(policies.Asked);
        Assert.Equal(("Draft", "Approved"), (status.Lifecycle.State, status.InternalApproval.State));
    }

    [Fact]
    public async Task Status_of_an_unknown_or_other_companys_draft_is_404_and_without_the_draft_read_403_and_no_owner_is_asked()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-1"))).Detail;
        var deliveries = new FakeDeliveries(order => new(Company, order, []));
        var policies = new FakeSalesPolicies(Ladder123());
        world.Deliveries = deliveries;
        world.SalesPolicies = policies;

        Assert.Equal((404, "not_found"), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().GetDraftStatusAsync(Guid.NewGuid()))));
        Assert.Equal(404, (await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().GetDraftStatusAsync(Guid.Empty))).Status);

        world.Authority.Denied.Add(ReadPermission);
        Assert.Equal((403, "forbidden"), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().GetDraftStatusAsync(created.Draft.Id))));
        Assert.Empty(deliveries.Asked);
        Assert.Empty(policies.Asked);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task A_draft_read_lost_while_an_owner_answered_is_403_and_no_axis_is_released(int lostAt)
    {
        var world = new World();
        var approved = await ApprovedTwoLineAsync(world);
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(approved.Lines[0].LineId, KgUnit, 1m, 1m)]));
        world.SalesPolicies = new FakeSalesPolicies(Ladder123());
        // Granted before and after the draft read; lost after the delivery owner (2) or after Settings (3).
        world.Authority.LostLater = (ReadPermission, world.Authority.Calls + lostAt);

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetDraftStatusAsync(approved.Draft.Id));

        Assert.Equal((403, "forbidden"), Status(refused));
    }

    [Fact]
    public async Task A_force_closed_order_reads_closed_and_its_short_lines_carry_the_closed_remainder_never_completed()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var line = approved.Lines[0].LineId;
        var (_, loadings) = Owners(world);
        loadings.Answer = order => new(Company, order, [new SalesOrderLineLoadingV1(line, KgUnit, 1, 1m, 1m, 1m, 1m)]);
        var preview = await world.Service().GetForceCloseImpactAsync(approved.Draft.Id);
        await world.Service().ForceCloseDraftAsync(approved.Draft.Id,
            new SalesDraftForceCloseRequest("4", "SENTETIK kalan istenmiyor", preview.ImpactToken), Metadata("k-close"));
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(line, KgUnit, 1m, 1m)]));
        world.SalesPolicies = new FakeSalesPolicies(Ladder123());

        var status = await world.Service().GetDraftStatusAsync(approved.Draft.Id);

        Assert.Equal(("Closed", "Approved", "ClosedWithRemainder"), (status.Lifecycle.State, status.InternalApproval.State,
            status.Realization.State));
        // 1 of 1.25 kg is 20 % short: measured because the line is closed (SAT-D16), block band, the rest is the closed remainder.
        Assert.Equal(new SalesDraftLineRealizationV1(line, 1, KgUnit, "1.25", "1", "1", "ClosedWithRemainder", "Under", "0.25", "Block", "0.25"),
            Assert.Single(status.Realization.Lines));
    }

    /// <summary>A two-line order (1.25 and 2.25 kg), submitted and approved: version 5.</summary>
    private static async Task<SalesOrderDraftDetailView> ApprovedTwoLineAsync(World world)
    {
        var created = (await world.Service().CreateDraftAsync(Request(lines: 2), Metadata("k-create"))).Detail.Draft;
        await world.Service().SubmitDraftAsync(created.Id, Command("3"), Metadata("k-submit"));
        return (await world.Service().ApproveDraftAsync(created.Id, Command("4"), Metadata("k-approve"))).Detail;
    }

    /// <summary>The SAT-D5 starting ladder as SYNTHETIC Settings data (1 / 2 / 3 %, version 3, from 2026-09-01, no end).</summary>
    private static Func<DateOnly, SalesDeliveryTolerancePolicyV1> Ladder123() => _ => new SalesDeliveryTolerancePolicyV1(Company,
        new ProfileVersionV1(TolerancePolicyProfile, 3), 1.00m, 2m, 3.0m, true, new DateOnly(2026, 9, 1), null);

    /// <summary>A SYNTHETIC Settings Sales policy port: answers the delivery tolerance the case gives it and records the dates asked.</summary>
    private sealed class FakeSalesPolicies(Func<DateOnly, SalesDeliveryTolerancePolicyV1> tolerance) : ISettingsSalesPolicyProviderV1
    {
        public List<DateOnly> Asked { get; } = [];

        public Task<SalesDeliveryTolerancePolicyV1> GetSalesDeliveryToleranceAsync(DateOnly effectiveOn,
            CancellationToken cancellationToken = default)
        {
            Asked.Add(effectiveOn);
            return Task.FromResult(tolerance(effectiveOn));
        }

        public Task<SalesDiscountLimitPolicyV1> GetSalesDiscountLimitAsync(Guid limitGroupId, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException("A status read asks no discount limit.");

        public Task<SalesApprovalAmountThresholdPolicyV1> GetSalesApprovalAmountThresholdAsync(Guid currencyId, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException("A status read asks no approval threshold.");
    }
}
