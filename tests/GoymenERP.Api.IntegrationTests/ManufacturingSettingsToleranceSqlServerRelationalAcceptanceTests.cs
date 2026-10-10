using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

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

            Assert.Equal(3, await CountAsync(factory.DatabaseConnectionString));
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
        var options = new DbContextOptionsBuilder<SettingsDbContext>()
            .UseSqlServer(connectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SettingsDbContext.Schema))
            .Options;
        var database = new SettingsDbContext(options, companyContext);
        return new ProviderScope(database,
            new SettingsManufacturingToleranceProviderV1(database, companyContext, new FixedActor(company)));
    }

    private static async Task InsertAsync(string connectionString, Guid companyId, Guid profileId,
        string process, long version, decimal maximumFirePercent, bool isActive, DateOnly from,
        DateOnly? through, string approvedBy, string reason)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
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
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertInsertRejectedAsync(string connectionString, Guid companyId, Guid profileId,
        string process, long version, decimal maximumFirePercent, bool isActive, DateOnly from,
        DateOnly? through, string approvedBy, string reason) =>
        await Assert.ThrowsAsync<SqlException>(() => InsertAsync(connectionString, companyId, profileId,
            process, version, maximumFirePercent, isActive, from, through, approvedBy, reason));

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
