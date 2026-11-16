using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// URN-D6 (F029-C) forward fix: the model's lifecycle and registry checks admit the prepare → publish single flow (a Published
/// row without an approval, the preparer as publisher, a 'Published' registry authorization) while approval-era history stays
/// valid; the migration only re-creates those three checks and its Down refuses first (THROW 51345) while single-flow data exists.
/// </summary>
public sealed class SettingsManufacturingProfileSingleFlowPersistenceModelTests
{
    private static readonly string[] SingleFlowChecks =
    [
        "CK_SettingsMfgParameterProfiles_Lifecycle",
        "CK_SettingsMfgTolerance_Approval",
        "CK_SettingsMfgToleranceVersions_Lifecycle",
    ];

    [Fact]
    public void The_model_checks_are_the_single_flow_checks()
    {
        using var context = CreateDbContext();

        Assert.Equal(SettingsDbContext.LifecycleCheck,
            Checks(Entity<ManufacturingParameterProfileRow>(context))["CK_SettingsMfgParameterProfiles_Lifecycle"]);
        Assert.Equal(SettingsDbContext.LifecycleCheck,
            Checks(Entity<ManufacturingToleranceProfileVersionRow>(context))["CK_SettingsMfgToleranceVersions_Lifecycle"]);
        Assert.Equal(SettingsDbContext.ToleranceRegistryApprovalCheck,
            Checks(Entity<ManufacturingTolerancePolicyRow>(context))["CK_SettingsMfgTolerance_Approval"]);
    }

    [Fact]
    public void A_published_row_needs_a_publication_and_either_no_approval_or_an_approval_by_another_actor()
    {
        var published = SettingsDbContext.LifecycleCheck[SettingsDbContext.LifecycleCheck.IndexOf(
            "[Status] = 'Published'", StringComparison.Ordinal)..];

        Assert.Contains("[ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL AND [ApprovalReason] IS NULL", published,
            StringComparison.Ordinal);
        Assert.Contains("[ApprovedBy] <> [PreparedBy]", published, StringComparison.Ordinal);
        Assert.Contains("[PublicationReason]", published, StringComparison.Ordinal);
        Assert.DoesNotContain("[PublishedBy] <> [PreparedBy]", SettingsDbContext.LifecycleCheck, StringComparison.Ordinal);
        Assert.Contains("[ApprovalStatus] IN ('Approved','Published')", SettingsDbContext.ToleranceRegistryApprovalCheck,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_migration_re_creates_only_the_three_checks_and_its_Down_refuses_first_while_single_flow_data_exists()
    {
        var migration = new AdoptManufacturingProfileSingleFlow();

        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        Assert.Equal(SingleFlowChecks,
            migration.UpOperations.OfType<DropCheckConstraintOperation>().Select(check => check.Name).Order(StringComparer.Ordinal));
        var added = migration.UpOperations.OfType<AddCheckConstraintOperation>().ToDictionary(check => check.Name, check => check.Sql);
        Assert.Equal(SingleFlowChecks, added.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(SettingsDbContext.LifecycleCheck, added["CK_SettingsMfgParameterProfiles_Lifecycle"]);
        Assert.Equal(SettingsDbContext.LifecycleCheck, added["CK_SettingsMfgToleranceVersions_Lifecycle"]);
        Assert.Equal(SettingsDbContext.ToleranceRegistryApprovalCheck, added["CK_SettingsMfgTolerance_Approval"]);
        Assert.Equal(6, migration.UpOperations.Count);

        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Contains("THROW 51345", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[settings].[ManufacturingParameterProfiles]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[settings].[ManufacturingToleranceProfileVersions]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[settings].[ManufacturingTolerancePolicies] WHERE [ApprovalStatus] = 'Published'", guard.Sql,
            StringComparison.Ordinal);
        Assert.Contains("[PublishedBy] = [PreparedBy]", guard.Sql, StringComparison.Ordinal);
        var restored = migration.DownOperations.OfType<AddCheckConstraintOperation>().ToDictionary(check => check.Name, check => check.Sql);
        Assert.Equal(SingleFlowChecks, restored.Keys.Order(StringComparer.Ordinal));
        Assert.Contains("[ApprovalStatus] = 'Approved'", restored["CK_SettingsMfgTolerance_Approval"], StringComparison.Ordinal);
        Assert.Contains("[PublishedBy] <> [PreparedBy]", restored["CK_SettingsMfgParameterProfiles_Lifecycle"],
            StringComparison.Ordinal);
    }

    private static IEntityType Entity<T>(SettingsDbContext context) =>
        context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(T))
        ?? throw new InvalidOperationException($"{typeof(T).Name} is not mapped.");

    private static Dictionary<string, string> Checks(IEntityType entity) =>
        entity.GetCheckConstraints().ToDictionary(check => check.Name!, check => check.Sql);

    private static SettingsDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<SettingsDbContext>()
            .UseSqlServer("Server=model-only;Database=model-only;Trusted_Connection=True;TrustServerCertificate=True")
            .Options, new CompanyContext());
}
