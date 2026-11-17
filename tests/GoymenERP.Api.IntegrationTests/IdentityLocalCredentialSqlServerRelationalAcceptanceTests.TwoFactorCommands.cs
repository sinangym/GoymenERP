using System.Data;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Credentials;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Infrastructure.LocalAccount;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Persistence.Migrations;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class IdentityLocalCredentialSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Two_factor_enrollment_audit_and_credential_commit_once_and_an_audit_failure_rolls_everything_back()
    {
        await WithDatabaseAsync(async factory =>
        {
            var now = new DateTimeOffset(2026, 11, 17, 1, 0, 0, TimeSpan.Zero);
            var userId = await SeedAsync(factory, "synthetic.self.factor");
            var password = "S-" + Guid.NewGuid().ToString("N");
            var protection = new LocalTwoFactorProtectionV1(new EphemeralDataProtectionProvider());
            Guid stamp; long version;
            var hasher = factory.Services.GetRequiredService<ILocalPasswordHasher>();
            await using (var setup = factory.Services.CreateAsyncScope())
            {
                var db = setup.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var credential = await db.LocalCredentials.SingleAsync(row => row.UserId == userId);
                credential.ChangePassword(hasher.Hash(password), hasher.FormatVersion, now, Guid.NewGuid());
                stamp = credential.SecurityStamp; version = credential.Version;
                var role = new RoleDefinition(Guid.NewGuid(), "SYNTHETIC-MFA-" + Guid.NewGuid().ToString("N"), "Sentetik üyelik", false);
                var assignment = new RoleAssignment(Guid.NewGuid(), userId, role.Id, TestCompanyIds.CompanyA, now.AddDays(-1), null, null);
                assignment.AddScope(AccessScopeType.Company, TestCompanyIds.CompanyA);
                db.Roles.Add(role); db.RoleAssignments.Add(assignment);
                await db.SaveChangesAsync();
            }
            var company = new CompanyContext(); company.Set(TestCompanyIds.CompanyA);
            var actor = new TwoFactorActor(userId, TestCompanyIds.CompanyA);
            var key = Guid.NewGuid().ToString("N"); var correlation = Guid.NewGuid().ToString("N");
            var metadata = new IdentityAccessCommandMetadataV1(TestCompanyIds.CompanyA, userId.ToString("D"), correlation,
                null, "Web", null, null, key);
            LocalTwoFactorEnrollmentV1 enrollment;
            LocalTwoFactorServiceV1 Service(IServiceProvider services, ILocalCredentialSelfServiceAuditWriterV1? audit = null) =>
                new(services.GetRequiredService<IdentityDbContext>(), actor, company, hasher, protection,
                    audit ?? services.GetRequiredService<ILocalCredentialSelfServiceAuditWriterV1>(), new TwoFactorClock(now));
            await using (var preparation = factory.Services.CreateAsyncScope())
            {
                enrollment = (await Service(preparation.ServiceProvider).BeginEnrollmentAsync(userId, stamp,
                    new(version.ToString(CultureInfo.InvariantCulture), password, null, null), CancellationToken.None))!;
                Assert.NotNull(enrollment);
            }
            var code = AuthenticatorCode(enrollment.SecretKey, now);
            var request = new LocalTwoFactorProofV1(version.ToString(CultureInfo.InvariantCulture), password, code, enrollment.EnrollmentToken);
            await using (var failed = factory.Services.CreateAsyncScope())
            {
                var real = failed.ServiceProvider.GetRequiredService<ILocalCredentialSelfServiceAuditWriterV1>();
                await Assert.ThrowsAsync<InvalidOperationException>(() => Service(failed.ServiceProvider, new ThrowAfterSelfAudit(real))
                    .ChangeAsync(userId, stamp, "enable", request, metadata, CancellationToken.None));
            }
            await using (var unchanged = factory.Services.CreateAsyncScope())
            {
                var db = unchanged.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var credential = await db.LocalCredentials.AsNoTracking().SingleAsync(row => row.UserId == userId);
                Assert.Equal((version, stamp, false), (credential.Version, credential.SecurityStamp, credential.TwoFactorEnabled));
                Assert.False(await db.LocalTwoFactorSecrets.AnyAsync(row => row.UserId == userId));
                Assert.False(await db.LocalCredentialCommands.AnyAsync(row => row.UserId == userId));
                unchanged.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
                Assert.False(await unchanged.ServiceProvider.GetRequiredService<AuditDbContext>().AuditEvents.AnyAsync(row => row.CorrelationId == correlation));
            }
            LocalTwoFactorCommandResultV1 applied;
            await using (var success = factory.Services.CreateAsyncScope())
                applied = await Service(success.ServiceProvider).ChangeAsync(userId, stamp, "enable", request, metadata, CancellationToken.None);
            Assert.Equal(LocalTwoFactorCommandStatusV1.Applied, applied.Status);
            Assert.True(applied.Enabled); Assert.Null(applied.RecoveryCodes);
            await using (var replay = factory.Services.CreateAsyncScope())
            {
                var answer = await Service(replay.ServiceProvider).ChangeAsync(userId, applied.SecurityStamp!.Value, "enable", request,
                    metadata with { CorrelationId = Guid.NewGuid().ToString("N") }, CancellationToken.None);
                Assert.Equal(LocalTwoFactorCommandStatusV1.Replayed, answer.Status);
                Assert.Equal(correlation, answer.OriginalCorrelationId); Assert.Null(answer.RecoveryCodes);
            }
            await using var final = factory.Services.CreateAsyncScope();
            final.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
            var auditEvent = Assert.Single(await final.ServiceProvider.GetRequiredService<AuditDbContext>().AuditEvents
                .Where(row => row.CorrelationId == correlation).ToArrayAsync());
            var journal = Assert.Single(await final.ServiceProvider.GetRequiredService<IdentityDbContext>().LocalCredentialCommands
                .Where(row => row.UserId == userId).ToArrayAsync());
            var evidence = JsonSerializer.Serialize(new { auditEvent, journal });
            foreach (var secret in enrollment.RecoveryCodes.Append(enrollment.SecretKey).Append(enrollment.EnrollmentToken).Append(password))
                Assert.DoesNotContain(secret, evidence, StringComparison.Ordinal);
            Assert.Null(auditEvent.PermissionCode); Assert.Equal(userId.ToString("D"), auditEvent.ActorId);
        });
    }

    [SqlServerFact]
    public async Task The_two_factor_down_guard_allows_unenrolled_credentials_and_protects_factor_and_replay_evidence()
    {
        await WithDatabaseAsync(async factory =>
        {
            var guard = Assert.IsType<SqlOperation>(new AddLocalTwoFactorSecrets().DownOperations[0]).Sql;
            var userId = await SeedAsync(factory, "synthetic.factor.guard");
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await db.Database.ExecuteSqlRawAsync(guard);
            var codes = LocalTotpV1.NewRecoveryCodes();
            db.LocalTwoFactorSecrets.Add(new(userId, "SYNTHETIC-PROTECTED", codes.Select(LocalTotpV1.RecoveryHash).ToArray(), 0));
            await db.SaveChangesAsync();
            Assert.Equal(51967, (await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(guard))).Number);
            Assert.Equal(1, await db.LocalTwoFactorSecrets.CountAsync(row => row.UserId == userId));
            await db.LocalTwoFactorSecrets.Where(row => row.UserId == userId).ExecuteDeleteAsync();
            db.LocalCredentialCommands.Add(new(Guid.NewGuid(), userId, "identity.local_account.two_factor.enable", "SYNTHETIC-KEY",
                new byte[32], 1, DateTimeOffset.UtcNow, "SYNTHETIC-CORRELATION"));
            await db.SaveChangesAsync();
            Assert.Equal(51967, (await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(guard))).Number);
            Assert.Equal(1, await db.LocalCredentialCommands.CountAsync(row => row.UserId == userId));
        });
    }

    private sealed class TwoFactorActor(Guid id, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string UserId => id.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid value) => AllowedCompanyIds.Contains(value);
    }
    // Independent authenticator client for the real enrollment protocol; the domain vectors are checked separately.
    private static string AuthenticatorCode(string secret, DateTimeOffset now)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>(); var buffer = 0; var bits = 0;
        foreach (var character in secret)
        {
            buffer = (buffer << 5) | alphabet.IndexOf(character); bits += 5;
            if (bits >= 8) { bits -= 8; bytes.Add((byte)(buffer >> bits)); }
        }
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, now.ToUnixTimeSeconds() / 30);
        var hash = HMACSHA1.HashData(bytes.ToArray(), counter);
        var offset = hash[^1] & 15;
        return ((BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(offset, 4)) & 0x7fffffff) % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }
    private sealed class ThrowAfterSelfAudit(ILocalCredentialSelfServiceAuditWriterV1 inner) : ILocalCredentialSelfServiceAuditWriterV1
    {
        public async Task AppendAsync(IDbTransaction transaction, LocalCredentialSelfServiceAuditV1 record, CancellationToken ct = default)
        { await inner.AppendAsync(transaction, record, ct); throw new InvalidOperationException("Synthetic audit owner fault."); }
    }
}
