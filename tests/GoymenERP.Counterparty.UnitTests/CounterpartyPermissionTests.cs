using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Counterparty.UnitTests;

public sealed class CounterpartyPermissionTests
{
    [Fact]
    public void PartyReadPermission_IsKnownAndRequiresCompanyWideScope()
    {
        Assert.True(ErpPermissions.IsKnown(ErpPermissions.CounterpartyPartiesRead));
        Assert.True(ErpPermissions.RequiresCompanyWideScope(
            ErpPermissions.CounterpartyPartiesRead));
    }
}
