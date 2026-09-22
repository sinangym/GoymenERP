using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Identity.UnitTests;

public sealed class ActiveRoleCodeReaderTests
{
    [Fact]
    public async Task Active_company_role_is_read_without_a_permission_and_each_call_is_fresh()
    {
        var actor = Guid.NewGuid();
        var company = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(new UserAccount(actor, "Persona reader actor", null));
        var assignment = Assign(dbContext, actor, company, ErpPersonaRoleCodes.SalesSupport,
            DateTimeOffset.UtcNow.AddMinutes(-5), null);
        await dbContext.SaveChangesAsync();
        var service = Service(dbContext, actor, company);

        Assert.Equal([ErpPersonaRoleCodes.SalesSupport],
            (await service.GetActiveRoleCodesAsync(company)).Order(StringComparer.Ordinal));

        dbContext.RoleAssignments.Remove(assignment);
        await dbContext.SaveChangesAsync();

        Assert.Empty(await service.GetActiveRoleCodesAsync(company));
    }

    [Fact]
    public async Task Expired_future_inactive_and_other_company_roles_do_not_cross_the_boundary()
    {
        var actor = Guid.NewGuid();
        var company = Guid.NewGuid();
        var otherCompany = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(new UserAccount(actor, "Persona reader actor", null));
        Assign(dbContext, actor, company, "EXPIRED_ROLE", DateTimeOffset.UtcNow.AddHours(-2),
            DateTimeOffset.UtcNow.AddHours(-1));
        Assign(dbContext, actor, company, "FUTURE_ROLE", DateTimeOffset.UtcNow.AddHours(1), null);
        Assign(dbContext, actor, otherCompany, "OTHER_COMPANY_ROLE", DateTimeOffset.UtcNow.AddMinutes(-5), null);
        var inactiveRole = Assign(dbContext, actor, company, "INACTIVE_ROLE",
            DateTimeOffset.UtcNow.AddMinutes(-5), null).RoleId;
        dbContext.Entry(dbContext.Roles.Local.Single(role => role.Id == inactiveRole))
            .Property(nameof(RoleDefinition.IsActive)).CurrentValue = false;
        await dbContext.SaveChangesAsync();
        var service = Service(dbContext, actor, company, otherCompany);

        Assert.Empty(await service.GetActiveRoleCodesAsync(company));
        Assert.Equal(["OTHER_COMPANY_ROLE"],
            (await service.GetActiveRoleCodesAsync(otherCompany)).Order(StringComparer.Ordinal));
        Assert.Empty(await Service(dbContext, actor).GetActiveRoleCodesAsync(company));
    }

    private static RoleAssignment Assign(
        IdentityDbContext dbContext,
        Guid actor,
        Guid company,
        string roleCode,
        DateTimeOffset validFromUtc,
        DateTimeOffset? validUntilUtc)
    {
        var roleId = Guid.NewGuid();
        dbContext.Roles.Add(new RoleDefinition(roleId, roleCode, roleCode, isSystem: false));
        var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company,
            validFromUtc, validUntilUtc, approvedByUserId: null);
        assignment.AddScope(AccessScopeType.Company, company);
        dbContext.RoleAssignments.Add(assignment);
        return assignment;
    }

    private static ErpAuthorizationService Service(
        IdentityDbContext dbContext,
        Guid actor,
        params Guid[] companies) => new(dbContext, new ProbeUser(actor, companies));

    private static IdentityDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase($"active-role-reader-{Guid.NewGuid():N}").Options);

    private sealed class ProbeUser(Guid userId, params Guid[] companies) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = companies.ToHashSet();
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
