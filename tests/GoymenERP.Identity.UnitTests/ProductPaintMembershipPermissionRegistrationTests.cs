using GoymenERP.BuildingBlocks.Security;
namespace GoymenERP.Identity.UnitTests;

/// <summary>BS-10 registration only. Real grants and role assignment remain human actions.</summary>
public sealed class ProductPaintMembershipPermissionRegistrationTests
{
    [Fact]
    public void Paint_membership_manage_is_known_once_company_wide_without_critical_classification()
    {
        const string name = "product.paint-memberships.manage";
        Assert.Equal(name, ErpPermissions.ProductPaintMembershipsManage);
        Assert.True(ErpPermissions.IsKnown(name));
        Assert.Single(ErpPermissions.All, x => x == name);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(name));
        Assert.False(ErpPermissions.IsCritical(name));
    }
    [Fact]
    public void Shipped_bootstrap_data_does_not_assign_the_new_permission_to_a_role_or_person()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "GoymenERP.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var seed = File.ReadAllText(Path.Combine(root.FullName, "src/backend/GoymenERP.Api/Seed/identity-seed.json"));
        Assert.DoesNotContain(ErpPermissions.ProductPaintMembershipsManage, seed, StringComparison.Ordinal);
    }
}
