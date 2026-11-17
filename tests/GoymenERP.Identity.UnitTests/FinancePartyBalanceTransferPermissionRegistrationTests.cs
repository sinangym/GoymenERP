using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Identity.UnitTests;

public sealed class FinancePartyBalanceTransferPermissionRegistrationTests
{
    [Fact]
    public void Full_balance_transfer_is_separately_registered_for_company_scope_without_a_new_critical_classification_or_seed_grant()
    {
        const string permission = "finance.party-balance-transfers.record";
        Assert.Equal(permission, ErpPermissions.FinancePartyBalanceTransfersRecord);
        Assert.True(ErpPermissions.IsKnown(permission));
        Assert.Single(ErpPermissions.All, name => name == permission);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(permission));
        Assert.False(ErpPermissions.IsCritical(permission));
        const string seed = "src/backend/GoymenERP.Api/Seed/identity-seed.json";
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, seed))) directory = directory.Parent;
        Assert.NotNull(directory);
        Assert.DoesNotContain(permission, File.ReadAllText(Path.Combine(directory.FullName, seed)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AccessScopeType.Site, false)]
    [InlineData(AccessScopeType.Company, true)]
    public async Task Only_an_explicit_company_assignment_grants_transfer_and_never_implies_ledger_disclosure(AccessScopeType scope, bool granted)
    {
        var actor = Guid.NewGuid(); var company = Guid.NewGuid(); var roleId = Guid.NewGuid();
        await using var context = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase($"identity-balance-transfer-{Guid.NewGuid():N}").Options);
        var role = new RoleDefinition(roleId, $"SENTETIK-{roleId:N}", "Sentetik izin testi", true);
        role.AddPermission(ErpPermissions.FinancePartyBalanceTransfersRecord);
        var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company, DateTimeOffset.UtcNow.AddMinutes(-1), null, null);
        assignment.AddScope(scope, scope == AccessScopeType.Company ? company : Guid.NewGuid());
        context.Users.Add(new UserAccount(actor, "Sentetik kişi", null));
        context.Roles.Add(role); context.RoleAssignments.Add(assignment);
        await context.SaveChangesAsync();
        var authorization = new ErpAuthorizationService(context, new TestUser(actor, company));
        Assert.Equal(granted, await authorization.HasPermissionAsync(ErpPermissions.FinancePartyBalanceTransfersRecord, company));
        Assert.False(await authorization.HasPermissionAsync(ErpPermissions.FinancePartyLedgerRead, company));
        Assert.False(await authorization.HasPermissionAsync(ErpPermissions.FinancePartyLedgerAmountRead, company));
        Assert.False(await authorization.HasPermissionAsync(ErpPermissions.FinancePartyBalanceTransfersRecord, Guid.NewGuid()));
    }

    private sealed class TestUser(Guid actor, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => actor.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => companyId == company;
    }
}
