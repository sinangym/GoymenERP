using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Identity.UnitTests;

public sealed class SettingsPurchaseInvoiceVariancePolicyPermissionRegistrationTests
{
    [Fact]
    public void The_invoice_policy_name_is_separate_company_wide_and_registered_without_assigning_it_to_a_seed_role()
    {
        const string permission = "settings.purchase-invoice-variance-policies.manage";
        Assert.Equal(permission, ErpPermissions.SettingsPurchaseInvoiceVariancePoliciesManage);
        Assert.True(ErpPermissions.IsKnown(permission));
        Assert.Single(ErpPermissions.All, candidate => candidate == permission);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(permission));
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        Assert.NotNull(directory);
        Assert.DoesNotContain(permission, File.ReadAllText(Path.Combine(directory.FullName,
            "src/backend/GoymenERP.Api/Seed/identity-seed.json")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AccessScopeType.Company, true, true)]
    [InlineData(AccessScopeType.Site, true, false)]
    [InlineData(AccessScopeType.Company, false, false)]
    public async Task Only_an_explicit_company_policy_assignment_grants_publication_and_order_approval_never_implies_it(
        AccessScopeType scope, bool grantPolicy, bool expected)
    {
        var actor = Guid.NewGuid(); var company = Guid.NewGuid(); var roleId = Guid.NewGuid();
        await using var database = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase($"invoice-policy-authority-{Guid.NewGuid():N}").Options);
        var role = new RoleDefinition(roleId, $"SENTETIK-{roleId:N}", "SENTETIK politika testi", true);
        role.AddPermission(grantPolicy ? ErpPermissions.SettingsPurchaseInvoiceVariancePoliciesManage : ErpPermissions.PurchasingOrdersApprove);
        var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company, DateTimeOffset.UtcNow.AddMinutes(-1), null, null);
        assignment.AddScope(scope, scope == AccessScopeType.Company ? company : Guid.NewGuid());
        database.Users.Add(new UserAccount(actor, "SENTETIK kişi", null));
        database.Roles.Add(role); database.RoleAssignments.Add(assignment); await database.SaveChangesAsync();
        var authorization = new ErpAuthorizationService(database, new Actor(actor, company));
        Assert.Equal(expected, await authorization.HasPermissionAsync(ErpPermissions.SettingsPurchaseInvoiceVariancePoliciesManage, company));
        Assert.False(await authorization.HasPermissionAsync(ErpPermissions.SettingsPurchaseInvoiceVariancePoliciesManage, Guid.NewGuid()));
    }

    private sealed class Actor(Guid id, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => id.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => companyId == company;
    }
}
