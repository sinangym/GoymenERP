using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// PUR-4a: the Purchasing owner's permission names are registered centrally with the same strings (company-wide
/// only, not Critical, no grant; #66 5743488059), and nothing else is registered under <c>purchasing.</c>.
/// </summary>
public sealed class PurchasingPermissionAndShellRegistrationTests
{
    [Theory]
    [InlineData(PurchasingPermissionManifestV1.OrdersCreate, ErpPermissions.PurchasingOrdersCreate)]
    [InlineData(PurchasingPermissionManifestV1.OrdersRead, ErpPermissions.PurchasingOrdersRead)]
    [InlineData(PurchasingPermissionManifestV1.OrdersApprove, ErpPermissions.PurchasingOrdersApprove)]
    public void The_owner_name_is_the_registered_central_name(string ownerName, string centralName)
    {
        Assert.Equal(ownerName, centralName);
        Assert.True(ErpPermissions.IsKnown(ownerName));
        Assert.Single(ErpPermissions.All, known => known == ownerName);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(ownerName));
        Assert.False(ErpPermissions.IsCritical(ownerName));
    }

    [Fact]
    public void Every_owner_name_is_registered_and_nothing_else_under_purchasing()
    {
        Assert.Equal(PurchasingPermissionManifestV1.Names.Order(StringComparer.Ordinal),
            ErpPermissions.All.Where(name => name.StartsWith("purchasing.", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
    }
}
