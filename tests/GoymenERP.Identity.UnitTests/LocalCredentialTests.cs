using GoymenERP.Modules.Identity.Domain.Credentials;

namespace GoymenERP.Identity.UnitTests;

/// <summary>ID-1 local credential domain rules: user name normalization and the lockout counter. SENTETIK.</summary>
public sealed class LocalCredentialTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("kullanici", "KULLANICI")]
    [InlineData("  Kullanici  ", "KULLANICI")]
    [InlineData("KULLANICI", "KULLANICI")]
    public void A_user_name_is_trimmed_and_upper_cased_invariantly(string input, string expected)
    {
        Assert.Equal(expected, LocalUserName.TryNormalize(input));
    }

    [Fact]
    public void A_decomposed_and_a_composed_user_name_normalize_to_the_same_key()
    {
        var composed = "çalışan";
        var decomposed = "çalışan";

        Assert.Equal(LocalUserName.TryNormalize(composed), LocalUserName.TryNormalize(decomposed));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_user_name_has_no_key(string? input)
    {
        Assert.Null(LocalUserName.TryNormalize(input));
    }

    [Fact]
    public void A_user_name_with_a_lone_surrogate_has_no_key()
    {
        // Built in code: a lone surrogate does not survive test-case serialization intact.
        Assert.Null(LocalUserName.TryNormalize("ali" + '\uD800'));
        Assert.Null(LocalUserName.TryNormalize('\uDC00' + "ali"));
        Assert.Null(LocalUserName.TryNormalize("a" + '\uDC00' + '\uD800' + "b"));
        Assert.NotNull(LocalUserName.TryNormalize("ali\U0001F600"));
    }

    [Fact]
    public void A_user_name_longer_than_the_column_has_no_key()
    {
        Assert.NotNull(LocalUserName.TryNormalize(new string('a', LocalUserName.MaxLength)));
        Assert.Null(LocalUserName.TryNormalize(new string('a', LocalUserName.MaxLength + 1)));
    }

    [Fact]
    public void A_new_credential_stores_the_normalized_key_and_starts_unlocked_with_version_one()
    {
        var userId = Guid.NewGuid();
        var stamp = Guid.NewGuid();

        var credential = new LocalCredential(userId, " Kullanici ", "hash", 3, mustChangePassword: true, Now, stamp);

        Assert.Equal((userId, "Kullanici", "KULLANICI"), (credential.UserId, credential.UserName, credential.NormalizedUserName));
        Assert.Equal((3, true, 0, (DateTimeOffset?)null, false), (credential.HashFormatVersion, credential.MustChangePassword,
            credential.FailedAccessCount, credential.LockoutEndUtc, credential.TwoFactorEnabled));
        Assert.Equal((stamp, 1L), (credential.SecurityStamp, credential.Version));
        Assert.Null(credential.TemporaryPasswordIssuedAtUtc);
    }

    [Fact]
    public void A_credential_refuses_empty_identities_hashes_and_stamps()
    {
        Assert.Throws<ArgumentException>(() => new LocalCredential(Guid.Empty, "a", "hash", 3, false, Now, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => new LocalCredential(Guid.NewGuid(), " ", "hash", 3, false, Now, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => new LocalCredential(Guid.NewGuid(), "a", " ", 3, false, Now, Guid.NewGuid()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LocalCredential(Guid.NewGuid(), "a", "hash", 0, false, Now, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => new LocalCredential(Guid.NewGuid(), "a", "hash", 3, false, Now, Guid.Empty));
    }

    [Fact]
    public void An_account_is_locked_only_while_the_lockout_end_is_in_the_future()
    {
        Assert.False(LocalCredentialLockout.IsLockedOut(null, Now));
        Assert.False(LocalCredentialLockout.IsLockedOut(Now, Now));
        Assert.False(LocalCredentialLockout.IsLockedOut(Now.AddTicks(-1), Now));
        Assert.True(LocalCredentialLockout.IsLockedOut(Now.AddTicks(1), Now));
    }

    [Fact]
    public void Failures_below_the_threshold_count_up_and_keep_the_lockout_end()
    {
        var policy = new LocalCredentialLockoutPolicy(3, TimeSpan.FromMinutes(5));
        var expired = Now.AddMinutes(-1);

        var transition = LocalCredentialLockout.AfterFailedAccess(1, expired, policy, Now);

        Assert.Equal(new LocalCredentialFailureTransition(2, expired, LockedNow: false), transition);
    }

    [Fact]
    public void The_failure_that_reaches_the_threshold_locks_for_the_duration_and_restarts_the_counter()
    {
        var policy = new LocalCredentialLockoutPolicy(3, TimeSpan.FromMinutes(5));

        var transition = LocalCredentialLockout.AfterFailedAccess(2, null, policy, Now);

        Assert.Equal(new LocalCredentialFailureTransition(0, Now.AddMinutes(5), LockedNow: true), transition);
        Assert.True(LocalCredentialLockout.IsLockedOut(transition.LockoutEndUtc, Now));
        Assert.False(LocalCredentialLockout.IsLockedOut(transition.LockoutEndUtc, Now.AddMinutes(5)));
    }

    [Fact]
    public void A_threshold_of_one_locks_on_the_first_failure()
    {
        var transition = LocalCredentialLockout.AfterFailedAccess(0, null, new LocalCredentialLockoutPolicy(1, TimeSpan.FromSeconds(1)), Now);

        Assert.True(transition.LockedNow);
    }

    [Fact]
    public void The_lockout_policy_refuses_a_non_positive_threshold_or_duration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LocalCredentialLockoutPolicy(0, TimeSpan.FromMinutes(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LocalCredentialLockoutPolicy(1, TimeSpan.Zero));
    }

    [Fact]
    public void Login_writes_change_the_counter_lock_and_hash_but_never_the_version_or_the_stamp()
    {
        var stamp = Guid.NewGuid();
        var credential = new LocalCredential(Guid.NewGuid(), "a", "old", 3, false, Now, stamp);
        var policy = new LocalCredentialLockoutPolicy(2, TimeSpan.FromMinutes(1));

        credential.ApplyFailedAccess(LocalCredentialLockout.AfterFailedAccess(credential.FailedAccessCount, credential.LockoutEndUtc, policy, Now));
        Assert.Equal(1, credential.FailedAccessCount);
        credential.ApplyFailedAccess(LocalCredentialLockout.AfterFailedAccess(credential.FailedAccessCount, credential.LockoutEndUtc, policy, Now));
        Assert.Equal((0, (DateTimeOffset?)Now.AddMinutes(1)), (credential.FailedAccessCount, credential.LockoutEndUtc));

        credential.ApplySuccessfulAccess("new", 4);

        Assert.Equal((0, (DateTimeOffset?)null, "new", 4), (credential.FailedAccessCount, credential.LockoutEndUtc, credential.PasswordHash, credential.HashFormatVersion));
        Assert.Equal((1L, stamp), (credential.Version, credential.SecurityStamp));
        Assert.Throws<ArgumentException>(() => credential.ApplySuccessfulAccess("only-hash", null));
    }
}
