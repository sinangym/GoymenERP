using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Üretim U1-a (API-01/02): the Settings owner's Manufacturing parameter and tolerance profile names are the registered central
/// names — each once, company-wide only, NOT Critical (maker ≠ approver is the lifecycle's command-time rule) — and handed to
/// nobody by the shipped seed (the grant is a human gate).
/// </summary>
public sealed class SettingsManufacturingProfilePermissionRegistrationTests
{
    private const string SeedPath = "src/backend/GoymenERP.Api/Seed/identity-seed.json";

    public static TheoryData<string, string, string> Names => new()
    {
        { ManufacturingParameterProfileActionsV1.Read, "settings.manufacturing-parameter-profiles.read", ErpPermissions.SettingsManufacturingParameterProfilesRead },
        { ManufacturingParameterProfileActionsV1.Prepare, "settings.manufacturing-parameter-profiles.prepare", ErpPermissions.SettingsManufacturingParameterProfilesPrepare },
        { ManufacturingParameterProfileActionsV1.Approve, "settings.manufacturing-parameter-profiles.approve", ErpPermissions.SettingsManufacturingParameterProfilesApprove },
        { ManufacturingParameterProfileActionsV1.Publish, "settings.manufacturing-parameter-profiles.publish", ErpPermissions.SettingsManufacturingParameterProfilesPublish },
        { ManufacturingToleranceProfileReadActionV1.Read, "settings.manufacturing-tolerance-profiles.read", ErpPermissions.SettingsManufacturingToleranceProfilesRead },
        { ManufacturingToleranceProfileActionsV1.Prepare, "settings.manufacturing-tolerance-profiles.prepare", ErpPermissions.SettingsManufacturingToleranceProfilesPrepare },
        { ManufacturingToleranceProfileActionsV1.Approve, "settings.manufacturing-tolerance-profiles.approve", ErpPermissions.SettingsManufacturingToleranceProfilesApprove },
        { ManufacturingToleranceProfileActionsV1.Publish, "settings.manufacturing-tolerance-profiles.publish", ErpPermissions.SettingsManufacturingToleranceProfilesPublish },
    };

    [Theory]
    [MemberData(nameof(Names))]
    public void The_owner_name_is_the_registered_central_name_company_wide_and_not_critical(string ownerName, string literal,
        string central)
    {
        Assert.Equal(literal, ownerName);
        Assert.Equal(ownerName, central);
        Assert.True(ErpPermissions.IsKnown(ownerName));
        Assert.Single(ErpPermissions.All, name => name == ownerName);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(ownerName));
        Assert.False(ErpPermissions.IsCritical(ownerName));
    }

    [Fact]
    public void No_seeded_role_carries_them()
    {
        var seed = File.ReadAllText(Path.Combine(FindRepositoryRoot(), SeedPath));
        Assert.Contains(ErpPermissions.OrganizationLocationsRead, seed, StringComparison.Ordinal);
        Assert.DoesNotContain("settings.manufacturing-", seed, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
