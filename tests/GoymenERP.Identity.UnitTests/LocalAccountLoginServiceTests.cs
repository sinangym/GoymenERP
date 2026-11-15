using System.Text.Json;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Credentials;
using GoymenERP.Modules.Identity.Infrastructure.LocalAccount;
using Microsoft.Extensions.Options;

namespace GoymenERP.Identity.UnitTests;

/// <summary>
/// ID-1 local account login: refusals, the lockout counter, the conditional writes, the fail-closed fences and the
/// <c>auth.login.*</c> security events. The store is an in-memory double with the same conditional semantics as
/// the SQL store (its relational behaviour is covered by the SQL Server relational facts). SENTETIK.
/// </summary>
public sealed class LocalAccountLoginServiceTests
{
    private const string UserName = "sentetik.kullanici";
    // Generated per run (spec "Varsayılan kimlik bilgisi yasağı" item 1); no password is written into the repository.
    private static readonly string Password = SyntheticPasswords.New();
    private static readonly string WrongPassword = SyntheticPasswords.New();
    private const int MaxFailedAttempts = 3;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_right_password_opens_a_session_and_keeps_the_version()
    {
        var harness = Harness.WithUser();

        var result = await harness.LoginAsync(UserName, Password);

        Assert.True(result.Succeeded);
        Assert.Equal((harness.Credential.UserId, harness.Credential.SecurityStamp), (result.UserId!.Value, result.SecurityStamp!.Value));
        Assert.Equal((0, (DateTimeOffset?)null, 1L), (harness.Credential.FailedAccessCount, harness.Credential.LockoutEndUtc, harness.Credential.Version));
    }

    [Fact]
    public async Task A_user_name_in_another_case_and_with_spaces_finds_the_same_account()
    {
        var harness = Harness.WithUser();

        var result = await harness.LoginAsync("  SENTETIK.Kullanici ", Password);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task The_success_event_is_a_platform_user_event_without_a_company()
    {
        var harness = Harness.WithUser();

        await harness.LoginAsync(UserName, Password);

        var written = Assert.Single(harness.Events.Written);
        Assert.Equal(
            (SecurityEventTypesV1.LoginSucceeded, SecurityEventOutcomesV1.Allowed, SecurityEventSeveritiesV1.Info),
            (written.EventType, written.Outcome, written.Severity));
        Assert.Equal(
            (SecurityEventScopeKindsV1.Platform, SecurityEventActorTypesV1.User, harness.Credential.UserId.ToString("D")),
            (written.ScopeKind, written.ActorType, written.ActorId));
        Assert.Null(written.CompanyId);
        Assert.Null(written.RequestedCompanyId);
        Assert.Null(written.ReasonCode);
        Assert.Equal((Now, "korelasyon-sentetik", SourceChannels.Web), (written.OccurredAtUtc, written.CorrelationId, written.SourceChannel));
        Assert.Empty(SecurityEventV1Validator.Validate(written));
    }

    [Fact]
    public async Task An_unknown_user_is_refused_after_one_dummy_verification_with_credentials_invalid()
    {
        var harness = Harness.WithUser();

        var result = await harness.LoginAsync("baska.kullanici", Password);

        Assert.False(result.Succeeded);
        Assert.Equal((1, 0), (harness.Hasher.DummyVerifications, harness.Hasher.Verifications));
        AssertSingleFailure(harness, SecurityEventReasonCodesV1.CredentialsInvalid);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData(UserName, null)]
    [InlineData(UserName, false)]
    public async Task A_missing_user_name_or_password_is_refused_without_reading_the_store(string? userName, bool? withPassword)
    {
        var harness = Harness.WithUser();
        // true: the generated password; false: an empty one; null: none.
        var password = withPassword switch { true => Password, false => string.Empty, null => null };

        var result = await harness.LoginAsync(userName, password);

        Assert.False(result.Succeeded);
        Assert.Equal((0, 1, 0), (harness.Store.LoginReads, harness.Hasher.DummyVerifications, harness.Hasher.Verifications));
        AssertSingleFailure(harness, SecurityEventReasonCodesV1.CredentialsInvalid);
        Assert.Equal(0, harness.Credential.FailedAccessCount);
    }

    [Fact]
    public async Task A_wrong_password_counts_one_failure_without_raising_the_version()
    {
        var harness = Harness.WithUser();

        var result = await harness.LoginAsync(UserName, WrongPassword);

        Assert.False(result.Succeeded);
        Assert.Equal((1, (DateTimeOffset?)null, 1L), (harness.Credential.FailedAccessCount, harness.Credential.LockoutEndUtc, harness.Credential.Version));
        Assert.Equal((0, 1), (harness.Hasher.DummyVerifications, harness.Hasher.Verifications));
        AssertSingleFailure(harness, SecurityEventReasonCodesV1.CredentialsInvalid);
    }

    [Fact]
    public async Task Reaching_the_configured_threshold_locks_the_account_for_the_configured_duration()
    {
        var harness = Harness.WithUser();

        for (var attempt = 0; attempt < MaxFailedAttempts; attempt++)
        {
            Assert.False((await harness.LoginAsync(UserName, WrongPassword)).Succeeded);
        }

        Assert.Equal((0, (DateTimeOffset?)(Now + LockoutDuration)), (harness.Credential.FailedAccessCount, harness.Credential.LockoutEndUtc));
        Assert.Equal(1L, harness.Credential.Version);
    }

    [Fact]
    public async Task While_locked_even_the_right_password_is_refused_with_policy_refused_and_nothing_is_written()
    {
        var harness = Harness.WithUser();
        harness.LockNow();
        harness.Events.Clear();
        harness.Hasher.Reset();

        var result = await harness.LoginAsync(UserName, Password);

        Assert.False(result.Succeeded);
        Assert.Equal((0, (DateTimeOffset?)(Now + LockoutDuration)), (harness.Credential.FailedAccessCount, harness.Credential.LockoutEndUtc));
        Assert.Equal((0, 1), (harness.Hasher.DummyVerifications, harness.Hasher.Verifications));
        Assert.Equal(0, harness.Store.Writes);
        AssertSingleFailure(harness, SecurityEventReasonCodesV1.PolicyRefused);
    }

    [Fact]
    public async Task A_wrong_password_while_locked_does_not_extend_the_lock()
    {
        var harness = Harness.WithUser();
        harness.LockNow();
        harness.Time.Advance(TimeSpan.FromMinutes(5));

        await harness.LoginAsync(UserName, WrongPassword);

        Assert.Equal((0, (DateTimeOffset?)(Now + LockoutDuration)), (harness.Credential.FailedAccessCount, harness.Credential.LockoutEndUtc));
    }

    [Fact]
    public async Task After_the_lock_ends_the_right_password_succeeds_and_the_lock_is_cleared()
    {
        var harness = Harness.WithUser();
        harness.LockNow();
        harness.Time.Advance(LockoutDuration);

        var result = await harness.LoginAsync(UserName, Password);

        Assert.True(result.Succeeded);
        Assert.Equal((0, (DateTimeOffset?)null), (harness.Credential.FailedAccessCount, harness.Credential.LockoutEndUtc));
    }

    [Fact]
    public async Task A_success_resets_an_earlier_failure_count()
    {
        var harness = Harness.WithUser();
        await harness.LoginAsync(UserName, WrongPassword);

        Assert.True((await harness.LoginAsync(UserName, Password)).Succeeded);

        Assert.Equal(0, harness.Credential.FailedAccessCount);
    }

    [Fact]
    public async Task An_inactive_user_is_refused_with_account_inactive_after_one_verification()
    {
        var harness = Harness.WithUser(userIsActive: false);

        var result = await harness.LoginAsync(UserName, Password);

        Assert.False(result.Succeeded);
        Assert.Equal((0, 1, 0), (harness.Hasher.DummyVerifications, harness.Hasher.Verifications, harness.Store.Writes));
        AssertSingleFailure(harness, SecurityEventReasonCodesV1.AccountInactive);
    }

    [Fact]
    public async Task A_forced_password_change_keeps_the_account_out_until_id2_and_id3_exist()
    {
        var harness = Harness.WithUser(mustChangePassword: true);

        var result = await harness.LoginAsync(UserName, Password);

        Assert.False(result.Succeeded);
        Assert.Equal(0, harness.Store.Writes);
        AssertSingleFailure(harness, SecurityEventReasonCodesV1.PolicyRefused);
    }

    [Fact]
    public async Task A_temporary_password_keeps_the_account_out_until_id2_and_id3_exist()
    {
        var harness = Harness.WithUser(temporaryPasswordIssuedAtUtc: Now.AddHours(-1));

        var result = await harness.LoginAsync(UserName, Password);

        Assert.False(result.Succeeded);
        Assert.Equal(0, harness.Store.Writes);
        AssertSingleFailure(harness, SecurityEventReasonCodesV1.PolicyRefused);
    }

    [Fact]
    public async Task A_wrong_password_on_a_forced_change_account_still_counts_as_a_failure()
    {
        var harness = Harness.WithUser(mustChangePassword: true);

        await harness.LoginAsync(UserName, WrongPassword);

        Assert.Equal(1, harness.Credential.FailedAccessCount);
        AssertSingleFailure(harness, SecurityEventReasonCodesV1.CredentialsInvalid);
    }

    [Fact]
    public async Task A_hash_with_an_older_cost_is_replaced_at_login_without_raising_the_version()
    {
        var harness = Harness.WithUser(storedHash: FakePasswordHasher.HashWithCost(Password, FakePasswordHasher.OlderCost));

        var result = await harness.LoginAsync(UserName, Password);

        Assert.True(result.Succeeded);
        Assert.Equal(FakePasswordHasher.HashWithCost(Password, FakePasswordHasher.CurrentCost), harness.Credential.PasswordHash);
        Assert.Equal((FakePasswordHasher.CurrentFormatVersion, 1L), (harness.Credential.HashFormatVersion, harness.Credential.Version));
    }

    [Fact]
    public async Task A_current_hash_is_not_rewritten_at_login()
    {
        var harness = Harness.WithUser();
        var before = harness.Credential.PasswordHash;

        await harness.LoginAsync(UserName, Password);

        Assert.Equal(before, harness.Credential.PasswordHash);
        Assert.Equal(0, harness.Hasher.Hashes);
    }

    [Fact]
    public async Task A_stamp_renewed_between_the_read_and_the_success_write_refuses_the_login()
    {
        var harness = Harness.WithUser();
        harness.Store.BeforeWrite = credential => harness.Store.ReplaceStamp(Guid.NewGuid());

        var result = await harness.LoginAsync(UserName, Password);

        Assert.False(result.Succeeded);
        AssertSingleFailure(harness, SecurityEventReasonCodesV1.CredentialsInvalid);
    }

    [Fact]
    public async Task A_lock_set_by_a_parallel_attempt_is_not_overwritten_by_a_late_failure()
    {
        var harness = Harness.WithUser();
        var lockedUntil = Now + LockoutDuration;
        harness.Store.BeforeWrite = credential =>
            credential.ApplyFailedAccess(new LocalCredentialFailureTransition(0, lockedUntil, LockedNow: true));

        var result = await harness.LoginAsync(UserName, WrongPassword);

        Assert.False(result.Succeeded);
        Assert.Equal((0, (DateTimeOffset?)lockedUntil), (harness.Credential.FailedAccessCount, harness.Credential.LockoutEndUtc));
    }

    [Fact]
    public async Task Repeating_a_successful_login_writes_the_same_state_and_never_raises_the_version()
    {
        var harness = Harness.WithUser();

        Assert.True((await harness.LoginAsync(UserName, Password)).Succeeded);
        Assert.True((await harness.LoginAsync(UserName, Password)).Succeeded);

        Assert.Equal((0, (DateTimeOffset?)null, 1L), (harness.Credential.FailedAccessCount, harness.Credential.LockoutEndUtc, harness.Credential.Version));
        Assert.Equal(2, harness.Events.Written.Count(written => written.EventType == SecurityEventTypesV1.LoginSucceeded));
    }

    [Fact]
    public async Task Every_path_runs_exactly_one_password_verification()
    {
        var paths = new (Harness Harness, string? UserName, string Password)[]
        {
            (Harness.WithUser(), UserName, Password),
            (Harness.WithUser(), UserName, WrongPassword),
            (Harness.WithUser(), "baska.kullanici", Password),
            (Harness.WithUser(userIsActive: false), UserName, Password),
            (Harness.WithUser(mustChangePassword: true), UserName, Password),
            (Harness.WithUser(storedHash: FakePasswordHasher.HashWithCost(Password, FakePasswordHasher.OlderCost)), UserName, Password),
            (Harness.Locked(), UserName, Password),
        };

        foreach (var (harness, userName, password) in paths)
        {
            harness.Hasher.Reset();
            await harness.LoginAsync(userName, password);
            Assert.Equal(1, harness.Hasher.DummyVerifications + harness.Hasher.Verifications);
        }
    }

    [Fact]
    public async Task No_event_carries_the_user_name_its_normalized_form_or_the_password()
    {
        var harnesses = new[]
        {
            Harness.WithUser(),
            Harness.WithUser(userIsActive: false),
            Harness.Locked(),
        };

        foreach (var harness in harnesses)
        {
            await harness.LoginAsync(UserName, Password);
            await harness.LoginAsync(UserName, WrongPassword);
            await harness.LoginAsync("baska.kullanici", Password);

            foreach (var written in harness.Events.Written)
            {
                var serialized = JsonSerializer.Serialize(written);
                Assert.DoesNotContain(UserName, serialized, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("baska.kullanici", serialized, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(Password, serialized, StringComparison.Ordinal);
                Assert.DoesNotContain(WrongPassword, serialized, StringComparison.Ordinal);
                Assert.Empty(SecurityEventV1Validator.Validate(written));
            }
        }
    }

    [Fact]
    public async Task Failure_events_have_an_anonymous_actor_and_no_company()
    {
        var harness = Harness.WithUser();

        await harness.LoginAsync(UserName, WrongPassword);

        var written = Assert.Single(harness.Events.Written);
        Assert.Equal((SecurityEventActorTypesV1.Anonymous, (string?)null), (written.ActorType, written.ActorId));
        Assert.Equal((SecurityEventScopeKindsV1.Platform, (Guid?)null, (Guid?)null), (written.ScopeKind, written.CompanyId, written.RequestedCompanyId));
    }

    [Fact]
    public async Task A_lost_security_event_does_not_change_the_login_decision()
    {
        var success = Harness.WithUser();
        success.Events.FailWrites = true;
        var failure = Harness.WithUser();
        failure.Events.FailWrites = true;

        Assert.True((await success.LoginAsync(UserName, Password)).Succeeded);
        Assert.False((await failure.LoginAsync(UserName, WrongPassword)).Succeeded);
    }

    private static void AssertSingleFailure(Harness harness, string reasonCode)
    {
        var written = Assert.Single(harness.Events.Written);
        Assert.Equal(
            (SecurityEventTypesV1.LoginFailed, SecurityEventOutcomesV1.Denied, SecurityEventSeveritiesV1.Warning, reasonCode),
            (written.EventType, written.Outcome, written.Severity, written.ReasonCode));
        Assert.Equal((SecurityEventActorTypesV1.Anonymous, (string?)null, (Guid?)null), (written.ActorType, written.ActorId, written.CompanyId));
        Assert.Empty(SecurityEventV1Validator.Validate(written));
    }

    private sealed class Harness
    {
        private Harness(LocalCredential credential, bool userIsActive)
        {
            Credential = credential;
            Store = new InMemoryLocalCredentialStore(credential, userIsActive);
            Service = new LocalAccountLoginService(
                Store,
                Hasher,
                Events,
                new FixedCorrelationContext(),
                Time,
                Options.Create(ValidOptions()));
        }

        public LocalCredential Credential { get; }

        public InMemoryLocalCredentialStore Store { get; }

        public FakePasswordHasher Hasher { get; } = new();

        public RecordingSecurityEventWriter Events { get; } = new();

        public ManualTimeProvider Time { get; } = new(Now);

        private LocalAccountLoginService Service { get; }

        public static Harness WithUser(
            bool userIsActive = true,
            bool mustChangePassword = false,
            DateTimeOffset? temporaryPasswordIssuedAtUtc = null,
            string? storedHash = null)
        {
            var credential = new LocalCredential(
                Guid.NewGuid(),
                UserName,
                storedHash ?? FakePasswordHasher.HashWithCost(Password, FakePasswordHasher.CurrentCost),
                FakePasswordHasher.CurrentFormatVersion,
                mustChangePassword,
                Now.AddDays(-1),
                Guid.NewGuid());
            if (temporaryPasswordIssuedAtUtc is not null)
            {
                typeof(LocalCredential)
                    .GetProperty(nameof(LocalCredential.TemporaryPasswordIssuedAtUtc))!
                    .SetValue(credential, temporaryPasswordIssuedAtUtc);
            }

            return new Harness(credential, userIsActive);
        }

        public static Harness Locked()
        {
            var harness = WithUser();
            harness.LockNow();
            harness.Events.Clear();
            harness.Hasher.Reset();
            return harness;
        }

        public void LockNow() =>
            Credential.ApplyFailedAccess(new LocalCredentialFailureTransition(0, Time.GetUtcNow() + LockoutDuration, LockedNow: true));

        public Task<LocalAccountLoginResult> LoginAsync(string? userName, string? password) =>
            Service.LoginAsync(userName, password, CancellationToken.None);
    }

    internal static LocalAccountOptions ValidOptions() => new()
    {
        Lockout = new LocalAccountLockoutSettings { MaxFailedAttempts = MaxFailedAttempts, Duration = LockoutDuration },
        Session = new LocalAccountSessionSettings { IdleTimeout = TimeSpan.FromHours(1), AbsoluteLifetime = TimeSpan.FromHours(2) },
        PasswordHashing = new LocalAccountPasswordHashingSettings
        {
            Algorithm = LocalAccountPasswordHashingSettings.Pbkdf2Algorithm,
            Pbkdf2Iterations = LocalAccountOptions.MinimumPbkdf2Iterations,
        },
        RateLimit = new LocalAccountRateLimitSettings
        {
            Login = new LocalAccountLoginRateLimitSettings
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                PartitionBy = LocalAccountLoginRateLimitPartition.IpAddressAndUserName,
            },
        },
    };
}

/// <summary>An in-memory login store with the SQL store's conditional write semantics. SENTETIK.</summary>
internal sealed class InMemoryLocalCredentialStore(LocalCredential credential, bool userIsActive) : ILocalCredentialStore
{
    public int LoginReads { get; private set; }

    public int Writes { get; private set; }

    public bool UserIsActive { get; set; } = userIsActive;

    /// <summary>Runs right before a conditional write, to simulate a concurrent change after the read.</summary>
    public Action<LocalCredential>? BeforeWrite { get; set; }

    public Task<LocalCredentialLoginState?> FindForLoginAsync(string normalizedUserName, CancellationToken cancellationToken)
    {
        LoginReads++;
        return Task.FromResult(string.Equals(credential.NormalizedUserName, normalizedUserName, StringComparison.Ordinal)
            ? new LocalCredentialLoginState(
                credential.UserId,
                credential.PasswordHash,
                credential.MustChangePassword,
                credential.FailedAccessCount,
                credential.LockoutEndUtc,
                credential.TemporaryPasswordIssuedAtUtc,
                credential.SecurityStamp,
                UserIsActive)
            : null);
    }

    public Task<bool> TryRecordFailedAccessAsync(
        LocalCredentialLoginState read,
        LocalCredentialLockoutPolicy policy,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        RunBeforeWrite();
        if (!Unchanged(read) || LocalCredentialLockout.IsLockedOut(credential.LockoutEndUtc, nowUtc))
        {
            return Task.FromResult(false);
        }

        Writes++;
        credential.ApplyFailedAccess(LocalCredentialLockout.AfterFailedAccess(
            credential.FailedAccessCount,
            credential.LockoutEndUtc,
            policy,
            nowUtc));
        return Task.FromResult(true);
    }

    public Task<bool> TryRecordSuccessfulAccessAsync(
        LocalCredentialLoginState read,
        string? rehashedPasswordHash,
        int? rehashedFormatVersion,
        CancellationToken cancellationToken)
    {
        RunBeforeWrite();
        if (!Unchanged(read))
        {
            return Task.FromResult(false);
        }

        Writes++;
        credential.ApplySuccessfulAccess(rehashedPasswordHash, rehashedFormatVersion);
        return Task.FromResult(true);
    }

    public Task<LocalCredentialSessionState?> FindForSessionAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(credential.UserId == userId
            ? new LocalCredentialSessionState(
                credential.SecurityStamp,
                UserIsActive,
                credential.MustChangePassword,
                credential.TemporaryPasswordIssuedAtUtc)
            : null);

    public Task<bool> TryRenewSecurityStampAsync(
        Guid userId,
        Guid readSecurityStamp,
        Guid newSecurityStamp,
        CancellationToken cancellationToken)
    {
        RunBeforeWrite();
        if (credential.UserId != userId || credential.SecurityStamp != readSecurityStamp)
        {
            return Task.FromResult(false);
        }

        Writes++;
        credential.RenewSecurityStamp(newSecurityStamp);
        return Task.FromResult(true);
    }

    public void ReplaceStamp(Guid stamp) =>
        typeof(LocalCredential).GetProperty(nameof(LocalCredential.SecurityStamp))!.SetValue(credential, stamp);

    private void RunBeforeWrite()
    {
        var hook = BeforeWrite;
        BeforeWrite = null;
        hook?.Invoke(credential);
    }

    private bool Unchanged(LocalCredentialLoginState read) =>
        credential.UserId == read.UserId
        && string.Equals(credential.PasswordHash, read.PasswordHash, StringComparison.Ordinal)
        && credential.SecurityStamp == read.SecurityStamp
        && credential.TemporaryPasswordIssuedAtUtc == read.TemporaryPasswordIssuedAtUtc;
}

/// <summary>A transparent hasher double; a hash records the password and a cost. SENTETIK, never a real format.</summary>
internal sealed class FakePasswordHasher : ILocalPasswordHasher
{
    public const int CurrentCost = 2;
    public const int OlderCost = 1;
    public const int CurrentFormatVersion = 3;

    public int Verifications { get; private set; }

    public int DummyVerifications { get; private set; }

    public int Hashes { get; private set; }

    public int FormatVersion => CurrentFormatVersion;

    public static string HashWithCost(string password, int cost) => $"sentetik-ozet|{cost}|{password}";

    public string Hash(string password)
    {
        Hashes++;
        return HashWithCost(password, CurrentCost);
    }

    public LocalPasswordVerification Verify(string passwordHash, string password)
    {
        Verifications++;
        if (string.Equals(passwordHash, HashWithCost(password, CurrentCost), StringComparison.Ordinal))
        {
            return LocalPasswordVerification.Success;
        }

        return string.Equals(passwordHash, HashWithCost(password, OlderCost), StringComparison.Ordinal)
            ? LocalPasswordVerification.SuccessRehashNeeded
            : LocalPasswordVerification.Failed;
    }

    public void VerifyAgainstDummy(string password) => DummyVerifications++;

    public void Reset()
    {
        Verifications = 0;
        DummyVerifications = 0;
        Hashes = 0;
    }
}

internal sealed class RecordingSecurityEventWriter : ISecurityEventWriterV1
{
    private readonly List<SecurityEventV1> written = [];

    public bool FailWrites { get; set; }

    public IReadOnlyList<SecurityEventV1> Written => written;

    public Task<SecurityEventWriteResultV1> WriteAsync(SecurityEventV1 securityEvent, CancellationToken cancellationToken = default)
    {
        written.Add(securityEvent);
        var mode = SecurityEventWriteFailureMode.FailOpenWithAlarm;
        return Task.FromResult(FailWrites
            ? SecurityEventWriteResultV1.NotWritten(mode, SecurityEventWriteFailureKindsV1.StoreUnavailable)
            : SecurityEventWriteResultV1.Written(mode));
    }

    public void Clear() => written.Clear();
}

internal sealed class FixedCorrelationContext : ICorrelationContext
{
    public bool IsResolved => true;

    public string CorrelationId => "korelasyon-sentetik";

    public string? CausationId => null;

    public string SourceChannel => SourceChannels.Web;

    public string? ClientIpAddress => null;

    public string? UserAgent => null;

    public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress, string? userAgent) =>
        throw new NotSupportedException();
}

internal sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => utcNow;

    public void Advance(TimeSpan by) => utcNow += by;
}

/// <summary>A random password for one test run; tests never carry a fixed password.</summary>
internal static class SyntheticPasswords
{
    public static string New() => "S-" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
}
