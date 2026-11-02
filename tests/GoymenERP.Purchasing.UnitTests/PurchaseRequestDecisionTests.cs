using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Quantities;
using GoymenERP.Modules.Purchasing.Domain.Requests;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// A1-1 (plan API-01, TEK-09; ALS-D2, No.161 §1): the request's decision transitions in the domain —
/// Submitted → Approved, Submitted | Approved → Rejected with a reason, maker ≠ checker (preparer and submitter),
/// an unknown maker refused (fail-closed), and the rejection blocked by a bound APPROVED order. SENTETIK.
/// </summary>
public sealed class PurchaseRequestDecisionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 9, 0, 0, TimeSpan.FromHours(3));
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid KgUnit = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private const string Preparer = "SENTETIK-preparer";
    private const string Submitter = "SENTETIK-submitter";
    private const string Manager = "SENTETIK-manager";

    private static PurchaseRequest Submitted(string preparer = Preparer, string submitter = Submitter)
    {
        var request = PurchaseRequest.Create(Guid.NewGuid(), Company, preparer, T0);
        request.AddLine(1, Guid.NewGuid(), Guid.NewGuid(), KgUnit, Quantity.Of(10m, QuantityUnit.Kilogram), T0);
        request.Submit(2, submitter, T0.AddMinutes(1));
        return request;
    }

    private static string Code(Action action) => Assert.Throws<PurchasingRuleException>(action).Code;

    [Fact]
    public void The_makers_are_recorded_and_a_manager_approves_a_submitted_request()
    {
        var request = Submitted();
        Assert.Equal((Preparer, Submitter), (request.PreparedBy, request.SubmittedBy));

        request.Approve(3, Manager, T0.AddMinutes(2));

        Assert.Equal((PurchaseRequestStatus.Approved, 4L), (request.Status, request.Version));
        Assert.Equal(new PurchaseRequestDecision(Manager, T0.AddMinutes(2).ToUniversalTime(), null), request.Approval);
        Assert.Null(request.Rejection);
        Assert.Equal(T0.AddMinutes(2).ToUniversalTime(), request.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(Preparer)]
    [InlineData(Submitter)]
    public void Neither_the_preparer_nor_the_submitter_decides(string maker)
    {
        var request = Submitted();
        Assert.Equal(PurchasingRuleCodes.RequestSameActor, Code(() => request.Approve(3, maker, T0)));
        Assert.Equal(PurchasingRuleCodes.RequestSameActor, Code(() => request.Reject(3, maker, "SENTETIK", false, T0)));
        Assert.Equal((PurchaseRequestStatus.Submitted, 3L), (request.Status, request.Version));
    }

    [Fact]
    public void Maker_comparison_is_ordinal()
    {
        var request = Submitted();
        request.Approve(3, Preparer.ToUpperInvariant(), T0);
        Assert.Equal(PurchaseRequestStatus.Approved, request.Status);
    }

    [Fact]
    public void An_unknown_maker_is_refused_never_assumed()
    {
        // Built without a preparer, or submitted without its actor (a submission recorded before A1-1): no decision.
        var noPreparer = PurchaseRequest.Create(Guid.NewGuid(), Company, T0);
        noPreparer.AddLine(1, Guid.NewGuid(), Guid.NewGuid(), KgUnit, Quantity.Of(1m, QuantityUnit.Kilogram), T0);
        noPreparer.Submit(2, Submitter, T0);
        Assert.Equal(PurchasingRuleCodes.RequestMakerUnknown, Code(() => noPreparer.Approve(3, Manager, T0)));

        var noSubmitter = PurchaseRequest.Create(Guid.NewGuid(), Company, Preparer, T0);
        noSubmitter.AddLine(1, Guid.NewGuid(), Guid.NewGuid(), KgUnit, Quantity.Of(1m, QuantityUnit.Kilogram), T0);
        noSubmitter.Submit(2, T0);
        Assert.Null(noSubmitter.SubmittedBy);
        Assert.Equal(PurchasingRuleCodes.RequestMakerUnknown, Code(() => noSubmitter.Approve(3, Manager, T0)));
        Assert.Equal(PurchasingRuleCodes.RequestMakerUnknown, Code(() => noSubmitter.Reject(3, Manager, "SENTETIK", false, T0)));
    }

    [Fact]
    public void Only_a_submitted_request_is_approved()
    {
        var draft = PurchaseRequest.Create(Guid.NewGuid(), Company, Preparer, T0);
        draft.AddLine(1, Guid.NewGuid(), Guid.NewGuid(), KgUnit, Quantity.Of(1m, QuantityUnit.Kilogram), T0);
        Assert.Equal(PurchasingRuleCodes.RequestNotSubmitted, Code(() => draft.Approve(2, Manager, T0)));

        var approved = Submitted();
        approved.Approve(3, Manager, T0);
        Assert.Equal(PurchasingRuleCodes.RequestNotSubmitted, Code(() => approved.Approve(4, Manager, T0)));

        var rejected = Submitted();
        rejected.Reject(3, Manager, "SENTETIK", false, T0);
        Assert.Equal(PurchasingRuleCodes.RequestNotSubmitted, Code(() => rejected.Approve(4, Manager, T0)));
    }

    [Fact]
    public void A_submitted_or_approved_request_is_rejected_with_its_reason_and_rejected_is_terminal()
    {
        var submitted = Submitted();
        submitted.Reject(3, Manager, "SENTETIK ihtiyaç kalktı", false, T0.AddMinutes(5));
        Assert.Equal((PurchaseRequestStatus.Rejected, 4L), (submitted.Status, submitted.Version));
        Assert.Equal(new PurchaseRequestDecision(Manager, T0.AddMinutes(5).ToUniversalTime(), "SENTETIK ihtiyaç kalktı"), submitted.Rejection);
        Assert.Null(submitted.Approval);

        var approved = Submitted();
        approved.Approve(3, Manager, T0);
        approved.Reject(4, "SENTETIK-other-manager", "SENTETIK", false, T0);
        Assert.Equal((PurchaseRequestStatus.Rejected, 5L), (approved.Status, approved.Version));
        Assert.NotNull(approved.Approval);

        Assert.Equal(PurchasingRuleCodes.RequestNotRejectable, Code(() => approved.Reject(5, Manager, "SENTETIK", false, T0)));
        var draft = PurchaseRequest.Create(Guid.NewGuid(), Company, Preparer, T0);
        Assert.Equal(PurchasingRuleCodes.RequestNotRejectable, Code(() => draft.Reject(1, Manager, "SENTETIK", false, T0)));
    }

    [Theory]
    [InlineData(null, PurchasingRuleCodes.RequestRejectionReasonRequired)]
    [InlineData("", PurchasingRuleCodes.RequestRejectionReasonRequired)]
    [InlineData(" \t", PurchasingRuleCodes.RequestRejectionReasonRequired)]
    public void A_rejection_needs_a_reason(string? reason, string code)
    {
        var request = Submitted();
        Assert.Equal(code, Code(() => request.Reject(3, Manager, reason, false, T0)));
        Assert.Equal(PurchaseRequestStatus.Submitted, request.Status);
    }

    [Fact]
    public void A_reason_over_the_bound_is_refused_never_cut()
    {
        var request = Submitted();
        Assert.Equal(PurchasingRuleCodes.RequestRejectionReasonTooLong,
            Code(() => request.Reject(3, Manager, new string('x', PurchaseRequest.MaximumReasonLength + 1), false, T0)));
        request.Reject(3, Manager, new string('x', PurchaseRequest.MaximumReasonLength), false, T0);
        Assert.Equal(PurchaseRequest.MaximumReasonLength, request.Rejection!.Reason!.Length);
    }

    [Fact]
    public void A_bound_approved_order_blocks_the_rejection()
    {
        // No.161 §1: the bound approved order is cancelled first; the request stays as it was.
        var request = Submitted();
        request.Approve(3, Manager, T0);
        Assert.Equal(PurchasingRuleCodes.RequestHasApprovedOrder, Code(() => request.Reject(4, Manager, "SENTETIK", true, T0)));
        Assert.Equal((PurchaseRequestStatus.Approved, 4L), (request.Status, request.Version));
        Assert.Null(request.Rejection);
    }

    [Fact]
    public void A_stale_version_is_a_conflict_before_any_rule()
    {
        var request = Submitted();
        Assert.Throws<PurchasingVersionConflictException>(() => request.Approve(2, Preparer, T0));
        Assert.Throws<PurchasingVersionConflictException>(() => request.Reject(9, Manager, null, true, T0));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" padded")]
    public void A_blank_or_padded_actor_is_refused(string actor)
    {
        Assert.Throws<ArgumentException>(() => PurchaseRequest.Create(Guid.NewGuid(), Company, actor, T0));
        var request = Submitted();
        Assert.Throws<ArgumentException>(() => request.Approve(3, actor, T0));
        Assert.Throws<ArgumentException>(() => request.Reject(3, actor, "SENTETIK", false, T0));
        Assert.Throws<ArgumentException>(() => request.Approve(3, new string('a', PurchaseRequest.MaximumActorLength + 1), T0));
    }
}
