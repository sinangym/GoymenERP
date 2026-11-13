using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// F-005 D1 (FNS-D26): the two owner names are the registered central names — once, company-wide only, NOT Critical —
/// and handed to nobody by the shipped seed (the grant is a human gate).
/// </summary>
public sealed class FinanceOfficialSalesInvoicePermissionRegistrationTests
{
    private const string SeedPath = "src/backend/GoymenERP.Api/Seed/identity-seed.json";

    [Theory]
    [InlineData(FinanceOfficialSalesInvoicePermissionNamesV1.Record,
        ErpPermissions.FinanceOfficialSalesInvoicesRecord)]
    [InlineData(FinanceOfficialSalesInvoicePermissionNamesV1.Approve,
        ErpPermissions.FinanceOfficialSalesInvoicesApprove)]
    public void Owner_command_name_is_the_known_central_name_company_wide_and_not_critical(
        string ownerName, string centralName)
    {
        Assert.Equal(ownerName, centralName);
        Assert.True(ErpPermissions.IsKnown(ownerName));
        Assert.Single(ErpPermissions.All, known => known == ownerName);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(ownerName));
        Assert.False(ErpPermissions.IsCritical(ownerName));
    }

    [Fact]
    public void No_seeded_role_carries_either_name()
    {
        var seed = File.ReadAllText(Path.Combine(FindRepositoryRoot(), SeedPath));
        Assert.Contains(ErpPermissions.OrganizationLocationsRead, seed, StringComparison.Ordinal);
        Assert.DoesNotContain("finance.official-sales-invoices.", seed, StringComparison.Ordinal);
        Assert.DoesNotContain(FinanceOfficialSalesInvoicePermissionNamesV1.Record, seed, StringComparison.Ordinal);
        Assert.DoesNotContain(FinanceOfficialSalesInvoicePermissionNamesV1.Approve, seed, StringComparison.Ordinal);
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
