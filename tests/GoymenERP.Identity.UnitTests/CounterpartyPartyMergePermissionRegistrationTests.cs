using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Identity.UnitTests;

/// <summary>
/// F-027 / EK-18 #76 (SINAN_KARARLARI_2026-10-21): the group-level duplicate Cari merge approval is a catalogue
/// REGISTRATION only, not Critical; who holds it (the group-manager persona) is a human gate. Company-wide only; no
/// shipped seed role carries it; it is neither the basic Cari edit name nor the legal-name name.
/// </summary>
public sealed class CounterpartyPartyMergePermissionRegistrationTests
{
    private const string SeedPath = "src/backend/GoymenERP.Api/Seed/identity-seed.json";

    private static readonly string[] AllNames =
    [
        "counterparty.parties.merge.approve",
    ];

    public static TheoryData<string> Names { get; } = new(AllNames);

    [Fact]
    public void The_names_are_registered_exactly_once_as_the_Counterparty_owner_spells_them()
    {
        Assert.Equal("counterparty.parties.merge.approve", ErpPermissions.CounterpartyPartiesMergeApprove);
        Assert.NotEqual(ErpPermissions.CounterpartyPartiesUpdateBasic, ErpPermissions.CounterpartyPartiesMergeApprove);
        Assert.NotEqual(ErpPermissions.CounterpartiesLegalNameApprove, ErpPermissions.CounterpartyPartiesMergeApprove);
        Assert.NotEqual(ErpPermissions.CounterpartyPartiesRead, ErpPermissions.CounterpartyPartiesMergeApprove);
        foreach (var name in AllNames)
        {
            Assert.True(ErpPermissions.IsKnown(name));
            Assert.Single(ErpPermissions.All, known => known == name);
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Each_requires_company_wide_scope(string permission) =>
        Assert.True(ErpPermissions.RequiresCompanyWideScope(permission));

    [Theory]
    [MemberData(nameof(Names))]
    public void It_is_not_critical(string permission) =>
        Assert.False(ErpPermissions.IsCritical(permission));

    [Theory]
    [MemberData(nameof(Names))]
    public void No_seeded_role_carries_it(string permission)
    {
        var text = File.ReadAllText(Path.Combine(FindRepositoryRoot(), SeedPath));
        Assert.DoesNotContain(permission, text);
        Assert.Contains(ErpPermissions.CounterpartyPartiesRead, text);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task A_site_scoped_assignment_does_not_grant_it(string permission)
    {
        var actor = Guid.NewGuid();
        var company = Guid.NewGuid();
        await using var dbContext = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase($"identity-cari-party-merge-perm-{Guid.NewGuid():N}").Options);
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
