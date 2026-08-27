using GoymenERP.Modules.Identity.Domain.Approvals;

namespace GoymenERP.Identity.UnitTests;

public sealed class AccessGrantRequestTests
{
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
        Assert.Throws<InvalidOperationException>(() => request.Reject(Guid.NewGuid(), decisionTime));
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
