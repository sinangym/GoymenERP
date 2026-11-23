using System.Data;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Own synthetic Audit-only databases: no host startup, host audit deletion or historical migration edits.</summary>
public sealed class AuditRollbackSqlServerRelationalAcceptanceTests
{
    private const string Initial = "20260827160529_InitialAudit";

    [SqlServerFact]
    public async Task Legacy_company_history_refuses_latest_to_previous_and_zero_without_changing_payload_or_migration_history()
    {
        await using var run = new AuditDatabase();
        await run.MigrateAsync(Initial);
        var envelope = LegacyEnvelope(run.CompanyId);
        await using (var connection = await run.OpenAsync())
        await using (var command = InsertCommand(connection, envelope))
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        await run.MigrateAsync(null);

        await using (var read = run.Context())
        {
            var row = Assert.Single(await read.AuditEvents.AsNoTracking().ToArrayAsync());
            Assert.Equal(envelope, row.ToIntegrityEnvelope());
            Assert.Equal(AuditEventIntegrity.Compute(envelope), row.IntegrityHash);
        }
        var payload = await run.PayloadAsync();
        var history = await run.HistoryAsync();
        // K3-B finding 3: the founder bootstrap audit migration is now the latest and refuses first (52081), before
        // ProtectAuditHistoryRollback (51900) could run; neither payload nor history changes.
        Assert.EndsWith("_AddFounderBootstrapSystemAudit", history[^1], StringComparison.Ordinal);
        Assert.EndsWith("_ProtectAuditHistoryRollback", history[^2], StringComparison.Ordinal);
        foreach (var target in new[] { history[^2], history[^3], Migration.InitialDatabase })
        {
            var refused = await Assert.ThrowsAsync<SqlException>(() => run.MigrateAsync(target));
            Assert.Equal(52081, refused.Number);
            Assert.Equal(payload, await run.PayloadAsync());
            Assert.Equal(history, await run.HistoryAsync());
        }
    }

    [SqlServerFact]
    public async Task Empty_audit_rolls_to_zero_and_reapplies_and_its_rollback_guard_blocks_a_concurrent_append_until_transaction_ends()
    {
        await using var run = new AuditDatabase();
        await run.MigrateAsync(null);
        var expectedHistory = await run.HistoryAsync();
        await run.MigrateAsync(Migration.InitialDatabase);
        Assert.Empty(await run.HistoryAsync());
        await using (var connection = await run.OpenAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT OBJECT_ID(N'[audit].[AuditEvents]', N'U');";
            Assert.IsType<DBNull>(await command.ExecuteScalarAsync());
        }
        await run.MigrateAsync(null);
        Assert.Equal(expectedHistory, await run.HistoryAsync());

        // EF Core 10 commits per migration. The inter-migration barrier must survive
        // the successful Down commit, rather than relying on that transaction's table lock.
        // Roll back below ProtectAuditHistoryRollback (the founder bootstrap audit migration sits above it).
        await run.MigrateAsync(expectedHistory[^3]);
        Assert.Equal(expectedHistory[..^2], await run.HistoryAsync());
        await using (var empty = await run.OpenAsync())
        await using (var noRows = empty.CreateCommand())
        {
            noRows.CommandText = "UPDATE [audit].[AuditEvents] SET [Reason]=[Reason]; DELETE FROM [audit].[AuditEvents];";
            await noRows.ExecuteNonQueryAsync();
        }
        await using (var betweenMigrations = await run.OpenAsync())
        await using (var append = InsertCommand(betweenMigrations, LegacyEnvelope(run.CompanyId), latestSchema: true))
        {
            var refused = await Assert.ThrowsAsync<SqlException>(() => append.ExecuteNonQueryAsync());
            Assert.Equal(51901, refused.Number);
        }
        await run.MigrateAsync(null);
        Assert.Equal(expectedHistory, await run.HistoryAsync());

        await using (var owner = await run.OpenAsync())
        await using (var transaction = (SqlTransaction)await owner.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            await using (var guard = owner.CreateCommand())
            {
                guard.Transaction = transaction;
                guard.CommandText = Assert.IsType<SqlOperation>(
                    Assert.Single(new ProtectAuditHistoryRollback().DownOperations)).Sql;
                await guard.ExecuteNonQueryAsync();
            }

            // The contender really executes while the owner's empty-table guard transaction is still open.
            // Its own bounded lock timeout is the witness; no sleep or scheduling assumption supplies success.
            await using var contender = await run.OpenAsync();
            await using (var timeout = contender.CreateCommand())
            {
                timeout.CommandText = "SET LOCK_TIMEOUT 200;";
                await timeout.ExecuteNonQueryAsync();
            }
            await using (var append = InsertCommand(contender, LegacyEnvelope(run.CompanyId), latestSchema: true))
            {
                var blocked = await Assert.ThrowsAsync<SqlException>(() => append.ExecuteNonQueryAsync());
                Assert.Equal(1222, blocked.Number);
            }
            await transaction.RollbackAsync();
            // Reset only this test-owned session and reuse it after rollback; no server setting changes.
            await using (var timeout = contender.CreateCommand())
            {
                timeout.CommandText = "SET LOCK_TIMEOUT -1;";
                await timeout.ExecuteNonQueryAsync();
            }
            await using var retry = InsertCommand(contender, LegacyEnvelope(run.CompanyId), latestSchema: true);
            Assert.Equal(1, await retry.ExecuteNonQueryAsync());
        }
        await using var read = run.Context();
        Assert.Single(await read.AuditEvents.AsNoTracking().ToArrayAsync());
        Assert.Equal(expectedHistory, await run.HistoryAsync());
        var payload = await run.PayloadAsync();
        foreach (var sql in new[]
        {
            "UPDATE [audit].[AuditEvents] SET [Reason]=N'synthetic-forbidden';",
            "DELETE FROM [audit].[AuditEvents];",
        })
        {
            await using var connection = await run.OpenAsync();
            await using var mutate = connection.CreateCommand();
            mutate.CommandText = sql;
            var refused = await Assert.ThrowsAsync<SqlException>(() => mutate.ExecuteNonQueryAsync());
            Assert.Equal(51000, refused.Number);
            Assert.Equal(payload, await run.PayloadAsync());
            Assert.Equal(expectedHistory, await run.HistoryAsync());
        }
    }

    private static AuditEventEnvelope LegacyEnvelope(Guid companyId) => new(
        Guid.NewGuid(), companyId, new DateTimeOffset(2026, 11, 19, 0, 0, 0, TimeSpan.Zero),
        "synthetic.audit.rollback.v1", "Synthetic.Entity", "synthetic-entity", "Update", 1, 2,
        "User", "synthetic-audit-actor", "synthetic.audit.permission", "[]", "{\"value\":1}",
        "{\"value\":2}", "Synthetic rollback preservation proof", null, "None", null, "Web",
        null, null, "synthetic-audit-rollback", null, new string('A', 64), "Succeeded", 1);

    private static SqlCommand InsertCommand(SqlConnection connection, AuditEventEnvelope envelope, bool latestSchema = false)
    {
        var command = connection.CreateCommand();
        // Closed test SQL; all record values are typed parameters. Initial-schema rows really predate SubjectKind.
        command.CommandText = """
            INSERT INTO [audit].[AuditEvents]
                ([Id], [CompanyId], [OccurredAtUtc], [EventType], [EntityType], [EntityId], [Operation],
                 [EntityVersionBefore], [EntityVersionAfter], [ActorType], [ActorId], [PermissionCode],
                 [RoleCodesJson], [BeforeJson], [AfterJson], [Reason], [ApprovalContextJson], [AiInvolvement],
                 [AiContextJson], [SourceChannel], [ClientIpAddress], [UserAgent], [CorrelationId], [CausationId],
                 [IdempotencyKeyHash], [Outcome], [SchemaVersion], [IntegrityHash]
            """ + (latestSchema ? ", [SubjectKind])" : ")") + """
            VALUES
                (@Id, @CompanyId, @OccurredAtUtc, @EventType, @EntityType, @EntityId, @Operation,
                 @EntityVersionBefore, @EntityVersionAfter, @ActorType, @ActorId, @PermissionCode,
                 @RoleCodesJson, @BeforeJson, @AfterJson, @Reason, @ApprovalContextJson, @AiInvolvement,
                 @AiContextJson, @SourceChannel, @ClientIpAddress, @UserAgent, @CorrelationId, @CausationId,
                 @IdempotencyKeyHash, @Outcome, @SchemaVersion, @IntegrityHash
            """ + (latestSchema ? ", N'Company');" : ");");
        foreach (var property in typeof(AuditEventEnvelope).GetProperties())
        {
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            var sqlType = type == typeof(Guid) ? SqlDbType.UniqueIdentifier
                : type == typeof(DateTimeOffset) ? SqlDbType.DateTimeOffset
                : type == typeof(long) ? SqlDbType.BigInt
                : type == typeof(int) ? SqlDbType.Int : SqlDbType.NVarChar;
            command.Parameters.Add(new SqlParameter("@" + property.Name, sqlType)
                { Value = property.GetValue(envelope) ?? DBNull.Value });
        }
        command.Parameters.Add(new SqlParameter("@IntegrityHash", SqlDbType.VarChar, 64)
            { Value = AuditEventIntegrity.Compute(envelope) });
        return command;
    }

    private sealed class AuditDatabase : IAsyncDisposable
    {
        private readonly string baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        private readonly string name = $"goymen_audit_rollback_{Guid.NewGuid():N}";
        private string? connectionString;
        public Guid CompanyId { get; } = Guid.NewGuid();
        private string ConnectionString => connectionString ??= SqlServerRelationalApiFactory.BuildDatabaseConnectionString(
            baseConnectionString, name, Environment.GetEnvironmentVariable("GOYMEN_ERP_TEST_SQLSERVER_DATA_DIRECTORY"));

        public AuditDbContext Context()
        {
            var company = new CompanyContext();
            company.Set(CompanyId);
            return new AuditDbContext(new DbContextOptionsBuilder<AuditDbContext>()
                .UseSqlServer(ConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "audit"))
                .Options, company);
        }

        public async Task MigrateAsync(string? target)
        {
            await using var context = Context();
            await context.GetService<IMigrator>().MigrateAsync(target);
        }

        public async Task<string[]> HistoryAsync()
        {
            await using var context = Context();
            return (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        }

        public async Task<SqlConnection> OpenAsync()
        {
            var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            return connection;
        }

        public async Task<string> PayloadAsync()
        {
            await using var connection = await OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COALESCE((SELECT * FROM [audit].[AuditEvents] ORDER BY [Id] FOR JSON PATH, INCLUDE_NULL_VALUES), N'[]');";
            return Assert.IsType<string>(await command.ExecuteScalarAsync());
        }

        public async ValueTask DisposeAsync()
        {
            // Name generated privately above; never use a caller-supplied DB name or a production database.
            await using var master = new SqlConnection(new SqlConnectionStringBuilder(baseConnectionString)
                { InitialCatalog = "master", AttachDBFilename = "" }.ConnectionString);
            await master.OpenAsync();
            await using var command = master.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(@name) IS NOT NULL
                BEGIN
                    ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{name}];
                END;
                """;
            command.Parameters.Add(new SqlParameter("@name", SqlDbType.NVarChar, 128) { Value = name });
            await command.ExecuteNonQueryAsync();
            if (connectionString is not null)
            {
                using var pool = new SqlConnection(connectionString);
                SqlConnection.ClearPool(pool);
            }
        }
    }
}
