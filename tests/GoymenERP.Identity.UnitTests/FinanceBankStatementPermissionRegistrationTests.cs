using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Identity.UnitTests;

public sealed class FinanceBankStatementPermissionRegistrationTests
{
    [Theory]
    [InlineData("finance.bank-statements.import")]
    [InlineData("finance.bank-statements.read")]
    [InlineData("finance.bank-statements.match")]
    [InlineData("counterparty.party-ibans.read")]
    [InlineData("counterparty.party-ibans.manage")]
    public void Statement_permissions_are_registered_company_wide_not_critical_and_not_seeded(string permission)
    {
        Assert.Contains(permission, new[] { ErpPermissions.FinanceBankStatementsImport, ErpPermissions.FinanceBankStatementsRead,
            ErpPermissions.FinanceBankStatementsMatch, ErpPermissions.CounterpartyPartyIbansRead,
            ErpPermissions.CounterpartyPartyIbansManage });
        Assert.True(ErpPermissions.IsKnown(permission));
        Assert.Single(ErpPermissions.All, name => name == permission);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(permission));
        Assert.False(ErpPermissions.IsCritical(permission));
        const string seed = "src/backend/GoymenERP.Api/Seed/identity-seed.json";
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, seed))) directory = directory.Parent;
        Assert.NotNull(directory);
        Assert.DoesNotContain(permission, File.ReadAllText(Path.Combine(directory.FullName, seed)), StringComparison.Ordinal);
    }
}
