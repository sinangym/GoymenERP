using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Approvals;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using GoymenERP.Modules.Identity.Domain.CompanySetup;
using GoymenERP.Modules.Identity.Infrastructure.Persistence.Migrations;

namespace GoymenERP.Identity.UnitTests;

public sealed class CompanySetupContractsAndAuthorizationTests
{
    [Fact]
    public void Company_opening_is_registered_Critical_without_granting_it()
    {
        Assert.Contains(ErpPermissions.OrganizationCompaniesOpen, ErpPermissions.All);
        Assert.True(ErpPermissions.IsCritical(ErpPermissions.OrganizationCompaniesOpen));
    }
    [Fact]
    public void Company_opener_mode_is_only_the_requesters_own_indefinite_assignment()
    {
        var opener = Guid.NewGuid(); var role = Guid.NewGuid(); var company = Guid.NewGuid();
        var request = new AccessGrantRequest(Guid.NewGuid(), opener, opener, role, company, true, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, "Sentetik");
        request.ApproveAsCompanyOpener(opener, DateTimeOffset.UnixEpoch, Guid.NewGuid());
        Assert.Equal(AccessGrantApprovalMode.CompanyOpener, request.ApprovalMode);
        Assert.Throws<InvalidOperationException>(() => new AccessGrantRequest(Guid.NewGuid(), opener, Guid.NewGuid(), role, company, true)
            .ApproveAsCompanyOpener(opener, DateTimeOffset.UnixEpoch, Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() => new AccessGrantRequest(Guid.NewGuid(), opener, opener, role, company, true,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1), null)
            .ApproveAsCompanyOpener(opener, DateTimeOffset.UnixEpoch, Guid.NewGuid()));
    }
    [Fact]
    public void The_opening_contract_uses_its_exact_owner_company_source_and_version()
    {
        var company = Guid.NewGuid(); var context = new CompanyContext(); context.Set(company);
        var payload = new OrganizationCompanyOpenedV1(company, Guid.NewGuid(), DateTimeOffset.UnixEpoch);
        var message = new WorkInboxEventV1(OrganizationCompanyOpenedV1.SourceName, Guid.NewGuid(), company, OrganizationCompanyOpenedV1.EventType,
            1, JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)), DateTimeOffset.UnixEpoch, "synthetic", null);
        Assert.Equal(payload, IdentityCompanySetupService.Read(message, context));
        Assert.Throws<OutboxPermanentFailureException>(() => IdentityCompanySetupService.Read(message with { SourceName = "foreign" }, context));
        Assert.Throws<OutboxPermanentFailureException>(() => IdentityCompanySetupService.Read(message with { CompanyId = Guid.NewGuid() }, context));
        Assert.Throws<OutboxPermanentFailureException>(() => IdentityCompanySetupService.Read(message with { Version = 2 }, context));
        Assert.Throws<OutboxPermanentFailureException>(() => IdentityCompanySetupService.Read(message with { PayloadJson = "{}" }, context));
        Assert.Equal(IdentityCompanyFirstAdminAssignedV1.EventId(company), IdentityCompanyFirstAdminAssignedV1.EventId(company));
        Assert.NotEqual(IdentityCompanyFirstAdminAssignedV1.EventId(company), IdentityCompanyFirstAdminAssignedV1.EventId(Guid.NewGuid()));
    }
    [Fact]
    public async Task Pending_unknown_and_foreign_lifecycle_fence_cached_fresh_snapshot_and_persona_authority()
    {
        var company = Guid.NewGuid(); var user = Guid.NewGuid(); var role = new RoleDefinition(Guid.NewGuid(), "SYNTHETIC", "Sentetik", true);
        role.AddPermission(ErpPermissions.OrganizationLocationsRead);
        await using var db = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        db.Users.Add(new UserAccount(user, "Sentetik", null)); db.Roles.Add(role);
        var assignment = new RoleAssignment(Guid.NewGuid(), user, role.Id, company, DateTimeOffset.UtcNow.AddDays(-1), null, null);
        assignment.AddScope(AccessScopeType.Company, company); db.RoleAssignments.Add(assignment); await db.SaveChangesAsync();
        var lifecycle = new Lifecycle(new(company, CompanyLifecycleStateV1.Active, true, 1));
        var gate = new CompanyLifecycleAuthorizationGate(new ErpAuthorizationService(db, new User(user, company)), lifecycle);
        Assert.True(await gate.HasPermissionAsync(ErpPermissions.OrganizationLocationsRead, company));
        lifecycle.Value = new(company, CompanyLifecycleStateV1.SetupPending, true, 1);
        Assert.False(await gate.HasPermissionAsync(ErpPermissions.OrganizationLocationsRead, company));
        Assert.False((await gate.RevalidatePermissionScopeAsync(ErpPermissions.OrganizationLocationsRead, company)).IsGranted);
        Assert.Empty(await gate.GetActiveRoleCodesAsync(company));
        var snapshot = await gate.GetAuthorizationSnapshotAsync(company, [ErpPermissions.OrganizationLocationsRead]);
        Assert.Empty(snapshot.AllowedCompanyIds); Assert.False(snapshot.PermissionScopes[ErpPermissions.OrganizationLocationsRead].IsGranted);
        lifecycle.Value = null;
        Assert.False(await gate.HasPermissionAsync(ErpPermissions.OrganizationLocationsRead, company));
        lifecycle.Value = new(Guid.NewGuid(), CompanyLifecycleStateV1.Active, true, 1);
        Assert.False(await gate.HasPermissionAsync(ErpPermissions.OrganizationLocationsRead, company));
    }
    [Fact]
    public async Task A_direct_company_opener_aggregate_cannot_be_saved_without_the_locked_inbox_transaction()
    {
        var company = Guid.NewGuid(); var actor = Guid.NewGuid(); var context = new CompanyContext(); context.Set(company);
        await using var db = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options, context);
        var request = new AccessGrantRequest(Guid.NewGuid(), actor, actor, Guid.NewGuid(), company, true);
        request.ApproveAsCompanyOpener(actor, DateTimeOffset.UnixEpoch, Guid.NewGuid()); db.AccessGrantRequests.Add(request);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
    [Fact]
    public void The_setup_model_is_company_filtered_and_its_generated_migration_is_DDL_with_a_first_Down_guard()
    {
        using var db = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseSqlServer("Server=unused.invalid;Database=unused").Options);
        var model = db.GetService<IDesignTimeModel>().Model;
        var receipt = model.FindEntityType(typeof(CompanySetupInboxReceipt))!;
        Assert.Equal("identity", receipt.GetSchema()); Assert.NotNull(receipt.GetDeclaredQueryFilters().SingleOrDefault());
        Assert.Equal(IdentityDbContext.CompanySetupReceiptIdentityCheck, Assert.Single(receipt.GetCheckConstraints()).Sql);
        var migration = new AddCompanySetupInboxAndOutboxPosition();
        Assert.DoesNotContain(migration.UpOperations, x => x is SqlOperation);
        Assert.Contains("THROW 51944", Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
    }
    [Fact]
    public async Task The_first_Admin_event_can_only_be_appended_in_its_own_company_and_never_updated_or_deleted()
    {
        var company = Guid.NewGuid(); var context = new CompanyContext(); context.Set(company);
        await using var db = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options, context);
        var message = new GoymenERP.Modules.Identity.Domain.Outbox.IdentityOutboxMessage(
            IdentityCompanyFirstAdminAssignedV1.EventId(company), company, IdentityCompanyFirstAdminAssignedV1.EventType, 1,
            "identity.company.first-admin.assign.v1", "identity.company-setup", company, 1, "{}", DateTimeOffset.UnixEpoch, "synthetic", null);
        db.OutboxMessages.Add(message); await db.SaveChangesAsync();
        db.Entry(message).Property(x => x.PayloadJson).CurrentValue = "{\"changed\":true}";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear(); db.OutboxMessages.Remove(message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        var foreign = Guid.NewGuid();
        db.OutboxMessages.Add(new(IdentityCompanyFirstAdminAssignedV1.EventId(foreign), foreign,
            IdentityCompanyFirstAdminAssignedV1.EventType, 1, "identity.company.first-admin.assign.v1", "identity.company-setup",
            foreign, 1, "{}", DateTimeOffset.UnixEpoch, "synthetic", null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
    private sealed class Lifecycle(CompanyLifecycleViewV1? value) : ICompanyLifecycleReadPortV1
    {
        public CompanyLifecycleViewV1? Value { get; set; } = value;
        public Task<CompanyLifecycleViewV1?> ReadAsync(Guid companyId, CancellationToken cancellationToken = default) => Task.FromResult(Value);
    }
    private sealed class User(Guid user, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true; public string UserId => user.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid id) => id == company;
    }
}
