using System.Net;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Approvals;
using GoymenERP.Modules.Identity.Domain.Bootstrap;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using GoymenERP.Modules.Organization.Domain.Companies;
using GoymenERP.Modules.Organization.Domain.CompanySetup;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class IdentityAccessAssignmentSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Company_opened_double_delivery_assigns_one_indefinite_Admin_and_only_Organization_completes_setup()
    {
        var code = SyntheticCode("SETUP-ADMIN");
        await WithDatabaseAsync(code, async factory =>
        {
            var role = await CreateRoleAsync(factory, code, IdentityPermissionManifestV1.RolesManage);
            var opener = await EnsureUserAsync(factory, TestUsers.NoAssignment);
            var opened = await CreatePendingCompanyFixtureAsync(factory, opener);
            await Task.WhenAll(DeliverCompanyOpenedAsync(factory, opened), DeliverCompanyOpenedAsync(factory, opened));
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(opened.CompanyId);
                var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var assignment = await identity.RoleAssignments.Include(x => x.Scopes).SingleAsync(x => x.CompanyId == opened.CompanyId);
                Assert.Equal((opener, role), (assignment.UserId, assignment.RoleId)); Assert.Null(assignment.ValidUntilUtc);
                Assert.Equal(opened.CompanyId, Assert.Single(assignment.Scopes).ScopeId);
                Assert.Equal(AccessGrantApprovalMode.CompanyOpener, (await identity.AccessGrantRequests.SingleAsync(x => x.CompanyId == opened.CompanyId)).ApprovalMode);
                Assert.Single(await identity.CompanySetupInboxReceipts.Where(x => x.CompanyId == opened.CompanyId).ToListAsync());
                Assert.Equal(CompanyLifecycleStateV1.SetupPending, (await scope.ServiceProvider.GetRequiredService<ICompanyLifecycleReadPortV1>().ReadAsync(opened.CompanyId))!.State);
                Assert.Contains(opened.CompanyId, await scope.ServiceProvider.GetRequiredService<IOutboxCompanyDirectoryV1>().ListCompanyIdsAsync(default));
            }
            var assigned = await ReadFirstAdminMessageAsync(factory, opened.CompanyId);
            await DeliverFirstAdminAsync(factory, assigned);
            await DeliverFirstAdminAsync(factory, assigned);
            await using var check = factory.Services.CreateAsyncScope();
            check.ServiceProvider.GetRequiredService<ICompanyContext>().Set(opened.CompanyId);
            var organization = check.ServiceProvider.GetRequiredService<OrganizationDbContext>();
            var company = await organization.Companies.AsNoTracking().SingleAsync(x => x.Id == opened.CompanyId);
            Assert.Equal(("Active", 2L), (company.LifecycleState, company.Version));
            Assert.Single(await organization.CompanySetupInboxReceipts.ToListAsync());
            Assert.Single(await check.ServiceProvider.GetRequiredService<AuditDbContext>().AuditEvents
                .Where(x => x.EventType == "organization.company.setup-completed.v1").ToListAsync());
            await Assert.ThrowsAsync<OutboxPermanentFailureException>(() => DeliverFirstAdminAsync(factory, assigned with { PayloadJson = assigned.PayloadJson + " " }));
        });
    }

    [SqlServerFact]
    public async Task First_Admin_Audit_failure_rolls_back_the_assignment_inbox_and_event_then_same_delivery_succeeds()
    {
        var fault = new AuditOwnerFault();
        var code = SyntheticCode("SETUP-ADMIN");
        await WithDatabaseAsync(code, async factory =>
        {
            await CreateRoleAsync(factory, code, IdentityPermissionManifestV1.RolesManage);
            var opener = await EnsureUserAsync(factory, TestUsers.NoAssignment);
            var opened = await CreatePendingCompanyFixtureAsync(factory, opener);
            await Assert.ThrowsAsync<InvalidOperationException>(() => DeliverCompanyOpenedAsync(factory, opened));
            await using (var check = factory.Services.CreateAsyncScope())
            {
                check.ServiceProvider.GetRequiredService<ICompanyContext>().Set(opened.CompanyId);
                var identity = check.ServiceProvider.GetRequiredService<IdentityDbContext>();
                Assert.Empty(await identity.RoleAssignments.Where(x => x.CompanyId == opened.CompanyId).ToListAsync());
                Assert.Empty(await identity.CompanySetupInboxReceipts.Where(x => x.CompanyId == opened.CompanyId).ToListAsync());
                Assert.Empty(await identity.OutboxMessages.Where(x => x.CompanyId == opened.CompanyId).ToListAsync());
                Assert.Empty(await check.ServiceProvider.GetRequiredService<AuditDbContext>().AuditEvents.ToListAsync());
            }
            fault.FailAfterAppend = false;
            await DeliverCompanyOpenedAsync(factory, opened);
            await DeliverCompanyOpenedAsync(factory, opened);
            await ReadFirstAdminMessageAsync(factory, opened.CompanyId);
        }, services =>
        {
            var registration = services.Single(x => x.ServiceType == typeof(IAuditTransactionWriterV1));
            services.Remove(registration);
            services.AddScoped<IAuditTransactionWriterV1>(provider => new FaultingAuditOwner(
                (IAuditTransactionWriterV1)ActivatorUtilities.CreateInstance(provider, registration.ImplementationType!), fault));
        });
    }

    [SqlServerFact]
    public async Task Opener_and_pending_founder_race_share_the_Admin_lock_and_emit_only_one_first_Admin_event()
    {
        var code = SyntheticCode("SETUP-ADMIN");
        await WithDatabaseAsync(code, async factory =>
        {
            await CreateRoleAsync(factory, code, IdentityPermissionManifestV1.RolesManage);
            var opener = await EnsureUserAsync(factory, TestUsers.CompanyB);
            var founder = await EnsureUserAsync(factory, TestUsers.NoAssignment);
            var target = await EnsureUserAsync(factory, TestUsers.CompanyA);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                db.BootstrapStates.Add(new IdentityBootstrapState(founder, DateTimeOffset.UtcNow));
                await db.SaveChangesAsync();
            }
            var opened = await CreatePendingCompanyFixtureAsync(factory, opener);
            using var client = CreateClient(factory, TestUsers.NoAssignment, opened.CompanyId);
            var founderCall = PostAsync(client, "/api/v1/identity/company-setup/first-admin",
                new AssignFirstCompanyAdminV1(target, 1, "Sentetik kurucu kurtarması"), NewKey(), NewKey());
            var openerCall = DeliverCompanyOpenedAsync(factory, opened);
            await Task.WhenAll(founderCall, openerCall);
            using var response = await founderCall;
            Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict });
            await using var check = factory.Services.CreateAsyncScope();
            check.ServiceProvider.GetRequiredService<ICompanyContext>().Set(opened.CompanyId);
            var identity = check.ServiceProvider.GetRequiredService<IdentityDbContext>();
            Assert.Single(await identity.RoleAssignments.Where(x => x.CompanyId == opened.CompanyId).ToListAsync());
            Assert.Single(await identity.OutboxMessages.Where(x => x.CompanyId == opened.CompanyId
                && x.EventType == IdentityCompanyFirstAdminAssignedV1.EventType).ToListAsync());
            await DeliverFirstAdminAsync(factory, await ReadFirstAdminMessageAsync(factory, opened.CompanyId));
        });
    }

    [SqlServerFact]
    public async Task An_inactive_opener_leaves_setup_pending_and_no_Admin_receipt_or_activation_event()
    {
        var code = SyntheticCode("SETUP-ADMIN");
        await WithDatabaseAsync(code, async factory =>
        {
            await CreateRoleAsync(factory, code, IdentityPermissionManifestV1.RolesManage);
            var opener = await EnsureUserAsync(factory, TestUsers.NoAssignment);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var user = await db.Users.SingleAsync(x => x.Id == opener);
                db.Entry(user).Property(x => x.IsActive).CurrentValue = false;
                await db.SaveChangesAsync();
            }
            var opened = await CreatePendingCompanyFixtureAsync(factory, opener);
            await Assert.ThrowsAsync<InvalidOperationException>(() => DeliverCompanyOpenedAsync(factory, opened));
            await using var check = factory.Services.CreateAsyncScope();
            check.ServiceProvider.GetRequiredService<ICompanyContext>().Set(opened.CompanyId);
            var identity = check.ServiceProvider.GetRequiredService<IdentityDbContext>();
            Assert.Empty(await identity.RoleAssignments.Where(x => x.CompanyId == opened.CompanyId).ToListAsync());
            Assert.Empty(await identity.CompanySetupInboxReceipts.Where(x => x.CompanyId == opened.CompanyId).ToListAsync());
            Assert.Empty(await identity.OutboxMessages.Where(x => x.CompanyId == opened.CompanyId).ToListAsync());
            Assert.Equal(CompanyLifecycleStateV1.SetupPending, (await check.ServiceProvider.GetRequiredService<ICompanyLifecycleReadPortV1>().ReadAsync(opened.CompanyId))!.State);
        });
    }

    [SqlServerFact]
    public async Task The_company_setup_Down_guards_allow_old_rows_and_refuse_before_owner_history_would_be_lost()
    {
        var code = SyntheticCode("SETUP-ADMIN");
        await WithDatabaseAsync(code, async factory =>
        {
            var identityGuard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(
                new GoymenERP.Modules.Identity.Infrastructure.Persistence.Migrations.AddCompanySetupInboxAndOutboxPosition().DownOperations[0]).Sql;
            var organizationGuard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(
                new GoymenERP.Modules.Organization.Infrastructure.Persistence.Migrations.AddCompanySetupLifecycleAndMessaging().DownOperations[0]).Sql;
            await using (var empty = factory.Services.CreateAsyncScope())
            {
                var db = empty.ServiceProvider.GetRequiredService<IdentityDbContext>();
                await db.Database.ExecuteSqlRawAsync(identityGuard);
                await db.Database.ExecuteSqlRawAsync(organizationGuard);
            }
            await CreateRoleAsync(factory, code, IdentityPermissionManifestV1.RolesManage);
            var opened = await CreatePendingCompanyFixtureAsync(factory, await EnsureUserAsync(factory, TestUsers.NoAssignment));
            await using (var pending = factory.Services.CreateAsyncScope())
            {
                var db = pending.ServiceProvider.GetRequiredService<IdentityDbContext>();
                Assert.Equal(51945, (await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => db.Database.ExecuteSqlRawAsync(organizationGuard))).Number);
                await db.Database.ExecuteSqlRawAsync(identityGuard);
            }
            await DeliverCompanyOpenedAsync(factory, opened);
            await using var assigned = factory.Services.CreateAsyncScope();
            var identity = assigned.ServiceProvider.GetRequiredService<IdentityDbContext>();
            Assert.Equal(51944, (await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => identity.Database.ExecuteSqlRawAsync(identityGuard))).Number);
            Assert.Single(await identity.RoleAssignments.Where(x => x.CompanyId == opened.CompanyId).ToListAsync());
            Assert.Single(await identity.OutboxMessages.Where(x => x.CompanyId == opened.CompanyId
                && x.EventType == IdentityCompanyFirstAdminAssignedV1.EventType).ToListAsync());
        });
    }

    // Synthetic fixture only: the production opening endpoint is still behind the unresolved authority-scope decision.
    private static async Task<WorkInboxEventV1> CreatePendingCompanyFixtureAsync(SqlServerRelationalApiFactory factory, Guid opener)
    {
        var id = Guid.NewGuid(); var eventId = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        var payload = JsonSerializer.Serialize(new OrganizationCompanyOpenedV1(id, opener, now), Json);
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(id);
        var db = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
        var prefix = Random.Shared.Next(100000000, 900000000).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var vkn = Enumerable.Range(0, 10).Select(x => prefix + x).Single(CompanyVknCheckDigit.IsSatisfiedBy);
        using (scope.ServiceProvider.GetRequiredService<OrganizationBootstrapWriteScope>().BeginBootstrapWrite())
        {
            db.Companies.Add(Company.OpenPending(id, "SYN-" + id.ToString("N"), "Sentetik kurulum şirketi", vkn, opener, now));
            db.CompanySetupOutboxMessages.Add(new OrganizationCompanySetupOutboxMessage(eventId, id, payload, now, NewKey(), null));
            await db.SaveChangesAsync();
        }
        var source = new OrganizationCompanySetupOutboxSource(db, scope.ServiceProvider.GetRequiredService<ICompanyContext>());
        var record = Assert.Single(await source.ReadAfterAsync(null, 10, default));
        return new(OrganizationCompanyOpenedV1.SourceName, record.EventId, id, record.EventType, record.Version,
            record.PayloadJson, record.OccurredAtUtc, record.CorrelationId, record.CausationId);
    }
    private static async Task DeliverCompanyOpenedAsync(SqlServerRelationalApiFactory factory, WorkInboxEventV1 message)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(message.CompanyId);
        await scope.ServiceProvider.GetRequiredService<IdentityCompanySetupService>().HandleAsync(message, default);
    }
    private static async Task<WorkInboxEventV1> ReadFirstAdminMessageAsync(SqlServerRelationalApiFactory factory, Guid companyId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(companyId);
        var source = ActivatorUtilities.CreateInstance<IdentityCompanySetupOutboxSource>(scope.ServiceProvider);
        var record = Assert.Single(await source.ReadAfterAsync(null, 10, default));
        Assert.Equal(IdentityCompanyFirstAdminAssignedV1.EventId(companyId), record.EventId);
        return new(IdentityCompanyFirstAdminAssignedV1.SourceName, record.EventId, companyId, record.EventType, record.Version,
            record.PayloadJson, record.OccurredAtUtc, record.CorrelationId, record.CausationId);
    }
    private static async Task DeliverFirstAdminAsync(SqlServerRelationalApiFactory factory, WorkInboxEventV1 message)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(message.CompanyId);
        await scope.ServiceProvider.GetRequiredService<OrganizationCompanySetupInboxConsumer>().HandleAsync(message, default);
    }
}
