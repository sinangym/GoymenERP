using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Identity.UnitTests;

/// <summary>
/// AI-D43 (WP-A3c): <c>finance.treasury-balance.read</c> — the treasury (kasa/banka) ledger balance read — is REGISTERED,
/// not granted; who holds it is a human gate. These cases pin the name, the company-wide scope, that it is not Critical,
/// and that no shipped seed role carries it.
/// </summary>
public sealed class FinanceTreasuryBalancePermissionRegistrationTests
{
    private const string SeedPath = "src/backend/GoymenERP.Api/Seed/identity-seed.json";
    private const string Name = "finance.treasury-balance.read";

    [Fact]
    public void The_name_is_registered_exactly_once_company_wide_and_not_critical()
    {
        Assert.Equal(Name, ErpPermissions.FinanceTreasuryBalanceRead);
        Assert.True(ErpPermissions.IsKnown(Name));
        Assert.Single(ErpPermissions.All, known => known == Name);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(Name));
        Assert.False(ErpPermissions.IsCritical(Name));
    }

    [Fact]
    public void No_seeded_role_carries_it()
    {
        var text = File.ReadAllText(Path.Combine(FindRepositoryRoot(), SeedPath));
        Assert.DoesNotContain(Name, text);
        // Sanity: the seed really carries other permissions, so the answer above is not an empty file.
        Assert.Contains(ErpPermissions.OrganizationLocationsRead, text);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SeedPath)))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("GöymenERP repository kökü bulunamadı.");
    }
}
