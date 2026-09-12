using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Identity.UnitTests;

// Explicitly controlled model/projection tests. Not actual SQL/IdP/issuer assignment evidence.
// Production Identity model/DI remains unchanged until the exact runtime admission.
public sealed class ProductMasterAuthorizationPreparationTests
{
    [Fact]
    public async Task MissingVerifiedIssuerDeniesBeforeAnUnregisteredModelCanBeQueried()
    {
        await using var database = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var company = new CompanyContext();
        company.Set(Guid.NewGuid());
        var authority = new SqlProductMasterAuthorizationService(database, new Actor(Guid.NewGuid(), company.CompanyId), company, null);
        Assert.False((await authority.GetSnapshotAsync(GroupSharedMasterKind.Product, GroupSharedMasterOperation.Read)).IsGranted);
        Assert.False((await authority.GetSnapshotAsync(GroupSharedMasterKind.Product, GroupSharedMasterOperation.DraftCreate)).IsGranted);
    }

    [Fact]
    public async Task SameScopedAdapterSeesRevocationInsteadOfCachingItsFirstGrant()
    {
        await using var setup = await Fixture.CreateAsync();
        var before = await setup.Authority.GetSnapshotAsync(GroupSharedMasterKind.Product, GroupSharedMasterOperation.DraftCreate);
        var unchanged = await setup.Authority.GetSnapshotAsync(GroupSharedMasterKind.Product, GroupSharedMasterOperation.DraftCreate);
        Assert.True(before.IsGranted);
        Assert.Equal(before.AuthorityRevision, unchanged.AuthorityRevision);
        Assert.False((await setup.Authority.GetSnapshotAsync(GroupSharedMasterKind.Product, GroupSharedMasterOperation.Read)).IsGranted);
        setup.Grant.Revoke(Guid.NewGuid(), "explicit-test-revocation", DateTimeOffset.UtcNow);
        await setup.Seed.SaveChangesAsync();
        var after = await setup.Authority.GetSnapshotAsync(GroupSharedMasterKind.Product, GroupSharedMasterOperation.DraftCreate);
        Assert.False(after.IsGranted);
    }

    [Fact]
    public async Task CompanySystemAdministratorWithoutAnExplicitGlobalAssignmentStillDenies()
    {
        await using var setup = await Fixture.CreateAsync();
        var other = new UserAccount(Guid.NewGuid(), "Company-only synthetic actor", null);
        var membership = new RoleAssignment(Guid.NewGuid(), other.Id, setup.SystemRole.Id, setup.Company.CompanyId,
            DateTimeOffset.UtcNow.AddHours(-1), null, null);
        membership.AddScope(AccessScopeType.Company, setup.Company.CompanyId);
        setup.Seed.AddRange(other, membership);
        await setup.Seed.SaveChangesAsync();
        setup.Current.ActorId = other.Id;
        Assert.False((await setup.Authority.GetSnapshotAsync(GroupSharedMasterKind.Product, GroupSharedMasterOperation.DraftCreate)).IsGranted);
    }

    [Fact]
    public async Task LosingOriginMembershipDeniesEvenWhileGlobalAssignmentRemainsActive()
    {
        await using var setup = await Fixture.CreateAsync();
        Assert.True((await setup.Authority.GetSnapshotAsync(GroupSharedMasterKind.Product, GroupSharedMasterOperation.DraftCreate)).IsGranted);
        setup.Seed.RemoveRange(await setup.Seed.Set<RoleAssignmentScope>().ToListAsync());
        await setup.Seed.SaveChangesAsync();
        Assert.True(setup.Grant.IsActive);
        Assert.False((await setup.Authority.GetSnapshotAsync(GroupSharedMasterKind.Product, GroupSharedMasterOperation.DraftCreate)).IsGranted);
    }

    private sealed class Actor(Guid actorId, Guid origin) : ICurrentUser
    {
        public Guid ActorId { get; set; } = actorId;
        public bool IsAuthenticated => true;
        public string UserId => ActorId.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { origin };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class AuthorityModel(DbContextOptions<AuthorityModel> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<UserAccount>().HasKey(x => x.Id);
            model.Entity<ExternalIdentityLink>().HasKey(x => x.Id);
            model.Entity<RoleDefinition>().HasKey(x => x.Id);
            model.Entity<RolePermission>().HasKey(x => new { x.RoleId, x.PermissionCode });
            model.Entity<RoleDefinition>().HasMany(x => x.Permissions).WithOne().HasForeignKey(x => x.RoleId);
            model.Entity<RoleAssignment>().HasKey(x => x.Id);
            model.Entity<RoleAssignmentScope>().HasKey(x => new { x.RoleAssignmentId, x.ScopeType, x.ScopeId });
            model.Entity<RoleAssignment>().HasMany(x => x.Scopes).WithOne()
                .HasForeignKey(x => new { x.CompanyId, x.RoleAssignmentId }).HasPrincipalKey(x => new { x.CompanyId, x.Id });
            model.ConfigureProductMasterAssignment();
        }
    }

    private sealed class Fixture(AuthorityModel seed, IdentityDbContext reader, CompanyContext company,
        Actor current, ProductMasterAssignment grant, RoleDefinition systemRole, SqlProductMasterAuthorizationService authority) : IAsyncDisposable
    {
        public AuthorityModel Seed => seed;
        public CompanyContext Company => company;
        public Actor Current => current;
        public ProductMasterAssignment Grant => grant;
        public RoleDefinition SystemRole => systemRole;
        public SqlProductMasterAuthorizationService Authority => authority;

        public static async Task<Fixture> CreateAsync()
        {
            var name = Guid.NewGuid().ToString();
            var root = new InMemoryDatabaseRoot();
            var seed = new AuthorityModel(new DbContextOptionsBuilder<AuthorityModel>().UseInMemoryDatabase(name, root).Options);
            var company = new CompanyContext();
            company.Set(Guid.NewGuid());
            var user = new UserAccount(Guid.NewGuid(), "Synthetic global actor", null);
            var issuer = new UserAccount(Guid.NewGuid(), "Synthetic decision identity, not Sinan", null);
            var external = new ExternalIdentityLink(Guid.NewGuid(), issuer.Id, "controlled-test-issuer", "controlled-decision-subject");
            var systemRole = new RoleDefinition(Guid.NewGuid(), ProductMasterAssignment.RequiredRoleCode, "Existing grantee role", true);
            var memberRole = new RoleDefinition(Guid.NewGuid(), "COMPANY_OPERATOR", "Origin membership only", false);
            var membership = new RoleAssignment(Guid.NewGuid(), user.Id, memberRole.Id, company.CompanyId,
                DateTimeOffset.UtcNow.AddHours(-1), null, null);
            membership.AddScope(AccessScopeType.Company, company.CompanyId);
            var grant = new ProductMasterAssignment(Guid.NewGuid(), user.Id, systemRole.Id, ProductMasterOperation.DraftCreate,
                issuer.Id, external.Id, Guid.NewGuid(), "explicit-controlled-test-decision", DateTimeOffset.UtcNow.AddHours(-1), null);
            seed.AddRange(user, issuer, external, systemRole, memberRole, membership, grant);
            // InMemory does not generate SQL rowversion. The fixture supplies bytes; this is
            // deliberately not evidence that SQL concurrency or issuer bootstrap has passed.
            seed.Entry(grant).Property(x => x.RowVersion).CurrentValue = [1, 0, 0, 0, 0, 0, 0, 0];
            await seed.SaveChangesAsync();
            var reader = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(name, root).UseModel(seed.Model).Options);
            var current = new Actor(user.Id, company.CompanyId);
            return new(seed, reader, company, current, grant, systemRole,
                new(reader, current, company, new(issuer.Id, external.Id)));
        }

        public async ValueTask DisposeAsync() { await reader.DisposeAsync(); await seed.DisposeAsync(); }
    }
}
