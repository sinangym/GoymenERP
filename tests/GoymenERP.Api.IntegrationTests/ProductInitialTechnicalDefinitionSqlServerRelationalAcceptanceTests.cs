using System.Text.RegularExpressions;
using System.Text.Json;
using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Fixtures;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using GoymenERP.Modules.Product.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Decision 4A against a real SQL Server. The migration publishes only the exact source-stamped
/// registry, replays through migration history, rolls back only when unreferenced and refuses any
/// pre-existing registry. The synthetic u02 fixture remains test-only and byte-for-byte semantic data.
/// </summary>
public sealed class ProductInitialTechnicalDefinitionSqlServerRelationalAcceptanceTests
{
    private const string PreviousMigration = "20260919100000_AddProductFamilyRegistry";
    private static readonly Guid ProductId = Guid.Parse("b7100000-0000-4000-8000-000000000401");
    private static readonly Guid GroupId = Guid.Parse("b7100000-0000-4000-8000-000000000101");

    [SqlServerFact]
    public async Task Exact_release_replays_and_the_unchanged_synthetic_fixture_coexists_idempotently()
    {
        await using var run = await Run.StartAsync();

        await using (var first = run.Factory.Services.CreateAsyncScope())
        {
            var context = first.ServiceProvider.GetRequiredService<ProductDbContext>();
            AssertExactGenuine(await new SqlProductFamilyDefinitionSource(context).GetAsync(default));
            await context.GetService<IMigrator>().MigrateAsync(); // Already recorded: exact no-op replay.
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var fixture = run.Factory.Services.CreateAsyncScope();
            await fixture.ServiceProvider.GetRequiredService<IProductTechnicalCardFixtureStore>()
                .EnsureAsync(default);
        }

        await using var read = run.Factory.Services.CreateAsyncScope();
        var definition = await new SqlProductFamilyDefinitionSource(
            read.ServiceProvider.GetRequiredService<ProductDbContext>()).GetAsync(default);
        ProductFamilyDefinitionRulesV2.RequireWellFormed(definition);
        Assert.Equal(5, definition.Families.Count);
        Assert.Equal(2, definition.Templates.Count);
        Assert.Equal(8, definition.Entries.Count);
        Assert.Equal(4, definition.Families.Count(row => row.SourceRef == ProductInitialTechnicalDefinitionV1.SourceRef));
        Assert.Equal(ProductTechnicalCardFixture.SourceRef,
            definition.Families.Single(row => row.Id == ProductTechnicalCardFixture.FamilyId).SourceRef);
        Assert.Equal(ProductTechnicalCardFixture.SourceRef,
            definition.Templates.Single(row => row.Id == ProductTechnicalCardFixture.TemplateId).SourceRef);
    }

    [SqlServerFact]
    public async Task Rollback_refuses_a_referenced_card_then_removes_only_the_unreferenced_release()
    {
        await using var run = await Run.StartAsync();

        await AssertChangedFootprintRefusesRollbackAsync(run.Factory,
            "UPDATE [product].[TemplateFields] SET [Role] = 'width' WHERE [TemplateId] = 'ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2' AND [TemplateVersion] = 1 AND [Key] = 'thickness-mm';",
            "UPDATE [product].[TemplateFields] SET [Role] = 'thickness' WHERE [TemplateId] = 'ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2' AND [TemplateVersion] = 1 AND [Key] = 'thickness-mm';");
        await AssertChangedFootprintRefusesRollbackAsync(run.Factory,
            "UPDATE [product].[TemplateFields] SET [ParentField] = 'coating' WHERE [TemplateId] = 'ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2' AND [TemplateVersion] = 1 AND [Key] = 'producer-grade';",
            "UPDATE [product].[TemplateFields] SET [ParentField] = 'steel-grade' WHERE [TemplateId] = 'ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2' AND [TemplateVersion] = 1 AND [Key] = 'producer-grade';");
        await AssertChangedFootprintRefusesRollbackAsync(run.Factory,
            "UPDATE [product].[DictionaryEntries] SET [SourceRef] = 'changed-source' WHERE [Dictionary] = 'gdc-producer-grades' AND [Code] = N'erdemir:3237';",
            "UPDATE [product].[DictionaryEntries] SET [SourceRef] = 'erdemir-flat-2021-p16' WHERE [Dictionary] = 'gdc-producer-grades' AND [Code] = N'erdemir:3237';");

        await ExecuteAsync(run.Factory.DatabaseConnectionString, """
            INSERT INTO [product].[CodeGroups] ([Id], [Name], [Prefix])
                VALUES ('b7100000-0000-4000-8000-000000000101', N'SENTETIK rollback guard group', 'RBG');
            INSERT INTO [product].[Products]
                ([Id], [Code], [Name], [GroupId], [ClassId], [FamilyId],
                 [PurchasableValue], [PurchasableProvenance], [SellableValue], [SellableProvenance],
                 [ProductionInputValue], [ProductionInputProvenance], [ProductionOutputValue], [ProductionOutputProvenance],
                 [Lifecycle], [Version], [CreatedAtUtc], [UpdatedAtUtc], [ReferencesJson])
            VALUES
                ('b7100000-0000-4000-8000-000000000401', 'RBG-1', N'SENTETIK rollback guard Product',
                 'b7100000-0000-4000-8000-000000000101', NULL, '2ffdf417-c7b0-4cc5-8c58-40b5cd906c55',
                 NULL, 0, NULL, 0, NULL, 0, NULL, 0, 0, 1,
                 '2026-10-04T00:00:00+00:00', '2026-10-04T00:00:00+00:00',
                 N'{"templateId":"ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2","templateVersion":"1"}');
            """);

        var refusal = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(run.Factory, PreviousMigration));
        Assert.Equal(51044, refusal.Number);
        Assert.Contains("Product cards reference", refusal.Message, StringComparison.Ordinal);
        await using (var preserved = run.Factory.Services.CreateAsyncScope())
            AssertExactGenuine(await new SqlProductFamilyDefinitionSource(
                preserved.ServiceProvider.GetRequiredService<ProductDbContext>()).GetAsync(default));

        await ExecuteAsync(run.Factory.DatabaseConnectionString, $"""
            DELETE FROM [product].[Products] WHERE [Id] = '{ProductId:D}';
            """);

        // The current draft no longer references 4A, but its immutable successful create response still
        // does. ProductCommandGateway replays that exact card/references payload, so Down must retain its source.
        await ExecuteAsync(run.Factory.DatabaseConnectionString, """
            INSERT INTO [product].[Products]
                ([Id], [Code], [Name], [GroupId], [ClassId], [FamilyId],
                 [PurchasableValue], [PurchasableProvenance], [SellableValue], [SellableProvenance],
                 [ProductionInputValue], [ProductionInputProvenance], [ProductionOutputValue], [ProductionOutputProvenance],
                 [Lifecycle], [Version], [CreatedAtUtc], [UpdatedAtUtc], [ReferencesJson])
            VALUES
                ('b7100000-0000-4000-8000-000000000401', 'RBG-1', N'SENTETIK rollback guard Product',
                 'b7100000-0000-4000-8000-000000000101', NULL, NULL,
                 NULL, 0, NULL, 0, NULL, 0, NULL, 0, 0, 2,
                 '2026-10-04T00:00:00+00:00', '2026-10-04T00:01:00+00:00', N'{}');
            """);
        await InsertHistoricalReplayAsync(run.Factory.DatabaseConnectionString);
        var historyRefusal = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(run.Factory, PreviousMigration));
        Assert.Equal(51044, historyRefusal.Number);
        Assert.Contains("replay history references", historyRefusal.Message, StringComparison.Ordinal);

        await ExecuteAsync(run.Factory.DatabaseConnectionString, $"""
            DELETE FROM [idempotency].[GroupSharedCommandTerminals]
                WHERE [Subject] = 'Product' AND [CommandName] = 'product.draft.create.v1'
                  AND [IdempotencyKeyHash] = '{new string('A', 64)}';
            DELETE FROM [product].[Products] WHERE [Id] = '{ProductId:D}';
            DELETE FROM [product].[CodeGroups] WHERE [Id] = '{GroupId:D}';
            """);
        await MigrateAsync(run.Factory, PreviousMigration);

        Assert.Equal(0, await ScalarAsync<int>(run.Factory.DatabaseConnectionString,
            "SELECT COUNT(*) FROM [product].[Families]"));
        Assert.Equal(0, await ScalarAsync<int>(run.Factory.DatabaseConnectionString,
            "SELECT COUNT(*) FROM [product].[Templates]"));
        Assert.Equal(0, await ScalarAsync<int>(run.Factory.DatabaseConnectionString,
            "SELECT COUNT(*) FROM [product].[DictionaryEntries]"));

        await MigrateAsync(run.Factory, ReleaseInitialProductTechnicalDefinition.MigrationId);
        await using var replayed = run.Factory.Services.CreateAsyncScope();
        AssertExactGenuine(await new SqlProductFamilyDefinitionSource(
            replayed.ServiceProvider.GetRequiredService<ProductDbContext>()).GetAsync(default));
    }

    [SqlServerFact]
    public async Task Release_refuses_a_preexisting_synthetic_registry_without_rewriting_it()
    {
        await using var run = await Run.StartAsync();
        await MigrateAsync(run.Factory, PreviousMigration);
        await using (var fixture = run.Factory.Services.CreateAsyncScope())
            await fixture.ServiceProvider.GetRequiredService<IProductTechnicalCardFixtureStore>()
                .EnsureAsync(default);

        var refusal = await Assert.ThrowsAsync<SqlException>(() =>
            MigrateAsync(run.Factory, ReleaseInitialProductTechnicalDefinition.MigrationId));
        Assert.Equal(51044, refusal.Number);
        Assert.Contains("requires an empty registry", refusal.Message, StringComparison.Ordinal);

        await using var preserved = run.Factory.Services.CreateAsyncScope();
        var actual = await new SqlProductFamilyDefinitionSource(
            preserved.ServiceProvider.GetRequiredService<ProductDbContext>()).GetAsync(default);
        Assert.Equal(ProductTechnicalCardFixture.FamilyId, Assert.Single(actual.Families).Id);
        Assert.Equal(ProductTechnicalCardFixture.TemplateId, Assert.Single(actual.Templates).Id);
        Assert.All(actual.Families, row => Assert.Equal(ProductTechnicalCardFixture.SourceRef, row.SourceRef));
    }

    private static void AssertExactGenuine(ProductFamilyDefinitionV2 actual)
    {
        ProductFamilyDefinitionRulesV2.RequireWellFormed(actual);
        var expected = ProductInitialTechnicalDefinitionV1.Definition();
        Assert.Equal(expected.Families.OrderBy(row => row.Id), actual.Families.OrderBy(row => row.Id));
        Assert.Equal(expected.Entries.OrderBy(row => row.Dictionary).ThenBy(row => row.Code),
            actual.Entries.OrderBy(row => row.Dictionary).ThenBy(row => row.Code));
        var expectedTemplate = Assert.Single(expected.Templates);
        var actualTemplate = Assert.Single(actual.Templates);
        Assert.Equal((expectedTemplate.Id, expectedTemplate.Version, expectedTemplate.Code, expectedTemplate.Name, expectedTemplate.SourceRef),
            (actualTemplate.Id, actualTemplate.Version, actualTemplate.Code, actualTemplate.Name, actualTemplate.SourceRef));
        Assert.Equal(expectedTemplate.FamilyIds.Order(), actualTemplate.FamilyIds.Order());
        Assert.Equal(expectedTemplate.Fields, actualTemplate.Fields);
    }

    private static async Task MigrateAsync(SqlServerRelationalApiFactory factory, string? targetMigration = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ProductDbContext>()
            .GetService<IMigrator>()
            .MigrateAsync(targetMigration);
    }

    private static async Task AssertChangedFootprintRefusesRollbackAsync(SqlServerRelationalApiFactory factory,
        string mutate, string restore)
    {
        await ExecuteAsync(factory.DatabaseConnectionString, mutate);
        var refusal = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(factory, PreviousMigration));
        Assert.Equal(51044, refusal.Number);
        Assert.Contains("partial or changed", refusal.Message, StringComparison.Ordinal);
        await ExecuteAsync(factory.DatabaseConnectionString, restore);
    }

    private static async Task InsertHistoricalReplayAsync(string connectionString)
    {
        var at = DateTimeOffset.Parse("2026-10-04T00:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture);
        var product = new ProductDraftView(ProductId, "RBG-1", "SENTETIK rollback guard Product", GroupId,
            null, ProductInitialTechnicalDefinitionV1.HotRolledSlitCoilFamilyId,
            new(new(null, "not-evaluated"), new(null, "not-evaluated"),
                new(null, "not-evaluated"), new(null, "not-evaluated")),
            "Draft", "1", at, at, new(null, null, null, null), new("Complete", []));
        var references = new ProductReferences(ProductInitialTechnicalDefinitionV1.TemplateId, "1",
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["thickness-mm"] = JsonSerializer.SerializeToElement(3m),
                ["width-mm"] = JsonSerializer.SerializeToElement(248m),
                ["steel-grade"] = JsonSerializer.SerializeToElement("S235JR"),
                ["coating"] = JsonSerializer.SerializeToElement("uncoated"),
            });
        var responseJson = JsonSerializer.Serialize(new ProductMutationResponse(new(product, references), false),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO [idempotency].[GroupSharedCommandTerminals]
                ([Subject], [CommandName], [IdempotencyKeyHash], [LocalActorId], [OriginCompanyId], [RequestHash],
                 [StatusCode], [ResponseJson], [OriginalCorrelationId], [SchemaVersion], [CompletedAtUtc])
            VALUES
                ('Product', 'product.draft.create.v1', @key, '20000000-0000-0000-0000-000000000003',
                 '10000000-0000-0000-0000-000000000001', @request, 201, @response,
                 'product-4a-rollback-history', 1, '2026-10-04T00:00:00+00:00');
            """;
        command.Parameters.AddWithValue("@key", new string('A', 64));
        command.Parameters.AddWithValue("@request", new string('B', 64));
        command.Parameters.AddWithValue("@response", responseJson);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)(await command.ExecuteScalarAsync())!;
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
            var factory = new SqlServerRelationalApiFactory(baseConnection,
                services => services.AddProductTechnicalCardFixtureStore(), databaseName: databaseName);
            using var startup = factory.CreateClient();
            // Product runtime is deliberately disabled by default, so host startup does not own this
            // migration acceptance test's Product schema. Apply the current Product migration chain
            // explicitly before reading or mutating its release tables.
            await using (var product = factory.Services.CreateAsyncScope())
            {
                await product.ServiceProvider.GetRequiredService<ProductDbContext>()
                    .Database.MigrateAsync();
            }

            return new Run(baseConnection, factory);
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
