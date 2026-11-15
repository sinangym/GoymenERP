using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;

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
    public void LegalNameUpdatePermission_IsSeparateFromTheBasicEditAndRequiresCompanyWideScope()
    {
        // ALS-D95 / 19.22: a separate permission, so basic-edit grants are not silently widened to the legal name.
        Assert.Equal(ErpPermissions.CounterpartyPartiesLegalNameUpdate, CounterpartyLegalNamePermissionsV1.Update);
        Assert.True(ErpPermissions.IsKnown(CounterpartyLegalNamePermissionsV1.Update));
        Assert.True(ErpPermissions.RequiresCompanyWideScope(CounterpartyLegalNamePermissionsV1.Update));
        Assert.False(ErpPermissions.IsCritical(CounterpartyLegalNamePermissionsV1.Update));
        Assert.NotEqual(ErpPermissions.CounterpartyPartiesUpdateBasic, CounterpartyLegalNamePermissionsV1.Update);
        Assert.NotEqual(ErpPermissions.CounterpartyPartiesRead, CounterpartyLegalNamePermissionsV1.Update);
    }

    [Fact]
    public void PartyReadPermission_IsKnownAndRequiresCompanyWideScope()
    {
        Assert.True(ErpPermissions.IsKnown(ErpPermissions.CounterpartyPartiesRead));
        Assert.True(ErpPermissions.RequiresCompanyWideScope(
            ErpPermissions.CounterpartyPartiesRead));
    }
}
