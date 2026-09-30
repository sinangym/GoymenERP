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
/// T07(a) (#310): the two Inventory read permissions are REGISTERED, not granted. The platform must
/// know the names, nothing may hand them out, an actor without an explicit assignment is denied, a
/// site-scoped assignment is denied (the reads are not filtered by site), and only an explicitly,
/// company-wide assigned synthetic actor is granted. The production grant is a human gate. Same shape
/// as the product operational-selection registration (#277).
/// </summary>
public sealed class InventoryReadPermissionRegistrationTests
{
    private const string SeedPath = "src/backend/GoymenERP.Api/Seed/identity-seed.json";

    public static TheoryData<string> Permissions => new()
    {
        ErpPermissions.InventoryLabelsRead,
        ErpPermissions.InventoryBalancesRead,
        // INV-OPEN-1 part 2 (T07 = A): the opening record name, registered the same way (reason on the constant).
        ErpPermissions.InventoryOpeningStockRecord,
        // STK/T11: allocation create/release is its own command permission, never implied by these reads.
        ErpPermissions.InventoryPhysicalAllocationWrite,
        // R19: label split/merge is its own command permission; its only grant is a disposable browser test actor.
        ErpPermissions.InventoryLabelTransformationRecord,
        // AT-037: the stock count's record and approve names, two separate grants (maker ≠ checker); their only
        // grants are two different disposable browser test actors.
        ErpPermissions.InventoryStockCountRecord,
        ErpPermissions.InventoryStockCountApprove,
    };

    [Fact]
    public void The_names_are_spelled_as_the_slice_spells_them()
    {
        Assert.Equal("inventory.labels.read", ErpPermissions.InventoryLabelsRead);
        Assert.Equal("inventory.balances.read", ErpPermissions.InventoryBalancesRead);
        Assert.Equal("inventory.opening-stock.record", ErpPermissions.InventoryOpeningStockRecord);
        Assert.Equal("inventory.physical-allocation.write", ErpPermissions.InventoryPhysicalAllocationWrite);
        Assert.Equal("inventory.label-transformation.record", ErpPermissions.InventoryLabelTransformationRecord);
        Assert.Equal("inventory.stock-count.record", ErpPermissions.InventoryStockCountRecord);
        Assert.Equal("inventory.stock-count.approve", ErpPermissions.InventoryStockCountApprove);
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
            .UseInMemoryDatabase($"inventory-perm-registration-{Guid.NewGuid():N}").Options);

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
