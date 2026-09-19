using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Logistics.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// LOG-4a: the Logistics owner's permission names are registered centrally with the same strings (company-wide
/// only, not Critical, no grant; #66 5743488059), and nothing else is registered under <c>logistics.</c>.
/// </summary>
public sealed class LogisticsPermissionAndShellRegistrationTests
{
    [Theory]
    [InlineData(LogisticsPermissionManifestV1.InstructionsCreate, ErpPermissions.LogisticsLoadingInstructionsCreate)]
    [InlineData(LogisticsPermissionManifestV1.InstructionsRead, ErpPermissions.LogisticsLoadingInstructionsRead)]
    [InlineData(LogisticsPermissionManifestV1.LoadsRecord, ErpPermissions.LogisticsLoadsRecord)]
    public void The_owner_name_is_the_registered_central_name(string ownerName, string centralName)
    {
        Assert.Equal(ownerName, centralName);
        Assert.True(ErpPermissions.IsKnown(ownerName));
        Assert.Single(ErpPermissions.All, known => known == ownerName);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(ownerName));
        Assert.False(ErpPermissions.IsCritical(ownerName));
    }

    [Fact]
    public void Every_owner_name_is_registered_and_nothing_else_under_logistics()
    {
        Assert.Equal(LogisticsPermissionManifestV1.Names.Order(StringComparer.Ordinal),
            ErpPermissions.All.Where(name => name.StartsWith("logistics.", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
    }
}
