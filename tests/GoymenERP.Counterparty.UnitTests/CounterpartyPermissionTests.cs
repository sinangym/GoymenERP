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
    public void PartyMergeApprovePermission_IsARegisteredNonCriticalCompanyWideName()
    {
        // F-027 / EK-18 #76: catalogue registration only, no Critical flag; it is not widened from any edit grant.
        Assert.Equal(ErpPermissions.CounterpartyPartiesMergeApprove, CounterpartyPartyMergePermissionsV1.Approve);
        Assert.True(ErpPermissions.IsKnown(CounterpartyPartyMergePermissionsV1.Approve));
        Assert.True(ErpPermissions.RequiresCompanyWideScope(CounterpartyPartyMergePermissionsV1.Approve));
        Assert.False(ErpPermissions.IsCritical(CounterpartyPartyMergePermissionsV1.Approve));
        Assert.NotEqual(ErpPermissions.CounterpartyPartiesUpdateBasic, CounterpartyPartyMergePermissionsV1.Approve);
        Assert.NotEqual(ErpPermissions.CounterpartyPartiesLegalNameUpdate, CounterpartyPartyMergePermissionsV1.Approve);
    }

    [Fact]
    public void SuccessionRecordPermission_IsSeparateFromTheAccountStatusPermissionAndRequiresCompanyWideScope()
    {
        // KLN-D38: a separate permission; the command also requires the account status permission, so no existing grant
        // is silently widened to record a successor.
        Assert.Equal(ErpPermissions.CounterpartyPartiesSuccessionRecord, CounterpartyPartySuccessionPermissionsV1.Record);
        Assert.True(ErpPermissions.IsKnown(CounterpartyPartySuccessionPermissionsV1.Record));
        Assert.True(ErpPermissions.RequiresCompanyWideScope(CounterpartyPartySuccessionPermissionsV1.Record));
        Assert.False(ErpPermissions.IsCritical(CounterpartyPartySuccessionPermissionsV1.Record));
        Assert.NotEqual(ErpPermissions.CounterpartyAccountsStatusUpdate, CounterpartyPartySuccessionPermissionsV1.Record);
        Assert.NotEqual(ErpPermissions.CounterpartyPartiesUpdateBasic, CounterpartyPartySuccessionPermissionsV1.Record);
    }

    [Fact]
    public void PartyReadPermission_IsKnownAndRequiresCompanyWideScope()
    {
        Assert.True(ErpPermissions.IsKnown(ErpPermissions.CounterpartyPartiesRead));
        Assert.True(ErpPermissions.RequiresCompanyWideScope(
            ErpPermissions.CounterpartyPartiesRead));
    }
}
