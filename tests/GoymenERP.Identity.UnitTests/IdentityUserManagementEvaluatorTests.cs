using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Identity.UnitTests;

/// <summary>
/// KIM-D16/KIM-D19 group-wide user management evaluator (spec ID-2r reset-password precondition 2): the actor needs
/// company-wide <c>identity.users.manage</c> in EVERY membership company of the target. SENTETIK.
/// </summary>
public sealed class IdentityUserManagementEvaluatorTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    private readonly IdentityDbContext context = new(new DbContextOptionsBuilder<IdentityDbContext>()
        .UseInMemoryDatabase($"identity-user-management-{Guid.NewGuid():N}")
        .Options);

    private readonly Guid managerRoleId = Guid.NewGuid();
    private readonly Guid plainRoleId = Guid.NewGuid();
    private readonly Guid companyA = Guid.NewGuid();
    private readonly Guid companyB = Guid.NewGuid();

    public IdentityUserManagementEvaluatorTests()
    {
        var manager = new RoleDefinition(managerRoleId, "SENTETIK-KULLANICI-YONETICI", "Sentetik kullanıcı yöneticisi", isSystem: false);
        manager.AddPermission(ErpPermissions.IdentityUsersManage);
        context.Roles.Add(manager);
        context.Roles.Add(new RoleDefinition(plainRoleId, "SENTETIK-OKUYUCU", "Sentetik okuyucu", isSystem: false));
        context.SaveChanges();
        context.ChangeTracker.Clear();
    }

    [Fact]
    public async Task An_actor_holding_the_permission_company_wide_in_every_membership_company_is_allowed()
    {
        var target = AddUser();
        var actor = AddUser();
        Assign(target, plainRoleId, companyA);
        Assign(target, plainRoleId, companyB);
        Assign(actor, managerRoleId, companyA);
        Assign(actor, managerRoleId, companyB);

        Assert.Equal(IdentityUserManagementDecision.Allowed, await Evaluator().EvaluateAsync(actor, target, Now, CancellationToken.None));
        Assert.True(await Evaluator().AnyOtherActiveUserMayManageAsync(target, [companyA, companyB], Now, CancellationToken.None));
    }

    [Fact]
    public async Task Missing_authority_in_one_membership_company_is_forbidden()
    {
        var target = AddUser();
        var actor = AddUser();
        Assign(target, plainRoleId, companyA);
        Assign(target, plainRoleId, companyB);
        Assign(actor, managerRoleId, companyA);

        Assert.Equal(IdentityUserManagementDecision.Forbidden, await Evaluator().EvaluateAsync(actor, target, Now, CancellationToken.None));
        Assert.False(await Evaluator().AnyOtherActiveUserMayManageAsync(target, [companyA, companyB], Now, CancellationToken.None));
    }

    [Fact]
    public async Task A_target_without_a_valid_assignment_requires_a_company_assignment()
    {
        var target = AddUser();
        var actor = AddUser();
        Assign(actor, managerRoleId, companyA);
        Assign(target, plainRoleId, companyA, validUntilUtc: Now.AddDays(-1));
        Assign(target, plainRoleId, companyB, validFromUtc: Now.AddDays(1));

        Assert.Equal(
            IdentityUserManagementDecision.CompanyAssignmentRequired,
            await Evaluator().EvaluateAsync(actor, target, Now, CancellationToken.None));
        Assert.Empty(await Evaluator().GetMembershipCompanyIdsAsync(target, Now, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Evaluator().AnyOtherActiveUserMayManageAsync(target, [], Now, CancellationToken.None));
    }

    [Fact]
    public async Task A_site_scope_an_inactive_holder_or_an_inactive_role_never_counts()
    {
        var target = AddUser();
        Assign(target, plainRoleId, companyA);
        Assign(AddUser(), managerRoleId, companyA, siteScopeOnly: true);
        Assign(AddUser(isActive: false), managerRoleId, companyA);
        var inactiveRoleId = Guid.NewGuid();
        var inactiveRole = new RoleDefinition(inactiveRoleId, "SENTETIK-PASIF", "Sentetik pasif", isSystem: false);
        inactiveRole.AddPermission(ErpPermissions.IdentityUsersManage);
        typeof(RoleDefinition).GetProperty(nameof(RoleDefinition.IsActive))!.SetValue(inactiveRole, false);
        context.Roles.Add(inactiveRole);
        context.SaveChanges();
        context.ChangeTracker.Clear();
        Assign(AddUser(), inactiveRoleId, companyA);

        Assert.False(await Evaluator().AnyOtherActiveUserMayManageAsync(target, [companyA], Now, CancellationToken.None));
    }

    [Fact]
    public async Task The_target_itself_is_never_the_other_manager()
    {
        var target = AddUser();
        Assign(target, managerRoleId, companyA);

        Assert.Equal(IdentityUserManagementDecision.Allowed, await Evaluator().EvaluateAsync(target, target, Now, CancellationToken.None));
        Assert.False(await Evaluator().AnyOtherActiveUserMayManageAsync(target, [companyA], Now, CancellationToken.None));
    }

    [Fact]
    public async Task Managed_companies_are_only_the_company_wide_holdings_of_the_actor()
    {
        var actor = AddUser();
        var siteCompany = Guid.NewGuid();
        var expiredCompany = Guid.NewGuid();
        Assign(actor, managerRoleId, companyA);
        Assign(actor, plainRoleId, companyB);
        Assign(actor, managerRoleId, siteCompany, siteScopeOnly: true);
        Assign(actor, managerRoleId, expiredCompany, validUntilUtc: Now.AddDays(-1));
        Assign(AddUser(), managerRoleId, companyB);

        Assert.Equal([companyA], await Evaluator().GetCompanyWideManagedCompanyIdsAsync(actor, Now, CancellationToken.None));
        Assert.Empty(await Evaluator().GetCompanyWideManagedCompanyIdsAsync(AddUser(isActive: false), Now, CancellationToken.None));
    }

    [Fact]
    public async Task The_list_holds_only_fully_manageable_members_of_the_context_company_and_own_unassigned_accounts()
    {
        var actor = AddUser();
        Assign(actor, managerRoleId, companyA);
        var onlyA = AddUser(); Assign(onlyA, plainRoleId, companyA);
        var onlyB = AddUser(); Assign(onlyB, plainRoleId, companyB);
        var both = AddUser(); Assign(both, plainRoleId, companyA); Assign(both, plainRoleId, companyB);
        var ownUnassigned = AddUser(createdBy: actor);
        var ownExpired = AddUser(createdBy: actor); Assign(ownExpired, plainRoleId, companyB, validUntilUtc: Now.AddDays(-1));
        var otherUnassigned = AddUser(createdBy: Guid.NewGuid());
        var creatorless = AddUser();

        var listed = await Evaluator().GetListedUserIdsAsync(actor, companyA, Now, CancellationToken.None);

        Assert.Equal(new[] { actor, onlyA, ownUnassigned, ownExpired }.Order(), listed.Order());
        Assert.DoesNotContain(onlyB, listed); Assert.DoesNotContain(both, listed);
        Assert.DoesNotContain(otherUnassigned, listed); Assert.DoesNotContain(creatorless, listed);
    }

    [Fact]
    public async Task Only_the_founder_named_by_the_bootstrap_marker_sees_every_unassigned_account()
    {
        var founder = AddUser();
        var other = AddUser();
        Assign(founder, managerRoleId, companyA);
        Assign(other, managerRoleId, companyA);
        var foreign = AddUser(createdBy: Guid.NewGuid());
        var creatorless = AddUser();
        var memberOfB = AddUser(); Assign(memberOfB, plainRoleId, companyB);

        // No marker: the founder branch admits nobody (fail-closed).
        Assert.DoesNotContain(foreign, await Evaluator().GetListedUserIdsAsync(founder, companyA, Now, CancellationToken.None));

        context.BootstrapStates.Add(new GoymenERP.Modules.Identity.Domain.Bootstrap.IdentityBootstrapState(founder, Now.AddDays(-1)));
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var founderList = await Evaluator().GetListedUserIdsAsync(founder, companyA, Now, CancellationToken.None);
        Assert.Contains(foreign, founderList); Assert.Contains(creatorless, founderList);
        Assert.DoesNotContain(memberOfB, founderList);
        var otherList = await Evaluator().GetListedUserIdsAsync(other, companyA, Now, CancellationToken.None);
        Assert.DoesNotContain(foreign, otherList); Assert.DoesNotContain(creatorless, otherList);
    }

    public void Dispose() => context.Dispose();

    private IdentityUserManagementEvaluator Evaluator() => new(context);

    private Guid AddUser(bool isActive = true, Guid? createdBy = null)
    {
        var user = new UserAccount(Guid.NewGuid(), "Sentetik Kullanıcı", email: null, createdByUserId: createdBy);
        if (!isActive)
        {
            typeof(UserAccount).GetProperty(nameof(UserAccount.IsActive))!.SetValue(user, false);
        }

        context.Users.Add(user);
        context.SaveChanges();
        context.ChangeTracker.Clear();
        return user.Id;
    }

    private void Assign(
        Guid userId,
        Guid roleId,
        Guid companyId,
        DateTimeOffset? validFromUtc = null,
        DateTimeOffset? validUntilUtc = null,
        bool siteScopeOnly = false)
    {
        var assignment = new RoleAssignment(
            Guid.NewGuid(),
            userId,
            roleId,
            companyId,
            validFromUtc ?? Now.AddDays(-10),
            validUntilUtc,
            approvedByUserId: null);
        assignment.AddScope(siteScopeOnly ? AccessScopeType.Site : AccessScopeType.Company, siteScopeOnly ? Guid.NewGuid() : companyId);
        context.RoleAssignments.Add(assignment);
        context.SaveChanges();
        context.ChangeTracker.Clear();
    }
}
