using GoymenERP.Modules.Identity.Domain.Approvals;

namespace GoymenERP.Identity.UnitTests;

public sealed class AccessGrantRequestTests
{
    private const string ReasonCode = "reason_code";

    [Fact]
    public void CriticalGrant_RequesterCannotApproveOwnRequest()
    {
        var requester = Guid.NewGuid();
        var request = CreateRequest(requester, Guid.NewGuid(), requiresSecondApproval: true);

        var exception = Assert.Throws<InvalidOperationException>(
            () => request.Approve(requester, DateTimeOffset.UtcNow));

        Assert.Equal(
            "Kritik yetki talebini talep eden veya hedef kullanıcı onaylayamaz.",
            exception.Message);
        Assert.Equal(AccessGrantRequestStatus.Pending, request.Status);
    }

    [Fact]
    public void CriticalGrant_TargetUserCannotSelfApprove()
    {
        var target = Guid.NewGuid();
        var request = CreateRequest(Guid.NewGuid(), target, requiresSecondApproval: true);

        Assert.Throws<InvalidOperationException>(
            () => request.Approve(target, DateTimeOffset.UtcNow));
        Assert.Equal(AccessGrantRequestStatus.Pending, request.Status);
    }

    [Fact]
    public void CriticalGrant_DistinctApproverCanApproveOnce()
    {
        var approver = Guid.NewGuid();
        var decisionTime = DateTimeOffset.UtcNow;
        var request = CreateRequest(Guid.NewGuid(), Guid.NewGuid(), requiresSecondApproval: true);

        request.Approve(approver, decisionTime);

        Assert.Equal(AccessGrantRequestStatus.Approved, request.Status);
        Assert.Equal(approver, request.DecidedByUserId);
        Assert.Equal(decisionTime, request.DecidedAtUtc);
        Assert.Throws<InvalidOperationException>(() => request.Reject(Guid.NewGuid(), ReasonCode, decisionTime));
    }

    [Fact]
    public void CriticalGrant_ApprovalRecordsTheSecondApprovalModeTheAssignmentAndOneVersionStep()
    {
        var assignmentId = Guid.NewGuid();
        var request = CreateRequest(Guid.NewGuid(), Guid.NewGuid(), requiresSecondApproval: true);

        request.Approve(Guid.NewGuid(), DateTimeOffset.UtcNow, assignmentId);

        Assert.Equal(AccessGrantApprovalMode.SecondApproval, request.ApprovalMode);
        Assert.Equal(assignmentId, request.ResultingRoleAssignmentId);
        Assert.Null(request.DecisionReasonCode);
        Assert.Equal(2, request.Version);
    }

    [Fact]
    public void Approval_RefusesAnEmptyResultingAssignmentAndStaysPending()
    {
        var request = CreateRequest(Guid.NewGuid(), Guid.NewGuid(), requiresSecondApproval: true);

        Assert.Throws<ArgumentException>(() => request.Approve(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.Empty));
        Assert.Equal(AccessGrantRequestStatus.Pending, request.Status);
        Assert.Equal(1, request.Version);
    }

    [Fact]
    public void FounderDirect_AppliesOnlyToTheFoundersOwnRequest()
    {
        var founder = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();
        var foreign = CreateRequest(Guid.NewGuid(), Guid.NewGuid(), requiresSecondApproval: true);
        var own = CreateRequest(founder, Guid.NewGuid(), requiresSecondApproval: true);

        Assert.Throws<InvalidOperationException>(() => foreign.ApproveAsFounder(founder, DateTimeOffset.UtcNow, assignmentId));
        Assert.Equal(AccessGrantRequestStatus.Pending, foreign.Status);

        own.ApproveAsFounder(founder, DateTimeOffset.UtcNow, assignmentId);

        Assert.Equal(AccessGrantRequestStatus.Approved, own.Status);
        Assert.Equal(AccessGrantApprovalMode.FounderDirect, own.ApprovalMode);
        Assert.Equal(founder, own.DecidedByUserId);
        Assert.Equal(assignmentId, own.ResultingRoleAssignmentId);
        Assert.Equal(2, own.Version);
    }

    [Fact]
    public void CriticalGrant_RequesterOrTargetCannotReject()
    {
        var requester = Guid.NewGuid();
        var target = Guid.NewGuid();
        var request = CreateRequest(requester, target, requiresSecondApproval: true);

        Assert.Throws<InvalidOperationException>(() => request.Reject(requester, ReasonCode, DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(() => request.Reject(target, ReasonCode, DateTimeOffset.UtcNow));
        Assert.Equal(AccessGrantRequestStatus.Pending, request.Status);
    }

    [Fact]
    public void Reject_RecordsTheCanonicalReasonCodeAndNoAssignment()
    {
        var checker = Guid.NewGuid();
        var request = CreateRequest(Guid.NewGuid(), Guid.NewGuid(), requiresSecondApproval: true);

        request.Reject(checker, ReasonCode, DateTimeOffset.UtcNow);

        Assert.Equal(AccessGrantRequestStatus.Rejected, request.Status);
        Assert.Equal(ReasonCode, request.DecisionReasonCode);
        Assert.Null(request.ApprovalMode);
        Assert.Null(request.ResultingRoleAssignmentId);
        Assert.Equal(checker, request.DecidedByUserId);
        Assert.Equal(2, request.Version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Upper")]
    [InlineData("1starts_with_digit")]
    [InlineData("has space")]
    [InlineData("with/slash")]
    public void Reject_RefusesANonCanonicalReasonCode(string reasonCode)
    {
        var request = CreateRequest(Guid.NewGuid(), Guid.NewGuid(), requiresSecondApproval: true);

        Assert.Throws<ArgumentException>(() => request.Reject(Guid.NewGuid(), reasonCode, DateTimeOffset.UtcNow));
        Assert.Equal(AccessGrantRequestStatus.Pending, request.Status);
    }

    [Fact]
    public void ReasonCode_IsBoundedByItsMaximumLength()
    {
        Assert.True(AccessGrantRequest.IsCanonicalReasonCode(new string('a', AccessGrantRequest.DecisionReasonCodeMaxLength)));
        Assert.False(AccessGrantRequest.IsCanonicalReasonCode(new string('a', AccessGrantRequest.DecisionReasonCodeMaxLength + 1)));
        Assert.True(AccessGrantRequest.IsCanonicalReasonCode("policy.scope-mismatch_2"));
    }

    [Fact]
    public void Withdraw_IsOnlyTheRequestersAndClosesThePendingRequest()
    {
        var requester = Guid.NewGuid();
        var target = Guid.NewGuid();
        var request = CreateRequest(requester, target, requiresSecondApproval: true);

        Assert.Throws<InvalidOperationException>(() => request.Withdraw(target, DateTimeOffset.UtcNow));
        Assert.Equal(AccessGrantRequestStatus.Pending, request.Status);

        request.Withdraw(requester, DateTimeOffset.UtcNow);

        Assert.Equal(AccessGrantRequestStatus.Withdrawn, request.Status);
        Assert.Equal(requester, request.DecidedByUserId);
        Assert.Null(request.ApprovalMode);
        Assert.Null(request.ResultingRoleAssignmentId);
        Assert.Throws<InvalidOperationException>(() => request.Approve(Guid.NewGuid(), DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(() => request.Withdraw(requester, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Decisions_RefuseAnEmptyActor()
    {
        var request = CreateRequest(Guid.NewGuid(), Guid.NewGuid(), requiresSecondApproval: false);

        Assert.Throws<ArgumentException>(() => request.Approve(Guid.Empty, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => request.Reject(Guid.Empty, ReasonCode, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => request.Withdraw(Guid.Empty, DateTimeOffset.UtcNow));
        Assert.Equal(AccessGrantRequestStatus.Pending, request.Status);
    }

    [Fact]
    public void Window_NeedsAStartForAnEndAndAnEndAfterTheStart()
    {
        var start = DateTimeOffset.UtcNow;

        Assert.True(AccessGrantRequest.IsValidWindow(null, null));
        Assert.True(AccessGrantRequest.IsValidWindow(start, null));
        Assert.True(AccessGrantRequest.IsValidWindow(start, start.AddDays(1)));
        Assert.False(AccessGrantRequest.IsValidWindow(null, start));
        Assert.False(AccessGrantRequest.IsValidWindow(start, start));
        Assert.Throws<ArgumentException>(() => new AccessGrantRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), true,
            start, start, start.AddSeconds(-1), reason: null));
    }

    [Fact]
    public void Reason_IsTrimmedBlankBecomesNullAndOverLongOrControlTextIsRefused()
    {
        Assert.Null(AccessGrantRequest.NormalizeReason("   "));
        Assert.Equal("gerekçe", AccessGrantRequest.NormalizeReason("  gerekçe "));
        Assert.Throws<ArgumentException>(() => AccessGrantRequest.NormalizeReason(new string('x', AccessGrantRequest.ReasonMaxLength + 1)));
        Assert.Throws<ArgumentException>(() => AccessGrantRequest.NormalizeReason("satır\u0007sonu"));
    }

    private static AccessGrantRequest CreateRequest(
        Guid requester,
        Guid target,
        bool requiresSecondApproval)
    {
        return new AccessGrantRequest(
            Guid.NewGuid(),
            requester,
            target,
            Guid.NewGuid(),
            Guid.NewGuid(),
            requiresSecondApproval);
    }
}
