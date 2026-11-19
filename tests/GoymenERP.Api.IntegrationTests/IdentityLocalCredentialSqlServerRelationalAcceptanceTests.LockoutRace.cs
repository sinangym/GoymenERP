using System.Text.Json;
using GoymenERP.Modules.Identity.Domain.Credentials;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class IdentityLocalCredentialSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task A_lock_committed_after_the_login_read_survives_normal_and_temporary_success_writes_on_real_SQL()
    {
        await WithDatabaseAsync(async factory =>
        {
            var now = new DateTimeOffset(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);
            foreach (var temporary in new[] { false, true })
            {
                var name = temporary ? "sentetik.kilit.gecici" : "sentetik.kilit.normal";
                var userId = await SeedAsync(factory, name);
                if (temporary)
                {
                    await using var setup = factory.Services.CreateAsyncScope();
                    var database = setup.ServiceProvider.GetRequiredService<IdentityDbContext>();
                    var credential = await database.LocalCredentials.SingleAsync(x => x.UserId == userId);
                    credential.IssueTemporaryPassword(OpaqueHash(), 3, now.AddMinutes(-1), now.AddHours(1), Guid.NewGuid());
                    await database.SaveChangesAsync();
                }
                var stale = await ReadAsync(factory, name);
                // The successful request has already read. A separate real SQL writer reaches the threshold first.
                Assert.True(await InStoreAsync(factory, store => store.TryRecordFailedAccessAsync(stale,
                    new LocalCredentialLockoutPolicy(1, Policy.Duration), now, default)));
                var locked = await ReadAsync(factory, name);
                Assert.Equal(now + Policy.Duration, locked.LockoutEndUtc);
                var snapshot = await Snapshot();
                foreach (var rehash in new[] { false, true })
                {
                    var hash = rehash ? OpaqueHash() : null;
                    int? format = rehash ? 4 : null;
                    var accepted = await InStoreAsync(factory, store => temporary
                        ? store.TryConsumeTemporaryPasswordAsync(stale, hash, format, now, default)
                        : store.TryRecordSuccessfulAccessAsync(stale, hash, format, default));
                    Assert.False(accepted);
                    Assert.Equal(snapshot, await Snapshot());
                }

                // A fresh read after expiration still supports the established successful-login reset.
                Assert.True(await InStoreAsync(factory, store => store.TryRecordFailedAccessAsync(locked,
                    Policy, now + Policy.Duration, default)));
                var fresh = await ReadAsync(factory, name);
                Assert.True(await InStoreAsync(factory, store => temporary
                    ? store.TryConsumeTemporaryPasswordAsync(fresh, null, null, now + Policy.Duration, default)
                    : store.TryRecordSuccessfulAccessAsync(fresh, null, null, default)));
                Assert.Equal((0, (DateTimeOffset?)null, temporary ? 2L : 1L), await StateAsync(factory, userId));

                async Task<string> Snapshot()
                {
                    await using var check = factory.Services.CreateAsyncScope();
                    var row = await check.ServiceProvider.GetRequiredService<IdentityDbContext>().LocalCredentials
                        .AsNoTracking().SingleAsync(x => x.UserId == userId);
                    return JsonSerializer.Serialize(row);
                }
            }
        });
    }
}
