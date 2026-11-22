using System.Net;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class IdentityAccessAssignmentSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Managed_user_list_hides_users_outside_the_context_company_or_outside_the_actor_management_scope()
    {
        await WithDatabaseAsync(SyntheticCode("ADMIN"), async factory =>
        {
            var manager = await CreateRoleAsync(factory, SyntheticCode("MANAGER"), ErpPermissions.IdentityUsersManage);
            var plain = await CreateRoleAsync(factory, SyntheticCode("PLAIN"), ErpPermissions.IdentityProfileRead);
            var actor = await EnsureUserAsync(factory, TestUsers.NoAssignment);
            await AssignDirectlyAsync(factory, actor, manager, TestCompanyIds.CompanyA);
            using var client = CreateClient(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA);
            async Task<Guid> CreateAsync(string label)
            {
                var id = Guid.NewGuid();
                using var created = await PostAsync(client, "/api/v1/identity/access/users", new CreateLocalUserV1(id, "0",
                    $"Sentetik {label}", $"synthetic-{label}-{id:N}", "SentetikOnly!Temporary.5172", "Sentetik oluşturma"), NewKey(), NewKey());
                Assert.Equal(HttpStatusCode.Created, created.StatusCode);
                return id;
            }
            var onlyA = await CreateAsync("only-a");
            var onlyB = await CreateAsync("only-b");
            var bothAandB = await CreateAsync("a-and-b");
            var expiredB = await CreateAsync("expired-b");
            var neverAssigned = await CreateAsync("never-assigned");
            await AssignDirectlyAsync(factory, onlyA, plain, TestCompanyIds.CompanyA);
            await AssignDirectlyAsync(factory, onlyB, plain, TestCompanyIds.CompanyB);
            await AssignDirectlyAsync(factory, bothAandB, plain, TestCompanyIds.CompanyA);
            await AssignDirectlyAsync(factory, bothAandB, plain, TestCompanyIds.CompanyB);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var expired = new GoymenERP.Modules.Identity.Domain.Assignments.RoleAssignment(Guid.NewGuid(), expiredB, plain,
                    TestCompanyIds.CompanyB, DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-1), approvedByUserId: null);
                expired.AddScope(GoymenERP.Modules.Identity.Domain.Assignments.AccessScopeType.Company, TestCompanyIds.CompanyB);
                identity.RoleAssignments.Add(expired);
                await identity.SaveChangesAsync();
            }

            async Task<Guid[]> ListAsync()
            {
                using var response = await client.GetAsync("/api/v1/identity/access/managed-users");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                return body.RootElement.EnumerateArray().Select(user => user.GetProperty("userId").GetGuid()).ToArray();
            }

            var listed = await ListAsync();
            Assert.Contains(onlyA, listed);
            Assert.Contains(neverAssigned, listed);
            Assert.DoesNotContain(onlyB, listed);
            Assert.DoesNotContain(expiredB, listed);
            Assert.DoesNotContain(bothAandB, listed);

            await AssignDirectlyAsync(factory, actor, manager, TestCompanyIds.CompanyB);
            listed = await ListAsync();
            Assert.Contains(bothAandB, listed);
            Assert.DoesNotContain(onlyB, listed);
            Assert.DoesNotContain(expiredB, listed);
        });
    }

    [SqlServerFact]
    public async Task User_management_requires_every_membership_company_and_replays_without_applying_a_second_password()
    {
        await WithDatabaseAsync(SyntheticCode("ADMIN"), async factory =>
        {
            var role = await CreateRoleAsync(factory, SyntheticCode("MANAGER"), ErpPermissions.IdentityUsersManage);
            var actor = await EnsureUserAsync(factory, TestUsers.NoAssignment);
            await AssignDirectlyAsync(factory, actor, role, TestCompanyIds.CompanyA);
            using var client = CreateClient(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA);
            var target = Guid.NewGuid();
            var request = new CreateLocalUserV1(target, "0", "Sentetik yeni kullanıcı", $"synthetic-{target:N}",
                "SentetikOnly!Temporary.8291", "Sentetik oluşturma");
            var creationKey = NewKey();
            using (var created = await PostAsync(client, "/api/v1/identity/access/users", request, creationKey, NewKey()))
                Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            using (var changedUserName = await PostAsync(client, "/api/v1/identity/access/users", request with { UserName = $"different-{target:N}" }, creationKey, NewKey()))
                await AssertProblemAsync(changedUserName, HttpStatusCode.Conflict, IdentityAccessProblemCodesV1.IdempotencyConflict);
            var reset = new ManageLocalUserV1("1", "SentetikOnly!Reset.7384", "Sentetik sıfırlama");
            var resetRoute = $"/api/v1/identity/access/users/{target}/password-reset";
            using (var noMembership = await PostAsync(client, resetRoute, reset, NewKey(), NewKey()))
                await AssertProblemAsync(noMembership, HttpStatusCode.Conflict, IdentityUserManagementV1.AssignmentRequired);
            await AssignDirectlyAsync(factory, target, role, TestCompanyIds.CompanyA);
            await AssignDirectlyAsync(factory, target, role, TestCompanyIds.CompanyB);
            using (var missingCompany = await PostAsync(client, resetRoute, reset, NewKey(), NewKey()))
                await AssertProblemAsync(missingCompany, HttpStatusCode.Forbidden, GoymenERP.Api.Errors.ErpProblemCodes.Forbidden);
            await AssignDirectlyAsync(factory, actor, role, TestCompanyIds.CompanyB);
            var key = NewKey();
            using (var applied = await PostAsync(client, resetRoute, reset, key, NewKey()))
            {
                Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
                Assert.Equal("2", (await ReadOutcomeAsync(applied)).Version);
            }
            string hash;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var credential = await db.LocalCredentials.AsNoTracking().SingleAsync(x => x.UserId == target);
                hash = credential.PasswordHash;
                Assert.True(credential.MustChangePassword);
                Assert.Null(credential.TemporaryPasswordIssuedAtUtc);
            }
            using (var replay = await PostAsync(client, resetRoute, reset with { Password = "DifferentSynthetic!NotApplied.71" }, key, NewKey()))
            {
                Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
                Assert.Equal("true", replay.Headers.GetValues(ReplayedHeader).Single());
            }
            await using var verify = factory.Services.CreateAsyncScope();
            var identity = verify.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var after = await identity.LocalCredentials.AsNoTracking().SingleAsync(x => x.UserId == target);
            Assert.Equal(hash, after.PasswordHash);
            Assert.Equal(2, after.Version);
            Assert.DoesNotContain(request.Password, JsonSerializer.Serialize(await identity.CommandExecutions.AsNoTracking().ToListAsync()));
            Assert.DoesNotContain(reset.Password!, JsonSerializer.Serialize(await identity.CommandExecutions.AsNoTracking().ToListAsync()));
        });
    }

    [SqlServerFact]
    public async Task Deactivation_refuses_the_last_admin_in_any_membership_company_and_leaves_the_credential_unchanged()
    {
        var adminCode = SyntheticCode("ADMIN");
        await WithDatabaseAsync(adminCode, async factory =>
        {
            var admin = await CreateRoleAsync(factory, adminCode, ErpPermissions.IdentityUsersManage);
            var manager = await CreateRoleAsync(factory, SyntheticCode("MANAGER"), ErpPermissions.IdentityUsersManage);
            var actor = await EnsureUserAsync(factory, TestUsers.NoAssignment);
            await AssignDirectlyAsync(factory, actor, admin, TestCompanyIds.CompanyA);
            await AssignDirectlyAsync(factory, actor, manager, TestCompanyIds.CompanyB);
            using var client = CreateClient(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA);
            var id = Guid.NewGuid();
            using (var created = await PostAsync(client, "/api/v1/identity/access/users", new CreateLocalUserV1(id, "0", "Sentetik yönetici", $"synthetic-{id:N}", "SyntheticOnly!Temporary.4829", "Sentetik"), NewKey(), NewKey()))
                Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            await AssignDirectlyAsync(factory, id, admin, TestCompanyIds.CompanyA);
            await AssignDirectlyAsync(factory, id, admin, TestCompanyIds.CompanyB);
            using (var lastAdmin = await PostAsync(client, $"/api/v1/identity/access/users/{id}/deactivate", new ManageLocalUserV1("1", null, "Sentetik pasifleştirme"), NewKey(), NewKey()))
                await AssertProblemAsync(lastAdmin, HttpStatusCode.Conflict, IdentityAccessProblemCodesV1.LastAdminRequired);
            await using (var unchanged = factory.Services.CreateAsyncScope())
            {
                var dbBefore = unchanged.ServiceProvider.GetRequiredService<IdentityDbContext>();
                Assert.True((await dbBefore.Users.AsNoTracking().SingleAsync(x => x.Id == id)).IsActive);
                Assert.Equal(1, (await dbBefore.LocalCredentials.AsNoTracking().SingleAsync(x => x.UserId == id)).Version);
            }
            await AssignDirectlyAsync(factory, actor, admin, TestCompanyIds.CompanyB);
            // With another enduring Admin in both companies, the same state change is now safe under both locks.
            using var response = await PostAsync(client, $"/api/v1/identity/access/users/{id}/deactivate", new ManageLocalUserV1("1", null, "Sentetik pasifleştirme"), NewKey(), NewKey());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            Assert.False((await db.Users.AsNoTracking().SingleAsync(x => x.Id == id)).IsActive);
            Assert.Equal(2, (await db.LocalCredentials.AsNoTracking().SingleAsync(x => x.UserId == id)).Version);
            Assert.True(await db.RoleAssignments.AsNoTracking().AnyAsync(x => x.UserId == id));
        });
    }
}
