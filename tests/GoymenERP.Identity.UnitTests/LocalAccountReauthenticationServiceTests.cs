using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Credentials;
using GoymenERP.Modules.Identity.Infrastructure.LocalAccount;
using Microsoft.Extensions.Options;

namespace GoymenERP.Identity.UnitTests;

/// <summary>
/// YOL-D27 password re-entry at an AI second approval: the LocalAccount verification with the login KS-8 lockout
/// counter and policy. In-memory stores with the SQL stores' conditional semantics. SENTETIK.
/// </summary>
public sealed class LocalAccountReauthenticationServiceTests
{
    private const string UserName = "sentetik.onayci";
    private static readonly string Password = SyntheticPasswords.New();
    private static readonly string WrongPassword = SyntheticPasswords.New();
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_right_password_verifies_and_resets_the_failure_counter_without_changing_the_stamp()
    {
        var harness = new Harness();
        await harness.VerifyAsync(WrongPassword);
        var stamp = harness.Credential.SecurityStamp;

        var result = await harness.VerifyAsync(Password);

        Assert.Equal(LocalAccountReauthenticationStatusV1.Verified, result);
        Assert.Equal(0, harness.Credential.FailedAccessCount);
        Assert.Equal(stamp, harness.Credential.SecurityStamp);
    }

    [Fact]
    public async Task A_wrong_password_is_refused_and_counts_toward_the_login_lockout()
    {
        var harness = new Harness();

        for (var attempt = 0; attempt < LocalAccountLoginServiceTests.ValidOptions().Lockout.MaxFailedAttempts; attempt++)
        {
            Assert.Equal(LocalAccountReauthenticationStatusV1.Refused, await harness.VerifyAsync(WrongPassword));
        }

        Assert.NotNull(harness.Credential.LockoutEndUtc);
        Assert.True(harness.Credential.LockoutEndUtc > Now);
        // While locked even the right password is refused (KS-8, same as login).
        Assert.Equal(LocalAccountReauthenticationStatusV1.Refused, await harness.VerifyAsync(Password));
    }

    [Fact]
    public async Task An_empty_password_an_unknown_user_or_an_inactive_account_is_refused()
    {
        var harness = new Harness();
        var inactive = new Harness(userIsActive: false);

        Assert.Equal(LocalAccountReauthenticationStatusV1.Refused, await harness.VerifyAsync(null));
        Assert.Equal(LocalAccountReauthenticationStatusV1.Refused, await harness.VerifyAsync(Password, Guid.NewGuid()));
        Assert.Equal(LocalAccountReauthenticationStatusV1.Refused, await inactive.VerifyAsync(Password));
    }

    [Fact]
    public async Task A_must_change_password_account_never_verifies()
    {
        var harness = new Harness(mustChangePassword: true);

        Assert.Equal(LocalAccountReauthenticationStatusV1.Refused, await harness.VerifyAsync(Password));
    }

    private sealed class Harness
    {
        private readonly LocalAccountReauthenticationService service;

        public Harness(bool userIsActive = true, bool mustChangePassword = false)
        {
            Credential = new LocalCredential(
                Guid.NewGuid(),
                UserName,
                FakePasswordHasher.HashWithCost(Password, FakePasswordHasher.CurrentCost),
                FakePasswordHasher.CurrentFormatVersion,
                mustChangePassword,
                Now.AddDays(-1),
                Guid.NewGuid());
            service = new LocalAccountReauthenticationService(
                new InMemoryLocalCredentialCommandStore(Credential, userIsActive),
                new InMemoryLocalCredentialStore(Credential, userIsActive),
                new FakePasswordHasher(),
                new ManualTimeProvider(Now),
                Options.Create(LocalAccountLoginServiceTests.ValidOptions()));
        }

        public LocalCredential Credential { get; }

        public Task<LocalAccountReauthenticationStatusV1> VerifyAsync(string? password, Guid? userId = null) =>
            service.VerifyAsync(userId ?? Credential.UserId, password, CancellationToken.None);
    }
}
