using System.Data;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Credentials;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.SecurityEvents.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>SENTETIK / runtime NOT_RUN. Uses real owner writers and real SQL transactions; no in-memory rollback claim.</summary>
public sealed class LocalTwoFactorOperatorRecoverySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Explicit_active_and_inactive_targets_without_company_memberships_recover_and_original_replay_never_disables_a_new_factor_on_real_sql()
    {
        await using var factory = await StartAsync();
        foreach (var active in new[] { true, false })
        {
            var target = await SeedAsync(factory, active); var console = new ConsoleInput(target.Name);
            var recovered = await RunAsync(factory, console);
            Assert.Equal(LocalTwoFactorOperatorRecoveryStatusV1.Recovered, recovered.Status); Assert.False(recovered.IsReplay);
            var after = await SnapshotAsync(factory, target.Id);
            Assert.False(after.Enabled); Assert.False(after.ActiveSecret); Assert.NotEqual(target.Stamp, after.Stamp);
            await using (var sessionScope = factory.Services.CreateAsyncScope())
                Assert.False(await sessionScope.ServiceProvider.GetRequiredService<ILocalAccountSessionValidator>()
                    .IsCurrentAsync(target.Id, target.Stamp, CancellationToken.None));
            Assert.Equal(active, after.ActiveUser); Assert.Equal(target.PasswordHash, after.PasswordHash); Assert.Equal(target.UserVersion, after.UserVersion);
            Assert.Equal(0, await ScalarAsync(factory, "SELECT COUNT(*) FROM [identity].[RoleAssignments] WHERE [UserId]=@id", target.Id));
            Assert.Equal(1, await ScalarAsync(factory, "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [Id]=@id AND [CompanyId] IS NULL AND [SubjectKind]=N'PlatformCredentialRecovery'", recovered.AttemptId!.Value));
            Assert.Equal(1, await ScalarAsync(factory, "SELECT COUNT(*) FROM [security].[SecurityEvents] WHERE [Id]=@id AND [CompanyId] IS NULL AND [ActorId]='identity.recover-two-factor'", recovered.AttemptId.Value));
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
                Assert.Empty(await scope.ServiceProvider.GetRequiredService<AuditDbContext>().AuditEvents.Where(row => row.Id == recovered.AttemptId).ToArrayAsync());
            }
            await ReenrollAsync(factory, target.Id);
            var reenrolled = await SnapshotAsync(factory, target.Id);
            var replay = await RunAsync(factory, console);
            Assert.True(replay.IsReplay); Assert.Equal(recovered.AttemptId, replay.AttemptId);
            Assert.Equal(recovered.RecordedCredentialVersion, replay.RecordedCredentialVersion); Assert.Equal(recovered.OriginalCorrelationId, replay.OriginalCorrelationId);
            Assert.Equal(reenrolled, await SnapshotAsync(factory, target.Id)); Assert.True(reenrolled.Enabled);
            foreach (var mismatch in new[] { console with { Name = "SENTETIK-other" }, console with { Operator = new("SENTETIK-other", "SENTETIK-machine") }, console with { Reason = "SENTETIK-other-reason" } })
            {
                var conflict = await RunAsync(factory, mismatch);
                Assert.Equal(LocalTwoFactorOperatorRecoveryStatusV1.IdempotencyConflict, conflict.Status); Assert.Null(conflict.TargetUserId);
                Assert.Equal(reenrolled, await SnapshotAsync(factory, target.Id));
            }
        }
    }

    [SqlServerFact]
    public async Task Audit_and_security_owner_failures_after_append_roll_back_factor_stamp_attempt_and_both_owner_rows_then_the_same_key_succeeds_on_real_sql()
    {
        foreach (var failAudit in new[] { true, false })
        {
            var fault = new FaultSwitch();
            await using var factory = await StartAsync(services =>
            {
                if (failAudit) Decorate<ILocalTwoFactorOperatorRecoveryAuditV1>(services, inner => new FaultAudit(inner, fault));
                else Decorate<ILocalTwoFactorOperatorRecoverySecurityWriterV1>(services, inner => new FaultSecurity(inner, fault));
            });
            var target = await SeedAsync(factory, true); var before = await SnapshotAsync(factory, target.Id); var console = new ConsoleInput(target.Name);
            var refused = await RunAsync(factory, console);
            Assert.Equal(failAudit ? LocalTwoFactorOperatorRecoveryStatusV1.AuditUnavailable : LocalTwoFactorOperatorRecoveryStatusV1.SecurityEventUnavailable, refused.Status);
            Assert.Equal(before, await SnapshotAsync(factory, target.Id));
            Assert.Equal(0, await ScalarAsync(factory, "SELECT COUNT(*) FROM [identity].[LocalTwoFactorOperatorRecoveryAttempts] WHERE [TargetUserId]=@id", target.Id));
            Assert.Equal(0, await ScalarAsync(factory, "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityId]=CONVERT(nvarchar(36),@id) AND [SubjectKind]=N'PlatformCredentialRecovery'", target.Id));
            Assert.Equal(0, await ScalarAsync(factory, "SELECT COUNT(*) FROM [security].[SecurityEvents] WHERE [EventType]='identity.two_factor.recovered'", target.Id));
            fault.Enabled = false;
            Assert.Equal(LocalTwoFactorOperatorRecoveryStatusV1.Recovered, (await RunAsync(factory, console)).Status);
        }
    }

    [SqlServerFact]
    public async Task A_factor_step_changed_during_operator_confirmation_records_an_immutable_version_refusal_without_disabling_it_on_real_sql()
    {
        await using var factory = await StartAsync(); var target = await SeedAsync(factory, true);
        // Confirm is synchronous like the real terminal. Run the owner mutation on its own thread so this
        // deliberate pre-transaction race does not block an xUnit synchronization-context continuation.
        var console = new ConsoleInput(target.Name) { BeforeConfirm = () => Task.Run(() => AdvanceStepAsync(factory, target.Id)).GetAwaiter().GetResult() };
        var result = await RunAsync(factory, console);
        Assert.Equal(LocalTwoFactorOperatorRecoveryStatusV1.ConcurrentChange, result.Status);
        var after = await SnapshotAsync(factory, target.Id); Assert.True(after.Enabled); Assert.True(after.ActiveSecret); Assert.Equal(target.Stamp, after.Stamp);
        console.BeforeConfirm = null;
        var replay = await RunAsync(factory, console); Assert.True(replay.IsReplay); Assert.Equal(result.AttemptId, replay.AttemptId);
        Assert.Equal(after, await SnapshotAsync(factory, target.Id));
    }

    [SqlServerFact]
    public async Task Global_recovery_history_rejects_update_delete_and_all_three_Down_guards_refuse_protected_rows_on_real_sql()
    {
        await using var factory = await StartAsync();
        var guards = new[]
        {
            (51922, Assert.IsType<SqlOperation>(new AddOperatorTwoFactorRecoveryAttempts().DownOperations[0]).Sql),
            (51923, Assert.IsType<SqlOperation>(new AddPlatformCredentialRecoveryAudit().DownOperations[0]).Sql),
            (51924, Assert.IsType<SqlOperation>(new AddOperatorTwoFactorRecoverySecurityEvents().DownOperations[0]).Sql),
        };
        foreach (var (_, sql) in guards) await ExecuteAsync(factory, sql);
        var target = await SeedAsync(factory, true); Assert.True((await RunAsync(factory, new(target.Name))).Completed);
        foreach (var (number, sql) in guards)
            Assert.Equal(number, (await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(factory, sql))).Number);
        foreach (var sql in new[]
        {
            "UPDATE [identity].[LocalTwoFactorOperatorRecoveryAttempts] SET [Reason]=[Reason]",
            "DELETE FROM [identity].[LocalTwoFactorOperatorRecoveryAttempts]",
        }) Assert.Equal(51925, (await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(factory, sql))).Number);
        Assert.Equal(1, await ScalarAsync(factory, "SELECT COUNT(*) FROM [identity].[LocalTwoFactorOperatorRecoveryAttempts] WHERE [TargetUserId]=@id", target.Id));
    }

    private static async Task<SqlServerRelationalApiFactory> StartAsync(Action<IServiceCollection>? configure = null)
    {
        var factory = new SqlServerRelationalApiFactory(Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!, configureServices: configure);
        using var client = factory.CreateClientFor(TestUsers.CompanyA, TestCompanyIds.CompanyA);
        using var health = await client.GetAsync("/health/live"); health.EnsureSuccessStatusCode(); return factory;
    }
    private sealed record Target(Guid Id, string Name, Guid Stamp, string PasswordHash, long UserVersion);
    private sealed record Snapshot(bool Enabled, bool ActiveSecret, Guid Stamp, long CredentialVersion, long SecretVersion, bool ActiveUser, long UserVersion, string PasswordHash);
    private static async Task<Target> SeedAsync(SqlServerRelationalApiFactory factory, bool active)
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = new UserAccount(Guid.NewGuid(), "SENTETIK recovery target", null);
        if (!active) user.Deactivate(DateTimeOffset.UtcNow);
        var credential = new LocalCredential(user.Id, "SENTETIK-" + user.Id.ToString("N"), "SENTETIK-only-password-hash", 1, false, DateTimeOffset.UtcNow, Guid.NewGuid());
        credential.ChangeTwoFactor(true, Guid.NewGuid());
        db.Users.Add(user); db.LocalCredentials.Add(credential); db.LocalTwoFactorSecrets.Add(new(user.Id, "SENTETIK-protected-seed", [new string('A', 64)], 1));
        await db.SaveChangesAsync(); return new(user.Id, credential.UserName, credential.SecurityStamp, credential.PasswordHash, user.Version);
    }
    private static async Task<Snapshot> SnapshotAsync(SqlServerRelationalApiFactory factory, Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var credential = await db.LocalCredentials.AsNoTracking().SingleAsync(row => row.UserId == id);
        var secret = await db.LocalTwoFactorSecrets.AsNoTracking().SingleAsync(row => row.UserId == id);
        var user = await db.Users.AsNoTracking().SingleAsync(row => row.Id == id);
        return new(credential.TwoFactorEnabled, secret.IsActive, credential.SecurityStamp, credential.Version, secret.Version, user.IsActive, user.Version, credential.PasswordHash);
    }
    private static async Task ReenrollAsync(SqlServerRelationalApiFactory factory, Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        (await db.LocalTwoFactorSecrets.SingleAsync(row => row.UserId == id)).Replace("SENTETIK-new-protected-seed", [new string('B', 64)], 3);
        (await db.LocalCredentials.SingleAsync(row => row.UserId == id)).ChangeTwoFactor(true, Guid.NewGuid()); await db.SaveChangesAsync();
    }
    private static async Task AdvanceStepAsync(SqlServerRelationalApiFactory factory, Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        (await db.LocalTwoFactorSecrets.SingleAsync(row => row.UserId == id)).AcceptStep(2); await db.SaveChangesAsync();
    }
    private static async Task<LocalTwoFactorOperatorRecoveryResultV1> RunAsync(SqlServerRelationalApiFactory factory, ConsoleInput console)
    {
        await using var scope = factory.Services.CreateAsyncScope(); Assert.False(scope.ServiceProvider.GetRequiredService<ICompanyContext>().IsResolved);
        scope.ServiceProvider.GetRequiredService<ICorrelationContext>().Set("SENTETIK-" + Guid.NewGuid().ToString("N"), null, SourceChannels.System, null, null);
        return await scope.ServiceProvider.GetRequiredService<ILocalTwoFactorOperatorRecoveryServiceV1>().RunAsync(true, console);
    }
    private sealed record ConsoleInput(string Name) : ILocalTwoFactorOperatorRecoveryConsoleV1
    {
        public string Key { get; init; } = Guid.NewGuid().ToString("N"); public string Reason { get; init; } = "SENTETIK-lost-factor-and-codes";
        public LocalTwoFactorRecoveryOperatorDeclarationV1 Operator { get; init; } = new("SENTETIK-operator", "SENTETIK-machine");
        public Action? BeforeConfirm { get; set; } public bool IsInteractive => true;
        public string? ReadTargetUserName() => Name; public string? ReadIdempotencyKey() => Key; public string? ReadReason() => Reason;
        public LocalTwoFactorRecoveryOperatorDeclarationV1? ReadOperatorDeclaration() => Operator;
        public bool Confirm(LocalTwoFactorOperatorRecoveryConfirmationV1 confirmation) { BeforeConfirm?.Invoke(); return true; }
    }
    private sealed class FaultSwitch { public bool Enabled { get; set; } = true; }
    private sealed class FaultAudit(ILocalTwoFactorOperatorRecoveryAuditV1 inner, FaultSwitch fault) : ILocalTwoFactorOperatorRecoveryAuditV1
    {
        public async Task AppendAsync(IDbTransaction tx, LocalTwoFactorOperatorRecoveryAuditV1 row, CancellationToken ct = default)
        { await inner.AppendAsync(tx, row, ct); if (fault.Enabled) throw new InvalidOperationException("SENTETIK-after-owner-append"); }
    }
    private sealed class FaultSecurity(ILocalTwoFactorOperatorRecoverySecurityWriterV1 inner, FaultSwitch fault) : ILocalTwoFactorOperatorRecoverySecurityWriterV1
    {
        public async Task AppendAsync(IDbTransaction tx, LocalTwoFactorOperatorRecoverySecurityV1 row, CancellationToken ct = default)
        { await inner.AppendAsync(tx, row, ct); if (fault.Enabled) throw new InvalidOperationException("SENTETIK-after-owner-append"); }
    }
    private static void Decorate<T>(IServiceCollection services, Func<T, T> wrap) where T : class
    {
        var descriptor = services.Last(item => item.ServiceType == typeof(T)); services.Remove(descriptor);
        services.AddScoped<T>(provider => wrap((T)(descriptor.ImplementationFactory?.Invoke(provider)
            ?? descriptor.ImplementationInstance ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!))));
    }
    private static async Task<int> ScalarAsync(SqlServerRelationalApiFactory factory, string sql, Guid id)
    {
        await using var connection = new SqlConnection(factory.DatabaseConnectionString); await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection); command.Parameters.AddWithValue("@id", id); return (int)(await command.ExecuteScalarAsync())!;
    }
    private static async Task ExecuteAsync(SqlServerRelationalApiFactory factory, string sql)
    {
        await using var connection = new SqlConnection(factory.DatabaseConnectionString); await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection); await command.ExecuteNonQueryAsync();
    }
}
