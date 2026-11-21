using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Credentials;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.LocalAccount;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// ID-1 <c>identity.LocalCredentials</c> on a real SQL Server: the conditional login writes are SQL translations
/// (one <c>UPDATE</c> each, evaluated on the stored row), the normalized user name is unique under its binary
/// collation and the forward-fix <c>Down</c> refuses while a credential exists. Hashes are opaque SENTETIK strings;
/// the store never verifies them.
/// </summary>
public sealed partial class IdentityLocalCredentialSqlServerRelationalAcceptanceTests
{
    private static readonly LocalCredentialLockoutPolicy Policy = new(3, TimeSpan.FromMinutes(15));

    [SqlServerFact]
    public async Task Failed_logins_count_and_lock_on_the_stored_row_without_changing_the_version_on_real_SQL()
    {
        await WithDatabaseAsync(async factory =>
        {
            var userId = await SeedAsync(factory, "sentetik.kilit");
            var now = new DateTimeOffset(2026, 11, 14, 9, 0, 0, TimeSpan.Zero);

            // Two parallel failures read the same row; SQL Server counts both because each SET reads the stored value.
            var read = await ReadAsync(factory, "sentetik.kilit");
            var parallel = await Task.WhenAll(
                InStoreAsync(factory, store => store.TryRecordFailedAccessAsync(read, Policy, now, CancellationToken.None)),
                InStoreAsync(factory, store => store.TryRecordFailedAccessAsync(read, Policy, now, CancellationToken.None)));
            Assert.Equal([true, true], parallel);
            Assert.Equal((2, (DateTimeOffset?)null, 1L), await StateAsync(factory, userId));

            Assert.True(await InStoreAsync(factory, store =>
                store.TryRecordFailedAccessAsync(read, Policy, now, CancellationToken.None)));
            Assert.Equal((0, (DateTimeOffset?)(now + Policy.Duration), 1L), await StateAsync(factory, userId));

            // While the lock is active a failure writes nothing; the lock neither grows nor restarts.
            Assert.False(await InStoreAsync(factory, store =>
                store.TryRecordFailedAccessAsync(read, Policy, now.AddMinutes(5), CancellationToken.None)));
            Assert.Equal((0, (DateTimeOffset?)(now + Policy.Duration), 1L), await StateAsync(factory, userId));

            // Once it has ended the next failure counts again from zero.
            var later = now + Policy.Duration;
            Assert.True(await InStoreAsync(factory, store =>
                store.TryRecordFailedAccessAsync(read, Policy, later, CancellationToken.None)));
            Assert.Equal((1, (DateTimeOffset?)(now + Policy.Duration), 1L), await StateAsync(factory, userId));
        });
    }

    [SqlServerFact]
    public async Task A_successful_login_clears_the_counter_and_stores_a_rehash_without_changing_the_version_on_real_SQL()
    {
        await WithDatabaseAsync(async factory =>
        {
            var userId = await SeedAsync(factory, "sentetik.basari");
            var now = new DateTimeOffset(2026, 11, 14, 9, 0, 0, TimeSpan.Zero);
            var read = await ReadAsync(factory, "sentetik.basari");
            Assert.True(await InStoreAsync(factory, store =>
                store.TryRecordFailedAccessAsync(read, Policy, now, CancellationToken.None)));

            var rehash = OpaqueHash();
            Assert.True(await InStoreAsync(factory, store =>
                store.TryRecordSuccessfulAccessAsync(read, rehash, 4, CancellationToken.None)));

            Assert.Equal((0, (DateTimeOffset?)null, 1L), await StateAsync(factory, userId));
            await using var scope = factory.Services.CreateAsyncScope();
            var stored = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().LocalCredentials
                .AsNoTracking().SingleAsync(credential => credential.UserId == userId);
            Assert.Equal(rehash, stored.PasswordHash);
            Assert.Equal(4, stored.HashFormatVersion);
            Assert.Equal(read.SecurityStamp, stored.SecurityStamp);
        });
    }

    [SqlServerFact]
    public async Task A_login_write_from_a_stale_read_affects_no_row_on_real_SQL()
    {
        await WithDatabaseAsync(async factory =>
        {
            var userId = await SeedAsync(factory, "sentetik.eski");
            var now = new DateTimeOffset(2026, 11, 14, 9, 0, 0, TimeSpan.Zero);
            var read = await ReadAsync(factory, "sentetik.eski");

            // A concurrent stamp renewal (a password change or a forced sign-out) lands between read and write.
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE [identity].[LocalCredentials] SET [SecurityStamp] = {Guid.NewGuid()} WHERE [UserId] = {userId};");
            }

            Assert.False(await InStoreAsync(factory, store =>
                store.TryRecordFailedAccessAsync(read, Policy, now, CancellationToken.None)));
            Assert.False(await InStoreAsync(factory, store =>
                store.TryRecordSuccessfulAccessAsync(read, OpaqueHash(), 4, CancellationToken.None)));
            Assert.Equal((0, (DateTimeOffset?)null, 1L), await StateAsync(factory, userId));
        });
    }

    [SqlServerFact]
    public async Task Logout_renews_the_stamp_and_raises_the_version_only_while_the_read_stamp_is_current_on_real_SQL()
    {
        await WithDatabaseAsync(async factory =>
        {
            var userId = await SeedAsync(factory, "sentetik.cikis");
            var sessionStamp = (await ReadAsync(factory, "sentetik.cikis")).SecurityStamp;
            var renewed = Guid.NewGuid();

            // KIM-D30: one conditional UPDATE on the stored row; the old stamp no longer passes the session check.
            Assert.True(await InStoreAsync(factory, store =>
                store.TryRenewSecurityStampAsync(userId, sessionStamp, renewed, CancellationToken.None)));
            Assert.Equal(renewed, (await ReadAsync(factory, "sentetik.cikis")).SecurityStamp);
            Assert.Equal((0, (DateTimeOffset?)null, 2L), await StateAsync(factory, userId));
            Assert.False(await InStoreAsync(factory, store =>
                new LocalAccountSessionValidator(store).IsCurrentAsync(userId, sessionStamp, CancellationToken.None)));

            // A second logout from the old session, or a renewal for another user, affects no row.
            Assert.False(await InStoreAsync(factory, store =>
                store.TryRenewSecurityStampAsync(userId, sessionStamp, Guid.NewGuid(), CancellationToken.None)));
            Assert.False(await InStoreAsync(factory, store =>
                store.TryRenewSecurityStampAsync(Guid.NewGuid(), renewed, Guid.NewGuid(), CancellationToken.None)));
            Assert.Equal(renewed, (await ReadAsync(factory, "sentetik.cikis")).SecurityStamp);
            Assert.Equal((0, (DateTimeOffset?)null, 2L), await StateAsync(factory, userId));
        });
    }

    [SqlServerFact]
    public async Task The_normalized_user_name_is_unique_and_a_credential_needs_an_existing_user_on_real_SQL()
    {
        await WithDatabaseAsync(async factory =>
        {
            await SeedAsync(factory, "sentetik.tekil");

            var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => SeedAsync(factory, " SENTETIK.Tekil "));
            Assert.Equal(2601, Assert.IsType<SqlException>(duplicate.InnerException).Number);

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.LocalCredentials.Add(NewCredential(Guid.NewGuid(), "sentetik.sahipsiz"));
            var orphan = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(547, Assert.IsType<SqlException>(orphan.InnerException).Number);
            Assert.Equal(1, await db.LocalCredentials.CountAsync());
        });
    }

    [SqlServerFact]
    public async Task The_down_guard_refuses_while_a_local_credential_exists_on_real_SQL()
    {
        await WithDatabaseAsync(async factory =>
        {
            var guard = Assert.IsType<SqlOperation>(new AddLocalCredentials().DownOperations[0]).Sql;
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            await db.Database.ExecuteSqlRawAsync(guard);

            await SeedAsync(factory, "sentetik.geri");
            var refused = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(guard));
            Assert.Equal(51280, refused.Number);
            Assert.Equal(1, await db.LocalCredentials.CountAsync());
        });
    }

    private static async Task<Guid> SeedAsync(SqlServerRelationalApiFactory factory, string userName)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var userId = Guid.NewGuid();
        db.Users.Add(new UserAccount(userId, "SENTETIK Yerel", $"{userId:N}@example.invalid"));
        db.LocalCredentials.Add(NewCredential(userId, userName));
        await db.SaveChangesAsync();
        return userId;
    }

    private static LocalCredential NewCredential(Guid userId, string userName) =>
        new(userId, userName, OpaqueHash(), 3, mustChangePassword: false,
            new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero), Guid.NewGuid());

    private static string OpaqueHash() => "SENTETIK-OZET-" + Guid.NewGuid().ToString("N");

    private static async Task<LocalCredentialLoginState> ReadAsync(SqlServerRelationalApiFactory factory, string userName)
    {
        var read = await InStoreAsync(factory, store =>
            store.FindForLoginAsync(LocalUserName.TryNormalize(userName)!, CancellationToken.None));
        Assert.NotNull(read);
        Assert.True(read.UserIsActive);
        return read;
    }

    private static async Task<T> InStoreAsync<T>(SqlServerRelationalApiFactory factory, Func<ILocalCredentialStore, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(new SqlLocalCredentialStore(scope.ServiceProvider.GetRequiredService<IdentityDbContext>()));
    }

    private static async Task<(int FailedAccessCount, DateTimeOffset? LockoutEndUtc, long Version)> StateAsync(
        SqlServerRelationalApiFactory factory,
        Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().LocalCredentials
            .AsNoTracking().SingleAsync(credential => credential.UserId == userId);
        return (stored.FailedAccessCount, stored.LockoutEndUtc, stored.Version);
    }

    private static async Task WithDatabaseAsync(Func<SqlServerRelationalApiFactory, Task> test)
    {
        var connection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var databaseName = $"GoymenERP_Identity_LocalCredential_{Guid.NewGuid():N}";
        await using var factory = new SqlServerRelationalApiFactory(connection,
            applyTestCompanyBTopologyTemplates: false, databaseName: databaseName);
        await using var cleanupScope = factory.Services.CreateAsyncScope();
        var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        // Validate the immutable cleanup target before running assertions, not in their finally path.
        Assert.Equal(databaseName, cleanupDb.Database.GetDbConnection().Database);
        try
        {
            await test(factory);
        }
        finally
        {
            await cleanupDb.Database.EnsureDeletedAsync();
        }
    }
}
