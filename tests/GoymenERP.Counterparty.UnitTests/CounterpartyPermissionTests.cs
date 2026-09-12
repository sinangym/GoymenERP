using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Counterparty.UnitTests;

public sealed class CounterpartyPermissionTests
{
    [Fact]
    public void BasicUpdatePermission_IsDedicatedAndRequiresCompanyWideScope()
    {
        Assert.Equal("counterparty.parties.update.basic", ErpPermissions.CounterpartyPartiesUpdateBasic);
        Assert.True(ErpPermissions.IsKnown(ErpPermissions.CounterpartyPartiesUpdateBasic));
        Assert.True(ErpPermissions.RequiresCompanyWideScope(
            ErpPermissions.CounterpartyPartiesUpdateBasic));
        Assert.NotEqual(ErpPermissions.CounterpartyPartiesRead, ErpPermissions.CounterpartyPartiesUpdateBasic);
        Assert.NotEqual(ErpPermissions.CounterpartyPartiesCreate, ErpPermissions.CounterpartyPartiesUpdateBasic);
        Assert.NotEqual(ErpPermissions.CounterpartyAccountsStatusUpdate, ErpPermissions.CounterpartyPartiesUpdateBasic);
    }

    [Fact]
    public void PartyReadPermission_IsKnownAndRequiresCompanyWideScope()
    {
        Assert.True(ErpPermissions.IsKnown(ErpPermissions.CounterpartyPartiesRead));
        Assert.True(ErpPermissions.RequiresCompanyWideScope(
            ErpPermissions.CounterpartyPartiesRead));
    }
}
