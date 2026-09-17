using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Identity.UnitTests;

/// <summary>
/// The narrow operational product-selection permission is REGISTERED, not granted (K-1(a),
/// Business #66 5719251344; registration asked for in 5720016574 §3). These cases keep the two
/// apart: the platform must know the name, nothing may hand it out, an actor without an explicit
/// assignment must still be denied, and only an explicitly assigned actor — here a synthetic test
/// one — may be granted. Production grant stays a separate human gate.
/// </summary>
public sealed class ProductOperationalSelectionPermissionRegistrationTests
{
    private const string Permission = "product.operational-selection.read";
    private const string SeedPath = "src/backend/GoymenERP.Api/Seed/identity-seed.json";

    [Fact]
    public void The_name_is_registered_exactly_once_and_spelled_as_the_decision_spells_it()
    {
        Assert.Equal(Permission, ErpPermissions.ProductOperationalSelectionRead);
        Assert.True(ErpPermissions.IsKnown(ErpPermissions.ProductOperationalSelectionRead));
        Assert.Contains(ErpPermissions.ProductOperationalSelectionRead, ErpPermissions.All);
        Assert.Single(ErpPermissions.All, name => name == Permission);
        // Not the Product master read, and not a second spelling of anything already registered.
        Assert.DoesNotContain("product.master.read", ErpPermissions.All);
    }

    [Fact]
    public void It_requires_company_wide_scope_and_is_not_a_privilege_governance_permission()
    {
        // The read answers for the server-resolved company and has no site/location/work-center
        // dimension, so a narrower assignment would have nothing to mean.
        Assert.True(ErpPermissions.RequiresCompanyWideScope(
            ErpPermissions.ProductOperationalSelectionRead));
        // Critical holds the identity/security governance names; a product-selection read is not
        // one, and nothing in this slice adds an approval rule for it.
        Assert.False(ErpPermissions.IsCritical(ErpPermissions.ProductOperationalSelectionRead));
    }

    [Fact]
    public void No_seeded_role_or_assignment_receives_it()
    {
        var seed = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), SeedPath)))
            .RootElement.GetProperty("IdentitySeed");

        var rolesCarryingIt = seed.GetProperty("Roles").EnumerateArray()
            .Where(role => role.GetProperty("Permissions").EnumerateArray()
                .Any(permission => permission.GetString() == Permission))
            .Select(role => role.GetProperty("Code").GetString())
            .ToArray();

        // Registration is not a grant: the bootstrap seed hands this name to nobody, including
        // SYSTEM_ADMINISTRATOR.
        Assert.Empty(rolesCarryingIt);
        Assert.DoesNotContain(Permission, File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), SeedPath)));
        // Sanity: the seed really does carry other permissions, so an empty result is not an
        // empty file or a renamed property.
        Assert.Contains(
            ErpPermissions.OrganizationLocationsRead,
            seed.GetProperty("Roles").EnumerateArray()
                .SelectMany(role => role.GetProperty("Permissions").EnumerateArray())
                .Select(permission => permission.GetString()));
    }

    [Fact]
    public async Task An_actor_without_an_explicit_assignment_is_still_denied()
    {
        var actor = Guid.NewGuid();
        var company = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        // A real user with a real company-wide grant for a DIFFERENT permission: being known and
        // being assigned are separate, and one assignment does not spill into another name.
        Grant(dbContext, actor, company, ErpPermissions.OrganizationLocationsRead);
        await dbContext.SaveChangesAsync();
        var service = Service(dbContext, actor, company);

        Assert.Equal(
            PermissionScope.Denied,
            await service.GetPermissionScopeAsync(ErpPermissions.ProductOperationalSelectionRead, company));
        Assert.False(
            await service.HasPermissionAsync(ErpPermissions.ProductOperationalSelectionRead, company));
    }

    [Fact]
    public async Task An_explicitly_assigned_synthetic_actor_is_granted_for_that_company_only()
    {
        var actor = Guid.NewGuid();
        var company = Guid.NewGuid();
        var otherCompany = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        Grant(dbContext, actor, company, ErpPermissions.ProductOperationalSelectionRead);
        await dbContext.SaveChangesAsync();
        var service = Service(dbContext, actor, company, otherCompany);

        var scope = await service.GetPermissionScopeAsync(
            ErpPermissions.ProductOperationalSelectionRead, company);

        Assert.True(scope.IsGranted);
        Assert.True(scope.IsCompanyWide);
        // The grant is per company: the actor's other company answers no.
        Assert.False((await service.GetPermissionScopeAsync(
            ErpPermissions.ProductOperationalSelectionRead, otherCompany)).IsGranted);
    }

    [Fact]
    public async Task A_site_scoped_assignment_does_not_grant_it()
    {
        var actor = Guid.NewGuid();
        var company = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        Grant(dbContext, actor, company, ErpPermissions.ProductOperationalSelectionRead,
            scopeType: AccessScopeType.Site, scopeId: Guid.NewGuid());
        await dbContext.SaveChangesAsync();
        var service = Service(dbContext, actor, company);

        // Company-wide is required, so a narrower assignment is denied rather than silently
        // treated as a company-wide one.
        Assert.Equal(
            PermissionScope.Denied,
            await service.GetPermissionScopeAsync(ErpPermissions.ProductOperationalSelectionRead, company));
    }

    [Fact]
    public async Task An_expired_assignment_stops_granting_it()
    {
        var actor = Guid.NewGuid();
        var company = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        var roleId = Guid.NewGuid();
        var role = new RoleDefinition(roleId, $"ROLE-{roleId:N}", "Registration test role", true);
        role.AddPermission(ErpPermissions.ProductOperationalSelectionRead);
        var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company,
            DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-1), approvedByUserId: null);
        assignment.AddScope(AccessScopeType.Company, company);
        dbContext.Users.Add(new UserAccount(actor, "Registration test actor", null));
        dbContext.Roles.Add(role);
        dbContext.RoleAssignments.Add(assignment);
        await dbContext.SaveChangesAsync();

        Assert.False(await Service(dbContext, actor, company)
            .HasPermissionAsync(ErpPermissions.ProductOperationalSelectionRead, company));
    }

    private static ErpAuthorizationService Service(
        IdentityDbContext dbContext, Guid actor, params Guid[] companies) =>
        new(dbContext, new RegistrationTestUser(actor, companies));

    private static void Grant(
        IdentityDbContext dbContext,
        Guid actor,
        Guid company,
        string permission,
        AccessScopeType scopeType = AccessScopeType.Company,
        Guid? scopeId = null)
    {
        var roleId = Guid.NewGuid();
        var role = new RoleDefinition(roleId, $"ROLE-{roleId:N}", "Registration test role", true);
        role.AddPermission(permission);
        var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company,
            DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
        assignment.AddScope(scopeType, scopeId ?? company);
        dbContext.Users.Add(new UserAccount(actor, "Registration test actor", null));
        dbContext.Roles.Add(role);
        dbContext.RoleAssignments.Add(assignment);
    }

    private static IdentityDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase($"identity-perm-registration-{Guid.NewGuid():N}").Options);

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SeedPath)))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("GöymenERP repository kökü bulunamadı.");
    }

    private sealed class RegistrationTestUser(Guid userId, params Guid[] companyIds) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = companyIds.ToHashSet();
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
