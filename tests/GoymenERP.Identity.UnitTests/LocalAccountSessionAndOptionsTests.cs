using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Credentials;
using GoymenERP.Modules.Identity.Infrastructure.LocalAccount;

namespace GoymenERP.Identity.UnitTests;

/// <summary>
/// ID-1 per-request session check against the stored security stamp, and the fail-closed
/// <c>Identity:LocalAccount</c> settings validation. SENTETIK.
/// </summary>
public sealed class LocalAccountSessionAndOptionsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_session_with_the_stored_stamp_of_an_active_user_is_current()
    {
        var (credential, store) = NewStore();

        Assert.True(await new LocalAccountSessionValidator(store).IsCurrentAsync(credential.UserId, credential.SecurityStamp, CancellationToken.None));
    }

    [Fact]
    public async Task A_renewed_stamp_ends_the_session()
    {
        var (credential, store) = NewStore();
        var oldStamp = credential.SecurityStamp;
        store.ReplaceStamp(Guid.NewGuid());

        Assert.False(await new LocalAccountSessionValidator(store).IsCurrentAsync(credential.UserId, oldStamp, CancellationToken.None));
    }

    [Fact]
    public async Task A_deactivated_user_loses_the_session()
    {
        var (credential, store) = NewStore();
        store.UserIsActive = false;

        Assert.False(await new LocalAccountSessionValidator(store).IsCurrentAsync(credential.UserId, credential.SecurityStamp, CancellationToken.None));
    }

    [Fact]
    public async Task A_forced_password_change_keeps_the_session_but_fences_it()
    {
        var (credential, store) = NewStore(mustChangePassword: true);
        var validator = new LocalAccountSessionValidator(store);

        Assert.Equal(
            LocalAccountSessionState.PasswordChangeRequired,
            await validator.GetSessionStateAsync(credential.UserId, credential.SecurityStamp, CancellationToken.None));
        Assert.False(await validator.IsCurrentAsync(credential.UserId, credential.SecurityStamp, CancellationToken.None));
    }

    [Fact]
    public async Task A_forced_password_change_with_another_stamp_or_an_inactive_user_still_ends_the_session()
    {
        var (credential, store) = NewStore(mustChangePassword: true);
        var validator = new LocalAccountSessionValidator(store);

        Assert.Equal(
            LocalAccountSessionState.NotCurrent,
            await validator.GetSessionStateAsync(credential.UserId, Guid.NewGuid(), CancellationToken.None));
        store.UserIsActive = false;
        Assert.Equal(
            LocalAccountSessionState.NotCurrent,
            await validator.GetSessionStateAsync(credential.UserId, credential.SecurityStamp, CancellationToken.None));
    }

    [Fact]
    public async Task An_outstanding_temporary_password_ends_the_session_even_with_the_stored_stamp()
    {
        var (credential, store) = NewStore(mustChangePassword: true);
        typeof(LocalCredential)
            .GetProperty(nameof(LocalCredential.TemporaryPasswordIssuedAtUtc))!
            .SetValue(credential, Now.AddHours(-1));

        Assert.Equal(
            LocalAccountSessionState.NotCurrent,
            await new LocalAccountSessionValidator(store).GetSessionStateAsync(credential.UserId, credential.SecurityStamp, CancellationToken.None));
    }

    [Fact]
    public async Task A_current_session_reports_the_current_state()
    {
        var (credential, store) = NewStore();

        Assert.Equal(
            LocalAccountSessionState.Current,
            await new LocalAccountSessionValidator(store).GetSessionStateAsync(credential.UserId, credential.SecurityStamp, CancellationToken.None));
    }

    [Fact]
    public async Task An_unknown_user_or_an_empty_identifier_is_never_current()
    {
        var (credential, store) = NewStore();
        var validator = new LocalAccountSessionValidator(store);

        Assert.False(await validator.IsCurrentAsync(Guid.NewGuid(), credential.SecurityStamp, CancellationToken.None));
        Assert.False(await validator.IsCurrentAsync(Guid.Empty, credential.SecurityStamp, CancellationToken.None));
        Assert.False(await validator.IsCurrentAsync(credential.UserId, Guid.Empty, CancellationToken.None));
    }

    [Fact]
    public void Complete_settings_are_valid_and_give_the_configured_lockout_policy()
    {
        var options = LocalAccountLoginServiceTests.ValidOptions();

        Assert.Empty(options.Validate());
        options.EnsureValid();
        Assert.Equal(new LocalCredentialLockoutPolicy(3, TimeSpan.FromMinutes(15)), options.ToLockoutPolicy());
    }

    [Fact]
    public void Empty_settings_name_every_missing_value_and_stop_the_host()
    {
        var options = new LocalAccountOptions();

        var errors = options.Validate();

        foreach (var key in new[]
        {
            "Lockout:MaxFailedAttempts", "Lockout:Duration", "Session:IdleTimeout", "Session:AbsoluteLifetime",
            "PasswordHashing:Algorithm", "PasswordHashing:Pbkdf2Iterations", "RateLimit:Login:PermitLimit",
            "RateLimit:Login:Window", "RateLimit:Login:PartitionBy", "PasswordPolicy:MinimumLength",
            "PasswordPolicy:MaximumLength", "PasswordPolicy:BlocklistEnabled", "PasswordPolicy:RequireCharacterClasses",
        })
        {
            Assert.Contains(errors, error => error.Contains(LocalAccountOptions.SectionName + ":" + key, StringComparison.Ordinal));
        }

        Assert.Throws<InvalidOperationException>(options.EnsureValid);
    }

    [Fact]
    public void Missing_rate_limit_values_alone_keep_the_mode_closed()
    {
        var options = LocalAccountLoginServiceTests.ValidOptions();
        options.RateLimit.Login = new LocalAccountLoginRateLimitSettings();

        Assert.Equal(3, options.Validate().Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_lockout_threshold_below_one_is_invalid(int maxFailedAttempts)
    {
        var options = LocalAccountLoginServiceTests.ValidOptions();
        options.Lockout.MaxFailedAttempts = maxFailedAttempts;

        Assert.Single(options.Validate());
    }

    [Fact]
    public void An_idle_timeout_longer_than_the_absolute_lifetime_is_invalid()
    {
        var options = LocalAccountLoginServiceTests.ValidOptions();
        options.Session.IdleTimeout = options.Session.AbsoluteLifetime + TimeSpan.FromMinutes(1);

        Assert.Single(options.Validate());
    }

    [Fact]
    public void An_iteration_count_below_the_framework_default_is_invalid()
    {
        var options = LocalAccountLoginServiceTests.ValidOptions();
        options.PasswordHashing.Pbkdf2Iterations = LocalAccountOptions.MinimumPbkdf2Iterations - 1;

        Assert.Single(options.Validate());
    }

    [Theory]
    [InlineData("pbkdf2")]
    [InlineData("Argon2id")]
    [InlineData("")]
    public void Only_the_pbkdf2_algorithm_is_accepted(string algorithm)
    {
        var options = LocalAccountLoginServiceTests.ValidOptions();
        options.PasswordHashing.Algorithm = algorithm;

        Assert.Single(options.Validate());
    }

    [Fact]
    public void An_undefined_partition_value_is_invalid()
    {
        var options = LocalAccountLoginServiceTests.ValidOptions();
        options.RateLimit.Login.PartitionBy = (LocalAccountLoginRateLimitPartition)99;

        Assert.Single(options.Validate());
    }

    [Fact]
    public void The_validated_password_policy_carries_the_configured_values()
    {
        var options = LocalAccountLoginServiceTests.ValidOptions();
        options.PasswordPolicy.BlockedTerms = ["sentetik-terim"];

        var policy = options.ToPasswordPolicy();

        Assert.Equal((8, 128, true), (policy.MinimumLength, policy.MaximumLength, policy.BlocklistEnabled));
        Assert.Equal(["sentetik-terim"], policy.BlockedTerms);
    }

    [Fact]
    public void Missing_password_policy_values_alone_keep_the_mode_closed()
    {
        var options = LocalAccountLoginServiceTests.ValidOptions();
        options.PasswordPolicy = new LocalAccountPasswordPolicySettings();

        // MinimumLength, MaximumLength, BlocklistEnabled and RequireCharacterClasses.
        Assert.Equal(4, options.Validate().Count);
        Assert.Throws<InvalidOperationException>(options.EnsureValid);
    }

    [Theory]
    [InlineData(0, 128)]
    [InlineData(8, 63)]
    [InlineData(200, 128)]
    public void A_password_length_window_outside_the_rules_is_invalid(int minimumLength, int maximumLength)
    {
        var options = LocalAccountLoginServiceTests.ValidOptions();
        options.PasswordPolicy.MinimumLength = minimumLength;
        options.PasswordPolicy.MaximumLength = maximumLength;

        Assert.Single(options.Validate());
    }

    [Fact]
    public void Turning_on_an_undecided_password_rule_stops_the_host()
    {
        var withClasses = LocalAccountLoginServiceTests.ValidOptions();
        withClasses.PasswordPolicy.RequireCharacterClasses = true;
        var withMaximumAge = LocalAccountLoginServiceTests.ValidOptions();
        withMaximumAge.PasswordPolicy.MaximumAgeDays = 90;
        var withBlankTerm = LocalAccountLoginServiceTests.ValidOptions();
        withBlankTerm.PasswordPolicy.BlockedTerms = [" "];

        Assert.Contains(withClasses.Validate(), error => error.Contains("RequireCharacterClasses", StringComparison.Ordinal));
        Assert.Contains(withMaximumAge.Validate(), error => error.Contains("MaximumAgeDays", StringComparison.Ordinal));
        Assert.Contains(withBlankTerm.Validate(), error => error.Contains("BlockedTerms", StringComparison.Ordinal));
    }

    private static (LocalCredential Credential, InMemoryLocalCredentialStore Store) NewStore(bool mustChangePassword = false)
    {
        var credential = new LocalCredential(
            Guid.NewGuid(),
            "sentetik.kullanici",
            FakePasswordHasher.HashWithCost(SyntheticPasswords.New(), FakePasswordHasher.CurrentCost),
            FakePasswordHasher.CurrentFormatVersion,
            mustChangePassword,
            Now,
            Guid.NewGuid());
        return (credential, new InMemoryLocalCredentialStore(credential, userIsActive: true));
    }
}
