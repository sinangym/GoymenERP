using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Identity.UnitTests;

/// <summary>
/// The fresh-authority path (Business #66 5720113915). One instance stands for one request: the
/// cached port and the revalidation port are the same object, as the DI registration binds them.
/// The permission used here is an already registered one — the path is permission-agnostic and
/// registering a new name is a separate step.
/// </summary>
public sealed class ErpAuthorityRevalidationServiceTests
{
    private const string Permission = ErpPermissions.OrganizationLocationsRead;

    [Fact]
    public async Task A_revocation_during_the_request_is_measured_and_the_request_view_moves_with_it()
    {
        var actor = Guid.NewGuid();
        var company = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        var assignment = Grant(dbContext, actor, company, Permission);
        await dbContext.SaveChangesAsync();
        var reads = new CountingObserver();
        var service = Service(dbContext, actor, company, reads);

        Assert.True(await service.HasPermissionAsync(Permission, company));
        Assert.Equal(1, reads.Count);

        // Another request revokes the grant while this one is still running.
        dbContext.RoleAssignments.Remove(assignment);
        await dbContext.SaveChangesAsync();

        // The cached answer is the request's view and still says yes: that is the documented
        // behaviour the fresh path exists to work around, not a bug being asserted away.
        Assert.True(await service.HasPermissionAsync(Permission, company));
        Assert.Equal(1, reads.Count);

        var revalidated = await service.RevalidatePermissionScopeAsync(Permission, company);

        Assert.False(revalidated.IsGranted);
        Assert.Equal(2, reads.Count);
        // The refreshed answer replaced the cached one: the grant cannot come back for the rest
        // of this request, on either port.
        Assert.False(await service.HasPermissionAsync(Permission, company));
        Assert.Equal(2, reads.Count);
    }

    [Fact]
    public async Task A_grant_that_stands_is_answered_the_same_way_by_both_ports()
    {
        var actor = Guid.NewGuid();
        var company = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        Grant(dbContext, actor, company, Permission);
        await dbContext.SaveChangesAsync();
        var reads = new CountingObserver();
        var service = Service(dbContext, actor, company, reads);

        var cached = await service.GetPermissionScopeAsync(Permission, company);
        var revalidated = await service.RevalidatePermissionScopeAsync(Permission, company);

        Assert.True(cached.IsGranted);
        // Field by field: PermissionScope's set members compare by reference, so two equal
        // answers are not the same record instance.
        Assert.Equal(
            (cached.IsGranted, cached.IsCompanyWide),
            (revalidated.IsGranted, revalidated.IsCompanyWide));
        Assert.Equal(cached.RoleCodes.Order(StringComparer.Ordinal), revalidated.RoleCodes.Order(StringComparer.Ordinal));
        Assert.Equal(cached.SiteIds.Order(), revalidated.SiteIds.Order());
        Assert.Equal(cached.LocationIds.Order(), revalidated.LocationIds.Order());
        Assert.Equal(cached.WorkCenterIds.Order(), revalidated.WorkCenterIds.Order());
        // One read for the first question, one for the refusal to reuse it.
        Assert.Equal(2, reads.Count);
    }

    [Fact]
    public async Task A_grant_added_during_the_request_is_also_measured_rather_than_remembered()
    {
        var actor = Guid.NewGuid();
        var company = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(new UserAccount(actor, "Fresh authority test actor", null));
        await dbContext.SaveChangesAsync();
        var service = Service(dbContext, actor, company);

        Assert.False(await service.HasPermissionAsync(Permission, company));

        Grant(dbContext, actor, company, Permission, addUser: false);
        await dbContext.SaveChangesAsync();

        Assert.True((await service.RevalidatePermissionScopeAsync(Permission, company)).IsGranted);
        // Freshness is not one-way: the refreshed answer is what the rest of the request sees.
        Assert.True(await service.HasPermissionAsync(Permission, company));
    }

    [Fact]
    public async Task The_next_request_answers_no_without_being_told()
    {
        var actor = Guid.NewGuid();
        var company = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        var assignment = Grant(dbContext, actor, company, Permission);
        await dbContext.SaveChangesAsync();
        Assert.True(await Service(dbContext, actor, company).HasPermissionAsync(Permission, company));

        dbContext.RoleAssignments.Remove(assignment);
        await dbContext.SaveChangesAsync();

        // A new instance is a new request: no cache to carry the revoked grant over.
        Assert.False(await Service(dbContext, actor, company).HasPermissionAsync(Permission, company));
        Assert.False((await Service(dbContext, actor, company)
            .RevalidatePermissionScopeAsync(Permission, company)).IsGranted);
    }

    [Theory]
    [InlineData("unknown name")]
    [InlineData("empty company")]
    [InlineData("no actor identifier")]
    public async Task The_fresh_path_fails_closed_for_the_same_inputs_as_the_cached_one_and_reads_nothing(
        string fault)
    {
        var actor = Guid.NewGuid();
        var company = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        // A row that would grant it, to prove the refusal is not "no rows found".
        Grant(dbContext, actor, company, fault == "unknown name" ? "product.not.registered.here" : Permission);
        await dbContext.SaveChangesAsync();
        var reads = new CountingObserver();
        var service = Service(dbContext, fault == "no actor identifier" ? null : actor, company, reads);
        var (permission, askedCompany) = fault switch
        {
            "unknown name" => ("product.not.registered.here", company),
            "empty company" => (Permission, Guid.Empty),
            _ => (Permission, company),
        };

        var revalidated = await service.RevalidatePermissionScopeAsync(permission, askedCompany);

        Assert.Equal(PermissionScope.Denied, revalidated);
        Assert.Equal(0, reads.Count);
    }

    [Fact]
    public async Task A_company_wide_only_permission_keeps_that_rule_on_the_fresh_path()
    {
        var actor = Guid.NewGuid();
        var company = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        // Site scope only: the permission requires company-wide scope, so it stays denied.
        Grant(dbContext, actor, company, ErpPermissions.IdentityAuditRead,
            scopeType: AccessScopeType.Site, scopeId: Guid.NewGuid());
        await dbContext.SaveChangesAsync();
        var service = Service(dbContext, actor, company);

        Assert.Equal(PermissionScope.Denied,
            await service.RevalidatePermissionScopeAsync(ErpPermissions.IdentityAuditRead, company));
    }

    [Fact]
    public async Task A_canceled_caller_gets_no_measurement()
    {
        var actor = Guid.NewGuid();
        var company = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        Grant(dbContext, actor, company, Permission);
        await dbContext.SaveChangesAsync();
        var service = Service(dbContext, actor, company);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.RevalidatePermissionScopeAsync(Permission, company, new CancellationToken(true)));
    }

    [Fact]
    public async Task The_capability_snapshot_stays_the_request_image_it_is_contracted_to_be()
    {
        var actor = Guid.NewGuid();
        var company = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        var assignment = Grant(dbContext, actor, company, Permission);
        await dbContext.SaveChangesAsync();
        var service = Service(dbContext, actor, company);
        var before = await service.GetAuthorizationSnapshotAsync(company, [Permission]);

        dbContext.RoleAssignments.Remove(assignment);
        await dbContext.SaveChangesAsync();
        Assert.False((await service.RevalidatePermissionScopeAsync(Permission, company)).IsGranted);

        // Refreshing one permission answer does not rewrite the snapshot rows: the capability
        // image is per request by contract and authorizes nothing on its own.
        var after = await service.GetAuthorizationSnapshotAsync(company, [Permission]);
        Assert.Equal(before.Revision, after.Revision);
        Assert.True(after.PermissionScopes[Permission].IsGranted);
    }

    private static ErpAuthorizationService Service(
        IdentityDbContext dbContext, Guid? actor, Guid company, IPermissionScopeReadObserver? reads = null) =>
        new(dbContext, new ProbeUser(actor, company), null, reads);

    private static RoleAssignment Grant(
        IdentityDbContext dbContext,
        Guid actor,
        Guid company,
        string permission,
        AccessScopeType scopeType = AccessScopeType.Company,
        Guid? scopeId = null,
        bool addUser = true)
    {
        var roleId = Guid.NewGuid();
        var role = new RoleDefinition(roleId, $"ROLE-{roleId:N}", "Fresh authority test role", true);
        role.AddPermission(permission);
        var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company,
            DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
        assignment.AddScope(scopeType, scopeId ?? company);
        if (addUser)
        {
            dbContext.Users.Add(new UserAccount(actor, "Fresh authority test actor", null));
        }

        dbContext.Roles.Add(role);
        dbContext.RoleAssignments.Add(assignment);
        return assignment;
    }

    private static IdentityDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase($"identity-revalidation-{Guid.NewGuid():N}").Options);

    private sealed class CountingObserver : IPermissionScopeReadObserver
    {
        public int Count { get; private set; }

        public void ObserveDatabaseRead(string permission, Guid companyId) => Count++;
    }

    private sealed class ProbeUser(Guid? userId, params Guid[] companyIds) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId?.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = companyIds.ToHashSet();
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
