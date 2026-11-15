using GoymenERP.Modules.Identity.Domain.Credentials;
using GoymenERP.Modules.Identity.Domain.Users;

namespace GoymenERP.Identity.UnitTests;

/// <summary>
/// ID-2r <c>recover-founder</c> domain transitions (KIM-D22 reactivate, KIM-D23 reset-password). The command itself
/// waits for the ADR-SEC-001 recovery codes; these rules are what it will apply. SENTETIK.
/// </summary>
public sealed class FounderRecoveryDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Reactivation_forces_a_new_password_resets_the_lockout_clears_the_temporary_password_and_renews_the_stamp()
    {
        var stamp = Guid.NewGuid();
        var credential = LockedCredentialWithTemporaryPassword(stamp);
        var versionBefore = credential.Version;
        var newStamp = Guid.NewGuid();

        credential.ApplyFounderReactivation(newStamp);

        Assert.Equal((true, 0, (DateTimeOffset?)null), (credential.MustChangePassword, credential.FailedAccessCount, credential.LockoutEndUtc));
        Assert.Equal(((DateTimeOffset?)null, (DateTimeOffset?)null, (DateTimeOffset?)null),
            (credential.TemporaryPasswordIssuedAtUtc, credential.TemporaryPasswordExpiresAtUtc, credential.TemporaryPasswordConsumedAtUtc));
        Assert.Equal((newStamp, versionBefore + 1), (credential.SecurityStamp, credential.Version));
        Assert.Equal(("temporary", 4), (credential.PasswordHash, credential.HashFormatVersion));
        Assert.False(credential.TwoFactorEnabled);
    }

    [Fact]
    public void Reactivation_refuses_an_empty_or_unchanged_stamp_and_then_changes_nothing()
    {
        var stamp = Guid.NewGuid();
        var credential = new LocalCredential(Guid.NewGuid(), "kurucu", "hash", 3, mustChangePassword: false, Now, stamp);

        Assert.Throws<ArgumentException>(() => credential.ApplyFounderReactivation(Guid.Empty));
        Assert.Throws<ArgumentException>(() => credential.ApplyFounderReactivation(stamp));

        Assert.Equal((false, stamp, 1L), (credential.MustChangePassword, credential.SecurityStamp, credential.Version));
    }

    [Fact]
    public void A_temporary_password_replaces_the_hash_opens_an_unconsumed_window_forces_a_change_and_renews_the_stamp()
    {
        var credential = new LocalCredential(Guid.NewGuid(), "kurucu", "old", 3, mustChangePassword: false, Now.AddDays(-30), Guid.NewGuid());
        credential.ApplyFailedAccess(new LocalCredentialFailureTransition(0, Now.AddMinutes(5), LockedNow: true));
        var newStamp = Guid.NewGuid();
        var expiresAt = Now.AddHours(1);

        credential.IssueTemporaryPassword("temporary", 4, Now, expiresAt, newStamp);

        Assert.Equal(("temporary", 4, true, Now), (credential.PasswordHash, credential.HashFormatVersion, credential.MustChangePassword,
            credential.PasswordChangedAtUtc));
        Assert.Equal(((DateTimeOffset?)Now, (DateTimeOffset?)expiresAt, (DateTimeOffset?)null),
            (credential.TemporaryPasswordIssuedAtUtc, credential.TemporaryPasswordExpiresAtUtc, credential.TemporaryPasswordConsumedAtUtc));
        Assert.Equal((0, (DateTimeOffset?)null), (credential.FailedAccessCount, credential.LockoutEndUtc));
        Assert.Equal((newStamp, 2L), (credential.SecurityStamp, credential.Version));
    }

    [Fact]
    public void A_temporary_password_refuses_an_empty_hash_a_bad_format_a_window_not_in_the_future_or_a_stale_stamp()
    {
        var stamp = Guid.NewGuid();
        var credential = new LocalCredential(Guid.NewGuid(), "kurucu", "old", 3, mustChangePassword: false, Now, stamp);

        Assert.Throws<ArgumentException>(() => credential.IssueTemporaryPassword(" ", 4, Now, Now.AddHours(1), Guid.NewGuid()));
        Assert.Throws<ArgumentOutOfRangeException>(() => credential.IssueTemporaryPassword("t", 0, Now, Now.AddHours(1), Guid.NewGuid()));
        Assert.Throws<ArgumentOutOfRangeException>(() => credential.IssueTemporaryPassword("t", 4, Now, Now, Guid.NewGuid()));
        Assert.Throws<ArgumentOutOfRangeException>(() => credential.IssueTemporaryPassword("t", 4, Now, Now.AddSeconds(-1), Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => credential.IssueTemporaryPassword("t", 4, Now, Now.AddHours(1), Guid.Empty));
        Assert.Throws<ArgumentException>(() => credential.IssueTemporaryPassword("t", 4, Now, Now.AddHours(1), stamp));

        Assert.Equal(("old", 1L, (DateTimeOffset?)null), (credential.PasswordHash, credential.Version, credential.TemporaryPasswordIssuedAtUtc));
    }

    [Fact]
    public void A_password_change_after_a_temporary_password_closes_the_window()
    {
        var credential = LockedCredentialWithTemporaryPassword(Guid.NewGuid());

        credential.ChangePassword("chosen", 4, Now.AddMinutes(10), Guid.NewGuid());

        Assert.False(credential.MustChangePassword);
        Assert.Equal(((DateTimeOffset?)null, (DateTimeOffset?)null, (DateTimeOffset?)null),
            (credential.TemporaryPasswordIssuedAtUtc, credential.TemporaryPasswordExpiresAtUtc, credential.TemporaryPasswordConsumedAtUtc));
    }

    [Fact]
    public void An_inactive_account_is_reactivated_with_one_version_step_and_an_active_one_is_refused()
    {
        var account = new UserAccount(Guid.NewGuid(), "Kurucu", email: null);
        Assert.Throws<InvalidOperationException>(() => account.Reactivate(Now));
        Assert.Equal((true, 1L), (account.IsActive, account.Version));

        typeof(UserAccount).GetProperty(nameof(UserAccount.IsActive))!.SetValue(account, false);

        account.Reactivate(Now);

        Assert.Equal((true, 2L, Now), (account.IsActive, account.Version, account.UpdatedAtUtc));
    }

    private static LocalCredential LockedCredentialWithTemporaryPassword(Guid stamp)
    {
        var credential = new LocalCredential(Guid.NewGuid(), "kurucu", "old", 3, mustChangePassword: false, Now.AddDays(-30), stamp);
        credential.IssueTemporaryPassword("temporary", 4, Now.AddDays(-2), Now.AddDays(-1), Guid.NewGuid());
        credential.ApplyFailedAccess(new LocalCredentialFailureTransition(2, Now.AddMinutes(5), LockedNow: false));
        return credential;
    }
}
