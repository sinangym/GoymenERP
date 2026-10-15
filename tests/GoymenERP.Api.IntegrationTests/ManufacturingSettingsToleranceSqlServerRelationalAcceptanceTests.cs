using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

public sealed class ManufacturingSettingsToleranceSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid CompanyB = Guid.Parse("10000000-0000-0000-0000-000000000002");

    [SqlServerFact]
    public async Task Approved_registry_is_company_scoped_versioned_audited_and_fail_closed()
    {
        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(baseConnection);
        try
        {
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();

            await using var empty = Provider(factory.DatabaseConnectionString, CompanyA);
            Assert.Equal("settings_provider_unavailable",
                (await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => empty.Value
                    .GetManufacturingToleranceAsync(SettingsManufacturingProcessesV1.Slitting,
                        new DateOnly(2026, 10, 10)))).Code);

            await InsertAsync(factory.DatabaseConnectionString, CompanyA, Guid.NewGuid(),
                SettingsManufacturingProcessesV1.Paint, 1, 0.500000m, true,
                new DateOnly(2026, 1, 1), null, "synthetic-settings-sql-actor", "synthetic SQL acceptance only");
            await InsertAsync(factory.DatabaseConnectionString, CompanyB, Guid.NewGuid(),
                SettingsManufacturingProcessesV1.Slitting, 1, 9.000000m, true,
                new DateOnly(2026, 1, 1), null, "synthetic-settings-sql-actor", "foreign company isolation");

            await using var missing = Provider(factory.DatabaseConnectionString, CompanyA);
            Assert.Equal(SettingsPrerequisiteCodesV1.ManufacturingToleranceProfileNotFound,
                (await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => missing.Value
                    .GetManufacturingToleranceAsync(SettingsManufacturingProcessesV1.Slitting,
                        new DateOnly(2026, 10, 10)))).Code);

            var profileId = Guid.NewGuid();
            await InsertAsync(factory.DatabaseConnectionString, CompanyA, profileId,
                SettingsManufacturingProcessesV1.Slitting, 7, 1.234567m, false,
                new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31),
                "synthetic-settings-sql-approver", "future inactive profile evidence");

            await using var exact = Provider(factory.DatabaseConnectionString, CompanyA);
            var result = await exact.Value.GetManufacturingToleranceAsync(
                SettingsManufacturingProcessesV1.Slitting, new DateOnly(2026, 10, 10));
            Assert.Equal(CompanyA, result.CompanyId);
            Assert.Equal(profileId, result.Profile.ProfileId);
            Assert.Equal(7, result.Profile.Version);
            Assert.Equal(1.234567m, result.MaximumFirePercent);
            Assert.False(result.IsActive);
            Assert.Equal(new DateOnly(2027, 1, 1), result.EffectiveFrom);
            Assert.Equal(new DateOnly(2027, 12, 31), result.EffectiveThrough);

            await AssertInsertRejectedAsync(factory.DatabaseConnectionString, CompanyA, Guid.NewGuid(),
                SettingsManufacturingProcessesV1.Slitting, 8, 2m, true,
                new DateOnly(2027, 6, 1), null, "synthetic-settings-sql-approver", "overlap");
            await AssertInsertRejectedAsync(factory.DatabaseConnectionString, CompanyA, Guid.NewGuid(),
                SettingsManufacturingProcessesV1.ColdRolling, 1, 2m, true,
                new DateOnly(2026, 1, 1), null, "", "missing approval actor");

            var fixedVersionProfile = Guid.NewGuid();
            await InsertAsync(factory.DatabaseConnectionString, CompanyA, fixedVersionProfile,
                SettingsManufacturingProcessesV1.CutToLength, 1, 0.750000m, true,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31),
                "synthetic-settings-sql-approver", "version uniqueness evidence");
            await AssertInsertRejectedAsync(factory.DatabaseConnectionString, CompanyA, Guid.NewGuid(),
                SettingsManufacturingProcessesV1.CutToLength, 1, 0.800000m, true,
                new DateOnly(2026, 2, 1), null,
                "synthetic-settings-sql-approver", "duplicate version must fail");

            const int contentionRounds = 4;
            for (var round = 0; round < contentionRounds; round++)
            {
                using var ready = new Barrier(2);
                var year = 2028 + round;
                var contenders = new[]
                {
                    ConcurrentInsertAsync(ready, factory.DatabaseConnectionString, CompanyA, Guid.NewGuid(),
                        (round * 2) + 1, new DateOnly(year, 1, 1), new DateOnly(year, 12, 31)),
                    ConcurrentInsertAsync(ready, factory.DatabaseConnectionString, CompanyA, Guid.NewGuid(),
                        (round * 2) + 2, new DateOnly(year, 6, 1), new DateOnly(year, 12, 31)),
                };
                var outcomes = await Task.WhenAll(contenders);
                Assert.Equal(1, outcomes.Count(x => x is null));
                Assert.Equal(1, outcomes.Count(x => x == 51001));
            }
            Assert.Equal(contentionRounds, await CountProcessAsync(factory.DatabaseConnectionString,
                SettingsManufacturingProcessesV1.ColdRolling));

            await AssertCommandRejectedAsync(factory.DatabaseConnectionString,
                "UPDATE [settings].[ManufacturingTolerancePolicies] SET [MaximumFirePercent] = 99 WHERE [CompanyId] = @company AND [ProfileId] = @profile",
                CompanyA, profileId);
            await AssertCommandRejectedAsync(factory.DatabaseConnectionString,
                "DELETE FROM [settings].[ManufacturingTolerancePolicies] WHERE [CompanyId] = @company AND [ProfileId] = @profile",
                CompanyA, profileId);

            Assert.Equal(4 + contentionRounds, await CountAsync(factory.DatabaseConnectionString));
            await using var rollback = Context(factory.DatabaseConnectionString, CompanyA);
            await Assert.ThrowsAsync<SqlException>(() =>
                rollback.GetService<IMigrator>().MigrateAsync("0"));
            Assert.Equal(4 + contentionRounds, await CountAsync(factory.DatabaseConnectionString));
        }
        finally
        {
            await factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await DropDatabaseAsync(baseConnection, factory.DatabaseName);
        }
    }

    private static ProviderScope Provider(string connectionString, Guid company)
    {
        var companyContext = new FixedCompany(company);
        var database = Context(connectionString, companyContext);
        return new ProviderScope(database,
            new SettingsManufacturingToleranceProviderV1(database, companyContext, new FixedActor(company)));
    }

    private static SettingsDbContext Context(string connectionString, Guid company) =>
        Context(connectionString, new FixedCompany(company));

    private static SettingsDbContext Context(string connectionString, ICompanyContext companyContext)
    {
        var options = new DbContextOptionsBuilder<SettingsDbContext>()
            .UseSqlServer(connectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SettingsDbContext.Schema))
            .Options;
        return new SettingsDbContext(options, companyContext);
    }

    private static async Task InsertAsync(string connectionString, Guid companyId, Guid profileId,
        string process, long version, decimal maximumFirePercent, bool isActive, DateOnly from,
        DateOnly? through, string approvedBy, string reason)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = InsertCommand(connection, companyId, profileId, process, version,
            maximumFirePercent, isActive, from, through, approvedBy, reason);
        await command.ExecuteNonQueryAsync();
    }

    private static SqlCommand InsertCommand(SqlConnection connection, Guid companyId, Guid profileId,
        string process, long version, decimal maximumFirePercent, bool isActive, DateOnly from,
        DateOnly? through, string approvedBy, string reason)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO [settings].[ManufacturingTolerancePolicies]
                ([CompanyId],[ProfileId],[Process],[Version],[MaximumFirePercent],[IsActive],
                 [EffectiveFrom],[EffectiveThrough],[ApprovalStatus],[ApprovedBy],[ApprovedAtUtc],[ApprovalReason])
            VALUES (@company,@profile,@process,@version,@percent,@active,@from,@through,
                    'Approved',@approvedBy,@approvedAt,@reason);
            """;
        command.Parameters.AddWithValue("@company", companyId);
        command.Parameters.AddWithValue("@profile", profileId);
        command.Parameters.AddWithValue("@process", process);
        command.Parameters.AddWithValue("@version", version);
        command.Parameters.AddWithValue("@percent", maximumFirePercent);
        command.Parameters.AddWithValue("@active", isActive);
        command.Parameters.AddWithValue("@from", from.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@through", through is null ? DBNull.Value : through.Value.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@approvedBy", approvedBy);
        command.Parameters.AddWithValue("@approvedAt", new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero));
        command.Parameters.AddWithValue("@reason", reason);
        return command;
    }

    private static async Task AssertInsertRejectedAsync(string connectionString, Guid companyId, Guid profileId,
        string process, long version, decimal maximumFirePercent, bool isActive, DateOnly from,
        DateOnly? through, string approvedBy, string reason) =>
        await Assert.ThrowsAsync<SqlException>(() => InsertAsync(connectionString, companyId, profileId,
            process, version, maximumFirePercent, isActive, from, through, approvedBy, reason));

    private static async Task<int?> ConcurrentInsertAsync(Barrier ready, string connectionString, Guid companyId,
        Guid profileId, long version, DateOnly from, DateOnly? through)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = InsertCommand(connection, companyId, profileId,
            SettingsManufacturingProcessesV1.ColdRolling, version, 0.625000m, true, from, through,
            "synthetic-settings-sql-approver", "concurrent overlap evidence");
        if (!ready.SignalAndWait(TimeSpan.FromSeconds(30)))
        {
            throw new TimeoutException("Concurrent tolerance publication contenders did not become ready.");
        }

        try
        {
            await command.ExecuteNonQueryAsync();
            return null;
        }
        catch (SqlException exception)
        {
            return exception.Number;
        }
    }

    private static async Task AssertCommandRejectedAsync(string connectionString, string commandText,
        Guid companyId, Guid profileId)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Parameters.AddWithValue("@company", companyId);
        command.Parameters.AddWithValue("@profile", profileId);
        await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());
    }

    private static async Task<int> CountProcessAsync(string connectionString, string process)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM [settings].[ManufacturingTolerancePolicies] WHERE [Process] = @process";
        command.Parameters.AddWithValue("@process", process);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int> CountAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM [settings].[ManufacturingTolerancePolicies]";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task DropDatabaseAsync(string baseConnectionString, string databaseName)
    {
        await using var connection = new SqlConnection(baseConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID(N'{databaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{databaseName}];
            END;
            """;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class ProviderScope(SettingsDbContext database,
        SettingsManufacturingToleranceProviderV1 value) : IAsyncDisposable
    {
        public SettingsManufacturingToleranceProviderV1 Value { get; } = value;
        public ValueTask DisposeAsync() => database.DisposeAsync();
    }

    private sealed class FixedCompany(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => true;
        public Guid CompanyId { get; } = companyId;
        public void Set(Guid id) => throw new NotSupportedException();
    }

    private sealed class FixedActor(Guid companyId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => "synthetic-settings-sql-actor";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { companyId };
        public bool CanAccessCompany(Guid candidate) => AllowedCompanyIds.Contains(candidate);
    }
}
