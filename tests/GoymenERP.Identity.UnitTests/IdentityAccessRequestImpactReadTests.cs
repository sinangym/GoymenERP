using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Infrastructure;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Identity.UnitTests;

/// <summary>Controlled native read/authority boundary, not SQL or an assignment/SoD acceptance test.</summary>
public sealed class IdentityAccessRequestImpactReadTests
{
    private static readonly Guid Company = Guid.Parse("c3100000-0000-4000-8000-000000000001");
    private static readonly Guid Target = Guid.Parse("a3100000-0000-4000-8000-000000000002");
    private static readonly Guid Role = Guid.Parse("b3100000-0000-4000-8000-000000000003");
    private static readonly Guid Actor = Guid.Parse("a3100000-0000-4000-8000-000000000004");
    private static readonly IdentityRoleRequestImpactQueryV1 Query = new(Target, Role);
    private static IdentityAccessPermission Permission(string code) => new(code, ErpPermissions.IsCritical(code));

    [Fact]
    public async Task Code_delta_uses_native_effective_permissions_and_role_critical_facts_without_claiming_scope_or_SoD_safety()
    {
        var rig = new Rig();
        var answer = await rig.Service.ReadAsync(Query);
        Assert.Equal(IdentityAccessRequestImpactOutcomeV1.Available, answer.Outcome);
        var impact = Assert.IsType<IdentityRoleRequestImpactV1>(answer.Impact);
        Assert.Equal((Company, Target, Role), (impact.CompanyId, impact.UserId, impact.RoleId));
        Assert.Equal([Permission(ErpPermissions.SalesDraftsRead)], impact.CurrentPermissionCodes);
        Assert.Equal([IdentityPermissionManifestV1.AssignmentsRead], impact.AddedPermissionCodes);
        Assert.Equal([ErpPermissions.SalesDraftsRead], impact.AlreadyHeldCodes);
        Assert.True(impact.RoleHasCriticalPermissions);
        Assert.Null(impact.HasSegregationOfDutiesConflict);
        Assert.Null(impact.ExpandsScope);
        Assert.Equal(2, rig.Reads.UserCalls);
        Assert.Equal(2, rig.Reads.RoleCalls);
        Assert.All(rig.Authority.Calls, permission => Assert.Equal(IdentityPermissionManifestV1.AssignmentsRead, permission));
        var json = JsonSerializer.Serialize(impact);
        Assert.DoesNotContain("SENTETIK person", json);
        Assert.DoesNotContain("SENTETIK role", json);
        // There is no assignment/admin/Settings dependency to call: registration is only this read port.
        var services = new ServiceCollection().AddIdentityAccessRequestImpactV1();
        Assert.Single(services);
        Assert.Equal(typeof(IIdentityAccessRequestImpactReadV1), services[0].ServiceType);
    }

    [Theory]
    [InlineData("unauthenticated")]
    [InlineData("nonmember")]
    [InlineData("unresolved")]
    [InlineData("denied")]
    [InlineData("narrow")]
    public async Task Missing_actor_company_membership_or_company_wide_authority_never_calls_native_reads(string fault)
    {
        var rig = new Rig();
        if (fault == "unauthenticated") rig.User.Authenticated = false;
        if (fault == "nonmember") rig.User.Member = false;
        if (fault == "unresolved") rig.Company.Set(Guid.Empty);
        if (fault == "denied") rig.Authority.DenyAt = 1;
        if (fault == "narrow") rig.Authority.CompanyWide = false;
        var answer = await rig.Service.ReadAsync(Query);
        Assert.Equal(IdentityAccessRequestImpactOutcomeV1.Denied, answer.Outcome);
        Assert.Null(answer.Impact);
        Assert.Equal(0, rig.Reads.UserCalls + rig.Reads.RoleCalls);
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("company")]
    [InlineData("membership")]
    [InlineData("authority")]
    public async Task Revocation_after_native_read_never_releases_the_earlier_facts(string fault)
    {
        var rig = new Rig();
        rig.Reads.AfterUserRead = () =>
        {
            if (fault == "actor") rig.User.ActorId = Guid.NewGuid().ToString("D");
            if (fault == "company") rig.Company.Set(Guid.NewGuid());
            if (fault == "membership") rig.User.Member = false;
            if (fault == "authority") rig.Authority.DenyAt = rig.Authority.Calls.Count + 1;
        };
        var answer = await rig.Service.ReadAsync(Query);
        Assert.Equal(IdentityAccessRequestImpactOutcomeV1.Denied, answer.Outcome);
        Assert.Null(answer.Impact);
        Assert.Equal(1, rig.Reads.UserCalls);
        Assert.Equal(0, rig.Reads.RoleCalls);
    }

    [Theory]
    [InlineData("user")]
    [InlineData("role")]
    public async Task Native_unknown_or_foreign_company_null_keeps_the_same_not_found_boundary(string missing)
    {
        var rig = new Rig();
        if (missing == "user") rig.Reads.User = null;
        else rig.Reads.Role = null;
        var answer = await rig.Service.ReadAsync(Query);
        Assert.Equal(IdentityAccessRequestImpactOutcomeV1.NotFound, answer.Outcome);
        Assert.Null(answer.Impact);
    }

    [Theory]
    [InlineData("user-id")]
    [InlineData("role-id")]
    [InlineData("inactive")]
    [InlineData("inactive-user")]
    [InlineData("critical")]
    [InlineData("unknown-code")]
    public async Task Malformed_or_ineligible_native_facts_are_unavailable_instead_of_a_partial_or_safe_impact(string fault)
    {
        var rig = new Rig();
        if (fault == "user-id") rig.Reads.User = rig.Reads.User! with { UserId = Guid.NewGuid() };
        if (fault == "role-id") rig.Reads.Role = rig.Reads.Role! with { RoleId = Guid.NewGuid() };
        if (fault == "inactive") rig.Reads.Role = rig.Reads.Role! with { IsActive = false };
        if (fault == "inactive-user") rig.Reads.User = rig.Reads.User! with { IsActive = false };
        if (fault == "critical") rig.Reads.Role = rig.Reads.Role! with { Permissions = [new(IdentityPermissionManifestV1.AssignmentsRead, false)] };
        if (fault == "unknown-code") rig.Reads.Role = rig.Reads.Role! with { Permissions = [new("SENTETIK.unknown", false)] };
        var answer = await rig.Service.ReadAsync(Query);
        Assert.Equal(IdentityAccessRequestImpactOutcomeV1.Unavailable, answer.Outcome);
        Assert.Null(answer.Impact);
    }

    [Fact]
    public async Task Native_permission_drift_between_reads_is_unavailable_and_not_replaced_with_a_fresh_grant_preview()
    {
        var rig = new Rig();
        rig.Reads.AfterRoleRead = () => rig.Reads.Role = rig.Reads.Role! with { Permissions = [Permission(ErpPermissions.SalesDraftsRead)] };
        var answer = await rig.Service.ReadAsync(Query);
        Assert.Equal(IdentityAccessRequestImpactOutcomeV1.Unavailable, answer.Outcome);
        Assert.Null(answer.Impact);
        Assert.Equal(2, rig.Reads.UserCalls);
        Assert.Equal(2, rig.Reads.RoleCalls);
    }

    [Fact]
    public async Task Native_reader_failure_is_unavailable_and_pre_cancelled_read_does_no_work()
    {
        var rig = new Rig();
        rig.Reads.Throw = true;
        Assert.Equal(IdentityAccessRequestImpactOutcomeV1.Unavailable, (await rig.Service.ReadAsync(Query)).Outcome);
        var cancelled = new Rig();
        await Assert.ThrowsAsync<OperationCanceledException>(() => cancelled.Service.ReadAsync(Query, new CancellationToken(true)));
        Assert.Empty(cancelled.Authority.Calls);
        Assert.Equal(0, cancelled.Reads.UserCalls);
    }

    private sealed class Rig
    {
        public MutableCompany Company { get; } = new(IdentityAccessRequestImpactReadTests.Company);
        public MutableUser User { get; } = new();
        public Authority Authority { get; } = new();
        public Reads Reads { get; } = new();
        public IIdentityAccessRequestImpactReadV1 Service => new IdentityAccessRequestImpactReadV1(Reads, Company, User, Authority);
    }

    private sealed class Reads : IIdentityAccessReadService
    {
        public IdentityAccessUserDetail? User { get; set; } = new(Target, "SENTETIK person", true, [],
            [new(ErpPermissions.SalesDraftsRead, ErpPermissions.IsCritical(ErpPermissions.SalesDraftsRead), ["SENTETIK_ROLE"])]);
        public IdentityAccessRoleDetail? Role { get; set; } = new(IdentityAccessRequestImpactReadTests.Role, "SENTETIK_ROLE", "SENTETIK role", false, true,
            [Permission(ErpPermissions.SalesDraftsRead), Permission(IdentityPermissionManifestV1.AssignmentsRead)], []);
        public int UserCalls { get; private set; }
        public int RoleCalls { get; private set; }
        public Action? AfterUserRead { get; set; }
        public Action? AfterRoleRead { get; set; }
        public bool Throw { get; set; }
        public Task<IdentityAccessUserDetail?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            Assert.Equal(Target, userId); UserCalls++;
            if (Throw) throw new InvalidOperationException("SENTETIK owner failure");
            var value = User; AfterUserRead?.Invoke(); return Task.FromResult(value);
        }
        public Task<IdentityAccessRoleDetail?> GetRoleAsync(Guid roleId, CancellationToken cancellationToken = default)
        {
            Assert.Equal(IdentityAccessRequestImpactReadTests.Role, roleId); RoleCalls++;
            var value = Role; AfterRoleRead?.Invoke(); return Task.FromResult(value);
        }
        public Task<IReadOnlyList<IdentityAccessUserSummary>> GetUsersAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException("No people scan.");
        public Task<IReadOnlyList<IdentityAccessRoleSummary>> GetRolesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException("No catalogue scan.");
    }

    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public List<string> Calls { get; } = [];
        public int DenyAt { get; set; } = int.MaxValue;
        public bool CompanyWide { get; set; } = true;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            Calls.Add(permission);
            return Task.FromResult(companyId == Company && Calls.Count < DenyAt
                ? new PermissionScope(true, CompanyWide, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied);
        }
    }

    private sealed class MutableCompany(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; private set; } = companyId;
        public void Set(Guid value) => CompanyId = value;
    }

    private sealed class MutableUser : ICurrentUser
    {
        public bool Authenticated { get; set; } = true;
        public bool Member { get; set; } = true;
        public string ActorId { get; set; } = Actor.ToString("D");
        public bool IsAuthenticated => Authenticated;
        public string? UserId => ActorId;
        public IReadOnlySet<Guid> AllowedCompanyIds => Member ? new HashSet<Guid>([Company]) : new HashSet<Guid>();
        public bool CanAccessCompany(Guid companyId) => Member && companyId == Company;
    }
}
