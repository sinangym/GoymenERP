using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// URT-S13 (URT-D47, URT-D65): <c>manufacturing.scrap-prices.read</c> and <c>manufacturing.scrap-prices.write</c> are
/// registered central names — once each, company-wide only, NOT Critical — and handed to nobody by the shipped seed. Who
/// enters the monthly scrap price is not decided, so the grant stays a human gate.
/// </summary>
public sealed class ManufacturingScrapPricePermissionRegistrationTests
{
    private const string SeedPath = "src/backend/GoymenERP.Api/Seed/identity-seed.json";

    public static TheoryData<string, string> Names => new()
    {
        { "manufacturing.scrap-prices.read", ErpPermissions.ManufacturingScrapPricesRead },
        { "manufacturing.scrap-prices.write", ErpPermissions.ManufacturingScrapPricesWrite },
    };

    [Theory]
    [MemberData(nameof(Names))]
    public void The_scrap_price_name_is_registered_company_wide_and_not_critical(string expected, string name)
    {
        Assert.Equal(expected, name);
        Assert.True(ErpPermissions.IsKnown(name));
        Assert.Single(ErpPermissions.All, registered => registered == name);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(name));
        Assert.False(ErpPermissions.IsCritical(name));
    }

    [Fact]
    public void No_seeded_role_carries_either_name()
    {
        var seed = File.ReadAllText(Path.Combine(FindRepositoryRoot(), SeedPath));
        Assert.Contains(ErpPermissions.OrganizationLocationsRead, seed, StringComparison.Ordinal);
        Assert.DoesNotContain("manufacturing.scrap-prices.", seed, StringComparison.Ordinal);
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
