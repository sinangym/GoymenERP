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
/// LOG-4a: the three Logistics names (the coordinator-confirmed technical mapping, #66 5743488059) are
/// REGISTERED, not granted. The platform must know the names, nothing may hand them out, an actor without an
/// explicit assignment is denied, a site-scoped assignment is denied (the instruction is company-wide), and
/// only an explicitly, company-wide assigned synthetic actor is granted. The production grant is a human gate.
/// Same shape as the Inventory registration.
/// </summary>
public sealed class LogisticsPermissionRegistrationTests
{
    private const string SeedPath = "src/backend/GoymenERP.Api/Seed/identity-seed.json";

    public static TheoryData<string> Permissions => new()
    {
        ErpPermissions.LogisticsLoadingInstructionsCreate,
        ErpPermissions.LogisticsLoadingInstructionsRead,
        ErpPermissions.LogisticsLoadsRecord,
        ErpPermissions.LogisticsShipmentsRecord,
        ErpPermissions.LogisticsDeliveriesRecord,
        ErpPermissions.LogisticsWeighingsRecord,
        ErpPermissions.LogisticsOperationsSalesWorkRead,
        ErpPermissions.LogisticsOperationsPurchaseWorkRead,
    };

    [Fact]
    public void The_names_are_spelled_as_the_slice_spells_them()
    {
        Assert.Equal("logistics.loading-instructions.create", ErpPermissions.LogisticsLoadingInstructionsCreate);
        Assert.Equal("logistics.loading-instructions.read", ErpPermissions.LogisticsLoadingInstructionsRead);
        Assert.Equal("logistics.loads.record", ErpPermissions.LogisticsLoadsRecord);
        Assert.Equal("logistics.shipments.record", ErpPermissions.LogisticsShipmentsRecord);
        Assert.Equal("logistics.deliveries.record", ErpPermissions.LogisticsDeliveriesRecord);
        Assert.Equal("logistics.weighings.record", ErpPermissions.LogisticsWeighingsRecord);
        Assert.Equal("logistics.operations-work.sales.read", ErpPermissions.LogisticsOperationsSalesWorkRead);
        Assert.Equal("logistics.operations-work.purchase.read", ErpPermissions.LogisticsOperationsPurchaseWorkRead);
    }

    [Theory]
    [MemberData(nameof(Permissions))]
    public void The_name_is_registered_once_company_wide_and_not_critical(string permission)
    {
        Assert.True(ErpPermissions.IsKnown(permission));
        Assert.Single(ErpPermissions.All, name => name == permission);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(permission));
        Assert.False(ErpPermissions.IsCritical(permission));
    }

    [Theory]
    [MemberData(nameof(Permissions))]
    public void No_seeded_role_carries_it_in_the_shipped_seed(string permission)
    {
        var seedText = File.ReadAllText(Path.Combine(FindRepositoryRoot(), SeedPath));
        var seed = JsonDocument.Parse(seedText).RootElement.GetProperty("IdentitySeed");
        Assert.DoesNotContain(permission, seedText);
        // Sanity: the seed really carries other permissions, so the absence is not an empty file.
        Assert.Contains(
            ErpPermissions.OrganizationLocationsRead,
            seed.GetProperty("Roles").EnumerateArray()
                .SelectMany(role => role.GetProperty("Permissions").EnumerateArray())
                .Select(value => value.GetString()));
    }

    [Theory]
    [MemberData(nameof(Permissions))]
    public async Task The_seeder_hands_it_to_nobody_and_an_explicit_company_wide_assignment_grants_it(string permission)
    {
        await using var dbContext = CreateDbContext();
        await new IdentityDataSeeder(dbContext, Options.Create(LoadShippedSeed())).SeedAsync(CancellationToken.None);

        var roles = await dbContext.Roles.Include(role => role.Permissions).ToListAsync();
        var assignments = await dbContext.RoleAssignments.Include(x => x.Scopes).ToListAsync();
        Assert.NotEmpty(roles);
        Assert.NotEmpty(assignments);
        Assert.DoesNotContain(roles, role => role.Permissions.Any(x => x.PermissionCode == permission));
        foreach (var assignment in assignments)
        {
            Assert.False(await Service(dbContext, assignment.UserId, assignment.CompanyId)
                .HasPermissionAsync(permission, assignment.CompanyId));
        }

        // Positive control in the same database: an explicit company-wide assignment is granted.
        var actor = Guid.NewGuid();
        var company = Guid.NewGuid();
        Grant(dbContext, actor, company, permission, AccessScopeType.Company, company);
        await dbContext.SaveChangesAsync();
        var scope = await Service(dbContext, actor, company).GetPermissionScopeAsync(permission, company);
        Assert.True(scope.IsGranted);
        Assert.True(scope.IsCompanyWide);
    }

    [Theory]
    [MemberData(nameof(Permissions))]
    public async Task A_site_scoped_assignment_does_not_grant_it(string permission)
    {
        var actor = Guid.NewGuid();
        var company = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        Grant(dbContext, actor, company, permission, AccessScopeType.Site, Guid.NewGuid());
        await dbContext.SaveChangesAsync();

        Assert.Equal(PermissionScope.Denied,
            await Service(dbContext, actor, company).GetPermissionScopeAsync(permission, company));
    }

    private static IdentitySeedOptions LoadShippedSeed()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(FindRepositoryRoot(), SeedPath)));
        var section = document.RootElement.GetProperty(IdentitySeedOptions.SectionName).GetRawText();
        var seed = JsonSerializer.Deserialize<IdentitySeedOptions>(
            section, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(seed);
        return seed;
    }

    private static ErpAuthorizationService Service(IdentityDbContext dbContext, Guid actor, params Guid[] companies) =>
        new(dbContext, new RegistrationTestUser(actor, companies));

    private static void Grant(IdentityDbContext dbContext, Guid actor, Guid company, string permission,
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
            .UseInMemoryDatabase($"logistics-perm-registration-{Guid.NewGuid():N}").Options);

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
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
