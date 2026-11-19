using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Approvals;
using GoymenERP.Modules.Identity.Domain.CompanySetup;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Domain.Companies;
using GoymenERP.Modules.Organization.Domain.CompanySetup;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class IdentityAccessAssignmentSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Opening_double_delivery_grants_nobody_and_explicit_selection_assigns_only_the_selected_Admin()
    {
        var code = SyntheticCode("SETUP-ADMIN");
        await WithDatabaseAsync(code, async factory =>
        {
            var role = await CreateRoleAsync(factory, code, IdentityPermissionManifestV1.RolesManage);
            var opener = await EnsureUserAsync(factory, TestUsers.NoAssignment);
            var opened = await CreatePendingCompanyFixtureAsync(factory, opener);
            await Task.WhenAll(DeliverCompanyOpenedAsync(factory, opened), DeliverCompanyOpenedAsync(factory, opened));
            var target = await EnsureUserAsync(factory, TestUsers.CompanyA);
            await GrantSetupAuthorityFixtureAsync(factory, opener, CompanySetupGroupOperationV1.AssignFirstAdmin);
            await using (var before = factory.Services.CreateAsyncScope())
            {
                var db = before.ServiceProvider.GetRequiredService<IdentityDbContext>();
                Assert.Empty(await db.RoleAssignments.Where(x => x.CompanyId == opened.CompanyId).ToListAsync());
                Assert.Empty(await db.AccessGrantRequests.Where(x => x.CompanyId == opened.CompanyId).ToListAsync());
                Assert.Empty(await db.OutboxMessages.Where(x => x.CompanyId == opened.CompanyId).ToListAsync());
            }
            using var client = CreateClient(factory, TestUsers.NoAssignment, opened.CompanyId);
            var key = NewKey(); var request = new AssignFirstCompanyAdminV1(target, 1, "Sentetik açık kullanıcı seçimi");
            using var selected = await PostAsync(client, "/api/v1/identity/company-setup/first-admin", request, key, NewKey());
            Assert.Equal(HttpStatusCode.Created, selected.StatusCode);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(opened.CompanyId);
                var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var assignment = await identity.RoleAssignments.Include(x => x.Scopes).SingleAsync(x => x.CompanyId == opened.CompanyId);
                Assert.Equal((target, role), (assignment.UserId, assignment.RoleId)); Assert.Null(assignment.ValidUntilUtc);
                Assert.Equal(opened.CompanyId, Assert.Single(assignment.Scopes).ScopeId);
                Assert.Equal(AccessGrantApprovalMode.CompanySetupAuthority, (await identity.AccessGrantRequests.SingleAsync(x => x.CompanyId == opened.CompanyId)).ApprovalMode);
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
            using var replay = await PostAsync(client, "/api/v1/identity/company-setup/first-admin", request, key, NewKey());
            Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
            Assert.Single(await check.ServiceProvider.GetRequiredService<AuditDbContext>().AuditEvents
                .Where(x => x.EventType == "organization.company.setup-completed.v1").ToListAsync());
            await Assert.ThrowsAsync<OutboxPermanentFailureException>(() => DeliverFirstAdminAsync(factory, assigned with { PayloadJson = assigned.PayloadJson + " " }));
        });
    }

    [SqlServerFact]
    public async Task Explicit_first_Admin_Audit_failure_rolls_back_assignment_and_event_and_same_key_can_retry()
    {
        var fault = new AuditOwnerFault();
        var code = SyntheticCode("SETUP-ADMIN");
        await WithDatabaseAsync(code, async factory =>
        {
            await CreateRoleAsync(factory, code, IdentityPermissionManifestV1.RolesManage);
            var opener = await EnsureUserAsync(factory, TestUsers.NoAssignment);
            var opened = await CreatePendingCompanyFixtureAsync(factory, opener);
            await DeliverCompanyOpenedAsync(factory, opened);
            var target = await EnsureUserAsync(factory, TestUsers.CompanyA);
            await GrantSetupAuthorityFixtureAsync(factory, opener, CompanySetupGroupOperationV1.AssignFirstAdmin);
            using var client = CreateClient(factory, TestUsers.NoAssignment, opened.CompanyId);
            var key = NewKey(); var request = new AssignFirstCompanyAdminV1(target, 1, "Sentetik açık kullanıcı seçimi");
            using var failed = await PostAsync(client, "/api/v1/identity/company-setup/first-admin", request, key, NewKey());
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
            await using (var check = factory.Services.CreateAsyncScope())
            {
                check.ServiceProvider.GetRequiredService<ICompanyContext>().Set(opened.CompanyId);
                var identity = check.ServiceProvider.GetRequiredService<IdentityDbContext>();
                Assert.Empty(await identity.RoleAssignments.Where(x => x.CompanyId == opened.CompanyId).ToListAsync());
                Assert.Single(await identity.CompanySetupInboxReceipts.Where(x => x.CompanyId == opened.CompanyId).ToListAsync());
                Assert.Empty(await identity.OutboxMessages.Where(x => x.CompanyId == opened.CompanyId).ToListAsync());
                Assert.Empty(await check.ServiceProvider.GetRequiredService<AuditDbContext>().AuditEvents.ToListAsync());
            }
            fault.FailAfterAppend = false;
            using var retried = await PostAsync(client, "/api/v1/identity/company-setup/first-admin", request, key, NewKey());
            Assert.Equal(HttpStatusCode.Created, retried.StatusCode);
            using var replay = await PostAsync(client, "/api/v1/identity/company-setup/first-admin", request, key, NewKey());
            Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
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
    public async Task Two_authorized_explicit_selections_share_the_Admin_lock_and_emit_one_first_Admin_event()
    {
        var code = SyntheticCode("SETUP-ADMIN");
        await WithDatabaseAsync(code, async factory =>
        {
            await CreateRoleAsync(factory, code, IdentityPermissionManifestV1.RolesManage);
            var opener = await EnsureUserAsync(factory, TestUsers.CompanyB);
            var manager = await EnsureUserAsync(factory, TestUsers.NoAssignment);
            var target = await EnsureUserAsync(factory, TestUsers.CompanyA);
            await GrantSetupAuthorityFixtureAsync(factory, opener, CompanySetupGroupOperationV1.AssignFirstAdmin);
            await GrantSetupAuthorityFixtureAsync(factory, manager, CompanySetupGroupOperationV1.AssignFirstAdmin);
            var opened = await CreatePendingCompanyFixtureAsync(factory, opener);
            using var managerClient = CreateClient(factory, TestUsers.NoAssignment, opened.CompanyId);
            using var openerClient = CreateClient(factory, TestUsers.CompanyB, opened.CompanyId);
            var a = PostAsync(managerClient, "/api/v1/identity/company-setup/first-admin",
                new AssignFirstCompanyAdminV1(target, 1, "Sentetik seçilmiş yönetici A"), NewKey(), NewKey());
            var b = PostAsync(openerClient, "/api/v1/identity/company-setup/first-admin",
                new AssignFirstCompanyAdminV1(manager, 1, "Sentetik seçilmiş yönetici B"), NewKey(), NewKey());
            await Task.WhenAll(a, b);
            using var first = await a; using var second = await b;
            Assert.Equal(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }, new[] { first.StatusCode, second.StatusCode }.Order().ToArray());
            await DeliverCompanyOpenedAsync(factory, opened);
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
    public async Task An_inactive_opener_event_is_deduplicated_without_any_Admin_or_activation_event()
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
            await DeliverCompanyOpenedAsync(factory, opened);
            await DeliverCompanyOpenedAsync(factory, opened);
            await using var check = factory.Services.CreateAsyncScope();
            check.ServiceProvider.GetRequiredService<ICompanyContext>().Set(opened.CompanyId);
            var identity = check.ServiceProvider.GetRequiredService<IdentityDbContext>();
            Assert.Empty(await identity.RoleAssignments.Where(x => x.CompanyId == opened.CompanyId).ToListAsync());
            Assert.Single(await identity.CompanySetupInboxReceipts.Where(x => x.CompanyId == opened.CompanyId).ToListAsync());
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
            var actor = await EnsureUserAsync(factory, TestUsers.NoAssignment);
            await GrantSetupAuthorityFixtureAsync(factory, actor, CompanySetupGroupOperationV1.AssignFirstAdmin);
            using var client = CreateClient(factory, TestUsers.NoAssignment, opened.CompanyId);
            using var selected = await PostAsync(client, "/api/v1/identity/company-setup/first-admin",
                new AssignFirstCompanyAdminV1(await EnsureUserAsync(factory, TestUsers.CompanyA), 1, "Sentetik açık yönetici seçimi"), NewKey(), NewKey());
            Assert.Equal(HttpStatusCode.Created, selected.StatusCode);
            await using var assigned = factory.Services.CreateAsyncScope();
            var identity = assigned.ServiceProvider.GetRequiredService<IdentityDbContext>();
            Assert.Equal(51944, (await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => identity.Database.ExecuteSqlRawAsync(identityGuard))).Number);
            Assert.Single(await identity.RoleAssignments.Where(x => x.CompanyId == opened.CompanyId).ToListAsync());
            Assert.Single(await identity.OutboxMessages.Where(x => x.CompanyId == opened.CompanyId
                && x.EventType == IdentityCompanyFirstAdminAssignedV1.EventType).ToListAsync());
        });
    }

    [SqlServerFact]
    public async Task Group_opening_is_one_pending_company_outbox_audit_and_global_terminal_with_no_implicit_assignment()
    {
        await WithDatabaseAsync(SyntheticCode("SETUP-ADMIN"), async factory =>
        {
            var actor = await EnsureUserAsync(factory, TestUsers.NoAssignment);
            await GrantSetupAuthorityFixtureAsync(factory, actor, CompanySetupGroupOperationV1.OpenCompany);
            var id = Guid.NewGuid();
            var prefix = Random.Shared.Next(100000000, 900000000).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var vkn = Enumerable.Range(0, 10).Select(digit => prefix + digit).Single(CompanyVknCheckDigit.IsSatisfiedBy);
            var body = new OpenCompanyV1(id, "SYN-" + id.ToString("N"), "SENTETIK açık şirket", vkn, "0", "SENTETIK açık açılış talebi");
            using var client = CreateClient(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA);
            var key = NewKey();
            using var first = await PostAsync(client, "/api/v1/organization/companies", body, key, NewKey());
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            var returned = await first.Content.ReadFromJsonAsync<CompanySetupCompanyV1>(Json);
            Assert.Equal((id, "SetupPending", 1L), (returned!.CompanyId, returned.LifecycleState, returned.Version));
            using var replay = await PostAsync(client, "/api/v1/organization/companies", body, key, NewKey());
            Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
            using var conflict = await PostAsync(client, "/api/v1/organization/companies", body with { CompanyId = Guid.NewGuid() }, key, NewKey());
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            var refusedId = Guid.NewGuid(); var refusedKey = NewKey();
            var refusedBody = body with { CompanyId = refusedId, Reason = "SENTETIK free reason must stay out of refused audit" };
            using var refused = await PostAsync(client, "/api/v1/organization/companies", refusedBody, refusedKey, NewKey());
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            using var refusedReplay = await PostAsync(client, "/api/v1/organization/companies", refusedBody, refusedKey, NewKey());
            Assert.Equal(HttpStatusCode.Conflict, refusedReplay.StatusCode);
            Assert.Equal("true", Assert.Single(refusedReplay.Headers.GetValues("Idempotency-Replayed")));
            await using (var refusedCheck = factory.Services.CreateAsyncScope())
            {
                refusedCheck.ServiceProvider.GetRequiredService<ICompanyContext>().Set(refusedId);
                var audit = await refusedCheck.ServiceProvider.GetRequiredService<AuditDbContext>().AuditEvents
                    .SingleAsync(row => row.CompanyId == refusedId && row.EventType == "organization.company.open.v1");
                Assert.Equal("Refused", audit.Outcome);
                Assert.DoesNotContain(refusedBody.Reason, audit.Reason, StringComparison.Ordinal);
                using var image = JsonDocument.Parse(audit.AfterJson);
                Assert.Equal(["fieldErrors", "httpStatus", "problemCode", "refusalClass"],
                    image.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
                Assert.Equal("organization.company.code_in_use", image.RootElement.GetProperty("problemCode").GetString());
                Assert.Null(audit.ClientIpAddress); Assert.Null(audit.UserAgent);
            }
            await using (var check = factory.Services.CreateAsyncScope())
            {
                check.ServiceProvider.GetRequiredService<ICompanyContext>().Set(id);
                var organization = check.ServiceProvider.GetRequiredService<OrganizationDbContext>();
                Assert.Single(await organization.Companies.Where(company => company.Id == id).ToListAsync());
                Assert.Single(await organization.CompanySetupOutboxMessages.ToListAsync());
                Assert.Single(await organization.CompanyOpeningExecutions.Where(row => row.CompanyId == id).ToListAsync());
                var identity = check.ServiceProvider.GetRequiredService<IdentityDbContext>();
                Assert.Empty(await identity.RoleAssignments.Where(row => row.CompanyId == id).ToListAsync());
                Assert.Empty(await identity.AccessGrantRequests.Where(row => row.CompanyId == id).ToListAsync());
                Assert.Single(await check.ServiceProvider.GetRequiredService<AuditDbContext>().AuditEvents
                    .Where(row => row.CompanyId == id && row.EventType == "organization.company.opened.v1").ToListAsync());
            }
            await using (var revoke = factory.Services.CreateAsyncScope())
            {
                var identity = revoke.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var assignment = await identity.CompanySetupGroupAssignments.SingleAsync(row => row.UserId == actor);
                assignment.Revoke(Guid.NewGuid(), "SENTETIK revoke before replay", DateTimeOffset.UtcNow);
                await identity.SaveChangesAsync();
            }
            using var deniedReplay = await PostAsync(client, "/api/v1/organization/companies", body, key, NewKey());
            Assert.Equal(HttpStatusCode.Forbidden, deniedReplay.StatusCode);
        });
    }

    [SqlServerFact]
    public async Task Concurrent_group_opening_with_one_global_key_commits_one_company_outbox_and_terminal()
    {
        var diagnostic = new OpeningFailureDiagnostic();
        await WithDatabaseAsync(SyntheticCode("SETUP-ADMIN"), async factory =>
        {
            var actor = await EnsureUserAsync(factory, TestUsers.NoAssignment);
            await GrantSetupAuthorityFixtureAsync(factory, actor, CompanySetupGroupOperationV1.OpenCompany);
            var id = Guid.NewGuid();
            var prefix = Random.Shared.Next(100000000, 900000000).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var vkn = Enumerable.Range(0, 10).Select(digit => prefix + digit).Single(CompanyVknCheckDigit.IsSatisfiedBy);
            var body = new OpenCompanyV1(id, "SYN-" + id.ToString("N"), "SENTETIK eşzamanlı açılış", vkn, "0", "SENTETIK aynı istek");
            using var a = CreateClient(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA);
            using var b = CreateClient(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyB);
            var key = NewKey();
            var first = PostAsync(a, "/api/v1/organization/companies", body, key, NewKey());
            var second = PostAsync(b, "/api/v1/organization/companies", body, key, NewKey());
            await Task.WhenAll(first, second);
            using var one = await first; using var two = await second;
            Assert.True(one.StatusCode == HttpStatusCode.Created, $"First opening expected201; status={(int)one.StatusCode}; {diagnostic.Summary}");
            Assert.True(two.StatusCode == HttpStatusCode.Created, $"Second opening expected201; status={(int)two.StatusCode}; {diagnostic.Summary}");
            Assert.Single(new[] { one, two }, response => response.Headers.Contains("Idempotency-Replayed"));
            await using var check = factory.Services.CreateAsyncScope();
            check.ServiceProvider.GetRequiredService<ICompanyContext>().Set(id);
            var organization = check.ServiceProvider.GetRequiredService<OrganizationDbContext>();
            Assert.Single(await organization.Companies.Where(company => company.Id == id).ToListAsync());
            Assert.Single(await organization.CompanySetupOutboxMessages.ToListAsync());
            Assert.Single(await organization.CompanyOpeningExecutions.Where(row => row.CompanyId == id).ToListAsync());
            Assert.Single(await check.ServiceProvider.GetRequiredService<AuditDbContext>().AuditEvents
                .Where(row => row.CompanyId == id && row.EventType == "organization.company.opened.v1").ToListAsync());
        }, services => services.Replace(ServiceDescriptor.Scoped<IOrganizationCompanyOpeningV1>(provider =>
            new ObservedCompanyOpening(ActivatorUtilities.CreateInstance<OrganizationCompanyOpeningV1>(provider), diagnostic))));
    }

    [SqlServerFact]
    public async Task Group_opening_Audit_failure_rolls_back_company_outbox_and_terminal_before_same_key_retry()
    {
        var fault = new AuditOwnerFault();
        await WithDatabaseAsync(SyntheticCode("SETUP-ADMIN"), async factory =>
        {
            var actor = await EnsureUserAsync(factory, TestUsers.NoAssignment);
            await GrantSetupAuthorityFixtureAsync(factory, actor, CompanySetupGroupOperationV1.OpenCompany);
            var id = Guid.NewGuid();
            var prefix = Random.Shared.Next(100000000, 900000000).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var vkn = Enumerable.Range(0, 10).Select(digit => prefix + digit).Single(CompanyVknCheckDigit.IsSatisfiedBy);
            var body = new OpenCompanyV1(id, "SYN-" + id.ToString("N"), "SENTETIK rollback şirketi", vkn, "0", "SENTETIK açık talep");
            using var client = CreateClient(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA);
            var key = NewKey();
            using var failed = await PostAsync(client, "/api/v1/organization/companies", body, key, NewKey());
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
            await using (var check = factory.Services.CreateAsyncScope())
            {
                check.ServiceProvider.GetRequiredService<ICompanyContext>().Set(id);
                var organization = check.ServiceProvider.GetRequiredService<OrganizationDbContext>();
                Assert.Empty(await organization.Companies.Where(company => company.Id == id).ToListAsync());
                Assert.Empty(await organization.CompanySetupOutboxMessages.ToListAsync());
                Assert.Empty(await organization.CompanyOpeningExecutions.Where(row => row.CompanyId == id).ToListAsync());
                Assert.Empty(await check.ServiceProvider.GetRequiredService<AuditDbContext>().AuditEvents.Where(row => row.CompanyId == id).ToListAsync());
            }
            fault.FailAfterAppend = false;
            using var retried = await PostAsync(client, "/api/v1/organization/companies", body, key, NewKey());
            Assert.Equal(HttpStatusCode.Created, retried.StatusCode);
        }, services =>
        {
            var registration = services.Single(descriptor => descriptor.ServiceType == typeof(IAuditTransactionWriterV1));
            services.Remove(registration);
            services.AddScoped<IAuditTransactionWriterV1>(provider => new FaultingAuditOwner(
                (IAuditTransactionWriterV1)ActivatorUtilities.CreateInstance(provider, registration.ImplementationType!), fault));
        });
    }

    [SqlServerFact]
    public async Task Group_authority_revoked_after_the_actual_opening_terminal_lookup_prevents_replay_disclosure()
    {
        var fault = new RevokeAfterOpeningLookup();
        await WithDatabaseAsync(SyntheticCode("SETUP-ADMIN"), async factory =>
        {
            var actor = await EnsureUserAsync(factory, TestUsers.NoAssignment);
            await GrantSetupAuthorityFixtureAsync(factory, actor, CompanySetupGroupOperationV1.OpenCompany);
            var id = Guid.NewGuid();
            var prefix = Random.Shared.Next(100000000, 900000000).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var vkn = Enumerable.Range(0, 10).Select(digit => prefix + digit).Single(CompanyVknCheckDigit.IsSatisfiedBy);
            var body = new OpenCompanyV1(id, "SYN-" + id.ToString("N"), "SENTETIK yarış şirketi", vkn, "0", "SENTETIK talep");
            using var client = CreateClient(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA);
            var key = NewKey();
            using var created = await PostAsync(client, "/api/v1/organization/companies", body, key, NewKey());
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            fault.Revoke = async () =>
            {
                await using var revoke = factory.Services.CreateAsyncScope();
                var identity = revoke.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var assignment = await identity.CompanySetupGroupAssignments.SingleAsync(row => row.UserId == actor);
                assignment.Revoke(Guid.NewGuid(), "SENTETIK revoke after terminal lookup", DateTimeOffset.UtcNow);
                await identity.SaveChangesAsync();
            };
            using var denied = await PostAsync(client, "/api/v1/organization/companies", body, key, NewKey());
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.False(denied.Headers.Contains("Idempotency-Replayed"));
            Assert.Equal(1, fault.LookupCuts);
            await using var check = factory.Services.CreateAsyncScope();
            var organization = check.ServiceProvider.GetRequiredService<OrganizationDbContext>();
            Assert.Single(await organization.CompanyOpeningExecutions.Where(row => row.CompanyId == id).ToListAsync());
        }, services => services.AddDbContext<OrganizationDbContext>((_, options) => options.AddInterceptors(fault)));
    }

    [SqlServerFact]
    public async Task Explicit_setup_migration_Down_guards_pass_empty_and_preserve_group_authority_and_opening_history()
    {
        await WithDatabaseAsync(SyntheticCode("SETUP-ADMIN"), async factory =>
        {
            var identityGuard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(
                new GoymenERP.Modules.Identity.Infrastructure.Persistence.Migrations.AddExplicitCompanySetupAuthority().DownOperations[0]).Sql;
            var organizationGuard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(
                new GoymenERP.Modules.Organization.Infrastructure.Persistence.Migrations.AddAuthorizedCompanyOpening().DownOperations[0]).Sql;
            await using (var empty = factory.Services.CreateAsyncScope())
            {
                var identity = empty.ServiceProvider.GetRequiredService<IdentityDbContext>();
                await identity.Database.ExecuteSqlRawAsync(identityGuard);
                await identity.Database.ExecuteSqlRawAsync(organizationGuard);
            }
            var actor = await EnsureUserAsync(factory, TestUsers.NoAssignment);
            await GrantSetupAuthorityFixtureAsync(factory, actor, CompanySetupGroupOperationV1.OpenCompany);
            var id = Guid.NewGuid();
            var prefix = Random.Shared.Next(100000000, 900000000).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var vkn = Enumerable.Range(0, 10).Select(digit => prefix + digit).Single(CompanyVknCheckDigit.IsSatisfiedBy);
            using var client = CreateClient(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA);
            using var created = await PostAsync(client, "/api/v1/organization/companies",
                new OpenCompanyV1(id, "SYN-" + id.ToString("N"), "SENTETIK korunan şirket", vkn, "0", "SENTETIK talep"), NewKey(), NewKey());
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            await using var check = factory.Services.CreateAsyncScope();
            var identityDb = check.ServiceProvider.GetRequiredService<IdentityDbContext>();
            Assert.Equal(51947, (await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => identityDb.Database.ExecuteSqlRawAsync(identityGuard))).Number);
            Assert.Equal(51948, (await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => identityDb.Database.ExecuteSqlRawAsync(organizationGuard))).Number);
            Assert.Single(await identityDb.CompanySetupGroupAssignments.Where(row => row.UserId == actor).ToListAsync());
            Assert.Single(await check.ServiceProvider.GetRequiredService<OrganizationDbContext>().CompanyOpeningExecutions.Where(row => row.CompanyId == id).ToListAsync());
        });
    }

    private sealed class RevokeAfterOpeningLookup : DbCommandInterceptor
    {
        public Func<Task>? Revoke { get; set; }
        public int LookupCuts { get; private set; }
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Revoke is { } revoke && command.CommandText.Contains("FROM [organization].[CompanyOpeningExecutions]", StringComparison.Ordinal))
            {
                Revoke = null; LookupCuts++;
                await revoke();
            }
            return result;
        }
    }

    // Synthetic owner fixture: explicit SQL tests below use the real group-authorized production opening command as well.
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
    // Observe the real owner before the HTTP boundary hides exceptions; never log raw messages or request data.
    private sealed class OpeningFailureDiagnostic
    {
        private string summary = "opening_failure=not_observed";
        public string Summary => Volatile.Read(ref summary);
        public void Capture(Exception exception)
        {
            var kind = "unknown";
            for (var depth = 0; exception is not null && depth < 8; depth++, exception = exception.InnerException!)
                kind = exception switch
                {
                    SqlException error => error.Number switch
                    {
                        1205 => "sql_deadlock", -2 => "sql_timeout", 547 => "sql_constraint",
                        2601 or 2627 => "sql_unique", 468 => "sql_collation", _ => "sql_other",
                    },
                    DbUpdateException => "db_update", UnauthorizedAccessException => "denied",
                    ArgumentException => "argument", InvalidOperationException => "invalid_operation", _ => kind,
                };
            Volatile.Write(ref summary, "opening_failure=" + kind);
        }
    }

    private sealed class ObservedCompanyOpening(OrganizationCompanyOpeningV1 inner, OpeningFailureDiagnostic diagnostic)
        : IOrganizationCompanyOpeningV1
    {
        public Task<CompanySetupCapabilitiesV1> ReadCapabilitiesAsync(CancellationToken cancellationToken = default) =>
            inner.ReadCapabilitiesAsync(cancellationToken);
        public Task<IReadOnlyList<CompanySetupCompanyV1>> ReadPendingAsync(CancellationToken cancellationToken = default) =>
            inner.ReadPendingAsync(cancellationToken);
        public async Task<CompanyOpeningResultV1> OpenAsync(OpenCompanyV1 request, CompanyOpeningMetadataV1 metadata,
            CancellationToken cancellationToken = default)
        {
            try { return await inner.OpenAsync(request, metadata, cancellationToken); }
            catch (Exception exception) { diagnostic.Capture(exception); throw; }
        }
    }

    private static async Task GrantSetupAuthorityFixtureAsync(SqlServerRelationalApiFactory factory, Guid user,
        CompanySetupGroupOperationV1 operation)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        db.CompanySetupGroupAssignments.Add(new CompanySetupGroupAssignment(Guid.NewGuid(), user, (int)operation,
            Guid.NewGuid(), "SENTETIK KIM-D56 disposable SQL fixture", DateTimeOffset.UtcNow.AddDays(-1), null));
        await db.SaveChangesAsync();
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
