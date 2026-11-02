using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Üretim U2-a (API-11): <c>manufacturing.plans.cancel</c> is a registered central name — once, company-wide only, NOT
/// Critical — and handed to nobody by the shipped seed (the grant is a human gate).
/// </summary>
public sealed class ManufacturingPlanCancelPermissionRegistrationTests
{
    private const string SeedPath = "src/backend/GoymenERP.Api/Seed/identity-seed.json";

    [Fact]
    public void The_cancel_name_is_registered_company_wide_and_not_critical()
    {
        Assert.Equal("manufacturing.plans.cancel", ErpPermissions.ManufacturingPlansCancel);
        Assert.True(ErpPermissions.IsKnown(ErpPermissions.ManufacturingPlansCancel));
        Assert.Single(ErpPermissions.All, name => name == ErpPermissions.ManufacturingPlansCancel);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(ErpPermissions.ManufacturingPlansCancel));
        Assert.False(ErpPermissions.IsCritical(ErpPermissions.ManufacturingPlansCancel));
    }

    [Fact]
    public void No_seeded_role_carries_it()
    {
        var seed = File.ReadAllText(Path.Combine(FindRepositoryRoot(), SeedPath));
        Assert.Contains(ErpPermissions.OrganizationLocationsRead, seed, StringComparison.Ordinal);
        Assert.DoesNotContain(ErpPermissions.ManufacturingPlansCancel, seed, StringComparison.Ordinal);
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
