using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Identity.UnitTests;

/// <summary>
/// KIM-S23 (KIM-D28, DEC-ORG-005): the two company VKN names are REGISTERED, not granted; who holds them is a human
/// gate. Company-wide only; neither is Critical (KIM-D36: the approve name is not Critical; the administrator persona
/// is its intended holder, no grant is made, maker-checker stays); no shipped seed role carries either.
/// </summary>
public sealed class OrganizationCompanyTaxIdPermissionRegistrationTests
{
    private const string SeedPath = "src/backend/GoymenERP.Api/Seed/identity-seed.json";

    private static readonly string[] AllNames =
    [
        "organization.company.tax-id.request",
        "organization.company.tax-id.approve",
    ];

    public static TheoryData<string> Names { get; } = new(AllNames);

    [Fact]
    public void The_names_are_registered_exactly_once_as_the_Organization_owner_spells_them()
    {
        Assert.Equal("organization.company.tax-id.request", ErpPermissions.OrganizationCompanyTaxIdRequest);
        Assert.Equal("organization.company.tax-id.approve", ErpPermissions.OrganizationCompanyTaxIdApprove);
        foreach (var name in AllNames)
        {
            Assert.True(ErpPermissions.IsKnown(name));
            Assert.Single(ErpPermissions.All, known => known == name);
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Each_requires_company_wide_scope_and_is_not_critical(string permission)
    {
        Assert.True(ErpPermissions.RequiresCompanyWideScope(permission));
        Assert.False(ErpPermissions.IsCritical(permission));
    }

    [Fact]
    public void The_approve_name_stays_outside_the_critical_set_and_no_seed_grants_it()
    {
        // KIM-D36: not Critical, no grant; the persona (administrator) is a human assignment decision.
        Assert.False(ErpPermissions.IsCritical(ErpPermissions.OrganizationCompanyTaxIdApprove));
        var text = File.ReadAllText(Path.Combine(FindRepositoryRoot(), SeedPath));
        Assert.DoesNotContain(ErpPermissions.OrganizationCompanyTaxIdApprove, text);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void No_seeded_role_carries_it(string permission)
    {
        var text = File.ReadAllText(Path.Combine(FindRepositoryRoot(), SeedPath));
        Assert.DoesNotContain(permission, text);
        Assert.Contains(ErpPermissions.OrganizationContextRead, text);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task A_site_scoped_assignment_does_not_grant_it(string permission)
    {
        var actor = Guid.NewGuid();
        var company = Guid.NewGuid();
        await using var dbContext = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase($"identity-org-tax-perm-{Guid.NewGuid():N}").Options);
        var roleId = Guid.NewGuid();
        var role = new RoleDefinition(roleId, $"ROLE-{roleId:N}", "Registration test role", true);
        role.AddPermission(permission);
        var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company,
            DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
        assignment.AddScope(AccessScopeType.Site, Guid.NewGuid());
        dbContext.Users.Add(new UserAccount(actor, "Registration test actor", null));
        dbContext.Roles.Add(role);
        dbContext.RoleAssignments.Add(assignment);
        await dbContext.SaveChangesAsync();

        Assert.Equal(
            PermissionScope.Denied,
            await new ErpAuthorizationService(dbContext, new RegistrationTestUser(actor, company))
                .GetPermissionScopeAsync(permission, company));
    }

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
