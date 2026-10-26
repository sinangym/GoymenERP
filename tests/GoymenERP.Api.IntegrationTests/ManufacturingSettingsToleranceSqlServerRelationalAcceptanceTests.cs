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
    private const string PredecessorMigration = "20261010095724_AddManufacturingTolerancePolicyRegistry";
    private const string SerializationMigration = "20261015150000_SerializeManufacturingTolerancePolicyWindows";
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
                new DateOnly(2027, 6, 1), null, "synthetic-settings-sql-approver", "overlap", 51001);
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

            await AssertIncomingBatchOverlapRejectedAsync(factory.DatabaseConnectionString);

            const int contentionRounds = 4;
            for (var round = 0; round < contentionRounds; round++)
            {
                var year = 2028 + round;
                await AssertContendedOverlapRejectedAsync(factory.DatabaseConnectionString,
                    (round * 2) + 1, (round * 2) + 2, year);
            }
            Assert.Equal(contentionRounds, await CountProcessAsync(factory.DatabaseConnectionString,
                SettingsManufacturingProcessesV1.ColdRolling));

            await AssertCommandRejectedAsync(factory.DatabaseConnectionString,
                "UPDATE [settings].[ManufacturingTolerancePolicies] SET [MaximumFirePercent] = 99 WHERE [CompanyId] = @company AND [ProfileId] = @profile",
                CompanyA, profileId, 51002);
            await AssertCommandRejectedAsync(factory.DatabaseConnectionString,
                "DELETE FROM [settings].[ManufacturingTolerancePolicies] WHERE [CompanyId] = @company AND [ProfileId] = @profile",
                CompanyA, profileId, 51002);

            Assert.Equal(4 + contentionRounds, await CountAsync(factory.DatabaseConnectionString));
            await using var rollback = Context(factory.DatabaseConnectionString, CompanyA);
            var rollbackException = await Assert.ThrowsAsync<SqlException>(() =>
                rollback.GetService<IMigrator>().MigrateAsync("0"));
            Assert.Equal(51003, rollbackException.Number);
            Assert.Equal(4 + contentionRounds, await CountAsync(factory.DatabaseConnectionString));
        }
        finally
        {
            await factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await DropDatabaseAsync(baseConnection, factory.DatabaseName);
        }
    }

    [SqlServerFact]
    public async Task Serialization_migration_accepts_valid_predecessor_history_without_mutation()
    {
        await InDisposableDatabaseAsync(async connectionString =>
        {
            await using var context = Context(connectionString, CompanyA);
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(PredecessorMigration);

            await InsertAsync(connectionString, CompanyA, Guid.NewGuid(),
                SettingsManufacturingProcessesV1.Slitting, 1, 0.625000m, true,
                new DateOnly(2037, 1, 1), new DateOnly(2037, 6, 30),
                "synthetic-settings-sql-approver", "valid predecessor history one");
            await InsertAsync(connectionString, CompanyA, Guid.NewGuid(),
                SettingsManufacturingProcessesV1.Slitting, 2, 0.625000m, true,
                new DateOnly(2037, 7, 1), new DateOnly(2037, 12, 31),
                "synthetic-settings-sql-approver", "valid predecessor history two");

            var before = await HistoryRowsAsync(connectionString);
            var predecessorTrigger = await TriggerStateAsync(connectionString);
            Assert.False(predecessorTrigger.IsInsteadOf);
            Assert.False(predecessorTrigger.IsDisabled);
            Assert.Equal(0, await MigrationHistoryCountAsync(connectionString));

            await migrator.MigrateAsync(SerializationMigration);

            Assert.Equal(before, await HistoryRowsAsync(connectionString));
            Assert.Equal(1, await MigrationHistoryCountAsync(connectionString));
            var serializedTrigger = await TriggerStateAsync(connectionString);
            Assert.True(serializedTrigger.IsInsteadOf);
            Assert.False(serializedTrigger.IsDisabled);
        });
    }

    [SqlServerFact]
    public async Task Serialization_migration_rejects_overlapping_predecessor_history_atomically()
    {
        await InDisposableDatabaseAsync(async connectionString =>
        {
            await using var context = Context(connectionString, CompanyA);
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(PredecessorMigration);

            await SetTriggerEnabledAsync(connectionString, enabled: false);
            try
            {
                await InsertAsync(connectionString, CompanyA, Guid.NewGuid(),
                    SettingsManufacturingProcessesV1.Paint, 1, 0.625000m, true,
                    new DateOnly(2038, 1, 1), new DateOnly(2038, 12, 31),
                    "synthetic-settings-sql-approver", "explicit disposable legacy overlap one");
                await InsertAsync(connectionString, CompanyA, Guid.NewGuid(),
                    SettingsManufacturingProcessesV1.Paint, 2, 0.625000m, true,
                    new DateOnly(2038, 6, 1), new DateOnly(2038, 12, 31),
                    "synthetic-settings-sql-approver", "explicit disposable legacy overlap two");
            }
            finally
            {
                await SetTriggerEnabledAsync(connectionString, enabled: true);
            }

            var before = await HistoryRowsAsync(connectionString);
            var predecessorTrigger = await TriggerStateAsync(connectionString);
            Assert.False(predecessorTrigger.IsInsteadOf);
            Assert.False(predecessorTrigger.IsDisabled);
            Assert.Equal(2, await CountProcessAsync(connectionString, SettingsManufacturingProcessesV1.Paint));
            Assert.Equal(0, await MigrationHistoryCountAsync(connectionString));

            var error = await Assert.ThrowsAsync<SqlException>(() =>
                migrator.MigrateAsync(SerializationMigration));

            Assert.Equal(51004, error.Number);
            Assert.Contains("settings_manufacturing_tolerance_existing_history_overlap", error.Message,
                StringComparison.Ordinal);
            Assert.Equal(0, await MigrationHistoryCountAsync(connectionString));
            Assert.Equal(before, await HistoryRowsAsync(connectionString));
            Assert.Equal(predecessorTrigger, await TriggerStateAsync(connectionString));
        });
    }

    [SqlServerFact]
    public async Task Empty_registry_can_downgrade_to_predecessor_and_reapply_serialization()
    {
        await InDisposableDatabaseAsync(async connectionString =>
        {
            await using var context = Context(connectionString, CompanyA);
            var migrator = context.GetService<IMigrator>();
            Assert.Equal(1, await MigrationHistoryCountAsync(connectionString));
            Assert.True((await TriggerStateAsync(connectionString)).IsInsteadOf);

            await migrator.MigrateAsync(PredecessorMigration);

            Assert.Equal(0, await MigrationHistoryCountAsync(connectionString));
            var predecessorTrigger = await TriggerStateAsync(connectionString);
            Assert.False(predecessorTrigger.IsInsteadOf);
            Assert.False(predecessorTrigger.IsDisabled);
            Assert.Equal([], await HistoryRowsAsync(connectionString));

            await migrator.MigrateAsync(SerializationMigration);

            Assert.Equal(1, await MigrationHistoryCountAsync(connectionString));
            var serializedTrigger = await TriggerStateAsync(connectionString);
            Assert.True(serializedTrigger.IsInsteadOf);
            Assert.False(serializedTrigger.IsDisabled);
            Assert.Equal([], await HistoryRowsAsync(connectionString));
        });
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
        DateOnly? through, string approvedBy, string reason, int? expectedNumber = null)
    {
        var exception = await Assert.ThrowsAsync<SqlException>(() => InsertAsync(connectionString, companyId,
            profileId, process, version, maximumFirePercent, isActive, from, through, approvedBy, reason));
        if (expectedNumber is not null)
        {
            Assert.Equal(expectedNumber.Value, exception.Number);
        }
    }

    private static async Task AssertIncomingBatchOverlapRejectedAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO [settings].[ManufacturingTolerancePolicies]
                ([CompanyId],[ProfileId],[Process],[Version],[MaximumFirePercent],[IsActive],
                 [EffectiveFrom],[EffectiveThrough],[ApprovalStatus],[ApprovedBy],[ApprovedAtUtc],[ApprovalReason])
            VALUES
                (@company,@firstProfile,@process,1,0.625000,1,'2036-01-01','2036-12-31',
                 'Approved',@approvedBy,@approvedAt,@reason),
                (@company,@secondProfile,@process,2,0.625000,1,'2036-06-01','2036-12-31',
                 'Approved',@approvedBy,@approvedAt,@reason);
            """;
        command.Parameters.AddWithValue("@company", CompanyA);
        command.Parameters.AddWithValue("@firstProfile", Guid.NewGuid());
        command.Parameters.AddWithValue("@secondProfile", Guid.NewGuid());
        command.Parameters.AddWithValue("@process", SettingsManufacturingProcessesV1.TubeProfile);
        command.Parameters.AddWithValue("@approvedBy", "synthetic-settings-sql-approver");
        command.Parameters.AddWithValue("@approvedAt",
            new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero));
        command.Parameters.AddWithValue("@reason", "incoming batch overlap evidence");

        var exception = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(51001, exception.Number);
        Assert.Equal(0, await CountProcessAsync(connectionString, SettingsManufacturingProcessesV1.TubeProfile));
    }

    private static async Task AssertContendedOverlapRejectedAsync(string connectionString,
        long firstVersion, long secondVersion, int year)
    {
        await using var firstConnection = new SqlConnection(connectionString);
        await firstConnection.OpenAsync();
        await using var firstTransaction = (SqlTransaction)await firstConnection.BeginTransactionAsync();
        await using var firstCommand = InsertCommand(firstConnection, CompanyA, Guid.NewGuid(),
            SettingsManufacturingProcessesV1.ColdRolling, firstVersion, 0.625000m, true,
            new DateOnly(year, 1, 1), new DateOnly(year, 12, 31),
            "synthetic-settings-sql-approver", "held contention winner evidence");
        firstCommand.Transaction = firstTransaction;
        await firstCommand.ExecuteNonQueryAsync();

        var contenderStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var contender = AttemptContendedInsertAsync(contenderStarted, connectionString, secondVersion,
            new DateOnly(year, 6, 1), new DateOnly(year, 12, 31));
        await contenderStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var completedBeforeCommit = await Task.WhenAny(contender, Task.Delay(TimeSpan.FromMilliseconds(500)))
            == contender;

        await firstTransaction.CommitAsync();
        var outcome = await contender.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.False(completedBeforeCommit);
        Assert.Equal(51001, outcome);
    }

    private static async Task<int?> AttemptContendedInsertAsync(TaskCompletionSource<bool> started,
        string connectionString, long version, DateOnly from, DateOnly through)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = InsertCommand(connection, CompanyA, Guid.NewGuid(),
            SettingsManufacturingProcessesV1.ColdRolling, version, 0.625000m, true, from, through,
            "synthetic-settings-sql-approver", "blocked contention loser evidence");
        started.TrySetResult(true);

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
        Guid companyId, Guid profileId, int expectedNumber)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Parameters.AddWithValue("@company", companyId);
        command.Parameters.AddWithValue("@profile", profileId);
        var exception = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(expectedNumber, exception.Number);
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

    private static async Task<int> MigrationHistoryCountAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM [settings].[__EFMigrationsHistory] WHERE [MigrationId] = @migration";
        command.Parameters.AddWithValue("@migration", SerializationMigration);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<IReadOnlyList<string>> HistoryRowsAsync(string connectionString)
    {
        var rows = new List<string>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CONCAT(
                CONVERT(varchar(36), [CompanyId]), N'|', CONVERT(varchar(36), [ProfileId]), N'|',
                [Process], N'|', CONVERT(varchar(20), [Version]), N'|',
                CONVERT(varchar(32), [MaximumFirePercent]), N'|', CONVERT(varchar(1), [IsActive]), N'|',
                CONVERT(varchar(10), [EffectiveFrom], 23), N'|',
                COALESCE(CONVERT(varchar(10), [EffectiveThrough], 23), N'<null>'), N'|',
                [ApprovalStatus], N'|', [ApprovedBy], N'|', CONVERT(varchar(40), [ApprovedAtUtc], 127), N'|',
                [ApprovalReason])
            FROM [settings].[ManufacturingTolerancePolicies]
            ORDER BY [CompanyId], [Process], [EffectiveFrom], [ProfileId];
            """;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }
        return rows;
    }

    private static async Task<TriggerState> TriggerStateAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT OBJECT_DEFINITION([object_id]), [is_instead_of_trigger], [is_disabled]
            FROM sys.triggers
            WHERE [parent_id] = OBJECT_ID(N'[settings].[ManufacturingTolerancePolicies]')
              AND [name] = N'TR_ManufacturingTolerancePolicies_NoOverlap';
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new TriggerState(reader.GetString(0), reader.GetBoolean(1), reader.GetBoolean(2));
    }

    private static async Task SetTriggerEnabledAsync(string connectionString, bool enabled)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            {(enabled ? "ENABLE" : "DISABLE")} TRIGGER [settings].[TR_ManufacturingTolerancePolicies_NoOverlap]
            ON [settings].[ManufacturingTolerancePolicies];
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InDisposableDatabaseAsync(Func<string, Task> acceptance)
    {
        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(baseConnection);
        try
        {
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            await acceptance(factory.DatabaseConnectionString);
        }
        finally
        {
            await factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await DropDatabaseAsync(baseConnection, factory.DatabaseName);
        }
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

    private sealed record TriggerState(string Definition, bool IsInsteadOf, bool IsDisabled);

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
