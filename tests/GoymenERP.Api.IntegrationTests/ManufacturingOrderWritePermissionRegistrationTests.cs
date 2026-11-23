using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// URT-S23 (URT-D66 (5)): <c>manufacturing.orders.write</c>, the order edit name the order's termin change runs under, is a
/// registered central name — once, company-wide only, NOT Critical — and handed to nobody by the shipped seed. Who edits an
/// order is not decided, so the grant stays a human gate.
/// </summary>
public sealed class ManufacturingOrderWritePermissionRegistrationTests
{
    private const string SeedPath = "src/backend/GoymenERP.Api/Seed/identity-seed.json";

    [Fact]
    public void The_order_write_name_is_registered_company_wide_and_not_critical()
    {
        Assert.Equal("manufacturing.orders.write", ErpPermissions.ManufacturingOrdersWrite);
        Assert.True(ErpPermissions.IsKnown(ErpPermissions.ManufacturingOrdersWrite));
        Assert.Single(ErpPermissions.All, registered => registered == ErpPermissions.ManufacturingOrdersWrite);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(ErpPermissions.ManufacturingOrdersWrite));
        Assert.False(ErpPermissions.IsCritical(ErpPermissions.ManufacturingOrdersWrite));
    }

    [Fact]
    public void No_seeded_role_carries_the_order_write_name()
    {
        var seed = File.ReadAllText(Path.Combine(FindRepositoryRoot(), SeedPath));
        Assert.Contains(ErpPermissions.OrganizationLocationsRead, seed, StringComparison.Ordinal);
        Assert.DoesNotContain(ErpPermissions.ManufacturingOrdersWrite, seed, StringComparison.Ordinal);
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
