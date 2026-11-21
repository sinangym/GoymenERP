using GoymenERP.Api.Security;
using GoymenERP.Modules.Identity.Domain.Credentials;
using GoymenERP.Modules.Identity.Infrastructure.LocalAccount;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class IdentityLocalCredentialSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Recovery_code_and_temporary_password_consumption_commit_or_rollback_together_on_real_SQL()
    {
        await WithDatabaseAsync(async factory =>
        {
            var userId = await SeedAsync(factory, "synthetic.twofactor");
            var now = new DateTimeOffset(2026, 11, 17, 1, 0, 0, TimeSpan.Zero);
            var protection = new LocalTwoFactorProtectionV1(new EphemeralDataProtectionProvider());
            var rawSecret = LocalTotpV1.NewSecret(); var codes = LocalTotpV1.NewRecoveryCodes();
            await using (var setup = factory.Services.CreateAsyncScope())
            {
                var db = setup.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var credential = await db.LocalCredentials.SingleAsync(row => row.UserId == userId);
                credential.ChangeTwoFactor(true, Guid.NewGuid());
                credential.IssueTemporaryPassword(OpaqueHash(), 3, now.AddMinutes(-1), now.AddMinutes(15), Guid.NewGuid());
                db.LocalTwoFactorSecrets.Add(new(userId, protection.Protect(userId, "authenticator", rawSecret), codes.Select(LocalTotpV1.RecoveryHash).ToArray(), 0));
                await db.SaveChangesAsync();
            }
            await using (var rejected = factory.Services.CreateAsyncScope())
            {
                var db = rejected.ServiceProvider.GetRequiredService<IdentityDbContext>(); var store = new SqlLocalCredentialStore(db);
                var read = await store.FindForLoginAsync(LocalUserName.TryNormalize("synthetic.twofactor")!, CancellationToken.None);
                var participant = new SqlLocalTwoFactorLoginV1(db, protection, new TwoFactorClock(now));
                Assert.False(await participant.TryCompleteAsync(read!, codes[0], () => Task.FromResult(false), CancellationToken.None));
            }
            await using (var unchanged = factory.Services.CreateAsyncScope())
            {
                var db = unchanged.ServiceProvider.GetRequiredService<IdentityDbContext>();
                Assert.Equal(10, (await db.LocalTwoFactorSecrets.AsNoTracking().SingleAsync(row => row.UserId == userId)).RecoveryCodeCount);
                Assert.Null((await db.LocalCredentials.AsNoTracking().SingleAsync(row => row.UserId == userId)).TemporaryPasswordConsumedAtUtc);
            }
            await using (var accepted = factory.Services.CreateAsyncScope())
            {
                var db = accepted.ServiceProvider.GetRequiredService<IdentityDbContext>(); var store = new SqlLocalCredentialStore(db);
                var read = await store.FindForLoginAsync(LocalUserName.TryNormalize("synthetic.twofactor")!, CancellationToken.None);
                var participant = new SqlLocalTwoFactorLoginV1(db, protection, new TwoFactorClock(now));
                Assert.True(await participant.TryCompleteAsync(read!, codes[0], () => store.TryConsumeTemporaryPasswordAsync(read!, null, null, now, CancellationToken.None), CancellationToken.None));
            }
            await using var verify = factory.Services.CreateAsyncScope();
            var finalDb = verify.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var factor = await finalDb.LocalTwoFactorSecrets.AsNoTracking().SingleAsync(row => row.UserId == userId);
            Assert.Equal(9, factor.RecoveryCodeCount);
            Assert.DoesNotContain(rawSecret, factor.ProtectedSecret);
            Assert.Equal(rawSecret, protection.Unprotect(userId, "authenticator", factor.ProtectedSecret));
            foreach (var code in codes) Assert.DoesNotContain(code, factor.RecoveryHashesJson);
            Assert.Equal(now, (await finalDb.LocalCredentials.AsNoTracking().SingleAsync(row => row.UserId == userId)).TemporaryPasswordConsumedAtUtc);
        });
    }
    private sealed class TwoFactorClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
}
