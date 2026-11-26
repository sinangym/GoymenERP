using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Identity.UnitTests;

/// <summary>SENTETIK database only: template identifiers are not real role definitions, assignments or acceptance.</summary>
public sealed class WorkDelegationPermissionHolderTests
{
    private static readonly Guid Company = Guid.Parse("d3120000-0000-4000-8000-000000000001");
    private static readonly DateTimeOffset At = new(2026, 11, 26, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task General_manager_identity_is_found_even_when_another_role_holds_the_requested_permission()
    {
        await using var db = Database();
        var actor = Guid.NewGuid();
        Grant(db, actor, ErpPersonaRoleCodes.GeneralManager, ErpPermissions.OrganizationContextRead, AccessScopeType.Company, Company);
        Grant(db, actor, "SENTETIK_ORDER_ROLE", ErpPermissions.SalesOrdersApprove, AccessScopeType.Company, Company);
        await db.SaveChangesAsync();
        var answer = await Port(db).ListAsync(new(Company, ErpPermissions.SalesOrdersApprove));
        Assert.Equal(UserDirectoryOutcomeV1.Found, answer.Outcome);
        var holder = Assert.Single(answer.Holders);
        Assert.True(holder.IsGeneralManager);
        Assert.Null(holder.ModuleManagerScope);
    }

    [Fact]
    public async Task System_only_permission_never_becomes_a_module_manager_scope_and_revocation_is_measured_again()
    {
        await using var db = Database();
        var system = Guid.NewGuid(); var manager = Guid.NewGuid();
        Grant(db, system, ErpPersonaRoleCodes.SystemAdministrator, ErpPermissions.SalesOrdersApprove, AccessScopeType.Company, Company);
        var expires = At.AddHours(1);
        Grant(db, manager, ErpPersonaRoleCodes.SalesManager, ErpPermissions.SalesOrdersApprove, AccessScopeType.Company, Company, expires);
        await db.SaveChangesAsync();
        var clock = new Clock(); var port = Port(db, clock);
        var answer = await port.ListAsync(new(Company, ErpPermissions.SalesOrdersApprove));
        Assert.Equal(UserDirectoryOutcomeV1.Found, answer.Outcome);
        Assert.Null(answer.Holders.Single(holder => holder.UserId == system).ModuleManagerScope);
        Assert.True(answer.Holders.Single(holder => holder.UserId == manager).ModuleManagerScope!.IsCompanyWide);
        clock.Now = expires;
        Assert.DoesNotContain((await port.ListAsync(new(Company, ErpPermissions.SalesOrdersApprove))).Holders, holder => holder.UserId == manager);
    }

    [Fact]
    public async Task A_broader_system_grant_is_kept_separate_from_the_managers_actual_site_scope()
    {
        await using var db = Database();
        var person = Guid.NewGuid(); var site = Guid.NewGuid();
        Grant(db, person, ErpPersonaRoleCodes.SystemAdministrator, ErpPermissions.IdentityAccessApprove, AccessScopeType.Company, Company);
        Grant(db, person, ErpPersonaRoleCodes.OperationsManager, ErpPermissions.IdentityAccessApprove, AccessScopeType.Site, site);
        await db.SaveChangesAsync();
        var answer = await Port(db).ListAsync(new(Company, ErpPermissions.IdentityAccessApprove));
        Assert.Equal(UserDirectoryOutcomeV1.Found, answer.Outcome);
        var holder = Assert.Single(answer.Holders);
        Assert.True(holder.IsCompanyWide);
        Assert.False(holder.ModuleManagerScope!.IsCompanyWide);
        Assert.Equal(new[] { site }, holder.ModuleManagerScope.SiteIds);
        Assert.Equal(UserDirectoryOutcomeV1.Denied, (await Port(db).ListAsync(new(Guid.NewGuid(), ErpPermissions.IdentityAccessApprove))).Outcome);
    }

    private static IdentityDbContext Database() => new(new DbContextOptionsBuilder<IdentityDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
    private static PermissionHolderListPortV1 Port(IdentityDbContext db, TimeProvider? clock = null)
    {
        var company = new CompanyContext(); company.Set(Company);
        return new(db, company, clock ?? new Clock());
    }
    private static void Grant(IdentityDbContext db, Guid actor, string code, string permission,
        AccessScopeType scopeType, Guid scopeId, DateTimeOffset? until = null)
    {
        if (!db.Users.Local.Any(user => user.Id == actor)) db.Users.Add(new UserAccount(actor, "SENTETIK", null));
        var role = new RoleDefinition(Guid.NewGuid(), code, "SENTETIK role", false); role.AddPermission(permission);
        var assignment = new RoleAssignment(Guid.NewGuid(), actor, role.Id, Company, At.AddMinutes(-1), until, null);
        assignment.AddScope(scopeType, scopeId);
        db.Roles.Add(role); db.RoleAssignments.Add(assignment);
    }
    private sealed class Clock : TimeProvider { public DateTimeOffset Now { get; set; } = At; public override DateTimeOffset GetUtcNow() => Now; }
}
