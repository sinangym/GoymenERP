using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// URT-S4 (F-009 D5, URT-D33): <c>manufacturing.orders.cancel</c>, the name the order cancel runs under, is a registered
/// central name — once, company-wide only, NOT Critical — and handed to nobody by the shipped seed. Who cancels an order
/// is not decided, so the grant stays a human gate.
/// </summary>
public sealed class ManufacturingOrderCancelPermissionRegistrationTests
{
    private const string SeedPath = "src/backend/GoymenERP.Api/Seed/identity-seed.json";

    [Fact]
    public void The_order_cancel_name_is_registered_company_wide_and_not_critical()
    {
        Assert.Equal("manufacturing.orders.cancel", ErpPermissions.ManufacturingOrdersCancel);
        Assert.True(ErpPermissions.IsKnown(ErpPermissions.ManufacturingOrdersCancel));
        Assert.Single(ErpPermissions.All, registered => registered == ErpPermissions.ManufacturingOrdersCancel);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(ErpPermissions.ManufacturingOrdersCancel));
        Assert.False(ErpPermissions.IsCritical(ErpPermissions.ManufacturingOrdersCancel));
    }

    [Fact]
    public void No_seeded_role_carries_the_order_cancel_name()
    {
        var seed = File.ReadAllText(Path.Combine(FindRepositoryRoot(), SeedPath));
        Assert.Contains(ErpPermissions.OrganizationLocationsRead, seed, StringComparison.Ordinal);
        Assert.DoesNotContain(ErpPermissions.ManufacturingOrdersCancel, seed, StringComparison.Ordinal);
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
