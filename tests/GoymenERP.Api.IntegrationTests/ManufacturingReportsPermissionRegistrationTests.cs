using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// URT-S19 (B-27, DEC-PRDCT-009, API-42): <c>manufacturing.reports.read</c> is a registered central name — once,
/// company-wide only, NOT Critical — and handed to nobody by the shipped seed. Who reads production cost data is not
/// decided, so the grant stays a human gate.
/// </summary>
public sealed class ManufacturingReportsPermissionRegistrationTests
{
    private const string SeedPath = "src/backend/GoymenERP.Api/Seed/identity-seed.json";

    [Fact]
    public void The_reports_read_name_is_registered_company_wide_and_not_critical()
    {
        const string name = ErpPermissions.ManufacturingReportsRead;
        Assert.Equal("manufacturing.reports.read", name);
        Assert.True(ErpPermissions.IsKnown(name));
        Assert.Single(ErpPermissions.All, registered => registered == name);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(name));
        Assert.False(ErpPermissions.IsCritical(name));
    }

    [Fact]
    public void No_seeded_role_carries_the_reports_read_name()
    {
        var seed = File.ReadAllText(Path.Combine(FindRepositoryRoot(), SeedPath));
        Assert.Contains(ErpPermissions.OrganizationLocationsRead, seed, StringComparison.Ordinal);
        Assert.DoesNotContain("manufacturing.reports.", seed, StringComparison.Ordinal);
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
