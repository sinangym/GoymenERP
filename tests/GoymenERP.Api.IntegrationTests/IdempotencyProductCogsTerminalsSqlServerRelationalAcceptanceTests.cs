using System.Text.RegularExpressions;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Idempotency.Infrastructure.Executions;
using GoymenERP.Modules.Idempotency.Infrastructure.Persistence;
using GoymenERP.Modules.Idempotency.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Actual Idempotency owner SQL and borrowed transactions. Authority snapshots here are explicitly synthetic.</summary>
public sealed class IdempotencyProductCogsTerminalsSqlServerRelationalAcceptanceTests
{
    private static readonly Guid ActorId = Guid.Parse("93000000-0000-0000-0000-000000000001");
    private static readonly Guid OriginId = Guid.Parse("94000000-0000-0000-0000-000000000001");
    private static readonly string Key = new('A', 64);
    private static readonly string Payload = new('B', 64);
    private const string CommandName = "product.card.cogs-classification.set.v1";
    private const string ExactResponse = " \n{\"source\":\"SENTETIK-COGS-owner-terminal\"} \n";

    [SqlServerFact]
    public Task Cogs_terminal_is_global_once_and_changed_actor_or_origin_cannot_mint_a_second_effect_or_read_the_first_answer() =>
        WithDatabaseAsync(async connection =>
        {
            var original = Context();
            var terminal = Terminal(original, Key);
            await InsertAsync(connection, original, Key, commit: true);
            var store = Store(connection);
            var stored = await store.ReadAsync(terminal.Binding.Key);
            Assert.NotNull(stored);
            Assert.Equal(terminal, stored);
            Assert.Equal(ExactResponse, stored.ResponseJson);
            var replay = await new GroupSharedCommandReplayResolver(new Authority(Snapshot()), store).ResolveAsync(
                GroupSharedMasterOperation.SetCogsClassification, Key, Payload);
            Assert.Equal(GroupSharedCommandReplayKind.MatchingReplay, replay.Kind);
            Assert.Equal(ExactResponse, replay.Terminal!.ResponseJson);

            foreach (var changed in new[] { Snapshot() with { LocalActorId = Guid.NewGuid() }, Snapshot() with { OriginCompanyId = Guid.NewGuid() } })
            {
                var context = GroupSharedMasterCommandContext.FromServerDecision(changed, GroupSharedMasterOperation.SetCogsClassification);
                Assert.Equal(terminal.Binding.Key, GroupSharedCommandBinding.Create(context, Key, Payload).Key);
                var conflict = await new GroupSharedCommandReplayResolver(new Authority(changed), store).ResolveAsync(
                    GroupSharedMasterOperation.SetCogsClassification, Key, Payload);
                Assert.Equal(GroupSharedCommandReplayKind.Conflict, conflict.Kind);
                Assert.Null(conflict.Terminal);
                await using var sql = new SqlConnection(connection);
                await sql.OpenAsync();
                await using var transaction = await sql.BeginTransactionAsync();
                await using var session = await GroupSharedCommandTerminalSession.EnlistAsync(sql, transaction, context, Key, Payload);
                session.Stage(Terminal(context, Key));
                await Assert.ThrowsAsync<GroupSharedCommandTerminalAlreadyExistsException>(() => session.FlushAsync(context));
                await transaction.RollbackAsync();
            }
            Assert.Equal(1, await CountAsync(connection));
            Assert.Equal(terminal, await store.ReadAsync(terminal.Binding.Key));

            // Flushing a second real terminal inside the borrowed transaction does not own its commit.
            var rolledBackKey = new string('C', 64);
            await InsertAsync(connection, original, rolledBackKey, commit: false);
            Assert.Null(await store.ReadAsync(GroupSharedCommandKey.Create(original, rolledBackKey)));
            Assert.Equal(1, await CountAsync(connection));
        });

    [SqlServerFact]
    public Task Cogs_terminals_keep_the_closed_sql_command_allowlist_authority_fence_and_forward_fix_Down_guard() =>
        WithDatabaseAsync(async connection =>
        {
            var guard = Assert.IsType<SqlOperation>(new AddProductCogsClassificationTerminals().DownOperations[0]).Sql;
            Assert.Contains("THROW 51981", guard, StringComparison.Ordinal);
            await ExecuteAsync(connection, guard);
            var legacy = GroupSharedMasterCommandContext.FromServerDecision(
                Snapshot() with { Operation = GroupSharedMasterOperation.DraftBasicEdit }, GroupSharedMasterOperation.DraftBasicEdit);
            await InsertAsync(connection, legacy, new string('D', 64), commit: true);
            await ExecuteAsync(connection, guard); // Existing commands still fit the old closed allowlist.

            var context = Context();
            await using (var sql = new SqlConnection(connection))
            {
                await sql.OpenAsync();
                await using var transaction = await sql.BeginTransactionAsync();
                await using var session = await GroupSharedCommandTerminalSession.EnlistAsync(sql, transaction, context, Key, Payload);
                session.Stage(Terminal(context, Key));
                var changed = GroupSharedMasterCommandContext.FromServerDecision(Snapshot() with { AuthorityRevision = "synthetic-rev-2" }, GroupSharedMasterOperation.SetCogsClassification);
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => session.FlushAsync(changed));
                await transaction.RollbackAsync();
            }
            Assert.Null(await Store(connection).ReadAsync(GroupSharedCommandKey.Create(context, Key)));
            await InsertAsync(connection, context, Key, commit: true);
            var refusedDown = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(connection, guard));
            Assert.Equal(51981, refusedDown.Number);
            Assert.Equal(2, await CountAsync(connection));
            foreach (var alias in new[] { "product.card.cogs-classification.set.v2", "Product.card.cogs-classification.set.v1", "product.card.cogs-classification.set.v1 " })
            {
                var invalid = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(connection, """
                    INSERT INTO [idempotency].[GroupSharedCommandTerminals]
                        ([Subject], [CommandName], [IdempotencyKeyHash], [LocalActorId], [OriginCompanyId], [RequestHash],
                         [StatusCode], [ResponseJson], [OriginalCorrelationId], [SchemaVersion], [CompletedAtUtc])
                    SELECT [Subject], @command, @key, [LocalActorId], [OriginCompanyId], [RequestHash],
                         [StatusCode], [ResponseJson], [OriginalCorrelationId], [SchemaVersion], [CompletedAtUtc]
                    FROM [idempotency].[GroupSharedCommandTerminals] WHERE [CommandName] = @original;
                    """, new SqlParameter("@command", alias), new SqlParameter("@key", new string('E', 64)), new SqlParameter("@original", CommandName)));
                Assert.Equal(547, invalid.Number);
                Assert.Contains("CK_GroupSharedTerminals_ProductCommands", invalid.Message, StringComparison.Ordinal);
            }
            Assert.Equal(2, await CountAsync(connection));
            Assert.Equal(ExactResponse, (await Store(connection).ReadAsync(GroupSharedCommandKey.Create(context, Key)))!.ResponseJson);
        });

    private static GroupSharedMasterAuthorizationSnapshot Snapshot() =>
        new(true, GroupSharedMasterKind.Product, GroupSharedMasterOperation.SetCogsClassification, ActorId, OriginId, "synthetic-rev-1");
    private static GroupSharedMasterCommandContext Context() =>
        GroupSharedMasterCommandContext.FromServerDecision(Snapshot(), GroupSharedMasterOperation.SetCogsClassification);
    private static GroupSharedCommandTerminal Terminal(GroupSharedMasterCommandContext context, string key) =>
        GroupSharedCommandTerminal.Create(GroupSharedCommandBinding.Create(context, key, Payload), 200, ExactResponse, "synthetic-original", 1);
    private static SqlGroupSharedCommandTerminalStore Store(string connection) =>
        new(new DbContextOptionsBuilder<GroupSharedCommandDbContext>().UseSqlServer(connection).Options);
    private static async Task InsertAsync(string connectionString, GroupSharedMasterCommandContext context, string key, bool commit)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var session = await GroupSharedCommandTerminalSession.EnlistAsync(connection, transaction, context, key, Payload);
        session.Stage(Terminal(context, key));
        await session.FlushAsync(context);
        Assert.True(session.IsFlushed);
        if (commit) await transaction.CommitAsync(); else await transaction.RollbackAsync();
    }
    private sealed class Authority(GroupSharedMasterAuthorizationSnapshot snapshot) : IGroupSharedMasterAuthorizationService
    {
        public Task<GroupSharedMasterAuthorizationSnapshot> GetSnapshotAsync(GroupSharedMasterKind subject,
            GroupSharedMasterOperation operation, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(GroupSharedMasterKind.Product, subject);
            Assert.Equal(GroupSharedMasterOperation.SetCogsClassification, operation);
            return Task.FromResult(snapshot);
        }
    }
    private static async Task<int> CountAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM [idempotency].[GroupSharedCommandTerminals];";
        return (int)(await command.ExecuteScalarAsync())!;
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
    private static async Task WithDatabaseAsync(Func<string, Task> test)
    {
        var baseConnection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(baseConnection);
        try
        {
            _ = factory.Services;
            // The ordinary test host keeps Product runtime disabled. Migrate this owner explicitly in the isolated
            // fixture, with its own history table; this is not a Product enablement or a new local command store.
            await using var database = new GroupSharedCommandDbContext(new DbContextOptionsBuilder<GroupSharedCommandDbContext>()
                .UseSqlServer(factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__GroupSharedCommandMigrationsHistory", "idempotency")).Options);
            await database.Database.MigrateAsync();
            await test(factory.DatabaseConnectionString);
        }
        finally
        {
            await factory.DisposeAsync();
            if (!Regex.IsMatch(factory.DatabaseName, "\\Agoymen_erp_ci_[0-9a-f]{32}\\z", RegexOptions.CultureInvariant))
                throw new InvalidOperationException("Refusing cleanup outside the isolated generated SQL fixture.");
            await using var master = new SqlConnection(new SqlConnectionStringBuilder(baseConnection) { InitialCatalog = "master" }.ConnectionString);
            await master.OpenAsync();
            await using var drop = master.CreateCommand();
            drop.CommandText = $"IF DB_ID(@name) IS NOT NULL BEGIN ALTER DATABASE [{factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{factory.DatabaseName}]; END;";
            drop.Parameters.AddWithValue("@name", factory.DatabaseName);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
