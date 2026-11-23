using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SettingsCollateralTypeSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Policy_role_reference_hash_tracks_the_real_permission_set_and_late_company_actor_or_prepare_loss_is_refused()
    {
        await using var run = await HostRun.StartAsync();
        await run.GrantAsync(TestUsers.CompanyA, CompanyA, SettingsFinancePolicyPermissionsV1.Prepare);
        var company = new ReboundCatalogCompany(CompanyA); var actor = new ReboundCatalogActor(); var authority = new PolicyReferenceAuthority();
        await using var db = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseSqlServer(run.Connection).Options, company);
        var roleId = Guid.NewGuid(); db.Roles.Add(new RoleDefinition(roleId, $"POLICY-{roleId:N}", "Synthetic policy reference", false));
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var reader = new IdentityFinancePolicyRoleReferences(db, company, actor, authority);
        var before = await reader.GetAsync(CompanyA);
        var original = before.Roles.Single(x => x.RoleId == roleId);
        await run.ExecuteAsync($"INSERT INTO [identity].[RolePermissions] ([RoleId],[PermissionCode]) VALUES ('{original.RoleId:D}',N'finance.sources.read')");
        var changed = (await reader.GetAsync(CompanyA)).Roles.Single(x => x.RoleId == original.RoleId);
        Assert.NotEqual(original.SourceVersion, changed.SourceVersion);
        foreach (var mode in new[] { "company", "actor", "membership", "permission" })
        {
            var currentCompany = new ReboundCatalogCompany(CompanyA); var currentActor = new ReboundCatalogActor(); var currentAuthority = new PolicyReferenceAuthority();
            var observer = new CatalogReadBoundary("[Roles]", () =>
            {
                if (mode == "company") currentCompany.CompanyId = CompanyB;
                if (mode == "actor") currentActor.UserId = Guid.NewGuid().ToString("D");
                if (mode == "membership") currentActor.HasAccess = false;
                if (mode == "permission") currentAuthority.Allowed = false;
            });
            await using var raced = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseSqlServer(run.Connection).AddInterceptors(observer).Options, currentCompany);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new IdentityFinancePolicyRoleReferences(raced, currentCompany, currentActor, currentAuthority).GetAsync(CompanyA));
            Assert.Equal(1, observer.Calls); Assert.False(raced.ChangeTracker.HasChanges());
        }
    }

    private sealed class PolicyReferenceAuthority : IErpAuthorityRevalidationService
    {
        public bool Allowed { get; set; } = true;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Allowed && permission == SettingsFinancePolicyPermissionsV1.Prepare
                ? new PermissionScope(true, true, new HashSet<string> { "Synthetic" }, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()) : PermissionScope.Denied);
    }
}
