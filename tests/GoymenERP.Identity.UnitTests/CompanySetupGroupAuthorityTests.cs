using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Approvals;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.CompanySetup;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GoymenERP.Identity.UnitTests;

public sealed class CompanySetupGroupAuthorityTests
{
    [Fact]
    public void First_Admin_selection_is_registered_separately_without_expanding_Product_or_granting_it()
    {
        Assert.Equal("identity.company-setup.first-admin.assign", ErpPermissions.IdentityCompanyFirstAdminAssign);
        Assert.Single(ErpPermissions.All, name => name == ErpPermissions.IdentityCompanyFirstAdminAssign);
        Assert.False(ErpPermissions.IsCritical(ErpPermissions.IdentityCompanyFirstAdminAssign));
        Assert.True(ErpPermissions.IsCritical(ErpPermissions.OrganizationCompaniesOpen));
        Assert.Equal([GroupSharedMasterKind.Product], Enum.GetValues<GroupSharedMasterKind>());
    }

    [Fact]
    public async Task An_explicit_group_assignment_authorizes_only_its_operation_and_fresh_revocation_denies()
    {
        var actor = Guid.NewGuid();
        await using var database = Database();
        database.Users.Add(new UserAccount(actor, "SENTETIK yönetici", null));
        await database.SaveChangesAsync();
        var service = new SqlCompanySetupGroupAuthorityV1(database, new User(actor), TimeProvider.System);
        Assert.False((await service.ReadAsync(CompanySetupGroupOperationV1.OpenCompany)).IsGranted);
        var assignment = new CompanySetupGroupAssignment(Guid.NewGuid(), actor, 1, Guid.NewGuid(), "SENTETIK human decision",
            DateTimeOffset.UtcNow.AddDays(-1), null);
        database.CompanySetupGroupAssignments.Add(assignment);
        database.Entry(assignment).Property(row => row.RowVersion).CurrentValue = new byte[8];
        await database.SaveChangesAsync();
        var granted = await service.ReadAsync(CompanySetupGroupOperationV1.OpenCompany);
        Assert.True(granted.IsGranted); Assert.Equal(actor, granted.ActorUserId); Assert.Equal(64, granted.Revision.Length);
        Assert.False((await service.ReadAsync(CompanySetupGroupOperationV1.AssignFirstAdmin)).IsGranted);
        Assert.False((await service.ReadAsync((CompanySetupGroupOperationV1)99)).IsGranted);
        assignment.Revoke(Guid.NewGuid(), "SENTETIK revoke decision", DateTimeOffset.UtcNow);
        await database.SaveChangesAsync();
        Assert.False((await service.ReadAsync(CompanySetupGroupOperationV1.OpenCompany)).IsGranted);
    }

    [Fact]
    public async Task Inactive_users_expired_windows_and_wrong_actors_never_gain_group_authority()
    {
        var actor = Guid.NewGuid();
        await using var database = Database();
        var user = new UserAccount(actor, "SENTETIK yönetici", null);
        database.Users.Add(user);
        var assignment = new CompanySetupGroupAssignment(Guid.NewGuid(), actor, 2, Guid.NewGuid(), "SENTETIK expired decision",
            DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-1));
        database.CompanySetupGroupAssignments.Add(assignment);
        database.Entry(assignment).Property(row => row.RowVersion).CurrentValue = new byte[8];
        await database.SaveChangesAsync();
        Assert.False((await new SqlCompanySetupGroupAuthorityV1(database, new User(actor), TimeProvider.System)
            .ReadAsync(CompanySetupGroupOperationV1.AssignFirstAdmin)).IsGranted);
        Assert.False((await new SqlCompanySetupGroupAuthorityV1(database, new User(Guid.NewGuid()), TimeProvider.System)
            .ReadAsync(CompanySetupGroupOperationV1.AssignFirstAdmin)).IsGranted);
        var valid = new CompanySetupGroupAssignment(Guid.NewGuid(), actor, 1, Guid.NewGuid(), "SENTETIK valid decision",
            DateTimeOffset.UtcNow.AddDays(-1), null);
        database.CompanySetupGroupAssignments.Add(valid);
        database.Entry(valid).Property(row => row.RowVersion).CurrentValue = new byte[8];
        await database.SaveChangesAsync();
        Assert.True((await new SqlCompanySetupGroupAuthorityV1(database, new User(actor), TimeProvider.System)
            .ReadAsync(CompanySetupGroupOperationV1.OpenCompany)).IsGranted);
        database.Entry(user).Property(row => row.IsActive).CurrentValue = false;
        await database.SaveChangesAsync();
        Assert.False((await new SqlCompanySetupGroupAuthorityV1(database, new User(actor), TimeProvider.System)
            .ReadAsync(CompanySetupGroupOperationV1.OpenCompany)).IsGranted);
    }

    [Fact]
    public async Task Company_role_permissions_even_with_both_names_are_not_group_company_setup_authority()
    {
        var actor = Guid.NewGuid(); var company = Guid.NewGuid();
        await using var database = Database();
        database.Users.Add(new UserAccount(actor, "SENTETIK şirket yetkilisi", null));
        var role = new RoleDefinition(Guid.NewGuid(), "SENTETIK", "SENTETIK rol", true);
        role.AddPermission(ErpPermissions.OrganizationCompaniesOpen);
        role.AddPermission(ErpPermissions.IdentityCompanyFirstAdminAssign);
        database.Roles.Add(role);
        var assignment = new RoleAssignment(Guid.NewGuid(), actor, role.Id, company, DateTimeOffset.UtcNow.AddDays(-1), null, actor);
        assignment.AddScope(AccessScopeType.Company, company); database.RoleAssignments.Add(assignment);
        await database.SaveChangesAsync();
        var user = new User(actor, company);
        Assert.True(await new ErpAuthorizationService(database, user).HasPermissionAsync(ErpPermissions.OrganizationCompaniesOpen, company));
        var service = new SqlCompanySetupGroupAuthorityV1(database, user, TimeProvider.System);
        Assert.False((await service.ReadAsync(CompanySetupGroupOperationV1.OpenCompany)).IsGranted);
        Assert.False((await service.ReadAsync(CompanySetupGroupOperationV1.AssignFirstAdmin)).IsGranted);
        Assert.Empty(await database.CompanySetupGroupAssignments.ToListAsync());
    }

    [Fact]
    public void Explicit_selection_records_the_chosen_target_and_one_authorized_decider_without_founder_or_opener_mode()
    {
        var actor = Guid.NewGuid(); var target = Guid.NewGuid(); var assignment = Guid.NewGuid();
        var request = new AccessGrantRequest(Guid.NewGuid(), actor, target, Guid.NewGuid(), Guid.NewGuid(), true);
        request.ApproveAsCompanySetupAuthority(actor, DateTimeOffset.UnixEpoch, assignment);
        Assert.Equal((target, actor, assignment, AccessGrantApprovalMode.CompanySetupAuthority),
            (request.TargetUserId, request.DecidedByUserId!.Value, request.ResultingRoleAssignmentId!.Value, request.ApprovalMode!.Value));
        Assert.Throws<InvalidOperationException>(() => new AccessGrantRequest(Guid.NewGuid(), actor, target, Guid.NewGuid(), Guid.NewGuid(), true)
            .ApproveAsCompanySetupAuthority(Guid.NewGuid(), DateTimeOffset.UnixEpoch, assignment));
    }

    [Fact]
    public async Task Direct_selected_approval_without_the_locked_owner_proof_and_mutation_of_global_history_are_rejected()
    {
        await using var database = Database();
        var actor = Guid.NewGuid();
        var request = new AccessGrantRequest(Guid.NewGuid(), actor, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), true);
        request.ApproveAsCompanySetupAuthority(actor, DateTimeOffset.UnixEpoch, Guid.NewGuid());
        database.AccessGrantRequests.Add(request);
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
        database.ChangeTracker.Clear();
        var grant = new CompanySetupGroupAssignment(Guid.NewGuid(), actor, 1, Guid.NewGuid(), "SENTETIK decision", DateTimeOffset.UnixEpoch, null);
        database.CompanySetupGroupAssignments.Add(grant); await database.SaveChangesAsync();
        database.Entry(grant).Property(row => row.UserId).CurrentValue = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
        database.ChangeTracker.Clear(); database.CompanySetupGroupAssignments.Remove(grant);
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
    }

    [Fact]
    public void The_group_authority_model_is_explicit_global_data_with_operation_validity_revocation_and_restrict_user_checks()
    {
        using var database = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseSqlServer("Server=unused.invalid;Database=unused").Options);
        var entity = database.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(CompanySetupGroupAssignment))!;
        Assert.Equal(("identity", "CompanySetupGroupAssignments"), (entity.GetSchema(), entity.GetTableName()));
        Assert.Null(entity.FindProperty("CompanyId"));
        Assert.Empty(entity.GetDeclaredQueryFilters());
        Assert.Equal(4, entity.GetCheckConstraints().Count());
        var current = Assert.Single(entity.GetIndexes(), index => index.Properties.Select(property => property.Name).SequenceEqual(["UserId", "Operation"]));
        Assert.True(current.IsUnique); Assert.Equal("[IsActive] = 1", current.GetFilter());
        Assert.True(entity.FindProperty(nameof(CompanySetupGroupAssignment.RowVersion))!.IsConcurrencyToken);
        Assert.Equal(DeleteBehavior.Restrict, Assert.Single(entity.GetForeignKeys()).DeleteBehavior);
    }

    [Fact]
    public void The_forward_migration_preserves_historical_modes_and_refuses_Down_before_new_authority_or_approval_is_lost()
    {
        var migration = new GoymenERP.Modules.Identity.Infrastructure.Persistence.Migrations.AddExplicitCompanySetupAuthority();
        Assert.Empty(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        Assert.Single(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.CreateTableOperation>(),
            operation => operation.Name == "CompanySetupGroupAssignments");
        var checks = migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.AddCheckConstraintOperation>().ToArray();
        Assert.All(checks, check => Assert.Contains("N'CompanyOpener'", check.Sql, StringComparison.Ordinal));
        Assert.All(checks, check => Assert.Contains("N'CompanySetupAuthority'", check.Sql, StringComparison.Ordinal));
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51947", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[identity].[CompanySetupGroupAssignments]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[ApprovalMode] = N'CompanySetupAuthority'", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
    }

    private static IdentityDbContext Database() => new(new DbContextOptionsBuilder<IdentityDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
    private sealed class User(Guid id, params Guid[] companies) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string UserId => id.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = companies.ToHashSet();
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
