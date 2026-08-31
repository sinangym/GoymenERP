using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GoymenERP.Identity.UnitTests;

public sealed class IdentityDataSeederAssignmentReplayTests
{
    private static readonly Guid UserId = Guid.Parse(
        "20000000-0000-0000-0000-000000000003");
    private static readonly Guid ApproverId = Guid.Parse(
        "20000000-0000-0000-0000-000000000004");
    private static readonly Guid RoleId = Guid.Parse(
        "30000000-0000-0000-0000-000000000003");
    private static readonly Guid OtherRoleId = Guid.Parse(
        "30000000-0000-0000-0000-000000000004");
    private static readonly Guid AssignmentId = Guid.Parse(
        "40000000-0000-0000-0000-000000000007");
    private static readonly Guid CompanyId = Guid.Parse(
        "10000000-0000-0000-0000-000000000003");
    private static readonly Guid OtherCompanyId = Guid.Parse(
        "10000000-0000-0000-0000-000000000002");
    private static readonly Guid SiteId = Guid.Parse(
        "11000000-0000-0000-0000-000000000099");
    private static readonly DateTimeOffset ValidFromUtc = new(
        2026,
        1,
        1,
        0,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public async Task ExactAssignmentReplay_IsIdempotentAndDoesNotRegrant()
    {
        await using var dbContext = CreateDbContext();
        var seed = CreateSeed(
            RoleId,
            CompanyId,
            AccessScopeType.Company,
            CompanyId);

        await SeedAsync(dbContext, seed);
        dbContext.ChangeTracker.Clear();
        await SeedAsync(dbContext, seed);

        var assignment = await dbContext.RoleAssignments
            .Include(x => x.Scopes)
            .SingleAsync();
        Assert.Equal(AssignmentId, assignment.Id);
        Assert.Equal(UserId, assignment.UserId);
        Assert.Equal(RoleId, assignment.RoleId);
        Assert.Equal(CompanyId, assignment.CompanyId);
        Assert.Equal(ValidFromUtc, assignment.ValidFromUtc);
        Assert.Null(assignment.ValidUntilUtc);
        Assert.Equal(ApproverId, assignment.ApprovedByUserId);
        Assert.Equal(RoleAssignmentStatus.Active, assignment.Status);
        var scope = Assert.Single(assignment.Scopes);
        Assert.Equal(AccessScopeType.Company, scope.ScopeType);
        Assert.Equal(CompanyId, scope.ScopeId);
        Assert.Equal(1, await dbContext.RoleAssignments.CountAsync());
    }

    [Fact]
    public async Task ExistingAssignment_WithDifferentCompany_IsRejected()
    {
        await using var dbContext = await CreateSeededDbContextAsync();
        var conflictingSeed = CreateSeed(
            RoleId,
            OtherCompanyId,
            AccessScopeType.Company,
            OtherCompanyId);

        await AssertSemanticCollisionAsync(dbContext, conflictingSeed);
    }

    [Fact]
    public async Task ExistingAssignment_WithDifferentScope_IsRejected()
    {
        await using var dbContext = await CreateSeededDbContextAsync();
        var conflictingSeed = CreateSeed(
            RoleId,
            CompanyId,
            AccessScopeType.Site,
            SiteId);

        await AssertSemanticCollisionAsync(dbContext, conflictingSeed);
    }

    [Fact]
    public async Task ExistingAssignment_WithDifferentRole_IsRejected()
    {
        await using var dbContext = await CreateSeededDbContextAsync();
        var conflictingSeed = CreateSeed(
            OtherRoleId,
            CompanyId,
            AccessScopeType.Company,
            CompanyId);

        await AssertSemanticCollisionAsync(dbContext, conflictingSeed);
    }

    [Fact]
    public async Task ExistingRevokedAssignment_IsRejectedWithoutRegrant()
    {
        await using var dbContext = await CreateSeededDbContextAsync();
        var assignment = await dbContext.RoleAssignments.SingleAsync();
        dbContext.Entry(assignment).Property(x => x.Status).CurrentValue =
            RoleAssignmentStatus.Revoked;
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SeedAsync(
                dbContext,
                CreateSeed(RoleId, CompanyId, AccessScopeType.Company, CompanyId)));

        Assert.Equal(
            $"Seed rol ataması kalıcı kayıtla eşleşmiyor: {AssignmentId:D}.",
            exception.Message);
        dbContext.ChangeTracker.Clear();
        Assert.Equal(
            RoleAssignmentStatus.Revoked,
            (await dbContext.RoleAssignments.SingleAsync()).Status);
    }

    private static async Task AssertSemanticCollisionAsync(
        IdentityDbContext dbContext,
        IdentitySeedOptions conflictingSeed)
    {
        dbContext.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => SeedAsync(dbContext, conflictingSeed));

        Assert.Equal(
            $"Seed rol ataması kalıcı kayıtla eşleşmiyor: {AssignmentId:D}.",
            exception.Message);
        Assert.Empty(dbContext.ChangeTracker.Entries());
        dbContext.ChangeTracker.Clear();
        var persisted = await dbContext.RoleAssignments
            .Include(x => x.Scopes)
            .SingleAsync();
        Assert.Equal(RoleId, persisted.RoleId);
        Assert.Equal(CompanyId, persisted.CompanyId);
        var scope = Assert.Single(persisted.Scopes);
        Assert.Equal(AccessScopeType.Company, scope.ScopeType);
        Assert.Equal(CompanyId, scope.ScopeId);
        Assert.Equal(1, await dbContext.RoleAssignments.CountAsync());
    }

    private static async Task<IdentityDbContext> CreateSeededDbContextAsync()
    {
        var dbContext = CreateDbContext();
        await SeedAsync(
            dbContext,
            CreateSeed(RoleId, CompanyId, AccessScopeType.Company, CompanyId));
        return dbContext;
    }

    private static Task SeedAsync(
        IdentityDbContext dbContext,
        IdentitySeedOptions seed)
    {
        var seeder = new IdentityDataSeeder(dbContext, Options.Create(seed));
        return seeder.SeedAsync(CancellationToken.None);
    }

    private static IdentityDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase($"identity-seed-replay-{Guid.NewGuid():N}")
            .Options;
        return new IdentityDbContext(options);
    }

    private static IdentitySeedOptions CreateSeed(
        Guid roleId,
        Guid companyId,
        AccessScopeType scopeType,
        Guid scopeId)
    {
        return new IdentitySeedOptions
        {
            Users =
            [
                new IdentityUserSeed
                {
                    Id = UserId,
                    DisplayName = "Grup Geliştirme Yöneticisi",
                },
                new IdentityUserSeed
                {
                    Id = ApproverId,
                    DisplayName = "Seed Onaylayıcısı",
                },
            ],
            Roles =
            [
                new IdentityRoleSeed
                {
                    Id = roleId,
                    Code = $"ROLE-{roleId:N}",
                    Name = "Seed replay test rolü",
                    IsSystem = true,
                    Permissions = [ErpPermissions.IdentityProfileRead],
                },
            ],
            Assignments =
            [
                new IdentityAssignmentSeed
                {
                    Id = AssignmentId,
                    UserId = UserId,
                    RoleId = roleId,
                    CompanyId = companyId,
                    ValidFromUtc = ValidFromUtc,
                    ValidUntilUtc = null,
                    ApprovedByUserId = ApproverId,
                    Scopes =
                    [
                        new IdentityScopeSeed
                        {
                            Type = scopeType.ToString(),
                            Id = scopeId,
                        },
                    ],
                },
            ],
        };
    }
}
