using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Quantities;
using GoymenERP.Modules.Purchasing.Domain.Requests;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// A1-1b (plan API-01b, TEK-09; ALS-D46): the submitter's withdrawal in the domain — Submitted → Withdrawn with a required
/// reason, only by the submitter (ordinal), refused from Draft / Approved / Rejected / Withdrawn, refused for an unrecorded
/// submitter (fail-closed) and while an APPROVED order is bound. A separate terminal outcome from Rejected. SENTETIK.
/// </summary>
public sealed class PurchaseRequestWithdrawalTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 9, 0, 0, TimeSpan.FromHours(3));
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid KgUnit = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private const string Preparer = "SENTETIK-preparer";
    private const string Submitter = "SENTETIK-submitter";
    private const string Manager = "SENTETIK-manager";

    private static PurchaseRequest Draft(string preparer = Preparer)
    {
        var request = PurchaseRequest.Create(Guid.NewGuid(), Company, preparer, T0);
        request.AddLine(1, Guid.NewGuid(), Guid.NewGuid(), KgUnit, Quantity.Of(10m, QuantityUnit.Kilogram), T0);
        return request;
    }

    private static PurchaseRequest Submitted(string preparer = Preparer, string submitter = Submitter)
    {
        var request = Draft(preparer);
        request.Submit(2, submitter, T0.AddMinutes(1));
        return request;
    }

    private static string Code(Action action) => Assert.Throws<PurchasingRuleException>(action).Code;

    [Fact]
    public void The_submitter_withdraws_a_submitted_request_with_the_reason_as_written()
    {
        var request = Submitted();

        request.Withdraw(3, Submitter, " SENTETIK yanlış ürün ", false, T0.AddMinutes(2));

        Assert.Equal((PurchaseRequestStatus.Withdrawn, 4L), (request.Status, request.Version));
        Assert.Equal(new PurchaseRequestDecision(Submitter, T0.AddMinutes(2).ToUniversalTime(), " SENTETIK yanlış ürün "), request.Withdrawal);
        Assert.Null(request.Approval);
        Assert.Null(request.Rejection);
        Assert.Equal(T0.AddMinutes(2).ToUniversalTime(), request.UpdatedAtUtc);
        // Terminal: nothing decides or withdraws it again.
        Assert.Equal(PurchasingRuleCodes.RequestNotWithdrawable, Code(() => request.Withdraw(4, Submitter, "SENTETIK", false, T0)));
        Assert.Equal(PurchasingRuleCodes.RequestNotSubmitted, Code(() => request.Approve(4, Manager, T0)));
        Assert.Equal(PurchasingRuleCodes.RequestNotRejectable, Code(() => request.Reject(4, Manager, "SENTETIK", false, T0)));
    }

    [Theory]
    [InlineData(Manager)]
    [InlineData(Preparer)]
    public void Only_the_submitter_withdraws_not_the_manager_nor_a_preparer_who_did_not_submit(string actor)
    {
        var request = Submitted();
        Assert.Equal(PurchasingRuleCodes.RequestNotSubmitter, Code(() => request.Withdraw(3, actor, "SENTETIK", false, T0)));
        Assert.Equal((PurchaseRequestStatus.Submitted, 3L, null), (request.Status, request.Version, request.Withdrawal));
    }

    [Fact]
    public void The_submitter_comparison_is_ordinal()
    {
        var request = Submitted();
        Assert.Equal(PurchasingRuleCodes.RequestNotSubmitter, Code(() => request.Withdraw(3, Submitter.ToUpperInvariant(), "SENTETIK", false, T0)));
    }

    [Fact]
    public void A_preparer_who_also_submitted_withdraws()
    {
        var request = Submitted(Preparer, Preparer);
        request.Withdraw(3, Preparer, "SENTETIK", false, T0);
        Assert.Equal(PurchaseRequestStatus.Withdrawn, request.Status);
    }

    [Fact]
    public void Draft_approved_and_rejected_requests_are_not_withdrawn()
    {
        var draft = Draft();
        Assert.Equal(PurchasingRuleCodes.RequestNotWithdrawable, Code(() => draft.Withdraw(2, Preparer, "SENTETIK", false, T0)));

        var approved = Submitted();
        approved.Approve(3, Manager, T0);
        Assert.Equal(PurchasingRuleCodes.RequestNotWithdrawable, Code(() => approved.Withdraw(4, Submitter, "SENTETIK", false, T0)));

        var rejected = Submitted();
        rejected.Reject(3, Manager, "SENTETIK", false, T0);
        Assert.Equal(PurchasingRuleCodes.RequestNotWithdrawable, Code(() => rejected.Withdraw(4, Submitter, "SENTETIK", false, T0)));
    }

    [Theory]
    [InlineData(null, PurchasingRuleCodes.RequestWithdrawalReasonRequired)]
    [InlineData("", PurchasingRuleCodes.RequestWithdrawalReasonRequired)]
    [InlineData(" \t ", PurchasingRuleCodes.RequestWithdrawalReasonRequired)]
    [InlineData("long", PurchasingRuleCodes.RequestWithdrawalReasonTooLong)]
    public void A_withdrawal_needs_a_reason_that_is_never_cut(string? reason, string code)
    {
        var request = Submitted();
        var text = reason == "long" ? new string('x', PurchaseRequest.MaximumReasonLength + 1) : reason;
        Assert.Equal(code, Code(() => request.Withdraw(3, Submitter, text, false, T0)));
        Assert.Equal(PurchaseRequestStatus.Submitted, request.Status);

        request.Withdraw(3, Submitter, new string('y', PurchaseRequest.MaximumReasonLength), false, T0);
        Assert.Equal(PurchaseRequest.MaximumReasonLength, request.Withdrawal!.Reason!.Length);
    }

    [Fact]
    public void An_unrecorded_submitter_is_refused_never_assumed()
    {
        // A submission recorded before A1-1 (no actor): nobody — not even the preparer — may withdraw it.
        var request = Draft();
        request.Submit(2, T0);
        Assert.Null(request.SubmittedBy);
        Assert.Equal(PurchasingRuleCodes.RequestMakerUnknown, Code(() => request.Withdraw(3, Preparer, "SENTETIK", false, T0)));
    }

    [Fact]
    public void A_bound_approved_order_refuses_the_withdrawal_and_nothing_moves()
    {
        var request = Submitted();
        Assert.Equal(PurchasingRuleCodes.RequestHasApprovedOrder, Code(() => request.Withdraw(3, Submitter, "SENTETIK", true, T0)));
        Assert.Equal((PurchaseRequestStatus.Submitted, 3L, null), (request.Status, request.Version, request.Withdrawal));
    }

    [Fact]
    public void A_stale_version_and_a_malformed_actor_are_refused_first()
    {
        var request = Submitted();
        Assert.Throws<PurchasingVersionConflictException>(() => request.Withdraw(2, Submitter, "SENTETIK", false, T0));
        Assert.Throws<ArgumentException>(() => request.Withdraw(3, " " + Submitter, "SENTETIK", false, T0));
        Assert.Throws<ArgumentException>(() => request.Withdraw(3, new string('a', PurchaseRequest.MaximumActorLength + 1), "SENTETIK", false, T0));
        Assert.Equal(PurchaseRequestStatus.Submitted, request.Status);
    }
}
