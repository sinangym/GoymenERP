using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Identity.UnitTests;

public sealed class TransferWarehousePermissionRegistrationTests
{
    [Theory]
    [InlineData("settings.warehouse-transfer-scale.manage")]
    [InlineData("logistics.transfer-departures.record")]
    [InlineData("logistics.transfer-outgoing-documents.read")]
    public void Transfer_owner_names_are_registered_once_company_wide_without_critical_or_seed_grants(string permission)
    {
        Assert.True(ErpPermissions.IsKnown(permission));
        Assert.Single(ErpPermissions.All, candidate => candidate == permission);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(permission));
        Assert.False(ErpPermissions.IsCritical(permission));
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        Assert.NotNull(directory);
        Assert.DoesNotContain(permission, File.ReadAllText(Path.Combine(directory.FullName, "src/backend/GoymenERP.Api/Seed/identity-seed.json")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("settings.warehouse-transfer-scale.manage", AccessScopeType.Company, true, true)]
    [InlineData("settings.warehouse-transfer-scale.manage", AccessScopeType.Site, true, false)]
    [InlineData("logistics.transfer-departures.record", AccessScopeType.Company, true, true)]
    [InlineData("logistics.transfer-departures.record", AccessScopeType.Company, false, false)]
    [InlineData("logistics.transfer-outgoing-documents.read", AccessScopeType.Company, true, true)]
    [InlineData("logistics.transfer-outgoing-documents.read", AccessScopeType.Site, true, false)]
    public async Task A_synthetic_explicit_company_assignment_alone_grants_each_new_purpose_and_no_other_company(
        string permission, AccessScopeType scope, bool assign, bool expected)
    {
        var actor = Guid.NewGuid(); var company = Guid.NewGuid(); var roleId = Guid.NewGuid();
        await using var database = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase("SENTETIK-transfer-purpose-" + Guid.NewGuid().ToString("N")).Options);
        var role = new RoleDefinition(roleId, "SENTETIK-" + roleId.ToString("N"), "SENTETIK", true);
        role.AddPermission(assign ? permission : ErpPermissions.InventoryStockTransferWrite);
        var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company, DateTimeOffset.UtcNow.AddMinutes(-1), null, null);
        assignment.AddScope(scope, scope == AccessScopeType.Company ? company : Guid.NewGuid());
        database.Users.Add(new UserAccount(actor, "SENTETIK", null)); database.Roles.Add(role); database.RoleAssignments.Add(assignment);
        await database.SaveChangesAsync();
        var authorization = new ErpAuthorizationService(database, new Actor(actor, company));
        Assert.Equal(expected, await authorization.HasPermissionAsync(permission, company));
        Assert.False(await authorization.HasPermissionAsync(permission, Guid.NewGuid()));
    }
    private sealed class Actor(Guid id, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => id.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid id) => AllowedCompanyIds.Contains(id);
    }
}
