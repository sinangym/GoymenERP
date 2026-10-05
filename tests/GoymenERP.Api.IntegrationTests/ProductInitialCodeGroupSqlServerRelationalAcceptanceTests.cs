using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using GoymenERP.Modules.Idempotency.Infrastructure.Persistence;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using GoymenERP.Modules.Product.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// The first normal Product code group against real SQL Server. The release is data-only, refuses
/// identity/name/prefix collisions and cannot be removed after Product or immutable replay history
/// starts referring to it.
/// </summary>
public sealed class ProductInitialCodeGroupSqlServerRelationalAcceptanceTests
{
    private const string PreviousMigration = ReleaseInitialProductTechnicalDefinition.MigrationId;
    private static readonly Guid ProductId = Guid.Parse("829e576d-c635-502a-97b8-9d5074c9d6a1");

    [SqlServerFact]
    public async Task Exact_release_is_readable_and_migration_history_replays_without_a_second_row()
    {
        await using var run = await Run.StartAsync();

        await AssertExactAsync(run.Factory.DatabaseConnectionString);
        await MigrateAsync(run.Factory); // Exact migration history replay: no second effect.
        await AssertExactAsync(run.Factory.DatabaseConnectionString);
    }

    [SqlServerFact]
    public async Task Release_refuses_an_existing_identity_name_or_prefix_without_adopting_it()
    {
        await using var run = await Run.StartAsync();
        await MigrateAsync(run.Factory, PreviousMigration);
        var conflictId = Guid.Parse("829e576d-c635-502a-97b8-9d5074c9d6a2");
        await ExecuteAsync(run.Factory.DatabaseConnectionString, $"""
            INSERT INTO [product].[CodeGroups] ([Id], [Name], [Prefix])
            VALUES ('{conflictId:D}', N'Unverified group', 'SAC');
            """);

        var refusal = await Assert.ThrowsAsync<SqlException>(() =>
            MigrateAsync(run.Factory, ReleaseInitialProductCodeGroup.MigrationId));

        Assert.Equal(51045, refusal.Number);
        Assert.Contains("conflicts with an existing group", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(("Unverified group", "SAC"), await ReadGroupAsync(run.Factory.DatabaseConnectionString, conflictId));
        Assert.Equal(0, await CountAsync(run.Factory.DatabaseConnectionString,
            $"SELECT COUNT(*) FROM [product].[CodeGroups] WHERE [Id] = '{ProductInitialCodeGroupV1.SacGroupId:D}'"));
    }

    [SqlServerFact]
    public async Task Rollback_refuses_changed_product_and_replay_references_then_removes_only_the_unused_exact_group()
    {
        await using var run = await Run.StartAsync();

        foreach (var mutation in new[]
                 {
                     "SET [Name] = N'Changed'",
                     "SET [Name] = N'sac'",
                     "SET [Name] = N'Sac '",
                     "SET [Prefix] = 'sac'",
                     "SET [Prefix] = 'SAC '",
                 })
        {
            await ExecuteAsync(run.Factory.DatabaseConnectionString, $"""
                UPDATE [product].[CodeGroups] {mutation}
                WHERE [Id] = '829e576d-c635-502a-97b8-9d5074c9d6a0';
                """);
            var changed = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(run.Factory, PreviousMigration));
            Assert.Equal(51045, changed.Number);
            Assert.Contains("missing or changed", changed.Message, StringComparison.Ordinal);
            Assert.Equal(1, await CountAsync(run.Factory.DatabaseConnectionString,
                $"SELECT COUNT(*) FROM [product].[CodeGroups] WHERE [Id] = '{ProductInitialCodeGroupV1.SacGroupId:D}'"));
            await ExecuteAsync(run.Factory.DatabaseConnectionString, """
                UPDATE [product].[CodeGroups] SET [Name] = N'Sac', [Prefix] = 'SAC'
                WHERE [Id] = '829e576d-c635-502a-97b8-9d5074c9d6a0';
                """);
        }

        await InsertProductAsync(run.Factory.DatabaseConnectionString);
        var referenced = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(run.Factory, PreviousMigration));
        Assert.Equal(51045, referenced.Number);
        Assert.Contains("Product cards or code history reference", referenced.Message, StringComparison.Ordinal);
        await ExecuteAsync(run.Factory.DatabaseConnectionString,
            $"DELETE FROM [product].[Products] WHERE [Id] = '{ProductId:D}'");

        await InsertReplayAsync(run.Factory.DatabaseConnectionString);
        var historical = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(run.Factory, PreviousMigration));
        Assert.Equal(51045, historical.Number);
        Assert.Contains("replay history references", historical.Message, StringComparison.Ordinal);
        await ExecuteAsync(run.Factory.DatabaseConnectionString, """
            DELETE FROM [idempotency].[GroupSharedCommandTerminals]
            WHERE [Subject] = 'Product' AND [IdempotencyKeyHash] = REPLICATE('C', 64);
            """);

        await InsertImportReplayAsync(run.Factory.DatabaseConnectionString);
        var importHistory = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(run.Factory, PreviousMigration));
        Assert.Equal(51045, importHistory.Number);
        Assert.Contains("replay history references", importHistory.Message, StringComparison.Ordinal);
        Assert.Equal(1, await CountAsync(run.Factory.DatabaseConnectionString,
            $"SELECT COUNT(*) FROM [product].[CodeGroups] WHERE [Id] = '{ProductInitialCodeGroupV1.SacGroupId:D}'"));
        await ExecuteAsync(run.Factory.DatabaseConnectionString, """
            DELETE FROM [idempotency].[GroupSharedCommandTerminals]
            WHERE [Subject] = 'Product' AND [IdempotencyKeyHash] = REPLICATE('E', 64);
            """);

        await MigrateAsync(run.Factory, PreviousMigration);
        Assert.Equal(0, await CountAsync(run.Factory.DatabaseConnectionString,
            $"SELECT COUNT(*) FROM [product].[CodeGroups] WHERE [Id] = '{ProductInitialCodeGroupV1.SacGroupId:D}'"));

        await MigrateAsync(run.Factory, ReleaseInitialProductCodeGroup.MigrationId);
        await AssertExactAsync(run.Factory.DatabaseConnectionString);
    }

    private static async Task InsertProductAsync(string connectionString) => await ExecuteAsync(connectionString, $$"""
        INSERT INTO [product].[Products]
            ([Id], [Code], [Name], [GroupId], [ClassId], [FamilyId],
             [PurchasableValue], [PurchasableProvenance], [SellableValue], [SellableProvenance],
             [ProductionInputValue], [ProductionInputProvenance], [ProductionOutputValue], [ProductionOutputProvenance],
             [Lifecycle], [Version], [CreatedAtUtc], [UpdatedAtUtc], [ReferencesJson])
        VALUES
            ('{{ProductId:D}}', 'SAC-1', N'Rollback reference', '{{ProductInitialCodeGroupV1.SacGroupId:D}}', NULL, NULL,
             NULL, 0, NULL, 0, NULL, 0, NULL, 0, 0, 1,
             '2026-10-04T00:00:00+00:00', '2026-10-04T00:00:00+00:00', N'{}');
        """);

    private static async Task InsertReplayAsync(string connectionString) => await ExecuteAsync(connectionString, $$"""
        INSERT INTO [idempotency].[GroupSharedCommandTerminals]
            ([Subject], [CommandName], [IdempotencyKeyHash], [LocalActorId], [OriginCompanyId], [RequestHash],
             [StatusCode], [ResponseJson], [OriginalCorrelationId], [SchemaVersion], [CompletedAtUtc])
        VALUES ('Product', 'product.draft.create.v1', REPLICATE('C', 64),
            '20000000-0000-0000-0000-000000000003', '10000000-0000-0000-0000-000000000001', REPLICATE('D', 64),
            201, N'{"card":{"product":{"groupId":"{{ProductInitialCodeGroupV1.SacGroupId:D}}"} } }',
            'product-sac-rollback-history', 1, '2026-10-04T00:00:00+00:00');
        """);

    private static async Task InsertImportReplayAsync(string connectionString) => await ExecuteAsync(connectionString, $$"""
        INSERT INTO [idempotency].[GroupSharedCommandTerminals]
            ([Subject], [CommandName], [IdempotencyKeyHash], [LocalActorId], [OriginCompanyId], [RequestHash],
             [StatusCode], [ResponseJson], [OriginalCorrelationId], [SchemaVersion], [CompletedAtUtc])
        VALUES ('Product', 'product.draft.create.v1', REPLICATE('E', 64),
            '20000000-0000-0000-0000-000000000003', '10000000-0000-0000-0000-000000000001', REPLICATE('F', 64),
            201, N'{"products":[{"productId":"{{ProductId:D}}","code":"SAC-1","legacyCode":"LEGACY-SAC-1","name":"Import history","groupId":"{{ProductInitialCodeGroupV1.SacGroupId:D}}"}],"isReplay":false,"originalCorrelationId":"product-sac-import-history"}',
            'product-sac-import-history', 1, '2026-10-04T00:00:00+00:00');
        """);

    private static async Task AssertExactAsync(string connectionString)
    {
        Assert.Equal(("Sac", "SAC"),
            await ReadGroupAsync(connectionString, ProductInitialCodeGroupV1.SacGroupId));
        Assert.Equal(1, await CountAsync(connectionString,
            "SELECT COUNT(*) FROM [product].[CodeGroups] WHERE [Name] = N'Sac' AND [Prefix] = 'SAC'"));
    }

    private static async Task<(string Name, string Prefix)> ReadGroupAsync(string connectionString, Guid id)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT [Name], [Prefix] FROM [product].[CodeGroups] WHERE [Id] = @id";
        command.Parameters.AddWithValue("@id", id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetString(1));
    }

    private static async Task<int> CountAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task MigrateAsync(SqlServerRelationalApiFactory factory, string? targetMigration = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ProductDbContext>()
            .GetService<IMigrator>().MigrateAsync(targetMigration);
    }

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnection;

        private Run(string baseConnection, SqlServerRelationalApiFactory factory)
        {
            this.baseConnection = baseConnection;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }

        public static async Task<Run> StartAsync()
        {
            var baseConnection = Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var databaseName = "goymen_erp_ci_" + Guid.NewGuid().ToString("N");
            var factory = new SqlServerRelationalApiFactory(baseConnection, databaseName: databaseName);
            var run = new Run(baseConnection, factory);
            var cleanupActions = new Stack<Func<ValueTask>>();
            var cleanupStarted = false;

            T Track<T>(T resource) where T : IDisposable
            {
                cleanupActions.Push(() =>
                {
                    resource.Dispose();
                    return ValueTask.CompletedTask;
                });
                return resource;
            }

            T TrackAsync<T>(T resource) where T : IAsyncDisposable
            {
                cleanupActions.Push(resource.DisposeAsync);
                return resource;
            }

            async Task DisposeCreatedAsync(bool suppressFailures)
            {
                if (cleanupStarted) return;
                cleanupStarted = true;
                ExceptionDispatchInfo? cleanupFailure = null;
                while (cleanupActions.TryPop(out var dispose))
                {
                    try { await dispose(); }
                    catch (Exception exception) { cleanupFailure ??= ExceptionDispatchInfo.Capture(exception); }
                }
                if (!suppressFailures) cleanupFailure?.Throw();
            }

            try
            {
                _ = Track(factory.CreateClient());
                var migration = TrackAsync(factory.Services.CreateAsyncScope());
                await migration.ServiceProvider.GetRequiredService<ProductDbContext>().Database.MigrateAsync();
                await migration.ServiceProvider.GetRequiredService<GroupSharedCommandDbContext>().Database.MigrateAsync();
                await DisposeCreatedAsync(suppressFailures: false);
                return run;
            }
            catch
            {
                await DisposeCreatedAsync(suppressFailures: true);
                try { await run.DisposeAsync(); }
                catch { }
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            if (!Regex.IsMatch(Factory.DatabaseName, "\\Agoymen_erp_ci_[0-9a-f]{32}\\z", RegexOptions.CultureInvariant))
                throw new InvalidOperationException("Refusing cleanup outside this test's generated database identity.");
            await using var master = new SqlConnection(new SqlConnectionStringBuilder(baseConnection)
                { InitialCatalog = "master" }.ConnectionString);
            await master.OpenAsync();
            await using var drop = master.CreateCommand();
            drop.CommandText = $"IF DB_ID(N'{Factory.DatabaseName}') IS NOT NULL BEGIN ALTER DATABASE [{Factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Factory.DatabaseName}]; END;";
            await drop.ExecuteNonQueryAsync();
        }
    }
}
