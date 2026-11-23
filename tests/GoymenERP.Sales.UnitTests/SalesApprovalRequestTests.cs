using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.ApprovalRequests;

namespace GoymenERP.Sales.UnitTests;

/// <summary>WP4 (SAT-D475 / SAT-D476): the approval request's facts and its one decision. SENTETIK actors only.</summary>
public sealed class SalesApprovalRequestTests
{
    private static readonly Guid Company = Guid.Parse("5a1e0000-0000-0000-0000-00000000c0a1");
    private static readonly Guid Subject = Guid.Parse("5a1e0000-0000-0000-00aa-000000000001");
    private static readonly Guid Line = Guid.Parse("5a1e0000-0000-0000-00aa-000000000002");
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-11-23T09:00:00+03:00");
    private const string Shipper = "SENTETIK-shipper";
    private const string Approver = "SENTETIK-approver";

    [Fact]
    public void An_over_delivery_request_records_who_when_how_much_and_which_line_and_only_another_actor_approves_it()
    {
        var request = OverDelivery();

        Assert.Equal((SalesApprovalRequestKind.OverDelivery, SalesApprovalRequestState.Requested, 1L), (request.Kind, request.State, request.Version));
        Assert.Equal((Line, "100", "15", "112.5", Shipper), (request.LineId, request.Ordered, request.Quantity, request.RequestedTotal, request.RequestedBy));
        Assert.Equal(T0.ToUniversalTime(), request.RequestedAtUtc);
        var self = Assert.Throws<SalesRuleException>(() => request.Approve(1, Shipper, T0.AddHours(1)));
        Assert.Equal(SalesRuleCodes.ApprovalRequestSelfDecision, self.Code);
        Assert.Throws<SalesVersionConflictException>(() => request.Approve(2, Approver, T0.AddHours(1)));

        request.Approve(1, Approver, T0.AddHours(1));

        Assert.Equal((SalesApprovalRequestState.Approved, 2L, Approver), (request.State, request.Version, request.Decision!.DecidedBy));
        Assert.Null(request.Decision.Reason);
        Assert.Equal(SalesRuleCodes.StateConflict, Assert.Throws<SalesRuleException>(() => request.Reject(2, Approver, "x", T0)).Code);
    }

    [Fact]
    public void A_rejection_needs_a_reason_and_the_shipper_never_rejects_their_own_over_delivery()
    {
        var request = OverDelivery();
        Assert.Equal(SalesRuleCodes.ApprovalRequestReasonRequired,
            Assert.Throws<SalesRuleException>(() => request.Reject(1, Approver, " ", T0)).Code);
        Assert.Equal(SalesRuleCodes.ApprovalRequestSelfDecision,
            Assert.Throws<SalesRuleException>(() => request.Reject(1, Shipper, "SENTETIK neden", T0)).Code);

        request.Reject(1, Approver, "SENTETIK neden", T0.AddHours(2));

        Assert.Equal((SalesApprovalRequestState.Rejected, "SENTETIK neden"), (request.State, request.Decision!.Reason));
    }

    [Fact]
    public void An_over_delivery_request_must_name_a_total_above_the_measured_quantity()
    {
        Assert.Throws<ArgumentException>(() => SalesApprovalRequest.RequestOverDelivery(Guid.NewGuid(), Company, Subject, 1, Line, 0,
            100m, 5m, 100m, Guid.NewGuid(), Guid.NewGuid(), Shipper, T0));
        Assert.Throws<ArgumentException>(() => SalesApprovalRequest.RequestOverDelivery(Guid.NewGuid(), Company, Subject, 1, Line, 0,
            100m, 0m, 101m, Guid.NewGuid(), Guid.NewGuid(), Shipper, T0));
    }

    [Fact]
    public void A_quote_request_is_answered_by_the_quote_approval_and_may_be_rejected_by_anyone_holding_the_approval()
    {
        var request = SalesApprovalRequest.RequestQuoteConversion(Guid.NewGuid(), Company, Subject, 1, 2, "SENTETIK not", "SENTETIK-seller", T0);

        Assert.Equal((SalesApprovalRequestKind.QuoteExpiredConversion, 2, "SENTETIK not"), (request.Kind, request.SubjectRevision, request.Note));
        Assert.Null(request.LineId);
        Assert.Equal(SalesRuleCodes.ApprovalRequestKindMismatch,
            Assert.Throws<SalesRuleException>(() => request.Approve(1, Approver, T0)).Code);

        // SAT-D44 (2): no maker ≠ checker is decided for the expired-quote approval, so the requester is not refused.
        request.Reject(1, "SENTETIK-seller", "SENTETIK vazgeçildi", T0.AddHours(1));
        Assert.Equal(SalesApprovalRequestState.Rejected, request.State);
    }

    [Fact]
    public void An_over_long_note_or_reason_is_refused_never_cut()
    {
        var text = new string('x', SalesApprovalRequest.MaximumTextLength + 1);
        Assert.Equal(SalesRuleCodes.ApprovalRequestTextTooLong, Assert.Throws<SalesRuleException>(() =>
            SalesApprovalRequest.RequestQuoteConversion(Guid.NewGuid(), Company, Subject, 1, 0, text, "SENTETIK-seller", T0)).Code);
        Assert.Equal(SalesRuleCodes.ApprovalRequestTextTooLong,
            Assert.Throws<SalesRuleException>(() => OverDelivery().Reject(1, Approver, text, T0)).Code);
    }

    [Fact]
    public void A_stored_request_is_restored_once_and_a_corrupt_decision_is_refused()
    {
        var rebuilt = Rebuild();
        rebuilt.Restore(SalesApprovalRequestState.Approved, new SalesApprovalRequestDecision(Approver, T0, null), 2, T0);
        Assert.Equal(SalesApprovalRequestState.Approved, rebuilt.State);
        Assert.Throws<InvalidOperationException>(() => rebuilt.Restore(SalesApprovalRequestState.Requested, null, 1, T0));

        // SAT-D216: a stored over-delivery decision by the shipper is refused.
        Assert.Throws<InvalidOperationException>(() =>
            Rebuild().Restore(SalesApprovalRequestState.Approved, new SalesApprovalRequestDecision(Shipper, T0, null), 2, T0));
        // An approval with a reason, or a version below the transitions, is refused.
        Assert.Throws<InvalidOperationException>(() =>
            Rebuild().Restore(SalesApprovalRequestState.Approved, new SalesApprovalRequestDecision(Approver, T0, "x"), 2, T0));
        Assert.Throws<InvalidOperationException>(() =>
            Rebuild().Restore(SalesApprovalRequestState.Rejected, new SalesApprovalRequestDecision(Approver, T0, "x"), 1, T0));
        // A quote request is never stored Approved.
        var quote = SalesApprovalRequest.Rebuild(Guid.NewGuid(), Company, SalesApprovalRequestKind.QuoteExpiredConversion, Subject, 1, 0,
            null, null, null, null, null, null, null, "SENTETIK-seller", T0);
        Assert.Throws<InvalidOperationException>(() =>
            quote.Restore(SalesApprovalRequestState.Approved, new SalesApprovalRequestDecision(Approver, T0, null), 2, T0));
        // Over-delivery facts on a quote request are refused.
        Assert.Throws<InvalidOperationException>(() => SalesApprovalRequest.Rebuild(Guid.NewGuid(), Company,
            SalesApprovalRequestKind.QuoteExpiredConversion, Subject, 1, 0, Line, "1", "1", "2", null, null, null, "SENTETIK-seller", T0));
    }

    private static SalesApprovalRequest OverDelivery() => SalesApprovalRequest.RequestOverDelivery(Guid.NewGuid(), Company, Subject, 1,
        Line, 0, 100m, 15m, 112.5m, Guid.NewGuid(), Guid.NewGuid(), Shipper, T0);

    private static SalesApprovalRequest Rebuild() => SalesApprovalRequest.Rebuild(Guid.NewGuid(), Company,
        SalesApprovalRequestKind.OverDelivery, Subject, 1, 0, Line, "100", "15", "112.5", Guid.NewGuid(), Guid.NewGuid(), null, Shipper, T0);
}
