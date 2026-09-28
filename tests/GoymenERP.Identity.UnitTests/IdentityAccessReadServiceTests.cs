using System.Reflection;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using GoymenERP.Modules.Identity.Infrastructure.Shell;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Identity.UnitTests;

/// <summary>
/// AT-004 read-only access view, on the in-memory provider. The authorization port is a probe here so the
/// company-wide gate can be exercised before the central catalogue registers the name; the real port and the real
/// SQL fence are measured by the SqlServerFact suite.
/// </summary>
public sealed class IdentityAccessReadServiceTests
{
    private static readonly Guid CompanyA = Guid.NewGuid();
    private static readonly Guid CompanyB = Guid.NewGuid();

    [Fact]
    public async Task Without_the_company_wide_read_every_answer_is_nothing_at_all()
    {
        await using var db = CreateDbContext();
        var seeded = Seed(db);
        await db.SaveChangesAsync();

        foreach (var scope in new[] { PermissionScope.Denied, SiteScoped() })
        {
            var service = Service(db, CompanyA, scope);
            Assert.Empty(await service.GetUsersAsync());
            Assert.Null(await service.GetUserAsync(seeded.Alice));
            Assert.Empty(await service.GetRolesAsync());
            Assert.Null(await service.GetRoleAsync(seeded.Operator));
        }
    }

    [Fact]
    public async Task The_gate_asks_for_the_manifest_name_in_the_context_company_only()
    {
        await using var db = CreateDbContext();
        Seed(db);
        await db.SaveChangesAsync();
        var probe = new ProbeAuthorization(CompanyWide());

        await new IdentityAccessReadService(db, Context(CompanyA), probe).GetUsersAsync();

        Assert.Equal([(IdentityPermissionManifestV1.AssignmentsRead, CompanyA)], probe.Asked);
        Assert.Equal("identity.assignments.read", IdentityPermissionManifestV1.AssignmentsRead);
    }

    [Fact]
    public async Task An_unresolved_company_context_reads_nothing_and_asks_nothing()
    {
        await using var db = CreateDbContext();
        Seed(db);
        await db.SaveChangesAsync();
        var probe = new ProbeAuthorization(CompanyWide());

        Assert.Empty(await new IdentityAccessReadService(db, new CompanyContext(), probe).GetUsersAsync());
        Assert.Empty(probe.Asked);
    }

    [Fact]
    public async Task Users_are_fenced_to_the_context_company()
    {
        await using var db = CreateDbContext();
        var seeded = Seed(db);
        await db.SaveChangesAsync();

        var inA = await Service(db, CompanyA).GetUsersAsync();
        Assert.Equal(
            [("Alice", 3, 1), ("Bora", 1, 1)],
            inA.Select(user => (user.DisplayName, user.AssignmentCount, user.EffectiveAssignmentCount)));
        Assert.DoesNotContain(inA, user => user.UserId == seeded.OnlyInB);

        // Company B sees its own holder and Alice's B assignment only, never her A assignments.
        var inB = await Service(db, CompanyB).GetUsersAsync();
        Assert.Equal([("Alice", 1), ("Cem", 1)], inB.Select(user => (user.DisplayName, user.AssignmentCount)));

        // A user with no assignment in the context company is the same null as an unknown id.
        Assert.Null(await Service(db, CompanyA).GetUserAsync(seeded.OnlyInB));
        Assert.Null(await Service(db, CompanyA).GetUserAsync(Guid.NewGuid()));
        Assert.Null(await Service(db, CompanyA).GetUserAsync(Guid.Empty));
    }

    [Fact]
    public async Task A_user_shows_each_assignment_with_validity_scopes_and_approver()
    {
        await using var db = CreateDbContext();
        var seeded = Seed(db);
        await db.SaveChangesAsync();

        var alice = Assert.IsType<IdentityAccessUserDetail>(await Service(db, CompanyA).GetUserAsync(seeded.Alice));

        Assert.Equal("Alice", alice.DisplayName);
        Assert.True(alice.IsActive);
        Assert.All(alice.Assignments, assignment => Assert.DoesNotContain(
            assignment.Scopes, scope => scope.Id == CompanyB));
        var byRole = alice.Assignments.ToDictionary(assignment => assignment.RoleCode);
        Assert.Equal(["AUDITOR", "OPERATOR", "RETIRED"], byRole.Keys.Order(StringComparer.Ordinal));

        var operatorAssignment = byRole["OPERATOR"];
        Assert.Equal(IdentityAccessValidity.Effective, operatorAssignment.Validity);
        Assert.Equal("Active", operatorAssignment.Status);
        Assert.Equal(seeded.Operator, operatorAssignment.RoleId);
        Assert.Equal(new IdentityAccessPerson(seeded.Approver, "Onaylayan Yönetici"), operatorAssignment.ApprovedBy);
        Assert.Equal([new IdentityAccessScope("Company", CompanyA), new IdentityAccessScope("Site", seeded.Site)],
            operatorAssignment.Scopes);

        Assert.Equal(IdentityAccessValidity.Scheduled, byRole["AUDITOR"].Validity);
        Assert.Null(byRole["AUDITOR"].ApprovedBy);
        Assert.Equal(IdentityAccessValidity.Ended, byRole["RETIRED"].Validity);
        Assert.NotNull(byRole["RETIRED"].ValidUntilUtc);
    }

    [Fact]
    public async Task Effective_permissions_come_only_from_effective_assignments_and_carry_the_critical_flag()
    {
        await using var db = CreateDbContext();
        var seeded = Seed(db);
        await db.SaveChangesAsync();

        var alice = Assert.IsType<IdentityAccessUserDetail>(await Service(db, CompanyA).GetUserAsync(seeded.Alice));

        // OPERATOR is effective; AUDITOR (scheduled, holds identity.audit.read) and RETIRED (ended) give nothing now.
        Assert.Equal(
            [
                new IdentityAccessEffectivePermission(ErpPermissions.OrganizationLocationsRead, false, ["OPERATOR"]),
                new IdentityAccessEffectivePermission(ErpPermissions.OrganizationLocationsStatusUpdate, false, ["OPERATOR"]),
            ],
            alice.EffectivePermissions,
            new EffectivePermissionComparer());
        Assert.True(ErpPermissions.IsCritical(ErpPermissions.IdentityAuditRead));
    }

    [Fact]
    public async Task Revoked_expired_passive_role_passive_user_and_scopeless_assignments_are_not_effective()
    {
        await using var db = CreateDbContext();
        var user = Guid.NewGuid();
        db.Users.Add(new UserAccount(user, "Deniz", "deniz@example.invalid"));
        var revoked = Assign(db, user, "REVOKED_ROLE", CompanyA, scoped: true);
        var expired = Assign(db, user, "EXPIRED_ROLE", CompanyA, scoped: true);
        var passiveRole = Assign(db, user, "PASSIVE_ROLE", CompanyA, scoped: true);
        Assign(db, user, "SCOPELESS_ROLE", CompanyA, scoped: false);
        db.Entry(revoked).Property(nameof(RoleAssignment.Status)).CurrentValue = RoleAssignmentStatus.Revoked;
        db.Entry(expired).Property(nameof(RoleAssignment.Status)).CurrentValue = RoleAssignmentStatus.Expired;
        db.Entry(db.Roles.Local.Single(role => role.Id == passiveRole.RoleId))
            .Property(nameof(RoleDefinition.IsActive)).CurrentValue = false;
        await db.SaveChangesAsync();

        var detail = Assert.IsType<IdentityAccessUserDetail>(await Service(db, CompanyA).GetUserAsync(user));
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["EXPIRED_ROLE"] = IdentityAccessValidity.Ended,
                ["PASSIVE_ROLE"] = IdentityAccessValidity.Inactive,
                ["REVOKED_ROLE"] = IdentityAccessValidity.Revoked,
                ["SCOPELESS_ROLE"] = IdentityAccessValidity.Inactive,
            },
            detail.Assignments.ToDictionary(assignment => assignment.RoleCode, assignment => assignment.Validity));
        Assert.Empty(detail.EffectivePermissions);
        Assert.False(detail.Assignments.Single(assignment => assignment.RoleCode == "PASSIVE_ROLE").RoleIsActive);

        // A passive user: an otherwise effective assignment does not count.
        var passiveUser = Guid.NewGuid();
        db.Users.Add(new UserAccount(passiveUser, "Ece", null));
        Assign(db, passiveUser, "ECE_ROLE", CompanyA, scoped: true);
        await db.SaveChangesAsync();
        db.Entry(db.Users.Single(candidate => candidate.Id == passiveUser))
            .Property(nameof(UserAccount.IsActive)).CurrentValue = false;
        await db.SaveChangesAsync();
        var ece = Assert.IsType<IdentityAccessUserDetail>(await Service(db, CompanyA).GetUserAsync(passiveUser));
        Assert.False(ece.IsActive);
        Assert.Equal(IdentityAccessValidity.Inactive, Assert.Single(ece.Assignments).Validity);
    }

    [Fact]
    public async Task A_role_shows_its_permissions_and_only_the_context_company_holders()
    {
        await using var db = CreateDbContext();
        var seeded = Seed(db);
        await db.SaveChangesAsync();

        var inA = Assert.IsType<IdentityAccessRoleDetail>(await Service(db, CompanyA).GetRoleAsync(seeded.Operator));
        Assert.Equal("OPERATOR", inA.Code);
        Assert.Equal(
            [ErpPermissions.OrganizationLocationsRead, ErpPermissions.OrganizationLocationsStatusUpdate],
            inA.Permissions.Select(permission => permission.Code));
        Assert.Equal(["Alice", "Bora"], inA.Holders.Select(holder => holder.DisplayName));
        Assert.All(inA.Holders, holder => Assert.Equal(IdentityAccessValidity.Effective, holder.Validity));

        // Same role, company B: only B's holder; A's holders are not disclosed.
        var inB = Assert.IsType<IdentityAccessRoleDetail>(await Service(db, CompanyB).GetRoleAsync(seeded.Operator));
        Assert.Equal(["Cem"], inB.Holders.Select(holder => holder.DisplayName));

        // Critical flag follows the catalogue.
        var auditor = Assert.IsType<IdentityAccessRoleDetail>(await Service(db, CompanyA).GetRoleAsync(seeded.Auditor));
        Assert.True(Assert.Single(auditor.Permissions).IsCritical);

        Assert.Null(await Service(db, CompanyA).GetRoleAsync(Guid.NewGuid()));
        Assert.Null(await Service(db, CompanyA).GetRoleAsync(Guid.Empty));
    }

    [Fact]
    public async Task The_role_list_counts_holders_in_the_context_company_only()
    {
        await using var db = CreateDbContext();
        var seeded = Seed(db);
        await db.SaveChangesAsync();

        var inA = (await Service(db, CompanyA).GetRolesAsync()).ToDictionary(role => role.Code);
        Assert.Equal((2, 2, 2), (inA["OPERATOR"].PermissionCount, inA["OPERATOR"].HolderCount,
            inA["OPERATOR"].EffectiveHolderCount));
        Assert.Equal((1, 1, 0), (inA["AUDITOR"].PermissionCount, inA["AUDITOR"].HolderCount,
            inA["AUDITOR"].EffectiveHolderCount));
        Assert.Equal((0, 0), (inA["UNUSED"].HolderCount, inA["UNUSED"].EffectiveHolderCount));

        var inB = (await Service(db, CompanyB).GetRolesAsync()).ToDictionary(role => role.Code);
        Assert.Equal((1, 1), (inB["OPERATOR"].HolderCount, inB["OPERATOR"].EffectiveHolderCount));
        Assert.Equal(seeded.Operator, inB["OPERATOR"].RoleId);
    }

    [Fact]
    public void No_contract_record_carries_an_email_or_an_external_identity()
    {
        var records = typeof(IIdentityAccessReadService).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(IIdentityAccessReadService).Namespace
                && type.Name.StartsWith("IdentityAccess", StringComparison.Ordinal)
                && type is { IsClass: true, IsAbstract: false });
        var properties = records
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Select(property => property.Name)
            .ToArray();
        Assert.NotEmpty(properties);
        Assert.DoesNotContain(properties, name =>
            name.Contains("Email", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Subject", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Issuer", StringComparison.OrdinalIgnoreCase)
            || name.Contains("External", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_shell_provider_declares_two_read_screens_on_the_one_read_name_and_no_control()
    {
        var declarations = new IdentityShellCapabilityProvider().GetDeclarations();

        Assert.Equal(
            [
                ("identity.assignments.list", "/ayarlar/kullanicilar-ve-yetkiler", "SY-01"),
                ("identity.roles.detail", "/ayarlar/kullanicilar-ve-yetkiler/roller/:roleId", "SY-02"),
            ],
            declarations.Select(declaration =>
                (declaration.CapabilityId, declaration.RoutePath, declaration.ScreenId)));
        Assert.All(declarations, declaration =>
        {
            Assert.Equal([IdentityPermissionManifestV1.AssignmentsRead], declaration.RequiredPermissions);
            Assert.Null(declaration.ParentCapabilityId);
            Assert.True(declaration.ImplementationAvailable);
            Assert.Equal(GoymenERP.BuildingBlocks.Shell.ShellCapabilityScopeRequirement.CompanyWide,
                declaration.ScopeRequirement);
            Assert.Equal(GoymenERP.BuildingBlocks.Shell.ShellCapabilityContextRequirement.SingleCompany,
                declaration.ContextRequirement);
        });
    }

    private static Seeded Seed(IdentityDbContext db)
    {
        var approver = Guid.NewGuid();
        var alice = Guid.NewGuid();
        var bora = Guid.NewGuid();
        var cem = Guid.NewGuid();
        var site = Guid.NewGuid();
        db.Users.Add(new UserAccount(approver, "Onaylayan Yönetici", "approver@example.invalid"));
        db.Users.Add(new UserAccount(alice, "Alice", "alice@example.invalid"));
        db.Users.Add(new UserAccount(bora, "Bora", null));
        db.Users.Add(new UserAccount(cem, "Cem", null));

        var operatorRole = new RoleDefinition(Guid.NewGuid(), "OPERATOR", "Operatör", isSystem: true);
        operatorRole.AddPermission(ErpPermissions.OrganizationLocationsStatusUpdate);
        operatorRole.AddPermission(ErpPermissions.OrganizationLocationsRead);
        var auditorRole = new RoleDefinition(Guid.NewGuid(), "AUDITOR", "Denetçi", isSystem: true);
        auditorRole.AddPermission(ErpPermissions.IdentityAuditRead);
        var retiredRole = new RoleDefinition(Guid.NewGuid(), "RETIRED", "Eski", isSystem: false);
        retiredRole.AddPermission(ErpPermissions.OrganizationSitesRead);
        var unusedRole = new RoleDefinition(Guid.NewGuid(), "UNUSED", "Kullanılmayan", isSystem: false);
        db.Roles.AddRange(operatorRole, auditorRole, retiredRole, unusedRole);

        var now = DateTimeOffset.UtcNow;
        var aliceOperator = new RoleAssignment(Guid.NewGuid(), alice, operatorRole.Id, CompanyA,
            now.AddDays(-10), null, approver);
        aliceOperator.AddScope(AccessScopeType.Company, CompanyA);
        aliceOperator.AddScope(AccessScopeType.Site, site);
        var aliceAuditor = new RoleAssignment(Guid.NewGuid(), alice, auditorRole.Id, CompanyA,
            now.AddDays(3), null, null);
        aliceAuditor.AddScope(AccessScopeType.Company, CompanyA);
        var aliceRetired = new RoleAssignment(Guid.NewGuid(), alice, retiredRole.Id, CompanyA,
            now.AddDays(-30), now.AddDays(-1), approver);
        aliceRetired.AddScope(AccessScopeType.Company, CompanyA);
        var aliceInB = new RoleAssignment(Guid.NewGuid(), alice, retiredRole.Id, CompanyB,
            now.AddDays(-5), null, approver);
        aliceInB.AddScope(AccessScopeType.Company, CompanyB);
        var boraOperator = new RoleAssignment(Guid.NewGuid(), bora, operatorRole.Id, CompanyA,
            now.AddDays(-5), null, approver);
        boraOperator.AddScope(AccessScopeType.Company, CompanyA);
        var cemOperator = new RoleAssignment(Guid.NewGuid(), cem, operatorRole.Id, CompanyB,
            now.AddDays(-5), null, approver);
        cemOperator.AddScope(AccessScopeType.Company, CompanyB);
        db.RoleAssignments.AddRange(aliceOperator, aliceAuditor, aliceRetired, aliceInB, boraOperator, cemOperator);

        return new Seeded(approver, alice, cem, site, operatorRole.Id, auditorRole.Id);
    }

    private static RoleAssignment Assign(IdentityDbContext db, Guid user, string roleCode, Guid company, bool scoped)
    {
        var role = new RoleDefinition(Guid.NewGuid(), roleCode, roleCode, isSystem: false);
        role.AddPermission(ErpPermissions.OrganizationSitesRead);
        db.Roles.Add(role);
        var assignment = new RoleAssignment(Guid.NewGuid(), user, role.Id, company,
            DateTimeOffset.UtcNow.AddDays(-1), null, null);
        if (scoped)
        {
            assignment.AddScope(AccessScopeType.Company, company);
        }

        db.RoleAssignments.Add(assignment);
        return assignment;
    }

    private static IdentityAccessReadService Service(IdentityDbContext db, Guid company, PermissionScope? scope = null) =>
        new(db, Context(company), new ProbeAuthorization(scope ?? CompanyWide()));

    private static CompanyContext Context(Guid company)
    {
        var context = new CompanyContext();
        context.Set(company);
        return context;
    }

    private static PermissionScope CompanyWide() => new(
        true, true, new HashSet<string>(StringComparer.Ordinal) { "PROBE" }, new HashSet<Guid>(),
        new HashSet<Guid>(), new HashSet<Guid>());

    private static PermissionScope SiteScoped() => new(
        true, false, new HashSet<string>(StringComparer.Ordinal) { "PROBE" }, new HashSet<Guid> { Guid.NewGuid() },
        new HashSet<Guid>(), new HashSet<Guid>());

    private static IdentityDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase($"identity-access-read-{Guid.NewGuid():N}").Options);

    private sealed record Seeded(Guid Approver, Guid Alice, Guid OnlyInB, Guid Site, Guid Operator, Guid Auditor);

    private sealed class ProbeAuthorization(PermissionScope scope) : IErpAuthorizationService
    {
        public List<(string Permission, Guid Company)> Asked { get; } = [];

        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(
            Guid companyId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> HasPermissionAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PermissionScope> GetPermissionScopeAsync(
            string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            Asked.Add((permission, companyId));
            return Task.FromResult(scope);
        }
    }

    private sealed class EffectivePermissionComparer : IEqualityComparer<IdentityAccessEffectivePermission>
    {
        public bool Equals(IdentityAccessEffectivePermission? x, IdentityAccessEffectivePermission? y) =>
            x is not null && y is not null && x.Code == y.Code && x.IsCritical == y.IsCritical
            && x.RoleCodes.SequenceEqual(y.RoleCodes);

        public int GetHashCode(IdentityAccessEffectivePermission obj) => obj.Code.GetHashCode(StringComparison.Ordinal);
    }
}
