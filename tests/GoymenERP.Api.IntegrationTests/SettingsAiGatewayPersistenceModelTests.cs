using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.Settings.Infrastructure.Shell;
using GoymenERP.BuildingBlocks.Shell;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// WP-A1 model facts without a database: the five AI gateway tables carry the company in every key and a company query
/// filter, their checks equal the DbContext constants (the cloud route rule included), nothing is seeded, the migration's
/// Up is pure DDL and Down first refuses with THROW 52180; the manage name is registered once, company-wide, not Critical,
/// granted to nobody by the seed, and the shell declares (not yet implements) its screen.
/// </summary>
public sealed class SettingsAiGatewayPersistenceModelTests
{
    private const string SeedPath = "src/backend/GoymenERP.Api/Seed/identity-seed.json";

    [Fact]
    public void The_ai_gateway_tables_are_company_keyed_filtered_unseeded_and_check_the_cloud_route_rule()
    {
        using var database = Create();
        var model = database.GetService<IDesignTimeModel>().Model;
        var expected = new (Type Row, string Table, string Check, string Sql)[]
        {
            (typeof(AiGatewayPolicyVersionRow), "AiGatewayPolicyVersions", "CK_SettingsAiGatewayPolicyVersions_Identity", SettingsDbContext.AiGatewayPolicyVersionCheck),
            (typeof(AiGatewayFeatureRow), "AiGatewayFeatures", "CK_SettingsAiGatewayFeatures_Feature", SettingsDbContext.AiGatewayFeatureCheck),
            (typeof(AiGatewayModelRouteRow), "AiGatewayModelRoutes", "CK_SettingsAiGatewayModelRoutes_Route", SettingsDbContext.AiGatewayModelRouteCheck),
            (typeof(AiBudgetMonthTotalRow), "AiBudgetMonthTotals", "CK_SettingsAiBudgetMonthTotals_Total", SettingsDbContext.AiBudgetMonthTotalCheck),
            (typeof(AiBudgetLedgerEntryRow), "AiBudgetLedgerEntries", "CK_SettingsAiBudgetLedgerEntries_Entry", SettingsDbContext.AiBudgetLedgerEntryCheck),
        };
        foreach (var (row, table, check, sql) in expected)
        {
            var entity = model.FindEntityType(row)!;
            Assert.Equal(table, entity.GetTableName());
            Assert.Equal(SettingsDbContext.Schema, entity.GetSchema());
            Assert.Equal("CompanyId", entity.FindPrimaryKey()!.Properties[0].Name);
            Assert.NotNull(entity.GetDeclaredQueryFilters().SingleOrDefault());
            Assert.Equal(sql, Assert.Single(entity.GetCheckConstraints(), constraint => constraint.Name == check).Sql);
            Assert.Empty(entity.GetSeedData());
        }

        Assert.Contains("([Placement] = 2 OR [DataClass] <= 2)", SettingsDbContext.AiGatewayModelRouteCheck, StringComparison.Ordinal);
        Assert.Equal(["CompanyId", "Version", "TaskKind", "DataClass"],
            model.FindEntityType(typeof(AiGatewayModelRouteRow))!.FindPrimaryKey()!.Properties.Select(property => property.Name));
        var policy = model.FindEntityType(typeof(AiGatewayPolicyVersionRow))!;
        Assert.True(policy.FindProperty(nameof(AiGatewayPolicyVersionRow.MonthlyUsageCeiling))!.IsNullable);
        Assert.True(policy.FindProperty(nameof(AiGatewayPolicyVersionRow.MaxUnitsPerRequest))!.IsNullable);
        Assert.Null(policy.FindProperty(nameof(AiGatewayPolicyVersionRow.MonthlyUsageCeiling))!.GetDefaultValue());
    }

    [Fact]
    public void The_ai_gateway_migration_is_ddl_only_and_Down_first_refuses_with_52180_while_versions_or_ledger_rows_exist()
    {
        using var database = Create();
        // WP2 (tren-2) appends the work digest times after the AI gateway migration.
        Assert.Equal(["20261122202350_AddAiGatewayPolicyAndBudgetLedger", "20261122224127_Wp2WorkDigestTimes"],
            database.Database.GetMigrations().TakeLast(2).ToArray());
        Assert.False(database.Database.HasPendingModelChanges());
        var migration = new AddAiGatewayPolicyAndBudgetLedger();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        Assert.Equal(["AiBudgetLedgerEntries", "AiBudgetMonthTotals", "AiGatewayFeatures", "AiGatewayModelRoutes", "AiGatewayPolicyVersions"],
            migration.UpOperations.OfType<CreateTableOperation>().Select(table => table.Name).Order(StringComparer.Ordinal).ToArray());
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 52180", guard, StringComparison.Ordinal);
        Assert.Contains("[settings].[AiGatewayPolicyVersions]", guard, StringComparison.Ordinal);
        Assert.Contains("[settings].[AiBudgetLedgerEntries]", guard, StringComparison.Ordinal);
        Assert.Contains("[settings].[AiBudgetMonthTotals]", guard, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
    }

    [Fact]
    public void The_ai_gateway_manage_name_is_registered_once_company_wide_not_critical_and_granted_to_nobody()
    {
        const string name = SettingsAiGatewayPermissionsV1.Manage;
        Assert.Equal("settings.ai-gateway.manage", name);
        Assert.Equal(ErpPermissions.SettingsAiGatewayManage, name);
        Assert.Single(ErpPermissions.All, item => item == name);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(name));
        Assert.False(ErpPermissions.IsCritical(name));
        Assert.DoesNotContain(name, File.ReadAllText(Path.Combine(FindRepositoryRoot(), SeedPath)), StringComparison.Ordinal);

        var declaration = Assert.Single(new SettingsShellCapabilityProvider().GetDeclarations(), item => item.CapabilityId == name);
        Assert.Equal("/ayarlar/yapay-zeka-ag-gecidi", declaration.RoutePath);
        Assert.Equal([name], declaration.RequiredPermissions);
        // WP-A5 implements the management screen (Settings provider 1.21).
        Assert.True(declaration.ImplementationAvailable);
        Assert.Equal(ShellCapabilityScopeRequirement.CompanyWide, declaration.ScopeRequirement);
    }

    [Fact]
    public void A_policy_that_was_never_recorded_is_closed_and_the_budget_month_follows_the_company_zone()
    {
        var closed = SettingsAiGatewayPolicyV1.NotRecorded(Guid.Parse("aaaaaaaa-0000-4000-8000-0000000000b1"));
        Assert.Equal((0L, (long?)null, (int?)null, 0, 0),
            (closed.Version, closed.MonthlyUsageCeiling, closed.MaxUnitsPerRequest, closed.EnabledFeatures.Count, closed.Routes.Count));
        Assert.Equal("ai.gateway-policy:0", closed.VersionText);
        Assert.Equal(202612, SettingsAiBudgetMonthV1.Of(new DateTimeOffset(2026, 11, 30, 21, 30, 0, TimeSpan.Zero)));
        Assert.Equal(202611, SettingsAiBudgetMonthV1.Of(new DateTimeOffset(2026, 11, 30, 20, 30, 0, TimeSpan.Zero)));
    }

    [Theory]
    [InlineData(AiModelPlacementV1.Cloud, AiDataClassV1.Public, true)]
    [InlineData(AiModelPlacementV1.Cloud, AiDataClassV1.Internal, true)]
    [InlineData(AiModelPlacementV1.Cloud, AiDataClassV1.Confidential, false)]
    [InlineData(AiModelPlacementV1.Cloud, AiDataClassV1.StrictlyConfidential, false)]
    [InlineData(AiModelPlacementV1.OnPremises, AiDataClassV1.StrictlyConfidential, true)]
    public void The_fixed_placement_rule_keeps_confidential_data_off_cloud_models(AiModelPlacementV1 placement, AiDataClassV1 dataClass,
        bool allowed)
    {
        Assert.Equal(allowed, AiGatewayPlacementRulesV1.PlacementAllows(placement, dataClass));
        Assert.False(AiGatewayPlacementRulesV1.FeatureAllows(AiFeatureV1.Document, AiModelPlacementV1.Cloud));
        Assert.False(AiGatewayPlacementRulesV1.FeatureAllows(AiFeatureV1.Email, AiModelPlacementV1.Cloud));
        Assert.True(AiGatewayPlacementRulesV1.FeatureAllows(AiFeatureV1.Document, AiModelPlacementV1.OnPremises));
    }

    private static SettingsDbContext Create() =>
        new(new DbContextOptionsBuilder<SettingsDbContext>()
            .UseSqlServer("Server=model-only;Database=model-only;Trusted_Connection=True;TrustServerCertificate=True").Options,
            new CompanyContext());

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
