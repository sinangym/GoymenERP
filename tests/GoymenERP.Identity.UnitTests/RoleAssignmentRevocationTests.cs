using GoymenERP.Modules.Identity.Domain.Assignments;

namespace GoymenERP.Identity.UnitTests;

/// <summary>Spec ID-3 / KIM-D15: revocation is one step and happens once. SENTETIK / NOT_ENV.</summary>
public sealed class RoleAssignmentRevocationTests
{
    [Fact]
    public void An_active_assignment_is_revoked_once_with_who_and_when_and_one_version_step()
    {
        var revoker = Guid.NewGuid();
        var revokedAt = DateTimeOffset.UtcNow;
        var assignment = NewAssignment();

        assignment.Revoke(revoker, revokedAt);

        Assert.Equal(RoleAssignmentStatus.Revoked, assignment.Status);
        Assert.Equal(revoker, assignment.RevokedByUserId);
        Assert.Equal(revokedAt, assignment.RevokedAtUtc);
        Assert.Equal(2, assignment.Version);
        Assert.False(assignment.IsEffectiveAt(revokedAt.AddSeconds(1)));
    }

    [Fact]
    public void A_revoked_assignment_cannot_be_revoked_again_and_keeps_its_first_revocation()
    {
        var firstRevoker = Guid.NewGuid();
        var revokedAt = DateTimeOffset.UtcNow;
        var assignment = NewAssignment();
        assignment.Revoke(firstRevoker, revokedAt);

        Assert.Throws<InvalidOperationException>(() => assignment.Revoke(Guid.NewGuid(), revokedAt.AddMinutes(1)));

        Assert.Equal(firstRevoker, assignment.RevokedByUserId);
        Assert.Equal(revokedAt, assignment.RevokedAtUtc);
        Assert.Equal(2, assignment.Version);
    }

    [Fact]
    public void A_revocation_without_a_revoker_is_refused_and_changes_nothing()
    {
        var assignment = NewAssignment();

        Assert.Throws<ArgumentException>(() => assignment.Revoke(Guid.Empty, DateTimeOffset.UtcNow));

        Assert.Equal(RoleAssignmentStatus.Active, assignment.Status);
        Assert.Null(assignment.RevokedByUserId);
        Assert.Null(assignment.RevokedAtUtc);
        Assert.Equal(1, assignment.Version);
    }

    [Fact]
    public void A_new_assignment_has_no_revocation()
    {
        var assignment = NewAssignment();

        Assert.Equal(RoleAssignmentStatus.Active, assignment.Status);
        Assert.Null(assignment.RevokedByUserId);
        Assert.Null(assignment.RevokedAtUtc);
    }

    private static RoleAssignment NewAssignment() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-1), null, null);
}
