using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.RiskExceptions;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// API-07 (EK21 SAT-D153…D162), the domain half and the read port's choice: the request needs a reason (bounded, never cut) and
/// one shipment; the decision moves once, by a decider who is not the requester, at the expected version; an approval is valid on
/// its Europe/Istanbul business date only (SAT-D160), never measured in UTC; a stored row is restored through the same rules and a
/// corrupt one is refused. Every identity is synthetic.
/// </summary>
public sealed class SalesShipmentRiskExceptionTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Order = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Shipment = Guid.Parse("e1e1e1e1-e1e1-4e1e-8e1e-e1e1e1e1e1e1");
    private static readonly DateTimeOffset T0 = new(2026, 11, 10, 7, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Day = new(2026, 11, 10);
    private const string Requester = "synthetic-shipper";
    private const string Approver = "synthetic-manager";

    private static SalesShipmentRiskException Requested(string requester = Requester) =>
        SalesShipmentRiskException.Request(Guid.NewGuid(), Company, Order, 1, Shipment, "SENTETIK müşteri ödeme sözü verdi", requester, T0);

    [Fact]
    public void A_request_is_pending_at_version_one_bound_to_one_shipment_and_carries_no_decision()
    {
        var item = Requested();

        Assert.Equal((SalesShipmentRiskExceptionState.Requested, 1L, Shipment, Requester, T0),
            (item.State, item.Version, item.LoadingInstructionId, item.RequestedBy, item.RequestedAtUtc));
        Assert.Null(item.Decision);
        Assert.Null(item.ValidOn);
        Assert.False(item.IsValidOn(Day));
    }

    [Theory]
    [InlineData(null, SalesRuleCodes.RiskExceptionReasonRequired)]
    [InlineData("   ", SalesRuleCodes.RiskExceptionReasonRequired)]
    public void A_request_without_a_reason_is_refused(string? reason, string code)
    {
        var refusal = Assert.Throws<SalesRuleException>(() =>
            SalesShipmentRiskException.Request(Guid.NewGuid(), Company, Order, 1, Shipment, reason, Requester, T0));

        Assert.Equal(code, refusal.Code);
    }

    [Fact]
    public void A_reason_over_the_bound_is_refused_never_cut_and_the_bound_itself_is_accepted()
    {
        var bound = new string('x', SalesShipmentRiskException.MaximumReasonLength);
        Assert.Equal(bound, SalesShipmentRiskException.Request(Guid.NewGuid(), Company, Order, 1, Shipment, bound, Requester, T0).Reason);

        var refusal = Assert.Throws<SalesRuleException>(() =>
            SalesShipmentRiskException.Request(Guid.NewGuid(), Company, Order, 1, Shipment, bound + "x", Requester, T0));
        Assert.Equal(SalesRuleCodes.RiskExceptionReasonTooLong, refusal.Code);
    }

    [Fact]
    public void A_request_without_a_shipment_order_company_or_a_positive_number_is_refused()
    {
        Assert.Throws<ArgumentException>(() => SalesShipmentRiskException.Request(Guid.NewGuid(), Company, Order, 1, Guid.Empty, "r", Requester, T0));
        Assert.Throws<ArgumentException>(() => SalesShipmentRiskException.Request(Guid.NewGuid(), Guid.Empty, Order, 1, Shipment, "r", Requester, T0));
        Assert.Throws<ArgumentException>(() => SalesShipmentRiskException.Request(Guid.NewGuid(), Company, Guid.Empty, 1, Shipment, "r", Requester, T0));
        Assert.Throws<ArgumentOutOfRangeException>(() => SalesShipmentRiskException.Request(Guid.NewGuid(), Company, Order, 0, Shipment, "r", Requester, T0));
    }

    [Fact]
    public void An_approval_by_another_actor_is_valid_on_its_business_date_only()
    {
        var item = Requested();

        item.Approve(1, Approver, T0, Day);

        Assert.Equal((SalesShipmentRiskExceptionState.Approved, 2L, Approver, (string?)null, Day),
            (item.State, item.Version, item.Decision!.DecidedBy, item.Decision.RejectionReason, item.ValidOn));
        Assert.True(item.IsValidOn(Day));
        Assert.False(item.IsValidOn(Day.AddDays(1)));
        Assert.False(item.IsValidOn(Day.AddDays(-1)));
    }

    [Fact]
    public void SAT_D160_the_business_date_is_the_istanbul_day_so_an_approval_at_23_local_lapses_at_local_midnight_not_utc_midnight()
    {
        // 20:30 UTC = 23:30 Istanbul on 10 Nov; 21:30 UTC = 00:30 Istanbul on 11 Nov (still 10 Nov in UTC).
        var late = new DateTimeOffset(2026, 11, 10, 20, 30, 0, TimeSpan.Zero);
        var item = Requested();
        item.Approve(1, Approver, late, SalesBusinessDay.DateOf(late));

        Assert.Equal(Day, item.ValidOn);
        Assert.True(item.IsValidOn(SalesBusinessDay.DateOf(late.AddMinutes(29))));
        Assert.False(item.IsValidOn(SalesBusinessDay.DateOf(late.AddMinutes(60))));
        Assert.Equal(Day, DateOnly.FromDateTime(late.AddMinutes(60).UtcDateTime));
    }

    [Fact]
    public void A_rejection_by_another_actor_carries_its_reason_and_releases_nothing()
    {
        var item = Requested();

        item.Reject(1, Approver, "SENTETIK risk çok yüksek", T0);

        Assert.Equal((SalesShipmentRiskExceptionState.Rejected, 2L, "SENTETIK risk çok yüksek"),
            (item.State, item.Version, item.Decision!.RejectionReason));
        Assert.Null(item.ValidOn);
        Assert.False(item.IsValidOn(Day));
        Assert.Equal(SalesRuleCodes.RiskExceptionReasonRequired,
            Assert.Throws<SalesRuleException>(() => Requested().Reject(1, Approver, " ", T0)).Code);
    }

    [Fact]
    public void SAT_D153_the_requester_never_approves_or_rejects_their_own_request_and_nothing_moves()
    {
        var item = Requested();

        Assert.Equal(SalesRuleCodes.RiskExceptionSelfDecision,
            Assert.Throws<SalesRuleException>(() => item.Approve(1, Requester, T0, Day)).Code);
        Assert.Equal(SalesRuleCodes.RiskExceptionSelfDecision,
            Assert.Throws<SalesRuleException>(() => item.Reject(1, " " + Requester + " ", "r", T0)).Code);
        Assert.Equal((SalesShipmentRiskExceptionState.Requested, 1L), (item.State, item.Version));
    }

    [Fact]
    public void A_stale_version_and_a_second_decision_are_refused_without_moving_anything()
    {
        var item = Requested();
        Assert.Throws<SalesVersionConflictException>(() => item.Approve(2, Approver, T0, Day));
        Assert.Equal(1L, item.Version);

        item.Approve(1, Approver, T0, Day);
        Assert.Equal(SalesRuleCodes.StateConflict, Assert.Throws<SalesRuleException>(() => item.Reject(2, "synthetic-other", "r", T0)).Code);
        Assert.Equal((SalesShipmentRiskExceptionState.Approved, 2L), (item.State, item.Version));
    }

    [Fact]
    public void A_stored_decision_is_restored_through_the_rules_and_a_corrupt_row_is_refused()
    {
        var approved = Requested();
        approved.Restore(SalesShipmentRiskExceptionState.Approved, new(Approver, T0, null), Day, 2, T0);
        Assert.True(approved.IsValidOn(Day));

        Assert.Throws<InvalidOperationException>(() =>
            Requested().Restore(SalesShipmentRiskExceptionState.Approved, new(Requester, T0, null), Day, 2, T0));
        Assert.Throws<InvalidOperationException>(() =>
            Requested().Restore(SalesShipmentRiskExceptionState.Approved, new(Approver, T0, null), null, 2, T0));
        Assert.Throws<InvalidOperationException>(() =>
            Requested().Restore(SalesShipmentRiskExceptionState.Rejected, new(Approver, T0, null), null, 2, T0));
        Assert.Throws<InvalidOperationException>(() =>
            Requested().Restore(SalesShipmentRiskExceptionState.Approved, new(Approver, T0, null), Day, 1, T0));
        Assert.Throws<InvalidOperationException>(() =>
            Requested().Restore(SalesShipmentRiskExceptionState.Requested, new(Approver, T0, null), null, 1, T0));
        Assert.Throws<InvalidOperationException>(() => approved.Restore(SalesShipmentRiskExceptionState.Approved,
            new(Approver, T0, null), Day, 2, T0));
    }

    [Fact]
    public void SAT_D286_the_requester_withdraws_their_own_pending_request_without_a_decision_and_it_releases_nothing()
    {
        var item = Requested();
        var later = T0.AddMinutes(5);

        item.Withdraw(1, " " + Requester + " ", later);

        Assert.Equal((SalesShipmentRiskExceptionState.Withdrawn, 2L, later), (item.State, item.Version, item.UpdatedAtUtc));
        Assert.Null(item.Decision);
        Assert.Null(item.ValidOn);
        Assert.False(item.IsValidOn(Day));
        Assert.Equal((Requester, T0), (item.RequestedBy, item.RequestedAtUtc));
    }

    [Fact]
    public void SAT_D286_another_actor_never_withdraws_a_request_and_nothing_moves()
    {
        var item = Requested();

        Assert.Equal(SalesRuleCodes.RiskExceptionNotRequester,
            Assert.Throws<SalesRuleException>(() => item.Withdraw(1, Approver, T0)).Code);
        Assert.Equal((SalesShipmentRiskExceptionState.Requested, 1L), (item.State, item.Version));
    }

    [Fact]
    public void SAT_D286_a_decided_or_withdrawn_exception_and_a_stale_version_are_not_withdrawn()
    {
        var item = Requested();
        Assert.Throws<SalesVersionConflictException>(() => item.Withdraw(2, Requester, T0));
        Assert.Equal(1L, item.Version);

        var approved = Requested();
        approved.Approve(1, Approver, T0, Day);
        Assert.Equal(SalesRuleCodes.StateConflict, Assert.Throws<SalesRuleException>(() => approved.Withdraw(2, Requester, T0)).Code);
        Assert.Equal((SalesShipmentRiskExceptionState.Approved, 2L), (approved.State, approved.Version));

        var rejected = Requested();
        rejected.Reject(1, Approver, "r", T0);
        Assert.Equal(SalesRuleCodes.StateConflict, Assert.Throws<SalesRuleException>(() => rejected.Withdraw(2, Requester, T0)).Code);

        item.Withdraw(1, Requester, T0);
        Assert.Equal(SalesRuleCodes.StateConflict, Assert.Throws<SalesRuleException>(() => item.Withdraw(2, Requester, T0)).Code);
        Assert.Equal(SalesRuleCodes.StateConflict, Assert.Throws<SalesRuleException>(() => item.Approve(2, Approver, T0, Day)).Code);
        Assert.Equal(SalesRuleCodes.StateConflict, Assert.Throws<SalesRuleException>(() => item.Reject(2, Approver, "r", T0)).Code);
        Assert.Equal((SalesShipmentRiskExceptionState.Withdrawn, 2L), (item.State, item.Version));
    }

    [Fact]
    public void SAT_D286_a_stored_withdrawal_is_restored_without_a_decision_and_a_corrupt_one_is_refused()
    {
        var withdrawn = Requested();
        withdrawn.Restore(SalesShipmentRiskExceptionState.Withdrawn, null, null, 2, T0);
        Assert.Equal((SalesShipmentRiskExceptionState.Withdrawn, 2L), (withdrawn.State, withdrawn.Version));
        Assert.False(withdrawn.IsValidOn(Day));

        Assert.Throws<InvalidOperationException>(() =>
            Requested().Restore(SalesShipmentRiskExceptionState.Withdrawn, new(Approver, T0, null), null, 2, T0));
        Assert.Throws<InvalidOperationException>(() =>
            Requested().Restore(SalesShipmentRiskExceptionState.Withdrawn, null, Day, 2, T0));
        Assert.Throws<InvalidOperationException>(() =>
            Requested().Restore(SalesShipmentRiskExceptionState.Withdrawn, null, null, 1, T0));
    }

    [Fact]
    public void SAT_D286_the_read_port_answers_withdrawn_for_the_newest_withdrawn_exception_and_prefers_a_new_pending_one()
    {
        var withdrawn = SalesShipmentRiskException.Request(Guid.NewGuid(), Company, Order, 1, Shipment, "r", Requester, T0);
        withdrawn.Withdraw(1, Requester, T0);
        Assert.Equal((SalesShipmentRiskExceptionStatusV1.Withdrawn, (int?)1),
            Answer(SalesShipmentRiskExceptionReadPort.Decide(Order, Shipment, [withdrawn], Day)));

        var pending = SalesShipmentRiskException.Request(Guid.NewGuid(), Company, Order, 2, Shipment, "r", Requester, T0);
        Assert.Equal((SalesShipmentRiskExceptionStatusV1.Pending, (int?)2),
            Answer(SalesShipmentRiskExceptionReadPort.Decide(Order, Shipment, [pending, withdrawn], Day)));
    }

    [Fact]
    public void The_read_port_answers_none_without_an_exception_and_prefers_valid_then_pending_then_the_newest()
    {
        Assert.Equal(SalesShipmentRiskExceptionStatusV1.None, SalesShipmentRiskExceptionReadPort.Decide(Order, Shipment, [], Day).Status);

        var lapsed = SalesShipmentRiskException.Request(Guid.NewGuid(), Company, Order, 1, Shipment, "r", Requester, T0);
        lapsed.Approve(1, Approver, T0, Day.AddDays(-1));
        var rejected = SalesShipmentRiskException.Request(Guid.NewGuid(), Company, Order, 2, Shipment, "r", Requester, T0);
        rejected.Reject(1, Approver, "r", T0);
        Assert.Equal((SalesShipmentRiskExceptionStatusV1.Rejected, (int?)2),
            Answer(SalesShipmentRiskExceptionReadPort.Decide(Order, Shipment, [rejected, lapsed], Day)));
        Assert.Equal((SalesShipmentRiskExceptionStatusV1.Lapsed, (int?)1),
            Answer(SalesShipmentRiskExceptionReadPort.Decide(Order, Shipment, [lapsed], Day)));

        var pending = SalesShipmentRiskException.Request(Guid.NewGuid(), Company, Order, 3, Shipment, "r", Requester, T0);
        Assert.Equal((SalesShipmentRiskExceptionStatusV1.Pending, (int?)3),
            Answer(SalesShipmentRiskExceptionReadPort.Decide(Order, Shipment, [pending, rejected, lapsed], Day)));

        var valid = SalesShipmentRiskException.Request(Guid.NewGuid(), Company, Order, 4, Shipment, "r", Requester, T0);
        valid.Approve(1, Approver, T0, Day);
        var answer = SalesShipmentRiskExceptionReadPort.Decide(Order, Shipment, [pending, valid, rejected, lapsed], Day);
        Assert.Equal((SalesShipmentRiskExceptionStatusV1.Valid, (int?)4, (DateOnly?)Day), (answer.Status, answer.ExceptionNo, answer.ValidOn));

        // The next Istanbul day the same approval has lapsed: the gate holds the shipment again.
        Assert.Equal(SalesShipmentRiskExceptionStatusV1.Lapsed,
            SalesShipmentRiskExceptionReadPort.Decide(Order, Shipment, [valid], Day.AddDays(1)).Status);
    }

    private static (SalesShipmentRiskExceptionStatusV1, int?) Answer(SalesShipmentRiskExceptionReadV1 read) => (read.Status, read.ExceptionNo);
}
