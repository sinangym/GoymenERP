using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Credentials;
using GoymenERP.Modules.Identity.Infrastructure.LocalAccount;
using Microsoft.Extensions.Options;

namespace GoymenERP.Identity.UnitTests;

/// <summary>
/// ID-2 password change without a database: the order guarded read, replay, expected version, current password,
/// policy, one conditional write; every refusal writes nothing and leaves the lockout counter alone. What SQL
/// Server enforces (conditional UPDATE, unique key) is proven by the relational acceptance tests. SENTETIK.
/// </summary>
public sealed class LocalAccountPasswordChangeServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Changing_the_password_clears_the_forced_change_and_the_lockout_and_renews_the_stamp()
    {
        var credential = NewCredential(mustChangePassword: true);
        credential.ApplyFailedAccess(new LocalCredentialFailureTransition(3, Now.AddMinutes(5), LockedNow: true));
        var oldStamp = credential.SecurityStamp;
        var newStamp = Guid.NewGuid();

        credential.ChangePassword("sentetik-yeni-ozet", FakePasswordHasher.CurrentFormatVersion, Now, newStamp);

        Assert.Equal(
            ("sentetik-yeni-ozet", false, Now, 0, (DateTimeOffset?)null, newStamp, 2L),
            (credential.PasswordHash, credential.MustChangePassword, credential.PasswordChangedAtUtc,
                credential.FailedAccessCount, credential.LockoutEndUtc, credential.SecurityStamp, credential.Version));
        Assert.NotEqual(oldStamp, credential.SecurityStamp);
        Assert.Null(credential.TemporaryPasswordIssuedAtUtc);
    }

    [Fact]
    public void Changing_the_password_refuses_an_empty_hash_or_an_unchanged_stamp()
    {
        var credential = NewCredential(mustChangePassword: true);

        Assert.Throws<ArgumentException>(() => credential.ChangePassword(" ", 1, Now, Guid.NewGuid()));
        Assert.Throws<ArgumentOutOfRangeException>(() => credential.ChangePassword("ozet", 0, Now, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => credential.ChangePassword("ozet", 1, Now, credential.SecurityStamp));
        Assert.Throws<ArgumentException>(() => credential.ChangePassword("ozet", 1, Now, Guid.Empty));
        Assert.Equal((1L, true), (credential.Version, credential.MustChangePassword));
    }

    [Fact]
    public async Task A_valid_change_writes_once_and_returns_the_new_stamp_and_version()
    {
        var harness = Harness.Create(mustChangePassword: true);
        var newPassword = SyntheticPasswords.New();

        var result = await harness.ChangeAsync(newPassword: newPassword);

        Assert.Equal(LocalAccountPasswordChangeStatus.Changed, result.Status);
        Assert.Equal(2L, result.CurrentVersion);
        Assert.Equal(harness.Credential.SecurityStamp, result.NewSecurityStamp);
        Assert.False(harness.Credential.MustChangePassword);
        Assert.Equal(FakePasswordHasher.HashWithCost(newPassword, FakePasswordHasher.CurrentCost), harness.Credential.PasswordHash);
        var record = Assert.Single(harness.Store.Records);
        Assert.Equal(
            (LocalCredentialCommand.PasswordChangeOperation, Harness.Key, 2L, Now),
            (record.Operation, record.IdempotencyKey, record.ResultVersion, record.CompletedAtUtc));
        Assert.Equal(1, harness.Store.Writes);
    }

    [Fact]
    public async Task The_same_key_and_request_from_the_reissued_session_replays_without_a_second_write()
    {
        var harness = Harness.Create(mustChangePassword: true);
        var first = await harness.ChangeAsync();

        var replay = await harness.ChangeAsync(sessionStamp: first.NewSecurityStamp, newPassword: SyntheticPasswords.New());

        Assert.Equal(LocalAccountPasswordChangeStatus.Replayed, replay.Status);
        Assert.Equal(2L, replay.CurrentVersion);
        Assert.Null(replay.NewSecurityStamp);
        Assert.Equal(1, harness.Store.Writes);
        Assert.Equal(2L, harness.Credential.Version);
    }

    [Fact]
    public async Task The_same_key_with_another_expected_version_is_an_idempotency_conflict()
    {
        var harness = Harness.Create(mustChangePassword: true);
        var first = await harness.ChangeAsync();

        var conflict = await harness.ChangeAsync(sessionStamp: first.NewSecurityStamp, expectedVersion: 2);

        Assert.Equal(LocalAccountPasswordChangeStatus.IdempotencyConflict, conflict.Status);
        Assert.Equal(1, harness.Store.Writes);
    }

    [Fact]
    public async Task The_old_session_cannot_replay_after_the_change_renewed_the_stamp()
    {
        var harness = Harness.Create(mustChangePassword: true);
        await harness.ChangeAsync();

        var stale = await harness.ChangeAsync();

        Assert.Equal(LocalAccountPasswordChangeStatus.SessionNotCurrent, stale.Status);
        Assert.Equal(1, harness.Store.Writes);
    }

    [Fact]
    public async Task A_stale_expected_version_is_a_conflict_with_the_stored_version()
    {
        var harness = Harness.Create();

        var result = await harness.ChangeAsync(expectedVersion: 7);

        Assert.Equal(LocalAccountPasswordChangeStatus.VersionConflict, result.Status);
        Assert.Equal(1L, result.CurrentVersion);
        Assert.Equal(0, harness.Store.Writes);
        Assert.Equal(0, harness.Hasher.Verifications);
    }

    [Fact]
    public async Task A_wrong_current_password_is_refused_without_counting_a_failed_attempt()
    {
        var harness = Harness.Create(mustChangePassword: true);

        var result = await harness.ChangeAsync(currentPassword: SyntheticPasswords.New());

        Assert.Equal(LocalAccountPasswordChangeStatus.CurrentPasswordInvalid, result.Status);
        Assert.Equal(0, harness.Store.Writes);
        Assert.Equal(0, harness.Credential.FailedAccessCount);
        Assert.Equal(0, harness.Hasher.Hashes);
        Assert.Empty(harness.Store.Records);
    }

    [Fact]
    public async Task A_new_password_against_the_policy_reports_the_rule_codes_and_writes_nothing()
    {
        var harness = Harness.Create(mustChangePassword: true);
        var digits = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100_000, 1_000_000)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);

        var shortResult = await harness.ChangeAsync(newPassword: SyntheticPasswords.New()[..5]);
        var userNameResult = await harness.ChangeAsync(newPassword: Harness.UserName + digits);
        var companyResult = await harness.ChangeAsync(newPassword: "Sentetik-Holding-" + digits);

        Assert.Equal(
            (LocalAccountPasswordChangeStatus.PolicyViolation, LocalPasswordPolicyViolationCodes.TooShort),
            (shortResult.Status, Assert.Single(shortResult.PolicyViolations)));
        Assert.Equal(
            (LocalAccountPasswordChangeStatus.PolicyViolation, LocalPasswordPolicyViolationCodes.Blocklisted),
            (userNameResult.Status, Assert.Single(userNameResult.PolicyViolations)));
        Assert.Equal(
            (LocalAccountPasswordChangeStatus.PolicyViolation, LocalPasswordPolicyViolationCodes.Blocklisted),
            (companyResult.Status, Assert.Single(companyResult.PolicyViolations)));
        Assert.Equal(0, harness.Store.Writes);
        Assert.True(harness.Credential.MustChangePassword);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task An_inactive_user_or_another_stamp_ends_the_session_without_a_write(bool userIsActive, bool otherStamp)
    {
        var harness = Harness.Create(userIsActive: userIsActive);

        var result = await harness.ChangeAsync(sessionStamp: otherStamp ? Guid.NewGuid() : null);

        Assert.Equal(LocalAccountPasswordChangeStatus.SessionNotCurrent, result.Status);
        Assert.Equal(0, harness.Store.Writes);
    }

    [Fact]
    public async Task An_outstanding_temporary_password_is_not_changed_by_this_flow()
    {
        var harness = Harness.Create(mustChangePassword: true);
        typeof(LocalCredential)
            .GetProperty(nameof(LocalCredential.TemporaryPasswordIssuedAtUtc))!
            .SetValue(harness.Credential, Now.AddHours(-1));

        var result = await harness.ChangeAsync();

        Assert.Equal(LocalAccountPasswordChangeStatus.SessionNotCurrent, result.Status);
        Assert.Equal(0, harness.Store.Writes);
    }

    [Fact]
    public async Task A_temporary_password_consumed_by_a_login_can_be_replaced_and_its_fields_are_cleared()
    {
        var harness = Harness.Create(mustChangePassword: true);
        harness.Credential.IssueTemporaryPassword(
            harness.Credential.PasswordHash,
            FakePasswordHasher.CurrentFormatVersion,
            Now.AddHours(-2),
            Now.AddHours(70),
            Guid.NewGuid());
        typeof(LocalCredential)
            .GetProperty(nameof(LocalCredential.TemporaryPasswordConsumedAtUtc))!
            .SetValue(harness.Credential, Now.AddHours(-1));

        var result = await harness.ChangeAsync();

        // KIM-D23: after the one login the fenced session sets the founder's own password; the temporary fields go.
        Assert.Equal(LocalAccountPasswordChangeStatus.Changed, result.Status);
        Assert.Equal(1, harness.Store.Writes);
        Assert.Equal(
            ((DateTimeOffset?)null, (DateTimeOffset?)null, (DateTimeOffset?)null, false),
            (harness.Credential.TemporaryPasswordIssuedAtUtc, harness.Credential.TemporaryPasswordExpiresAtUtc,
                harness.Credential.TemporaryPasswordConsumedAtUtc, harness.Credential.MustChangePassword));
    }

    [Fact]
    public async Task A_row_that_moved_before_the_write_is_a_version_conflict_and_nothing_is_written()
    {
        var harness = Harness.Create(mustChangePassword: true);
        harness.Store.BeforeWrite = credential =>
            typeof(LocalCredential).GetProperty(nameof(LocalCredential.Version))!.SetValue(credential, 5L);

        var result = await harness.ChangeAsync();

        Assert.Equal(LocalAccountPasswordChangeStatus.VersionConflict, result.Status);
        Assert.Equal(5L, result.CurrentVersion);
        Assert.Empty(harness.Store.Records);
        Assert.True(harness.Credential.MustChangePassword);
    }

    [Fact]
    public async Task A_concurrent_winner_with_the_same_request_ends_this_session_and_another_request_conflicts()
    {
        var sameRequest = Harness.Create(mustChangePassword: true);
        sameRequest.Store.TakeKeyBeforeWrite = fingerprint => fingerprint;
        var anotherRequest = Harness.Create(mustChangePassword: true);
        anotherRequest.Store.TakeKeyBeforeWrite = _ => new byte[LocalCredentialCommand.FingerprintLength];

        var same = await sameRequest.ChangeAsync();
        var another = await anotherRequest.ChangeAsync();

        Assert.Equal(LocalAccountPasswordChangeStatus.SessionNotCurrent, same.Status);
        Assert.Equal(LocalAccountPasswordChangeStatus.IdempotencyConflict, another.Status);
        Assert.True(sameRequest.Credential.MustChangePassword);
        Assert.True(anotherRequest.Credential.MustChangePassword);
    }

    [Fact]
    public void The_request_fingerprint_binds_the_key_the_user_and_the_expected_version()
    {
        var userId = Guid.NewGuid();
        var fingerprint = LocalAccountPasswordChangeService.ComputeFingerprint("anahtar-1", userId, 1);

        Assert.Equal(LocalCredentialCommand.FingerprintLength, fingerprint.Length);
        Assert.Equal(fingerprint, LocalAccountPasswordChangeService.ComputeFingerprint("anahtar-1", userId, 1));
        Assert.NotEqual(fingerprint, LocalAccountPasswordChangeService.ComputeFingerprint("anahtar-2", userId, 1));
        Assert.NotEqual(fingerprint, LocalAccountPasswordChangeService.ComputeFingerprint("anahtar-1", Guid.NewGuid(), 1));
        Assert.NotEqual(fingerprint, LocalAccountPasswordChangeService.ComputeFingerprint("anahtar-1", userId, 2));
    }

    private static LocalCredential NewCredential(bool mustChangePassword) =>
        new(
            Guid.NewGuid(),
            Harness.UserName,
            FakePasswordHasher.HashWithCost(Harness.Password, FakePasswordHasher.CurrentCost),
            FakePasswordHasher.CurrentFormatVersion,
            mustChangePassword,
            Now.AddDays(-1),
            Guid.NewGuid());

    private sealed class Harness
    {
        public const string Key = "sifre-degisimi-anahtar-1";
        public const string UserName = "kurucukullanici";
        public static readonly string Password = SyntheticPasswords.New();

        private readonly LocalAccountPasswordChangeService service;

        private Harness(LocalCredential credential, bool userIsActive)
        {
            Credential = credential;
            InitialStamp = credential.SecurityStamp;
            Store = new InMemoryLocalCredentialCommandStore(credential, userIsActive);
            var options = LocalAccountLoginServiceTests.ValidOptions();
            service = new LocalAccountPasswordChangeService(
                Store,
                Hasher,
                new FixedContextTermSource(["Sentetik Holding"]),
                new ManualTimeProvider(Now),
                Options.Create(options));
        }

        public LocalCredential Credential { get; }

        public InMemoryLocalCredentialCommandStore Store { get; }

        public FakePasswordHasher Hasher { get; } = new();

        public static Harness Create(bool mustChangePassword = false, bool userIsActive = true) =>
            new(NewCredential(mustChangePassword), userIsActive);

        public Task<LocalAccountPasswordChangeResult> ChangeAsync(
            Guid? sessionStamp = null,
            long expectedVersion = 1,
            string? currentPassword = null,
            string? newPassword = null) =>
            service.ChangeAsync(
                new LocalAccountPasswordChangeCommand(
                    Credential.UserId,
                    sessionStamp ?? InitialStamp,
                    Key,
                    expectedVersion,
                    currentPassword ?? Password,
                    newPassword ?? SyntheticPasswords.New()),
                CancellationToken.None);

        private Guid InitialStamp { get; }
    }

    private sealed class FixedContextTermSource(IReadOnlyList<string> terms) : ILocalPasswordContextTermSource
    {
        public Task<IReadOnlyList<string>> GetCompanyTermsAsync(CancellationToken cancellationToken) => Task.FromResult(terms);
    }
}

/// <summary>A credential command store over one in-memory credential; mirrors the SQL store's conditional write.</summary>
internal sealed class InMemoryLocalCredentialCommandStore(LocalCredential credential, bool userIsActive) : ILocalCredentialCommandStore
{
    private readonly List<LocalCredentialCommand> records = [];

    public IReadOnlyList<LocalCredentialCommand> Records => records;

    public int Writes { get; private set; }

    /// <summary>Moves the stored row between the read and the write (a concurrent command).</summary>
    public Action<LocalCredential>? BeforeWrite { get; set; }

    /// <summary>Lets a concurrent request take the key first; returns that request's fingerprint.</summary>
    public Func<byte[], byte[]>? TakeKeyBeforeWrite { get; set; }

    public Task<LocalCredentialCommandState?> FindForCommandAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<LocalCredentialCommandState?>(userId == credential.UserId
            ? new LocalCredentialCommandState(
                credential.UserId,
                credential.UserName,
                credential.PasswordHash,
                credential.SecurityStamp,
                credential.Version,
                userIsActive,
                credential.TemporaryPasswordIssuedAtUtc,
                credential.TemporaryPasswordConsumedAtUtc)
            : null);

    public Task<LocalCredentialCommandRecord?> FindCommandAsync(
        Guid userId,
        string operation,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var record = records.SingleOrDefault(candidate =>
            candidate.UserId == userId
            && string.Equals(candidate.Operation, operation, StringComparison.Ordinal)
            && string.Equals(candidate.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
        return Task.FromResult(record is null ? null : new LocalCredentialCommandRecord(record.RequestFingerprint, record.ResultVersion));
    }

    public Task<bool> TryChangePasswordAsync(
        LocalCredentialCommandState read,
        string newPasswordHash,
        int hashFormatVersion,
        Guid newSecurityStamp,
        DateTimeOffset changedAtUtc,
        LocalCredentialCommand record,
        CancellationToken cancellationToken)
    {
        BeforeWrite?.Invoke(credential);
        if (TakeKeyBeforeWrite is { } take)
        {
            records.Add(new LocalCredentialCommand(
                Guid.NewGuid(),
                record.UserId,
                record.Operation,
                record.IdempotencyKey,
                take(record.RequestFingerprint),
                record.ResultVersion,
                changedAtUtc));
            throw new LocalCredentialCommandKeyTakenException(new InvalidOperationException("sentetik eşzamanlı anahtar"));
        }

        if (credential.Version != read.Version
            || credential.SecurityStamp != read.SecurityStamp
            || !string.Equals(credential.PasswordHash, read.PasswordHash, StringComparison.Ordinal))
        {
            return Task.FromResult(false);
        }

        credential.ChangePassword(newPasswordHash, hashFormatVersion, changedAtUtc, newSecurityStamp);
        records.Add(record);
        Writes++;
        return Task.FromResult(true);
    }
}
