using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Identity.UnitTests;

public sealed class ErpAuthorizationServiceTests
{
    [Fact]
    public async Task RolePermissionAndSiteScope_AreCombinedFailClosed()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        AddAssignment(
            dbContext,
            userId,
            companyId,
            ErpPermissions.OrganizationLocationsRead,
            AccessScopeType.Site,
            siteId);
        await dbContext.SaveChangesAsync();
        var service = new ErpAuthorizationService(
            dbContext,
            new FakeCurrentUser(userId, companyId));

        var scope = await service.GetPermissionScopeAsync(
            ErpPermissions.OrganizationLocationsRead,
            companyId);
        var otherCompanyScope = await service.GetPermissionScopeAsync(
            ErpPermissions.OrganizationLocationsRead,
            Guid.NewGuid());

        Assert.True(scope.IsGranted);
        Assert.False(scope.IsCompanyWide);
        Assert.Single(scope.RoleCodes);
        Assert.Contains(siteId, scope.SiteIds);
        Assert.False(otherCompanyScope.IsGranted);
    }

    [Fact]
    public async Task UnknownPermission_IsNeverGranted()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        var service = new ErpAuthorizationService(
            dbContext,
            new FakeCurrentUser(userId, companyId));

        // Must stay unregistered: a registration would turn this into a real grant check.
        const string unregisteredPermission = "unknown.permission";
        Assert.DoesNotContain(unregisteredPermission, ErpPermissions.All);

        var scope = await service.GetPermissionScopeAsync(unregisteredPermission, companyId);

        Assert.Equal(PermissionScope.Denied, scope);
    }

    [Fact]
    public async Task AuditRead_WithSiteScope_IsDeniedBecauseCompanyScopeIsRequired()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        AddAssignment(
            dbContext,
            userId,
            companyId,
            ErpPermissions.IdentityAuditRead,
            AccessScopeType.Site,
            Guid.NewGuid());
        await dbContext.SaveChangesAsync();
        var service = new ErpAuthorizationService(
            dbContext,
            new FakeCurrentUser(userId, companyId));

        var scope = await service.GetPermissionScopeAsync(
            ErpPermissions.IdentityAuditRead,
            companyId);
        var isGranted = await service.HasPermissionAsync(
            ErpPermissions.IdentityAuditRead,
            companyId);

        Assert.Equal(PermissionScope.Denied, scope);
        Assert.False(isGranted);
    }

    [Fact]
    public async Task ExpiredAssignment_IsDenied()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        var role = new RoleDefinition(roleId, "EXPIRED-ROLE", "Süresi dolmuş rol", true);
        role.AddPermission(ErpPermissions.OrganizationLocationsRead);
        var assignment = new RoleAssignment(
            Guid.NewGuid(),
            userId,
            roleId,
            companyId,
            DateTimeOffset.UtcNow.AddDays(-2),
            DateTimeOffset.UtcNow.AddDays(-1),
            Guid.NewGuid());
        assignment.AddScope(AccessScopeType.Company, companyId);
        dbContext.Users.Add(new UserAccount(userId, "Süresi dolmuş kullanıcı", null));
        dbContext.Roles.Add(role);
        dbContext.RoleAssignments.Add(assignment);
        await dbContext.SaveChangesAsync();
        var service = new ErpAuthorizationService(
            dbContext,
            new FakeCurrentUser(userId, companyId));

        var scope = await service.GetPermissionScopeAsync(
            ErpPermissions.OrganizationLocationsRead,
            companyId);

        Assert.False(scope.IsGranted);
    }

    private static IdentityDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase($"identity-unit-{Guid.NewGuid():N}")
            .Options;
        return new IdentityDbContext(options);
    }

    private static void AddAssignment(
        IdentityDbContext dbContext,
        Guid userId,
        Guid companyId,
        string permission,
        AccessScopeType scopeType,
        Guid scopeId)
    {
        var roleId = Guid.NewGuid();
        var role = new RoleDefinition(roleId, $"ROLE-{roleId:N}", "Test rolü", true);
        role.AddPermission(permission);
        var assignment = new RoleAssignment(
            Guid.NewGuid(),
            userId,
            roleId,
            companyId,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            null,
            Guid.NewGuid());
        assignment.AddScope(scopeType, scopeId);

        dbContext.Users.Add(new UserAccount(userId, "Test kullanıcısı", null));
        dbContext.Roles.Add(role);
        dbContext.RoleAssignments.Add(assignment);
    }

    private sealed class FakeCurrentUser(Guid userId, params Guid[] companyIds)
        : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public string UserId => userId.ToString("D");

        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = companyIds.ToHashSet();

        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
