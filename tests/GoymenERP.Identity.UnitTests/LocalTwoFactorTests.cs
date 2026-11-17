using System.Text;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Credentials;
using GoymenERP.Modules.Identity.Infrastructure.LocalAccount;
using Microsoft.Extensions.Options;

namespace GoymenERP.Identity.UnitTests;

public sealed class LocalTwoFactorTests
{
    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void The_six_digit_protocol_matches_the_RFC_6238_SHA1_vectors(long unixSeconds, string expected)
    {
        Assert.Equal(expected, LocalTotpV1.AtStep(Encoding.ASCII.GetBytes("12345678901234567890"), unixSeconds / 30));
    }

    [Fact]
    public void A_totp_step_and_each_recovery_code_can_only_be_consumed_once()
    {
        var codes = LocalTotpV1.NewRecoveryCodes();
        var row = new LocalTwoFactorSecret(Guid.NewGuid(), "SYNTHETIC-PROTECTED", codes.Select(LocalTotpV1.RecoveryHash).ToArray(), 1);
        Assert.Equal(10, codes.Distinct().Count());
        Assert.True(row.ConsumeRecoveryCode(codes[0]));
        Assert.False(row.ConsumeRecoveryCode(codes[0]));
        Assert.Equal(9, row.RecoveryCodeCount);
        row.AcceptStep(2);
        Assert.Throws<InvalidOperationException>(() => row.AcceptStep(2));
        row.Deactivate();
        Assert.False(row.ConsumeRecoveryCode(codes[1]));
        foreach (var code in codes) Assert.DoesNotContain(code, row.RecoveryHashesJson);
    }

    [Fact]
    public async Task A_correct_password_with_a_bad_factor_counts_one_failure_and_never_consumes_the_temporary_password()
    {
        var now = new DateTimeOffset(2026, 11, 17, 1, 0, 0, TimeSpan.Zero);
        var password = SyntheticPasswords.New(); var hasher = new FakePasswordHasher();
        var credential = new LocalCredential(Guid.NewGuid(), "synthetic.mfa", hasher.Hash(password), hasher.FormatVersion, false, now, Guid.NewGuid());
        credential.ChangeTwoFactor(true, Guid.NewGuid());
        credential.IssueTemporaryPassword(hasher.Hash(password), hasher.FormatVersion, now.AddMinutes(-1), now.AddMinutes(15), Guid.NewGuid());
        var version = credential.Version;
        var store = new InMemoryLocalCredentialStore(credential, true);
        var factor = new RejectFactor();
        var service = new LocalAccountLoginService(store, hasher, new RecordingSecurityEventWriter(), new FixedCorrelationContext(),
            new ManualTimeProvider(now), Options.Create(LocalAccountLoginServiceTests.ValidOptions()), factor);
        Assert.False((await service.LoginAsync(credential.UserName, password, "000000", CancellationToken.None)).Succeeded);
        Assert.Equal(1, credential.FailedAccessCount);
        Assert.Null(credential.TemporaryPasswordConsumedAtUtc);
        Assert.Equal(version, credential.Version);
        Assert.False(factor.CredentialWriteCalled);
    }

    [Fact]
    public void Protected_and_plaintext_credential_request_debug_text_is_redacted()
    {
        Assert.Equal(nameof(LocalTwoFactorProofV1), new LocalTwoFactorProofV1("1", "password", "123456", "token").ToString());
        Assert.Equal(nameof(LocalTwoFactorEnrollmentV1), new LocalTwoFactorEnrollmentV1("1", "seed", "uri", ["code"], "token").ToString());
    }

    private sealed class RejectFactor : ILocalTwoFactorLoginV1
    {
        public bool CredentialWriteCalled { get; private set; }
        public Task<bool> TryCompleteAsync(LocalCredentialLoginState state, string? code, Func<Task<bool>> completeCredential, CancellationToken cancellationToken)
        {
            CredentialWriteCalled = false;
            return Task.FromResult(false);
        }
    }
}
