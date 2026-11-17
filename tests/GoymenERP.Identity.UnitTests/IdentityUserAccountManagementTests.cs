using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Credentials;
using GoymenERP.Modules.Identity.Domain.Users;

namespace GoymenERP.Identity.UnitTests;

public sealed class IdentityUserAccountManagementTests
{
    [Fact]
    public void Administrative_reset_forces_password_change_clears_founder_temporary_fields_and_ends_sessions()
    {
        var now = DateTimeOffset.UtcNow;
        var credential = NewCredential();
        credential.IssueTemporaryPassword("old-hash", 1, now, now.AddHours(1), Guid.NewGuid());
        credential.ApplyFailedAccess(new LocalCredentialFailureTransition(4, now.AddMinutes(15), true));
        var before = credential.Version;
        var stamp = Guid.NewGuid();
        credential.ResetPassword("new-hash", 1, now.AddMinutes(1), stamp);
        Assert.Equal("new-hash", credential.PasswordHash);
        Assert.True(credential.MustChangePassword);
        Assert.Null(credential.TemporaryPasswordIssuedAtUtc);
        Assert.Null(credential.TemporaryPasswordExpiresAtUtc);
        Assert.Null(credential.TemporaryPasswordConsumedAtUtc);
        Assert.Equal(0, credential.FailedAccessCount);
        Assert.Null(credential.LockoutEndUtc);
        Assert.Equal(stamp, credential.SecurityStamp);
        Assert.Equal(before + 1, credential.Version);
    }

    [Fact]
    public void Unlock_changes_command_version_without_replacing_the_password_or_security_stamp()
    {
        var credential = NewCredential();
        var stamp = credential.SecurityStamp;
        credential.ApplyFailedAccess(new LocalCredentialFailureTransition(5, DateTimeOffset.UtcNow.AddMinutes(15), true));
        credential.Unlock();
        Assert.Equal(0, credential.FailedAccessCount);
        Assert.Null(credential.LockoutEndUtc);
        Assert.Equal("synthetic-hash", credential.PasswordHash);
        Assert.Equal(stamp, credential.SecurityStamp);
        Assert.Equal(2, credential.Version);
    }

    [Fact]
    public void Deactivation_is_a_versioned_state_change_and_never_deletes_the_account()
    {
        var user = new UserAccount(Guid.NewGuid(), "Sentetik kullanıcı", null);
        var changed = DateTimeOffset.UtcNow.AddMinutes(1);
        user.Deactivate(changed);
        Assert.False(user.IsActive);
        Assert.Equal(2, user.Version);
        Assert.Equal(changed, user.UpdatedAtUtc);
        Assert.Throws<InvalidOperationException>(() => user.Deactivate(changed));
    }

    [Fact]
    public void Credential_bearing_request_debug_text_never_contains_a_password()
    {
        const string password = "SYNTHETIC-SECRET";
        Assert.DoesNotContain(password, new CreateLocalUserV1(Guid.NewGuid(), "0", "Sentetik", "sentetik", password, "Gerekçe").ToString());
        Assert.DoesNotContain(password, new ManageLocalUserV1("1", password, "Gerekçe").ToString());
    }

    private static LocalCredential NewCredential() => new(Guid.NewGuid(), "synthetic.user", "synthetic-hash", 1, false, DateTimeOffset.UtcNow, Guid.NewGuid());
}
