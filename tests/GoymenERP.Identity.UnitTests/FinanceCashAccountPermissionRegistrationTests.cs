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
/// The Finance cash-account (kasa) permissions (API-30) are REGISTERED, not granted; who holds which is
/// a human gate. These cases keep registration and grant apart, pin the company-wide scope, and pin that
/// propose and approve are Critical like the bank master's pair (user decision of 13 November 2026 on
/// Issue #66) while read is not. The passivation and reactivation pairs (Issue #66 6857269313;
/// ASSUMPTION-API30-4) are separate names and Critical like the opening pair; holding one pair grants
/// nothing of another.
/// </summary>
public sealed class FinanceCashAccountPermissionRegistrationTests
{
    private const string SeedPath = "src/backend/GoymenERP.Api/Seed/identity-seed.json";

    private static readonly string[] AllNames =
    [
        "finance.cash-account.read",
        "finance.cash-account.propose",
        "finance.cash-account.approve",
        "finance.cash-account.passivation.propose",
        "finance.cash-account.passivation.approve",
        "finance.cash-account.reactivation.propose",
        "finance.cash-account.reactivation.approve",
    ];

    public static TheoryData<string> Names { get; } = new(AllNames);

    [Fact]
    public void The_names_are_registered_exactly_once_as_the_Finance_owner_spells_them()
    {
        Assert.Equal("finance.cash-account.read", ErpPermissions.FinanceCashAccountRead);
        Assert.Equal("finance.cash-account.propose", ErpPermissions.FinanceCashAccountPropose);
        Assert.Equal("finance.cash-account.approve", ErpPermissions.FinanceCashAccountApprove);
        Assert.Equal("finance.cash-account.passivation.propose", ErpPermissions.FinanceCashAccountPassivationPropose);
        Assert.Equal("finance.cash-account.passivation.approve", ErpPermissions.FinanceCashAccountPassivationApprove);
        Assert.Equal("finance.cash-account.reactivation.propose", ErpPermissions.FinanceCashAccountReactivationPropose);
        Assert.Equal("finance.cash-account.reactivation.approve", ErpPermissions.FinanceCashAccountReactivationApprove);
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
        // A cash account belongs to the server-resolved company; there is no site, location or
        // work-center dimension for a narrower assignment to mean.
        Assert.True(ErpPermissions.RequiresCompanyWideScope(permission));
    }

    [Fact]
    public void Propose_and_approve_are_critical_and_read_is_not()
    {
        Assert.True(ErpPermissions.IsCritical(ErpPermissions.FinanceCashAccountPropose));
        Assert.True(ErpPermissions.IsCritical(ErpPermissions.FinanceCashAccountApprove));
        Assert.True(ErpPermissions.IsCritical(ErpPermissions.FinanceCashAccountPassivationPropose));
        Assert.True(ErpPermissions.IsCritical(ErpPermissions.FinanceCashAccountPassivationApprove));
        Assert.True(ErpPermissions.IsCritical(ErpPermissions.FinanceCashAccountReactivationPropose));
        Assert.True(ErpPermissions.IsCritical(ErpPermissions.FinanceCashAccountReactivationApprove));
        Assert.False(ErpPermissions.IsCritical(ErpPermissions.FinanceCashAccountRead));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void No_seeded_role_carries_it(string permission)
    {
        var text = File.ReadAllText(Path.Combine(FindRepositoryRoot(), SeedPath));
        Assert.DoesNotContain(permission, text);
        // Sanity: the seed really carries other permissions, so the answer above is not an empty file.
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

        // Positive control in the same seeded database: an explicit company-wide grant of one name
        // grants that name only.
        var actor = Guid.NewGuid();
        var company = Guid.NewGuid();
        Grant(dbContext, actor, company, ErpPermissions.FinanceCashAccountPropose, AccessScopeType.Company, company);
        await dbContext.SaveChangesAsync();
        var service = Service(dbContext, actor, company);
        Assert.True(await service.HasPermissionAsync(ErpPermissions.FinanceCashAccountPropose, company));
        Assert.False(await service.HasPermissionAsync(ErpPermissions.FinanceCashAccountApprove, company));
        Assert.False(await service.HasPermissionAsync(ErpPermissions.FinanceCashAccountRead, company));
        Assert.False(await service.HasPermissionAsync(ErpPermissions.FinanceCashAccountPassivationPropose, company));
        Assert.False(await service.HasPermissionAsync(ErpPermissions.FinanceCashAccountReactivationPropose, company));
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
            .UseInMemoryDatabase($"identity-finance-cash-perm-registration-{Guid.NewGuid():N}").Options);

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
