using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Credentials;
using GoymenERP.Modules.Identity.Infrastructure.LocalAccount;

namespace GoymenERP.Identity.UnitTests;

/// <summary>
/// KIM-D30 logout: the security stamp is renewed with one conditional write, so every cookie session of the user
/// fails the per-request stamp check; a stamp that already moved writes nothing. SENTETIK.
/// </summary>
public sealed class LocalAccountLogoutServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Logout_renews_the_stamp_once_raises_the_version_and_ends_the_session_check()
    {
        var (credential, store) = NewCredential();
        var sessionStamp = credential.SecurityStamp;
        var validator = new LocalAccountSessionValidator(store);
        Assert.True(await validator.IsCurrentAsync(credential.UserId, sessionStamp, CancellationToken.None));

        var result = await new LocalAccountLogoutService(store).LogoutAsync(
            credential.UserId,
            sessionStamp,
            CancellationToken.None);

        Assert.Equal(LocalAccountLogoutResult.StampRenewed, result);
        Assert.Equal(1, store.Writes);
        Assert.NotEqual(sessionStamp, credential.SecurityStamp);
        Assert.NotEqual(Guid.Empty, credential.SecurityStamp);
        Assert.Equal(2, credential.Version);
        Assert.False(await validator.IsCurrentAsync(credential.UserId, sessionStamp, CancellationToken.None));
    }

    [Fact]
    public async Task A_second_logout_with_the_old_stamp_writes_nothing()
    {
        var (credential, store) = NewCredential();
        var sessionStamp = credential.SecurityStamp;
        var service = new LocalAccountLogoutService(store);
        await service.LogoutAsync(credential.UserId, sessionStamp, CancellationToken.None);
        var renewed = credential.SecurityStamp;

        var again = await service.LogoutAsync(credential.UserId, sessionStamp, CancellationToken.None);

        Assert.Equal(LocalAccountLogoutResult.AlreadyEnded, again);
        Assert.Equal(1, store.Writes);
        Assert.Equal(renewed, credential.SecurityStamp);
        Assert.Equal(2, credential.Version);
    }

    [Fact]
    public async Task A_stamp_changed_after_the_session_read_is_not_overwritten()
    {
        var (credential, store) = NewCredential();
        var sessionStamp = credential.SecurityStamp;
        var parallelStamp = Guid.NewGuid();
        store.BeforeWrite = _ => store.ReplaceStamp(parallelStamp);

        var result = await new LocalAccountLogoutService(store).LogoutAsync(
            credential.UserId,
            sessionStamp,
            CancellationToken.None);

        Assert.Equal(LocalAccountLogoutResult.AlreadyEnded, result);
        Assert.Equal(0, store.Writes);
        Assert.Equal(parallelStamp, credential.SecurityStamp);
        Assert.Equal(1, credential.Version);
    }

    [Fact]
    public async Task An_empty_user_or_stamp_writes_nothing()
    {
        var (credential, store) = NewCredential();
        var service = new LocalAccountLogoutService(store);

        Assert.Equal(
            LocalAccountLogoutResult.AlreadyEnded,
            await service.LogoutAsync(Guid.Empty, credential.SecurityStamp, CancellationToken.None));
        Assert.Equal(
            LocalAccountLogoutResult.AlreadyEnded,
            await service.LogoutAsync(credential.UserId, Guid.Empty, CancellationToken.None));
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task Another_users_logout_does_not_touch_this_credential()
    {
        var (credential, store) = NewCredential();
        var stamp = credential.SecurityStamp;

        var result = await new LocalAccountLogoutService(store).LogoutAsync(
            Guid.NewGuid(),
            stamp,
            CancellationToken.None);

        Assert.Equal(LocalAccountLogoutResult.AlreadyEnded, result);
        Assert.Equal(stamp, credential.SecurityStamp);
        Assert.Equal(1, credential.Version);
    }

    [Fact]
    public void Renewing_the_stamp_refuses_an_empty_or_unchanged_value()
    {
        var (credential, _) = NewCredential();
        var stamp = credential.SecurityStamp;

        Assert.Throws<ArgumentException>(() => credential.RenewSecurityStamp(Guid.Empty));
        Assert.Throws<ArgumentException>(() => credential.RenewSecurityStamp(stamp));
        Assert.Equal(stamp, credential.SecurityStamp);
        Assert.Equal(1, credential.Version);
    }

    private static (LocalCredential Credential, InMemoryLocalCredentialStore Store) NewCredential()
    {
        var credential = new LocalCredential(
            Guid.NewGuid(),
            "sentetik.cikis",
            FakePasswordHasher.HashWithCost(SyntheticPasswords.New(), FakePasswordHasher.CurrentCost),
            FakePasswordHasher.CurrentFormatVersion,
            mustChangePassword: false,
            Now.AddDays(-1),
            Guid.NewGuid());
        return (credential, new InMemoryLocalCredentialStore(credential, userIsActive: true));
    }
}
