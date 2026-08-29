using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Application.Contracts;

namespace GoymenERP.Identity.UnitTests;

public sealed class SystemRolePermissionReconciliationPlanTests
{
    private const string ContractPath =
        "src/backend/Modules/Counterparty/COUNTERPARTY_ACCOUNT_CREATE_ROLE_POLICY.json";
    private static readonly Guid SystemAdministratorRoleId = Guid.Parse(
        "30000000-0000-0000-0000-000000000003");

    [Fact]
    public void AccountCreatePermission_IsKnownAndRequiresCompanyWideScope()
    {
        Assert.True(ErpPermissions.IsKnown(ErpPermissions.CounterpartyAccountsCreate));
        Assert.True(ErpPermissions.RequiresCompanyWideScope(
            ErpPermissions.CounterpartyAccountsCreate));
        Assert.False(ErpPermissions.IsCritical(ErpPermissions.CounterpartyAccountsCreate));
    }

    [Fact]
    public void AcceptedOptionAContract_BuildsExactGenericPlan()
    {
        var repositoryRoot = FindRepositoryRoot();
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(repositoryRoot, ContractPath)));
        var root = document.RootElement;
        var role = Assert.Single(root.GetProperty("roles").EnumerateArray().ToArray());
        var permissions = role.GetProperty("permissions_to_ensure")
            .EnumerateArray()
            .Select(permission => permission.GetString()!)
            .ToArray();

        Assert.Equal("CARI-PERM-001", root.GetProperty("decision_id").GetString());
        Assert.Equal(
            ErpPermissions.CounterpartyAccountsCreate,
            root.GetProperty("permission_code").GetString());
        Assert.False(role.GetProperty("create_if_missing").GetBoolean());
        Assert.False(root.GetProperty("existing_install_reconciliation")
            .GetProperty("add_missing_roles")
            .GetBoolean());
        Assert.True(root.GetProperty("existing_install_reconciliation")
            .GetProperty("add_missing_permissions")
            .GetBoolean());
        Assert.Empty(root.GetProperty("automatic_user_assignments").EnumerateArray());

        var plan = new SystemRolePermissionReconciliationPlan(
            [
                new SystemRolePermissionTarget(
                    Guid.Parse(role.GetProperty("id").GetString()!),
                    role.GetProperty("code").GetString()!,
                    permissions),
            ]);

        var target = Assert.Single(plan.Targets);
        Assert.Equal(SystemAdministratorRoleId, target.RoleId);
        Assert.Equal("SYSTEM_ADMINISTRATOR", target.RoleCode);
        Assert.Equal(
            [ErpPermissions.CounterpartyAccountsCreate],
            target.PermissionsToEnsure);
    }

    [Fact]
    public void Target_RejectsUnknownDuplicateAndNonExactPermissionCodes()
    {
        Assert.Throws<ArgumentException>(() => new SystemRolePermissionTarget(
            SystemAdministratorRoleId,
            "SYSTEM_ADMINISTRATOR",
            ["unknown.permission"]));
        Assert.Throws<ArgumentException>(() => new SystemRolePermissionTarget(
            SystemAdministratorRoleId,
            "SYSTEM_ADMINISTRATOR",
            [
                ErpPermissions.CounterpartyAccountsCreate,
                ErpPermissions.CounterpartyAccountsCreate,
            ]));
        Assert.Throws<ArgumentException>(() => new SystemRolePermissionTarget(
            SystemAdministratorRoleId,
            "SYSTEM_ADMINISTRATOR",
            [$" {ErpPermissions.CounterpartyAccountsCreate}"]));
    }

    [Fact]
    public void Plan_RejectsDuplicateRoleIdentityOrCode()
    {
        var target = new SystemRolePermissionTarget(
            SystemAdministratorRoleId,
            "SYSTEM_ADMINISTRATOR",
            [ErpPermissions.CounterpartyAccountsCreate]);
        var sameId = new SystemRolePermissionTarget(
            SystemAdministratorRoleId,
            "OTHER_ADMINISTRATOR",
            [ErpPermissions.CounterpartyAccountsCreate]);
        var sameCode = new SystemRolePermissionTarget(
            Guid.NewGuid(),
            "SYSTEM_ADMINISTRATOR",
            [ErpPermissions.CounterpartyAccountsCreate]);

        Assert.Throws<ArgumentException>(() =>
            new SystemRolePermissionReconciliationPlan([target, sameId]));
        Assert.Throws<ArgumentException>(() =>
            new SystemRolePermissionReconciliationPlan([target, sameCode]));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, ContractPath)))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("GöymenERP repository kökü bulunamadı.");
    }
}
