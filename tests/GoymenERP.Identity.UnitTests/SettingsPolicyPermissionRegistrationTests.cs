using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Identity.UnitTests;

/// <summary>No133/K16 registration only. No assignment, seeded role or new Critical classification.</summary>
public sealed class SettingsPolicyPermissionRegistrationTests
{
    public static TheoryData<string, string> Names => new()
    {
        { ErpPermissions.SettingsPoliciesPrepare, "settings.policies.prepare" },
        { ErpPermissions.SettingsPoliciesApprove, "settings.policies.approve" },
        { ErpPermissions.SettingsPoliciesPublish, "settings.policies.publish" },
        { ErpPermissions.SettingsPoliciesPublishCritical, "settings.policies.publish-critical" },
    };

    [Theory]
    [MemberData(nameof(Names))]
    public void KIM_S19_policy_names_are_registered_once_company_wide_without_a_new_Critical_classification(
        string name, string expected)
    {
        Assert.Equal(expected, name);
        Assert.True(ErpPermissions.IsKnown(name));
        Assert.Single(ErpPermissions.All, item => item == name);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(name));
        Assert.DoesNotContain(name, ErpPermissions.Critical);
    }

    [Fact]
    public void KIM_S19_registration_does_not_assign_a_policy_name_to_any_shipped_role()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "AGENTS.md"))) current = current.Parent;
        var root = current?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        var seed = File.ReadAllText(Path.Combine(root, "src/backend/GoymenERP.Api/Seed/identity-seed.json"));

        Assert.DoesNotContain("settings.policies.", seed, StringComparison.Ordinal);
    }
}
