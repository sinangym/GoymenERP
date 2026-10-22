using System.Text.RegularExpressions;
using System.Text.Json;
using GoymenERP.Modules.Idempotency.Infrastructure.Persistence;
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
using Xunit.Abstractions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Decision 4A against a real SQL Server. The migration publishes only the exact source-stamped
/// registry, replays through migration history, rolls back only when unreferenced and refuses any
/// pre-existing registry. The synthetic u02 fixture remains test-only and byte-for-byte semantic data.
/// </summary>
public sealed class ProductInitialTechnicalDefinitionSqlServerRelationalAcceptanceTests(ITestOutputHelper output)
{
    private const string RollbackSetupPhase = "CI_SAFE_DIAGNOSTIC:product_technical_release_rollback_setup";
    private const string RollbackFootprintPhase = "CI_SAFE_DIAGNOSTIC:product_technical_release_rollback_footprint";
    private const string RollbackCardReferencePhase = "CI_SAFE_DIAGNOSTIC:product_technical_release_rollback_card_reference";
    private const string RollbackReplayPrerequisitePhase = "CI_SAFE_DIAGNOSTIC:product_technical_release_rollback_replay_prerequisite";
    private const string RollbackReplayTerminalInsertPhase = "CI_SAFE_DIAGNOSTIC:product_technical_release_rollback_replay_terminal_insert";
    private const string RollbackReplayRefusalPhase = "CI_SAFE_DIAGNOSTIC:product_technical_release_rollback_replay_refusal";
    private const string RollbackUnreferencedPhase = "CI_SAFE_DIAGNOSTIC:product_technical_release_rollback_unreferenced";
    private const string RollbackReapplyPhase = "CI_SAFE_DIAGNOSTIC:product_technical_release_rollback_reapply";
    private const string RollbackLifetimePhase = "CI_SAFE_DIAGNOSTIC:product_technical_release_rollback_lifetime";
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
        Assert.Equal(9, definition.Families.Count);
        Assert.Equal(6, definition.Templates.Count);
        Assert.Equal(8, definition.Entries.Count);
        Assert.Equal(4, definition.Families.Count(row => row.SourceRef == ProductInitialTechnicalDefinitionV1.SourceRef));
        Assert.Equal(ProductInitialTechnicalDefinitionV1.CutSheetPlateSourceRef,
            definition.Families.Single(row =>
                row.Id == ProductInitialTechnicalDefinitionV1.HotRolledCutSheetPlateFamilyId).SourceRef);
        Assert.Equal(3, definition.Families.Count(row =>
            row.SourceRef == ProductInitialTechnicalDefinitionV1.WeldedTubeProfileSourceRef));
        Assert.Equal(3, definition.Templates.Count(row =>
            row.SourceRef == ProductInitialTechnicalDefinitionV1.WeldedTubeProfileSourceRef));
        Assert.Equal(ProductTechnicalCardFixture.SourceRef,
            definition.Families.Single(row => row.Id == ProductTechnicalCardFixture.FamilyId).SourceRef);
        Assert.Equal(ProductTechnicalCardFixture.SourceRef,
            definition.Templates.Single(row => row.Id == ProductTechnicalCardFixture.TemplateId).SourceRef);
    }

    [SqlServerFact]
    public async Task Welded_release_refuses_changed_card_or_replay_footprints_then_reapplies_exactly()
    {
        await using var run = await Run.StartAsync();

        await MigrateAsync(run.Factory, ReleaseHotRolledCutSheetPlateTechnicalDefinition.MigrationId);
        await AssertWeldedPrerequisiteDriftRefusesPublicationAsync(run.Factory,
            "UPDATE [product].[DictionaryEntries] SET [ParentCode] = N'changed-parent' WHERE [Dictionary] = 'gdc-producer-grades' AND [Code] = N'erdemir:3237';",
            "UPDATE [product].[DictionaryEntries] SET [ParentCode] = N'S235JR' WHERE [Dictionary] = 'gdc-producer-grades' AND [Code] = N'erdemir:3237';");
        await AssertWeldedPrerequisiteDriftRefusesPublicationAsync(run.Factory,
            "UPDATE [product].[DictionaryEntries] SET [DisplayName] = N's235jr' WHERE [Dictionary] = 'gdc-steel-grades' AND [Code] = N'S235JR';",
            "UPDATE [product].[DictionaryEntries] SET [DisplayName] = N'S235JR' WHERE [Dictionary] = 'gdc-steel-grades' AND [Code] = N'S235JR';");
        await AssertWeldedPrerequisiteDriftRefusesPublicationAsync(run.Factory,
            "UPDATE [product].[DictionaryEntries] SET [SourceRef] = 'PRODUCT-SOURCE-40EB0B82' WHERE [Dictionary] = 'gdc-steel-grades' AND [Code] = N'S235JR';",
            "UPDATE [product].[DictionaryEntries] SET [SourceRef] = 'product-source-40eb0b82' WHERE [Dictionary] = 'gdc-steel-grades' AND [Code] = N'S235JR';");
        await AssertWeldedPrerequisiteDriftRefusesPublicationAsync(run.Factory,
            "UPDATE [product].[DictionaryEntries] SET [SourceRef] = 'product-source-40eb0b82 ' WHERE [Dictionary] = 'gdc-steel-grades' AND [Code] = N'S235JR';",
            "UPDATE [product].[DictionaryEntries] SET [SourceRef] = 'product-source-40eb0b82' WHERE [Dictionary] = 'gdc-steel-grades' AND [Code] = N'S235JR';");
        await MigrateAsync(run.Factory);

        await ExecuteAsync(run.Factory.DatabaseConnectionString, """
            UPDATE [product].[TemplateFields]
               SET [Role] = 'outside-diameter-changed'
             WHERE [TemplateId] = 'f944d764-56f6-4a04-9a31-65074b2c06c1'
               AND [TemplateVersion] = 1 AND [Key] = 'outside-diameter-mm';
            """);
        var changed = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(run.Factory,
            ReleaseHotRolledCutSheetPlateTechnicalDefinition.MigrationId));
        Assert.Equal(51045, changed.Number);
        Assert.Contains("partial or changed", changed.Message, StringComparison.Ordinal);
        await ExecuteAsync(run.Factory.DatabaseConnectionString, """
            UPDATE [product].[TemplateFields]
               SET [Role] = 'outside-diameter'
             WHERE [TemplateId] = 'f944d764-56f6-4a04-9a31-65074b2c06c1'
               AND [TemplateVersion] = 1 AND [Key] = 'outside-diameter-mm';
            INSERT INTO [product].[CodeGroups] ([Id], [Name], [Prefix])
                VALUES ('b7100000-0000-4000-8000-000000000101', N'SENTETIK welded rollback group', 'WRG');
            INSERT INTO [product].[Products]
                ([Id], [Code], [Name], [GroupId], [ClassId], [FamilyId],
                 [PurchasableValue], [PurchasableProvenance], [SellableValue], [SellableProvenance],
                 [ProductionInputValue], [ProductionInputProvenance], [ProductionOutputValue], [ProductionOutputProvenance],
                 [Lifecycle], [Version], [CreatedAtUtc], [UpdatedAtUtc], [ReferencesJson])
            VALUES
                ('b7100000-0000-4000-8000-000000000401', 'WRG-1', N'SENTETIK kaynaklı yuvarlak boru',
                 'b7100000-0000-4000-8000-000000000101', NULL, 'b5f197c1-9d8f-4d75-a170-4dd1cf0a6a99',
                 NULL, 0, NULL, 0, NULL, 0, NULL, 0, 0, 1,
                 '2026-10-16T10:00:00+00:00', '2026-10-16T10:00:00+00:00',
                 N'{"templateId":"f944d764-56f6-4a04-9a31-65074b2c06c1","templateVersion":"1","attributes":{"outside-diameter-mm":48.3,"wall-thickness-mm":1.5,"length-mm":6000,"steel-grade":"S235JR","coating":"uncoated"}}');
            """);

        var referenced = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(run.Factory,
            ReleaseHotRolledCutSheetPlateTechnicalDefinition.MigrationId));
        Assert.Equal(51045, referenced.Number);
        Assert.Contains("Product cards reference", referenced.Message, StringComparison.Ordinal);

        await ExecuteAsync(run.Factory.DatabaseConnectionString, $"""
            DELETE FROM [product].[Products] WHERE [Id] = '{ProductId:D}';
            """);
        await InsertWeldedHistoricalReplayAsync(run.Factory.DatabaseConnectionString);
        var replay = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(run.Factory,
            ReleaseHotRolledCutSheetPlateTechnicalDefinition.MigrationId));
        Assert.Equal(51045, replay.Number);
        Assert.Contains("replay history references", replay.Message, StringComparison.Ordinal);

        await ExecuteAsync(run.Factory.DatabaseConnectionString, $"""
            DELETE FROM [idempotency].[GroupSharedCommandTerminals]
             WHERE [Subject] = 'Product' AND [CommandName] = 'product.draft.create.v1'
               AND [IdempotencyKeyHash] = '{new string('C', 64)}';
            DELETE FROM [product].[CodeGroups] WHERE [Id] = '{GroupId:D}';
            """);
        await MigrateAsync(run.Factory, ReleaseHotRolledCutSheetPlateTechnicalDefinition.MigrationId);
        Assert.Equal(5, await ScalarAsync<int>(run.Factory.DatabaseConnectionString,
            "SELECT COUNT(*) FROM [product].[Families]"));
        Assert.Equal(2, await ScalarAsync<int>(run.Factory.DatabaseConnectionString,
            "SELECT COUNT(*) FROM [product].[Templates]"));
        Assert.Equal(0, await ScalarAsync<int>(run.Factory.DatabaseConnectionString,
            "SELECT COUNT(*) FROM [product].[Families] WHERE [SourceRef] = 'issue66-6210091477-p013-a'"));

        await MigrateAsync(run.Factory);
        await using var replayed = run.Factory.Services.CreateAsyncScope();
        AssertExactGenuine(await new SqlProductFamilyDefinitionSource(
            replayed.ServiceProvider.GetRequiredService<ProductDbContext>()).GetAsync(default));
    }

    [SqlServerFact]
    public async Task Cut_sheet_plate_release_refuses_a_referenced_card_then_replays_without_touching_legacy_rows()
    {
        await using var run = await Run.StartAsync();
        await ExecuteAsync(run.Factory.DatabaseConnectionString, """
            INSERT INTO [product].[CodeGroups] ([Id], [Name], [Prefix])
                VALUES ('b7100000-0000-4000-8000-000000000101', N'SENTETIK cut-sheet rollback group', 'CSG');
            INSERT INTO [product].[Products]
                ([Id], [Code], [Name], [GroupId], [ClassId], [FamilyId],
                 [PurchasableValue], [PurchasableProvenance], [SellableValue], [SellableProvenance],
                 [ProductionInputValue], [ProductionInputProvenance], [ProductionOutputValue], [ProductionOutputProvenance],
                 [Lifecycle], [Version], [CreatedAtUtc], [UpdatedAtUtc], [ReferencesJson])
            VALUES
                ('b7100000-0000-4000-8000-000000000401', 'CSG-1', N'SENTETIK kesilmiş sac Product',
                 'b7100000-0000-4000-8000-000000000101', NULL, '075d01d5-c57c-44b9-903a-89c00e4db029',
                 NULL, 0, NULL, 0, NULL, 0, NULL, 0, 0, 1,
                 '2026-10-15T11:00:00+00:00', '2026-10-15T11:00:00+00:00',
                 N'{"templateId":"57937348-393e-4982-a5a4-63eff8be40c9","templateVersion":"1","attributes":{"thickness-mm":3,"width-mm":1250,"length-mm":2500,"steel-grade":"S235JR","coating":"uncoated"}}');
            """);

        var refusal = await Assert.ThrowsAsync<SqlException>(() =>
            MigrateAsync(run.Factory, ReleaseInitialProductTechnicalDefinition.MigrationId));
        Assert.Equal(51044, refusal.Number);
        Assert.Contains("Product cards reference the hot-rolled cut-sheet/plate release", refusal.Message,
            StringComparison.Ordinal);

        await ExecuteAsync(run.Factory.DatabaseConnectionString, $"""
            DELETE FROM [product].[Products] WHERE [Id] = '{ProductId:D}';
            DELETE FROM [product].[CodeGroups] WHERE [Id] = '{GroupId:D}';
            """);
        await MigrateAsync(run.Factory, ReleaseInitialProductTechnicalDefinition.MigrationId);
        Assert.Equal(4, await ScalarAsync<int>(run.Factory.DatabaseConnectionString,
            "SELECT COUNT(*) FROM [product].[Families]"));
        Assert.Equal(1, await ScalarAsync<int>(run.Factory.DatabaseConnectionString,
            "SELECT COUNT(*) FROM [product].[Templates]"));

        await MigrateAsync(run.Factory);
        await using var replayed = run.Factory.Services.CreateAsyncScope();
        AssertExactGenuine(await new SqlProductFamilyDefinitionSource(
            replayed.ServiceProvider.GetRequiredService<ProductDbContext>()).GetAsync(default));
    }

    [SqlServerFact]
    public async Task Rollback_refuses_a_referenced_card_then_removes_only_the_unreferenced_release()
    {
        var failurePhase = RollbackSetupPhase;
        try
        {
            await using var run = await Run.StartAsync();

            failurePhase = RollbackFootprintPhase;
            await AssertChangedFootprintRefusesRollbackAsync(run.Factory,
            "UPDATE [product].[TemplateFields] SET [Role] = 'width' WHERE [TemplateId] = 'ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2' AND [TemplateVersion] = 1 AND [Key] = 'thickness-mm';",
            "UPDATE [product].[TemplateFields] SET [Role] = 'thickness' WHERE [TemplateId] = 'ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2' AND [TemplateVersion] = 1 AND [Key] = 'thickness-mm';");
            await AssertChangedFootprintRefusesRollbackAsync(run.Factory,
            "UPDATE [product].[TemplateFields] SET [ParentField] = 'coating' WHERE [TemplateId] = 'ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2' AND [TemplateVersion] = 1 AND [Key] = 'producer-grade';",
            "UPDATE [product].[TemplateFields] SET [ParentField] = 'steel-grade' WHERE [TemplateId] = 'ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2' AND [TemplateVersion] = 1 AND [Key] = 'producer-grade';");
            await AssertChangedFootprintRefusesRollbackAsync(run.Factory,
            "UPDATE [product].[DictionaryEntries] SET [SourceRef] = 'changed-source' WHERE [Dictionary] = 'gdc-producer-grades' AND [Code] = N'erdemir:3237';",
            "UPDATE [product].[DictionaryEntries] SET [SourceRef] = 'erdemir-flat-2021-p16' WHERE [Dictionary] = 'gdc-producer-grades' AND [Code] = N'erdemir:3237';");
            await AssertChangedFootprintRefusesRollbackAsync(run.Factory,
                "UPDATE [product].[Families] SET [Name] = N'kaynaklı Yuvarlak Boru' WHERE [Id] = 'b5f197c1-9d8f-4d75-a170-4dd1cf0a6a99';",
                "UPDATE [product].[Families] SET [Name] = N'Kaynaklı Yuvarlak Boru' WHERE [Id] = 'b5f197c1-9d8f-4d75-a170-4dd1cf0a6a99';");
            await AssertChangedFootprintRefusesRollbackAsync(run.Factory,
                "UPDATE [product].[Families] SET [Cins] = N'Boru ' WHERE [Id] = 'b5f197c1-9d8f-4d75-a170-4dd1cf0a6a99';",
                "UPDATE [product].[Families] SET [Cins] = N'Boru' WHERE [Id] = 'b5f197c1-9d8f-4d75-a170-4dd1cf0a6a99';");
            await AssertChangedFootprintRefusesRollbackAsync(run.Factory,
                "UPDATE [product].[Families] SET [SourceRef] = 'issue66-6210091477-p013-a ' WHERE [Id] = 'b5f197c1-9d8f-4d75-a170-4dd1cf0a6a99';",
                "UPDATE [product].[Families] SET [SourceRef] = 'issue66-6210091477-p013-a' WHERE [Id] = 'b5f197c1-9d8f-4d75-a170-4dd1cf0a6a99';");
            await AssertChangedFootprintRefusesRollbackAsync(run.Factory,
                "UPDATE [product].[Templates] SET [Name] = N'gdc Kaynaklı Yuvarlak Boru Teknik Kartı' WHERE [Id] = 'f944d764-56f6-4a04-9a31-65074b2c06c1' AND [Version] = 1;",
                "UPDATE [product].[Templates] SET [Name] = N'GDC Kaynaklı Yuvarlak Boru Teknik Kartı' WHERE [Id] = 'f944d764-56f6-4a04-9a31-65074b2c06c1' AND [Version] = 1;");
            await AssertChangedFootprintRefusesRollbackAsync(run.Factory,
                "UPDATE [product].[Templates] SET [SourceRef] = 'ISSUE66-6210091477-P013-A' WHERE [Id] = 'f944d764-56f6-4a04-9a31-65074b2c06c1' AND [Version] = 1;",
                "UPDATE [product].[Templates] SET [SourceRef] = 'issue66-6210091477-p013-a' WHERE [Id] = 'f944d764-56f6-4a04-9a31-65074b2c06c1' AND [Version] = 1;");
            await AssertChangedFootprintRefusesRollbackAsync(run.Factory,
                "UPDATE [product].[TemplateFields] SET [Role] = 'outside-diameter ' WHERE [TemplateId] = 'f944d764-56f6-4a04-9a31-65074b2c06c1' AND [TemplateVersion] = 1 AND [Key] = 'outside-diameter-mm';",
                "UPDATE [product].[TemplateFields] SET [Role] = 'outside-diameter' WHERE [TemplateId] = 'f944d764-56f6-4a04-9a31-65074b2c06c1' AND [TemplateVersion] = 1 AND [Key] = 'outside-diameter-mm';");
            await AssertChangedFootprintRefusesRollbackAsync(run.Factory,
                "UPDATE [product].[TemplateFields] SET [Dictionary] = 'gdc-steel-grades ' WHERE [TemplateId] = 'f944d764-56f6-4a04-9a31-65074b2c06c1' AND [TemplateVersion] = 1 AND [Key] = 'steel-grade';",
                "UPDATE [product].[TemplateFields] SET [Dictionary] = 'gdc-steel-grades' WHERE [TemplateId] = 'f944d764-56f6-4a04-9a31-65074b2c06c1' AND [TemplateVersion] = 1 AND [Key] = 'steel-grade';");
            await AssertChangedFootprintRefusesRollbackAsync(run.Factory,
                "UPDATE [product].[TemplateFields] SET [Key] = 'outside-diameter-mm ' WHERE [TemplateId] = 'f944d764-56f6-4a04-9a31-65074b2c06c1' AND [TemplateVersion] = 1 AND [Key] = 'outside-diameter-mm';",
                "UPDATE [product].[TemplateFields] SET [Key] = 'outside-diameter-mm' WHERE [TemplateId] = 'f944d764-56f6-4a04-9a31-65074b2c06c1' AND [TemplateVersion] = 1 AND [Key] = 'outside-diameter-mm ';");

            failurePhase = RollbackCardReferencePhase;
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
                AssertExactInitialRelease(await new SqlProductFamilyDefinitionSource(
                    preserved.ServiceProvider.GetRequiredService<ProductDbContext>()).GetAsync(default));

            failurePhase = RollbackReplayPrerequisitePhase;
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
            failurePhase = RollbackReplayTerminalInsertPhase;
            await InsertHistoricalReplayAsync(run.Factory.DatabaseConnectionString);
            failurePhase = RollbackReplayRefusalPhase;
            var historyRefusal = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(run.Factory, PreviousMigration));
            Assert.Equal(51044, historyRefusal.Number);
            Assert.Contains("replay history references", historyRefusal.Message, StringComparison.Ordinal);

            failurePhase = RollbackUnreferencedPhase;
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

            failurePhase = RollbackReapplyPhase;
            await MigrateAsync(run.Factory);
            await using var replayed = run.Factory.Services.CreateAsyncScope();
            AssertExactGenuine(await new SqlProductFamilyDefinitionSource(
                replayed.ServiceProvider.GetRequiredService<ProductDbContext>()).GetAsync(default));
            failurePhase = RollbackLifetimePhase;
        }
        catch
        {
            output.WriteLine(failurePhase);
            throw;
        }
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
        var expected = ProductInitialTechnicalDefinitionV1.Definition();
        AssertExact(expected, actual);
    }

    private static void AssertExactInitialRelease(ProductFamilyDefinitionV2 actual)
    {
        var full = ProductInitialTechnicalDefinitionV1.Definition();
        var additiveFamilyIds = new HashSet<Guid>
        {
            ProductInitialTechnicalDefinitionV1.HotRolledCutSheetPlateFamilyId,
            ProductInitialTechnicalDefinitionV1.WeldedRoundPipeFamilyId,
            ProductInitialTechnicalDefinitionV1.WeldedSquareProfileFamilyId,
            ProductInitialTechnicalDefinitionV1.WeldedRectangleProfileFamilyId,
        };
        var additiveTemplateIds = new HashSet<Guid>
        {
            ProductInitialTechnicalDefinitionV1.HotRolledCutSheetPlateTemplateId,
            ProductInitialTechnicalDefinitionV1.WeldedRoundPipeTemplateId,
            ProductInitialTechnicalDefinitionV1.WeldedSquareProfileTemplateId,
            ProductInitialTechnicalDefinitionV1.WeldedRectangleProfileTemplateId,
        };
        var expected = new ProductFamilyDefinitionV2(
            [.. full.Families.Where(row => !additiveFamilyIds.Contains(row.Id))],
            [.. full.Templates.Where(row => !additiveTemplateIds.Contains(row.Id))],
            full.Entries);
        AssertExact(expected, actual);
    }

    private static void AssertExact(ProductFamilyDefinitionV2 expected, ProductFamilyDefinitionV2 actual)
    {
        ProductFamilyDefinitionRulesV2.RequireWellFormed(actual);
        Assert.Equal(expected.Families.OrderBy(row => row.Id), actual.Families.OrderBy(row => row.Id));
        Assert.Equal(expected.Entries.OrderBy(row => row.Dictionary).ThenBy(row => row.Code),
            actual.Entries.OrderBy(row => row.Dictionary).ThenBy(row => row.Code));
        Assert.Equal(expected.Templates.Count, actual.Templates.Count);
        foreach (var expectedTemplate in expected.Templates)
        {
            var actualTemplate = actual.Templates.Single(candidate =>
                candidate.Id == expectedTemplate.Id && candidate.Version == expectedTemplate.Version);
            Assert.Equal((expectedTemplate.Code, expectedTemplate.Name, expectedTemplate.SourceRef),
                (actualTemplate.Code, actualTemplate.Name, actualTemplate.SourceRef));
            Assert.Equal(expectedTemplate.FamilyIds.Order(), actualTemplate.FamilyIds.Order());
            Assert.Equal(expectedTemplate.Fields, actualTemplate.Fields);
        }
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
        Assert.Equal(51045, refusal.Number);
        Assert.Contains("partial or changed", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(3, await ScalarAsync<int>(factory.DatabaseConnectionString,
            "SELECT COUNT(*) FROM [product].[Families] WHERE [Id] IN ('b5f197c1-9d8f-4d75-a170-4dd1cf0a6a99', 'b5d078ba-dbb8-4ada-9b6f-28a5ecdeac54', 'f3ebc5b8-d55c-44dd-af7c-bfe2285f44df')"));
        Assert.Equal(3, await ScalarAsync<int>(factory.DatabaseConnectionString,
            "SELECT COUNT(*) FROM [product].[Templates] WHERE [Id] IN ('f944d764-56f6-4a04-9a31-65074b2c06c1', '93772f5f-5be9-4dba-8c61-f9942f4e523d', 'b97f74f3-11d8-4dc8-b5ba-bd1254e5fcc2')"));
        Assert.Equal(3, await ScalarAsync<int>(factory.DatabaseConnectionString,
            "SELECT COUNT(*) FROM [product].[TemplateFamilies] WHERE [TemplateId] IN ('f944d764-56f6-4a04-9a31-65074b2c06c1', '93772f5f-5be9-4dba-8c61-f9942f4e523d', 'b97f74f3-11d8-4dc8-b5ba-bd1254e5fcc2')"));
        Assert.Equal(20, await ScalarAsync<int>(factory.DatabaseConnectionString,
            "SELECT COUNT(*) FROM [product].[TemplateFields] WHERE [TemplateId] IN ('f944d764-56f6-4a04-9a31-65074b2c06c1', '93772f5f-5be9-4dba-8c61-f9942f4e523d', 'b97f74f3-11d8-4dc8-b5ba-bd1254e5fcc2')"));
        await ExecuteAsync(factory.DatabaseConnectionString, restore);
    }

    private static async Task AssertWeldedPrerequisiteDriftRefusesPublicationAsync(
        SqlServerRelationalApiFactory factory, string mutate, string restore)
    {
        await ExecuteAsync(factory.DatabaseConnectionString, mutate);
        var refusal = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(factory));
        Assert.Equal(51045, refusal.Number);
        Assert.Contains("prerequisite is unavailable", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(5, await ScalarAsync<int>(factory.DatabaseConnectionString,
            "SELECT COUNT(*) FROM [product].[Families]"));
        Assert.Equal(2, await ScalarAsync<int>(factory.DatabaseConnectionString,
            "SELECT COUNT(*) FROM [product].[Templates]"));
        Assert.Equal(0, await ScalarAsync<int>(factory.DatabaseConnectionString,
            "SELECT COUNT(*) FROM [product].[Families] WHERE [SourceRef] = 'issue66-6210091477-p013-a'"));
        Assert.Equal(0, await ScalarAsync<int>(factory.DatabaseConnectionString,
            "SELECT COUNT(*) FROM [product].[Templates] WHERE [SourceRef] = 'issue66-6210091477-p013-a'"));
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

    private static async Task InsertWeldedHistoricalReplayAsync(string connectionString)
    {
        var at = DateTimeOffset.Parse("2026-10-16T10:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture);
        var product = new ProductDraftView(ProductId, "WRG-1", "SENTETIK kaynaklı yuvarlak boru", GroupId,
            null, ProductInitialTechnicalDefinitionV1.WeldedRoundPipeFamilyId,
            new(new(null, "not-evaluated"), new(true, "manual"),
                new(null, "not-evaluated"), new(null, "not-evaluated")),
            "Draft", "1", at, at, new(null, null, null, null), new("Complete", []));
        var references = new ProductReferences(ProductInitialTechnicalDefinitionV1.WeldedRoundPipeTemplateId, "1",
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["outside-diameter-mm"] = JsonSerializer.SerializeToElement(48.3m),
                ["wall-thickness-mm"] = JsonSerializer.SerializeToElement(1.5m),
                ["length-mm"] = JsonSerializer.SerializeToElement(6000m),
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
                 'product-p013-rollback-history', 1, '2026-10-16T10:00:00+00:00');
            """;
        command.Parameters.AddWithValue("@key", new string('C', 64));
        command.Parameters.AddWithValue("@request", new string('D', 64));
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
            // migration acceptance test's Product or shared replay schema. Apply both registered owned
            // migration chains explicitly before reading or mutating their release and terminal tables.
            await using (var migration = factory.Services.CreateAsyncScope())
            {
                await migration.ServiceProvider.GetRequiredService<ProductDbContext>()
                    .Database.MigrateAsync();
                await migration.ServiceProvider.GetRequiredService<GroupSharedCommandDbContext>()
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
