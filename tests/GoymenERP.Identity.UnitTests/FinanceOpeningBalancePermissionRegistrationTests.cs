using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Seeding;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GoymenERP.Identity.UnitTests;

/// <summary>
/// The Finance opening balance permissions (FIN-S9; FNS-D23) are REGISTERED, not granted; who holds which (the specialist
/// proposes, the manager approves) is a human gate. These cases keep registration and grant apart, pin the company-wide
/// scope and that no name is Critical (no Critical classification was decided). No role is seeded with any of them.
/// </summary>
public sealed class FinanceOpeningBalancePermissionRegistrationTests
{
    private const string SeedPath = "src/backend/GoymenERP.Api/Seed/identity-seed.json";

    private static readonly string[] AllNames =
    [
        "finance.opening-balances.read",
        "finance.opening-balances.propose",
        "finance.opening-balances.approve",
    ];

    public static TheoryData<string> Names { get; } = new(AllNames);

    [Fact]
    public void The_names_are_registered_exactly_once_as_the_Finance_owner_spells_them()
    {
        Assert.Equal("finance.opening-balances.read", ErpPermissions.FinanceOpeningBalancesRead);
        Assert.Equal("finance.opening-balances.propose", ErpPermissions.FinanceOpeningBalancesPropose);
        Assert.Equal("finance.opening-balances.approve", ErpPermissions.FinanceOpeningBalancesApprove);
        foreach (var name in AllNames)
        {
            Assert.True(ErpPermissions.IsKnown(name));
            Assert.Single(ErpPermissions.All, known => known == name);
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Each_requires_company_wide_scope(string permission)
    {
        // An opening balance belongs to the server-resolved company; there is no site dimension.
        Assert.True(ErpPermissions.RequiresCompanyWideScope(permission));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void None_is_critical(string permission)
    {
        Assert.False(ErpPermissions.IsCritical(permission));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void No_seeded_role_carries_it(string permission)
    {
        var text = File.ReadAllText(Path.Combine(FindRepositoryRoot(), SeedPath));
        Assert.DoesNotContain(permission, text);
        Assert.Contains(ErpPermissions.OrganizationLocationsRead, text);
    }

    [Fact]
    public async Task The_seeder_hands_none_of_them_to_a_seeded_actor_and_an_explicit_grant_is_the_only_way()
    {
        await using var dbContext = CreateDbContext();
        await new IdentityDataSeeder(dbContext, Options.Create(LoadShippedSeed())).SeedAsync(CancellationToken.None);
        var seededAssignments = await dbContext.RoleAssignments.ToListAsync();
        Assert.NotEmpty(seededAssignments);

        foreach (var assignment in seededAssignments)
        {
            foreach (var name in AllNames)
            {
                Assert.False(
                    await Service(dbContext, assignment.UserId, assignment.CompanyId)
                        .HasPermissionAsync(name, assignment.CompanyId),
                    $"seeded actor {assignment.UserId} must not hold {name} in {assignment.CompanyId}");
            }
        }

        // Positive control: an explicit grant of approve grants approve only; it implies neither propose nor read.
        var actor = Guid.NewGuid();
        var company = Guid.NewGuid();
        Grant(dbContext, actor, company, ErpPermissions.FinanceOpeningBalancesApprove, AccessScopeType.Company, company);
        await dbContext.SaveChangesAsync();
        var service = Service(dbContext, actor, company);
        Assert.True(await service.HasPermissionAsync(ErpPermissions.FinanceOpeningBalancesApprove, company));
        Assert.False(await service.HasPermissionAsync(ErpPermissions.FinanceOpeningBalancesPropose, company));
        Assert.False(await service.HasPermissionAsync(ErpPermissions.FinanceOpeningBalancesRead, company));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task A_site_scoped_assignment_does_not_grant_it(string permission)
    {
        var actor = Guid.NewGuid();
        var company = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        Grant(dbContext, actor, company, permission, AccessScopeType.Site, Guid.NewGuid());
        await dbContext.SaveChangesAsync();

        Assert.Equal(
            PermissionScope.Denied,
            await Service(dbContext, actor, company).GetPermissionScopeAsync(permission, company));
    }

    private static IdentitySeedOptions LoadShippedSeed()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), SeedPath)));
        var section = document.RootElement.GetProperty(IdentitySeedOptions.SectionName).GetRawText();
        var seed = JsonSerializer.Deserialize<IdentitySeedOptions>(
            section,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(seed);
        return seed;
    }

    private static ErpAuthorizationService Service(
        IdentityDbContext dbContext, Guid actor, params Guid[] companies) =>
        new(dbContext, new RegistrationTestUser(actor, companies));

    private static void Grant(
        IdentityDbContext dbContext, Guid actor, Guid company, string permission,
        AccessScopeType scopeType, Guid scopeId)
    {
        var roleId = Guid.NewGuid();
        var role = new RoleDefinition(roleId, $"ROLE-{roleId:N}", "Registration test role", true);
        role.AddPermission(permission);
        var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company,
            DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
        assignment.AddScope(scopeType, scopeId);
        dbContext.Users.Add(new UserAccount(actor, "Registration test actor", null));
        dbContext.Roles.Add(role);
        dbContext.RoleAssignments.Add(assignment);
    }

    private static IdentityDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase($"identity-finance-opening-balance-perm-registration-{Guid.NewGuid():N}").Options);

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
