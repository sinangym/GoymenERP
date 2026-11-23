using System.Data;
using GoymenERP.Modules.Idempotency.Domain.Retention;
using GoymenERP.Modules.Idempotency.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.Idempotency.Infrastructure.Retention;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Logging.Abstractions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// OPEN-021 / NS-12 idempotency retention purge on a real, migrated SQL Server database: the purge store deletes only
/// one company's records completed strictly before the cutoff, in batches, each with its append-only evidence row; the
/// command execution trigger keeps every other delete and every update refused; the forward-fix Down refuses while
/// evidence exists. The purge job stays off in the API (it is constructed here directly). SENTETIK / NOT_ENV.
/// </summary>
public sealed class IdempotencyRetentionPurgeSqlServerRelationalAcceptanceTests
{
    private const string PolicyRef = "SENTETIK-retention-policy";
    private const string CommandName = "sentetik.retention.probe.v1";

    [SqlServerFact]
    public async Task The_purge_deletes_only_each_companys_records_older_than_the_cutoff_in_batches_and_writes_one_evidence_row_per_batch()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var now = TruncatedUtcNow();
            var companyA = Guid.NewGuid();
            var companyB = Guid.NewGuid();
            var expiredA = new[]
            {
                await InsertExecutionAsync(connectionString, companyA, now.AddDays(-100)),
                await InsertExecutionAsync(connectionString, companyA, now.AddDays(-120)),
                await InsertExecutionAsync(connectionString, companyA, now.AddDays(-91)),
            };
            var atCutoffA = await InsertExecutionAsync(connectionString, companyA, now.AddDays(-90));
            var recentA = await InsertExecutionAsync(connectionString, companyA, now.AddDays(-10));
            var expiredB = await InsertExecutionAsync(connectionString, companyB, now.AddDays(-365));

            var job = Job(connectionString, now, batchSize: 2);
            var summary = await job.RunOnceAsync(CancellationToken.None);

            Assert.Equal(now.AddDays(-90), summary.CutoffUtc);
            Assert.Equal((2, 4L), (summary.PurgedCompanies, summary.PurgedRecords));
            foreach (var id in expiredA.Append(expiredB))
            {
                Assert.Equal(0, await ScalarAsync<int>(connectionString,
                    "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [Id] = @id;", IdParameter("@id", id)));
            }

            // Strictly older than the cutoff: the record completed exactly at the cutoff and the recent one stay.
            Assert.Equal(2, await ScalarAsync<int>(connectionString,
                "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [Id] IN (@atCutoff, @recent);",
                IdParameter("@atCutoff", atCutoffA), IdParameter("@recent", recentA)));

            var evidence = await ReadEvidenceAsync(connectionString);
            Assert.Equal(
                new[] { (companyA, 1), (companyA, 2), (companyB, 1) }.Order().ToArray(),
                evidence.Select(row => (row.CompanyId, row.DeletedCount)).Order().ToArray());
            Assert.All(evidence, row =>
            {
                Assert.Equal(summary.RunId, row.RunId);
                Assert.Equal(summary.CutoffUtc, row.CutoffUtc);
                Assert.Equal("P90D", row.RetentionPeriod);
                Assert.Equal(PolicyRef, row.PolicyRef);
            });

            var second = await job.RunOnceAsync(CancellationToken.None);
            Assert.Equal((0, 0L), (second.PurgedCompanies, second.PurgedRecords));
            Assert.Equal(3, (await ReadEvidenceAsync(connectionString)).Count);
        });
    }

    [SqlServerFact]
    public async Task A_delete_without_the_purge_window_stays_refused_and_a_window_delete_of_a_record_at_or_after_the_cutoff_is_refused()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var now = TruncatedUtcNow();
            var companyId = Guid.NewGuid();
            var expired = await InsertExecutionAsync(connectionString, companyId, now.AddDays(-100));
            var recent = await InsertExecutionAsync(connectionString, companyId, now.AddDays(-10));

            var plainDelete = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(connectionString,
                "DELETE FROM [idempotency].[CommandExecutions] WHERE [Id] = @id;", IdParameter("@id", expired)));
            Assert.Equal(51010, plainDelete.Number);

            const string WindowSql = """
                EXEC sys.sp_set_session_context @key = @key, @value = @cutoff;
                DELETE FROM [idempotency].[CommandExecutions] WHERE [Id] = @id;
                """;
            var newerDelete = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(connectionString, WindowSql,
                Key(), Instant("@cutoff", now.AddDays(-90)), IdParameter("@id", recent)));
            Assert.Equal(51562, newerDelete.Number);

            var windowUpdate = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(connectionString, """
                EXEC sys.sp_set_session_context @key = @key, @value = @cutoff;
                UPDATE [idempotency].[CommandExecutions] SET [Outcome] = N'Changed' WHERE [Id] = @id;
                """, Key(), Instant("@cutoff", now.AddDays(-90)), IdParameter("@id", expired)));
            Assert.Equal(51010, windowUpdate.Number);

            Assert.Equal(2, await ScalarAsync<int>(connectionString,
                "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [Id] IN (@expired, @recent);",
                IdParameter("@expired", expired), IdParameter("@recent", recent)));
            Assert.Empty(await ReadEvidenceAsync(connectionString));
        });
    }

    [SqlServerFact]
    public async Task The_purge_evidence_is_append_only_and_its_checks_refuse_an_empty_count_and_a_local_time()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var now = TruncatedUtcNow();
            var companyId = Guid.NewGuid();
            await InsertExecutionAsync(connectionString, companyId, now.AddDays(-100));
            await Job(connectionString, now, batchSize: 10).RunOnceAsync(CancellationToken.None);
            var row = Assert.Single(await ReadEvidenceAsync(connectionString));

            var update = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(connectionString,
                "UPDATE [idempotency].[RetentionPurgeRecords] SET [DeletedCount] = 99 WHERE [Id] = @id;", IdParameter("@id", row.Id)));
            Assert.Equal(51561, update.Number);
            var delete = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(connectionString,
                "DELETE FROM [idempotency].[RetentionPurgeRecords] WHERE [Id] = @id;", IdParameter("@id", row.Id)));
            Assert.Equal(51561, delete.Number);

            var zeroCount = await Assert.ThrowsAsync<SqlException>(() => InsertEvidenceAsync(
                connectionString, companyId, now.AddDays(-90), now, deletedCount: 0));
            Assert.Equal(547, zeroCount.Number);
            var localTime = await Assert.ThrowsAsync<SqlException>(() => InsertEvidenceAsync(
                connectionString, companyId, now.AddDays(-90).ToOffset(TimeSpan.FromHours(3)), now, deletedCount: 1));
            Assert.Equal(547, localTime.Number);

            Assert.Equal(1, Assert.Single(await ReadEvidenceAsync(connectionString)).DeletedCount);
        });
    }

    [SqlServerFact]
    public async Task The_retention_migrations_Down_passes_without_evidence_and_refuses_while_evidence_exists()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var guard = Assert.IsType<SqlOperation>(new AddIdempotencyRetentionPurge().DownOperations[0]).Sql;
            var now = TruncatedUtcNow();
            var companyId = Guid.NewGuid();
            await InsertExecutionAsync(connectionString, companyId, now.AddDays(-10));

            await ExecuteAsync(connectionString, guard);

            await InsertEvidenceAsync(connectionString, companyId, now.AddDays(-90), now, deletedCount: 1);
            var refused = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(connectionString, guard));
            Assert.Equal(51564, refused.Number); // Its own number: 51563 is the Finance one-step credit limit Down guard.
            Assert.Single(await ReadEvidenceAsync(connectionString));
        });
    }

    private static IdempotencyRetentionPurgeJob Job(string connectionString, DateTimeOffset now, int batchSize)
    {
        Assert.True(IdempotencyRetentionPeriod.TryParse("P90D", out var period));
        return new IdempotencyRetentionPurgeJob(
            new SqlIdempotencyRetentionPurgeStore(connectionString, TimeSpan.FromSeconds(30)),
            new IdempotencyRetentionPurgeSettings(period, PolicyRef, batchSize, TimeSpan.FromHours(6), TimeSpan.FromSeconds(30)),
            new FixedTime(now),
            NullLogger<IdempotencyRetentionPurgeJob>.Instance);
    }

    private static DateTimeOffset TruncatedUtcNow()
    {
        var now = DateTimeOffset.UtcNow;
        return new DateTimeOffset(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, TimeSpan.Zero);
    }

    private static async Task<Guid> InsertExecutionAsync(string connectionString, Guid companyId, DateTimeOffset completedAtUtc)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(connectionString, """
            INSERT INTO [idempotency].[CommandExecutions]
                ([Id], [CompanyId], [CommandName], [IdempotencyKeyHash], [RequestHash], [ActorId], [Outcome],
                 [HttpStatusCode], [ResponseJson], [OriginalCorrelationId], [CompletedAtUtc], [SchemaVersion])
            VALUES
                (@id, @companyId, @commandName, @keyHash, @requestHash, N'sentetik-actor', N'Succeeded',
                 200, N'{}', @correlationId, @completedAt, 1);
            """,
            IdParameter("@id", id),
            IdParameter("@companyId", companyId),
            new SqlParameter("@commandName", SqlDbType.NVarChar, 160) { Value = CommandName },
            new SqlParameter("@keyHash", SqlDbType.VarChar, 64) { Value = Hash() },
            new SqlParameter("@requestHash", SqlDbType.VarChar, 64) { Value = Hash() },
            new SqlParameter("@correlationId", SqlDbType.NVarChar, 128) { Value = $"sentetik-{id:N}" },
            Instant("@completedAt", completedAtUtc));
        return id;
    }

    private static Task InsertEvidenceAsync(
        string connectionString,
        Guid companyId,
        DateTimeOffset cutoffUtc,
        DateTimeOffset recordedAtUtc,
        int deletedCount) =>
        ExecuteAsync(connectionString, """
            INSERT INTO [idempotency].[RetentionPurgeRecords]
                ([Id], [RunId], [CompanyId], [CutoffUtc], [RetentionPeriod], [PolicyRef], [DeletedCount], [RecordedAtUtc])
            VALUES
                (@id, @runId, @companyId, @cutoff, 'P90D', @policyRef, @deletedCount, @recordedAt);
            """,
            IdParameter("@id", Guid.NewGuid()),
            IdParameter("@runId", Guid.NewGuid()),
            IdParameter("@companyId", companyId),
            Instant("@cutoff", cutoffUtc),
            new SqlParameter("@policyRef", SqlDbType.NVarChar, 64) { Value = PolicyRef },
            new SqlParameter("@deletedCount", SqlDbType.Int) { Value = deletedCount },
            Instant("@recordedAt", recordedAtUtc));

    private static async Task<List<EvidenceRow>> ReadEvidenceAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT [Id], [RunId], [CompanyId], [CutoffUtc], [RetentionPeriod], [PolicyRef], [DeletedCount]
            FROM [idempotency].[RetentionPurgeRecords];
            """;
        var rows = new List<EvidenceRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new EvidenceRow(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetDateTimeOffset(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetInt32(6)));
        }

        return rows;
    }

    private static async Task ExecuteAsync(string connectionString, string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static SqlParameter IdParameter(string name, Guid value) =>
        new(name, SqlDbType.UniqueIdentifier) { Value = value };

    private static SqlParameter Instant(string name, DateTimeOffset value) =>
        new(name, SqlDbType.DateTimeOffset) { Value = value };

    private static SqlParameter Key() =>
        new("@key", SqlDbType.NVarChar, 128) { Value = SqlIdempotencyRetentionPurgeStore.CutoffSessionKey };

    private static string Hash() => Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

    private static async Task WithDatabaseAsync(Func<string, Task> test)
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(baseConnectionString);
        try
        {
            // Starting the host migrates every module, the idempotency retention migration included.
            _ = factory.Services;
            await test(factory.DatabaseConnectionString);
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    private static async Task DropDatabaseAsync(string baseConnectionString, string databaseName)
    {
        Assert.Matches("^goymen_erp_ci_[0-9a-f]{32}$", databaseName);
        SqlConnection.ClearAllPools();
        await using var master = new SqlConnection(new SqlConnectionStringBuilder(baseConnectionString)
        {
            InitialCatalog = "master",
        }.ConnectionString);
        await master.OpenAsync();
        await using var drop = master.CreateCommand();
        drop.CommandText = $"""
            IF DB_ID(@name) IS NOT NULL
            BEGIN
                ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{databaseName}];
            END;
            """;
        drop.Parameters.AddWithValue("@name", databaseName);
        await drop.ExecuteNonQueryAsync();
    }

    private sealed record EvidenceRow(
        Guid Id,
        Guid RunId,
        Guid CompanyId,
        DateTimeOffset CutoffUtc,
        string RetentionPeriod,
        string PolicyRef,
        int DeletedCount);

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
