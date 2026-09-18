using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// The fresh-authority path against the real provider and a real database, through the host's own
/// DI: no authorization double anywhere. One service scope stands for one request, which is what
/// makes the cached answer and the refreshed one distinguishable (Business #66 5720113915).
/// </summary>
public sealed class CentralAuthorityRevalidationSqlServerRelationalAcceptanceTests
{
    private const string Permission = ErpPermissions.OrganizationLocationsRead;

    [SqlServerFact]
    public async Task A_revocation_is_measured_inside_the_request_and_refuses_the_next_one()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var actor = Guid.NewGuid();
        var company = TestCompanyIds.CompanyA;
        var reads = new CountingPermissionScopeReadObserver();
        var factory = new SqlServerRelationalApiFactory(
            baseConnectionString,
            services =>
            {
                services.RemoveAll<ICurrentUser>();
                services.AddSingleton<ICurrentUser>(new FixedCurrentUser(actor, company));
                services.RemoveAll<IPermissionScopeReadObserver>();
                services.AddSingleton<IPermissionScopeReadObserver>(reads);
            });

        try
        {
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();

            await GrantAsync(factory, actor, company);

            // One request: both ports resolve to the same scoped instance.
            await using var request = factory.Services.CreateAsyncScope();
            var cached = request.ServiceProvider.GetRequiredService<IErpAuthorizationService>();
            var fresh = request.ServiceProvider.GetRequiredService<IErpAuthorityRevalidationService>();
            Assert.Same(cached, fresh);

            Assert.True(await cached.HasPermissionAsync(Permission, company));
            Assert.Equal(1, reads.Count);

            // Another request revokes the grant while this one is still open.
            await RevokeAsync(factory, actor);

            // The request's view is unchanged, and no read was paid for it.
            Assert.True(await cached.HasPermissionAsync(Permission, company));
            Assert.Equal(1, reads.Count);

            var revalidated = await fresh.RevalidatePermissionScopeAsync(Permission, company);

            Assert.False(revalidated.IsGranted);
            // Exactly one more read: the price of the guarantee, per call.
            Assert.Equal(2, reads.Count);
            // The revoked grant cannot come back on either port for the rest of the request.
            Assert.False(await cached.HasPermissionAsync(Permission, company));
            Assert.Equal(2, reads.Count);

            // A new request answers no on its own.
            await using var nextRequest = factory.Services.CreateAsyncScope();
            Assert.False(await nextRequest.ServiceProvider
                .GetRequiredService<IErpAuthorizationService>()
                .HasPermissionAsync(Permission, company));
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [SqlServerFact]
    public async Task A_standing_grant_is_answered_the_same_by_both_ports_and_an_unknown_name_reads_nothing()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var actor = Guid.NewGuid();
        var company = TestCompanyIds.CompanyA;
        var reads = new CountingPermissionScopeReadObserver();
        var factory = new SqlServerRelationalApiFactory(
            baseConnectionString,
            services =>
            {
                services.RemoveAll<ICurrentUser>();
                services.AddSingleton<ICurrentUser>(new FixedCurrentUser(actor, company));
                services.RemoveAll<IPermissionScopeReadObserver>();
                services.AddSingleton<IPermissionScopeReadObserver>(reads);
            });

        try
        {
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();

            await GrantAsync(factory, actor, company);

            await using var request = factory.Services.CreateAsyncScope();
            var cached = request.ServiceProvider.GetRequiredService<IErpAuthorizationService>();
            var fresh = request.ServiceProvider.GetRequiredService<IErpAuthorityRevalidationService>();

            var cachedScope = await cached.GetPermissionScopeAsync(Permission, company);
            var freshScope = await fresh.RevalidatePermissionScopeAsync(Permission, company);

            Assert.True(cachedScope.IsGranted);
            Assert.True(cachedScope.IsCompanyWide);
            Assert.Equal(cachedScope.RoleCodes, freshScope.RoleCodes);
            Assert.Equal(cachedScope.IsGranted, freshScope.IsGranted);
            Assert.Equal(2, reads.Count);

            // A name the platform does not know is denied without touching the database, on the
            // fresh path exactly as on the cached one.
            Assert.Equal(PermissionScope.Denied,
                await fresh.RevalidatePermissionScopeAsync("product.operational-selection.read", company));
            Assert.Equal(PermissionScope.Denied,
                await fresh.RevalidatePermissionScopeAsync(Permission, Guid.Empty));
            Assert.Equal(2, reads.Count);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    private static async Task GrantAsync(SqlServerRelationalApiFactory factory, Guid actor, Guid company)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var roleId = Guid.NewGuid();
        var role = new RoleDefinition(roleId, $"REVALIDATION-{roleId:N}", "Fresh authority test role", true);
        role.AddPermission(Permission);
        // No approver: an arbitrary id would break the FK to identity.Users, and the approval
        // trail is not what this test measures.
        var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company,
            DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
        assignment.AddScope(AccessScopeType.Company, company);
        dbContext.Users.Add(new UserAccount(actor, "Fresh authority test actor", null));
        dbContext.Roles.Add(role);
        dbContext.RoleAssignments.Add(assignment);
        await dbContext.SaveChangesAsync();
    }

    private static async Task RevokeAsync(SqlServerRelationalApiFactory factory, Guid actor)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var assignment = await dbContext.RoleAssignments
            .Include(x => x.Scopes)
            .SingleAsync(x => x.UserId == actor);
        dbContext.RoleAssignments.Remove(assignment);
        await dbContext.SaveChangesAsync();
    }

    private sealed class CountingPermissionScopeReadObserver : IPermissionScopeReadObserver
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void ObserveDatabaseRead(string permission, Guid companyId) => Interlocked.Increment(ref _count);
    }

    private sealed class FixedCurrentUser(Guid userId, params Guid[] companyIds) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = companyIds.ToHashSet();
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
