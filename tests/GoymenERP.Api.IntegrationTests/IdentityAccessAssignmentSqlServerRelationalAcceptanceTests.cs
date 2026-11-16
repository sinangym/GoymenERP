using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Api.Contracts;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Approvals;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// ID-3 (spec section ID-3; KIM-D1, KIM-D5, KIM-D15, KIM-D17, KIM-D19; BS-6) through the real host on a real SQL
/// Server: one transaction per change (row + command record + owner audit + outbox), the stored answer replays with the original
/// correlation, a Critical role needs a second person, two approvals of one request leave one assignment, the
/// company's last effective Admin stays, and the migration Down refuses while workflow data would be lost.
/// <para>
/// Every role, grant and Admin role code here is synthetic and lives only in this test's own database; none is a Sinan
/// or production grant, and none is seeded. SENTETIK / NOT_ENV.
/// </para>
/// </summary>
public sealed partial class IdentityAccessAssignmentSqlServerRelationalAcceptanceTests
{
    private const string AssignmentsRoute = "/api/v1/identity/access/assignments";
    private const string GrantRequestsRoute = "/api/v1/identity/access/grant-requests";
    private const string ReplayedHeader = "Idempotency-Replayed";
    private const string OriginalCorrelationHeader = "Idempotency-Original-Correlation-Id";
    private const int DownGuardErrorNumber = 51484;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [SqlServerFact]
    public async Task A_non_critical_role_is_assigned_and_revoked_in_one_save_each_and_the_stored_answer_replays()
    {
        var adminRoleCode = SyntheticCode("ADMIN");
        await WithDatabaseAsync(adminRoleCode, async factory =>
        {
            var managerRole = await CreateRoleAsync(factory, adminRoleCode,
                IdentityPermissionManifestV1.RolesManage);
            var manager = await EnsureUserAsync(factory, TestUsers.NoAssignment);
            await AssignDirectlyAsync(factory, manager, managerRole, TestCompanyIds.CompanyA);
            var target = await EnsureUserAsync(factory, TestUsers.CompanyA);
            var operatorRole = await CreateRoleAsync(factory, SyntheticCode("OPERATOR"),
                ErpPermissions.OrganizationLocationsStatusUpdate);
            Assert.False(ErpPermissions.IsCritical(ErpPermissions.OrganizationLocationsStatusUpdate));
            using var client = CreateClient(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA);

            var grant = new AssignIdentityRoleRequestV1(target, operatorRole, null, null, "Sentetik atama gerekçesi");
            var assignKey = NewKey();
            var firstCorrelation = NewKey();
            Outcome assigned;
            using (var response = await PostAsync(client, AssignmentsRoute, grant, assignKey, firstCorrelation))
            {
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                Assert.False(response.Headers.Contains(ReplayedHeader));
                assigned = await ReadOutcomeAsync(response);
            }

            Assert.Equal("1", assigned.Version);
            Assert.Equal(nameof(RoleAssignmentStatus.Active), assigned.Status);
            Assert.Equal(assigned.Id, assigned.RoleAssignmentId);

            var afterAssign = await ReadAssignmentEffectsAsync(factory, assigned.Id);
            Assert.Equal(new AssignmentEffects(1, 1, 1, 0, 1, 0), afterAssign);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var row = await identity.RoleAssignments.IgnoreQueryFilters().AsNoTracking()
                    .SingleAsync(x => x.Id == assigned.Id);
                Assert.Equal(TestCompanyIds.CompanyA, row.CompanyId);
                Assert.Equal(target, row.UserId);
                Assert.Equal(operatorRole, row.RoleId);
                Assert.Null(row.ApprovedByUserId);
                var message = await identity.OutboxMessages.IgnoreQueryFilters().AsNoTracking()
                    .SingleAsync(x => x.AggregateId == assigned.Id);
                Assert.Equal(IdentityPermissionChangedV1.EventType, message.EventType);
                Assert.Equal(TestCompanyIds.CompanyA, message.CompanyId);
                Assert.Equal(firstCorrelation, message.CorrelationId);
                var payload = JsonSerializer.Deserialize<IdentityPermissionChangedV1>(message.PayloadJson, Json)!;
                Assert.Equal(target, payload.UserId);
                Assert.Equal(manager, payload.ActorUserId);
                Assert.Equal(ErpPermissions.OrganizationLocationsStatusUpdate, payload.Permission);
                Assert.Equal(IdentityPermissionChangedV1.Granted, payload.Change);
                Assert.Null(payload.RequestId);
            }

            // The same key and body: the stored 201, the original correlation, and nothing written again.
            using (var replay = await PostAsync(client, AssignmentsRoute, grant, assignKey, NewKey()))
            {
                Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
                Assert.Equal("true", Assert.Single(replay.Headers.GetValues(ReplayedHeader)));
                Assert.Equal(firstCorrelation, Assert.Single(replay.Headers.GetValues(OriginalCorrelationHeader)));
                Assert.Equal(assigned, await ReadOutcomeAsync(replay));
            }

            Assert.Equal(afterAssign, await ReadAssignmentEffectsAsync(factory, assigned.Id));

            // The same key with another body is a conflict, never the stored answer.
            var otherBody = grant with { Reason = "Başka gerekçe" };
            using (var conflict = await PostAsync(client, AssignmentsRoute, otherBody, assignKey, NewKey()))
            {
                await AssertProblemAsync(conflict, HttpStatusCode.Conflict, IdentityAccessProblemCodesV1.IdempotencyConflict);
            }

            // A second active assignment of the same role is refused, the first stays alone.
            using (var duplicate = await PostAsync(client, AssignmentsRoute, grant, NewKey(), NewKey()))
            {
                await AssertProblemAsync(duplicate, HttpStatusCode.Conflict,
                    IdentityAccessProblemCodesV1.DuplicateActiveAssignment);
            }

            var revokeRoute = $"{AssignmentsRoute}/{assigned.Id}/revoke";
            var revokeKey = NewKey();
            var revokeCorrelation = NewKey();
            Outcome revoked;
            using (var response = await PostAsync(client, revokeRoute,
                       new RevokeIdentityRoleAssignmentRequestV1("1", "Sentetik geri alma"), revokeKey, revokeCorrelation))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                revoked = await ReadOutcomeAsync(response);
            }

            Assert.Equal(new Outcome(assigned.Id, "2", nameof(RoleAssignmentStatus.Revoked), assigned.Id), revoked);
            Assert.Equal(new AssignmentEffects(1, 2, 2, 0, 2, 1), await ReadAssignmentEffectsAsync(factory, assigned.Id));
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var row = await identity.RoleAssignments.IgnoreQueryFilters().AsNoTracking()
                    .SingleAsync(x => x.Id == assigned.Id);
                Assert.Equal(RoleAssignmentStatus.Revoked, row.Status);
                Assert.Equal(manager, row.RevokedByUserId);
                Assert.NotNull(row.RevokedAtUtc);
                Assert.Equal(2, row.Version);
                var revocation = await identity.OutboxMessages.IgnoreQueryFilters().AsNoTracking()
                    .SingleAsync(x => x.AggregateId == assigned.Id && x.CorrelationId == revokeCorrelation);
                var payload = JsonSerializer.Deserialize<IdentityPermissionChangedV1>(revocation.PayloadJson, Json)!;
                Assert.Equal(IdentityPermissionChangedV1.Revoked, payload.Change);
                Assert.Equal(2, revocation.AggregateVersion);
            }

            // The revoke key replays its 200; a stale version and a second revocation are recorded refusals.
            using (var replay = await PostAsync(client, revokeRoute,
                       new RevokeIdentityRoleAssignmentRequestV1("1", "Sentetik geri alma"), revokeKey, NewKey()))
            {
                Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
                Assert.Equal(revokeCorrelation, Assert.Single(replay.Headers.GetValues(OriginalCorrelationHeader)));
            }

            using (var stale = await PostAsync(client, revokeRoute,
                       new RevokeIdentityRoleAssignmentRequestV1("1", null), NewKey(), NewKey()))
            {
                await AssertProblemAsync(stale, HttpStatusCode.Conflict, IdentityAccessProblemCodesV1.VersionConflict);
            }

            using (var again = await PostAsync(client, revokeRoute,
                       new RevokeIdentityRoleAssignmentRequestV1("2", null), NewKey(), NewKey()))
            {
                await AssertProblemAsync(again, HttpStatusCode.Conflict, IdentityAccessProblemCodesV1.AssignmentNotActive);
            }

            // Another company's assignment id is one code-less 404 from company B's administrator.
            await AssignDirectlyAsync(factory, manager, managerRole, TestCompanyIds.CompanyB);
            using var otherCompany = CreateClient(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyB);
            using (var foreign = await PostAsync(otherCompany, revokeRoute,
                       new RevokeIdentityRoleAssignmentRequestV1("2", null), NewKey(), NewKey()))
            {
                await AssertCodeLessAsync(foreign, HttpStatusCode.NotFound);
            }

            Assert.Equal(2, (await ReadAssignmentEffectsAsync(factory, assigned.Id)).AssignmentVersion);
        });
    }

    [SqlServerFact]
    public async Task A_critical_role_is_assigned_only_after_a_second_person_who_is_neither_the_requester_nor_the_target_approves()
    {
        var adminRoleCode = SyntheticCode("ADMIN");
        await WithDatabaseAsync(adminRoleCode, async factory =>
        {
            // The requester holds both permissions, so only the two-person rule can refuse their own approval.
            var managerRole = await CreateRoleAsync(factory, adminRoleCode,
                IdentityPermissionManifestV1.RolesManage, IdentityPermissionManifestV1.AccessApprove);
            var approverRole = await CreateRoleAsync(factory, SyntheticCode("APPROVER"),
                IdentityPermissionManifestV1.AccessApprove);
            var criticalRole = await CreateRoleAsync(factory, SyntheticCode("KRITIK"),
                IdentityPermissionManifestV1.AssignmentsRead);
            Assert.True(ErpPermissions.IsCritical(IdentityPermissionManifestV1.AssignmentsRead));
            var requester = await EnsureUserAsync(factory, TestUsers.NoAssignment);
            var approver = await EnsureUserAsync(factory, TestUsers.Unprovisioned);
            var target = await EnsureUserAsync(factory, TestUsers.CompanyA);
            await AssignDirectlyAsync(factory, requester, managerRole, TestCompanyIds.CompanyA);
            await AssignDirectlyAsync(factory, approver, approverRole, TestCompanyIds.CompanyA);
            using var requesterClient = CreateClient(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA);
            using var approverClient = CreateClient(factory, TestUsers.Unprovisioned, TestCompanyIds.CompanyA);

            // One step is refused for a Critical role and nothing is assigned.
            var direct = new AssignIdentityRoleRequestV1(target, criticalRole, null, null, null);
            using (var refused = await PostAsync(requesterClient, AssignmentsRoute, direct, NewKey(), NewKey()))
            {
                await AssertProblemAsync(refused, HttpStatusCode.Conflict,
                    IdentityAccessProblemCodesV1.SecondApprovalRequired);
            }

            Assert.Equal(0, await CountAssignmentsAsync(factory, target, criticalRole));

            var open = new CreateAccessGrantRequestRequestV1(target, criticalRole, null, null, "Sentetik kritik talep");
            Outcome pending;
            using (var response = await PostAsync(requesterClient, GrantRequestsRoute, open, NewKey(), NewKey()))
            {
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                pending = await ReadOutcomeAsync(response);
            }

            Assert.Equal(new Outcome(pending.Id, "1", nameof(AccessGrantRequestStatus.Pending), null), pending);
            Assert.Equal(0, await CountAssignmentsAsync(factory, target, criticalRole));

            // A second open request for the same user and role is refused.
            using (var duplicate = await PostAsync(requesterClient, GrantRequestsRoute, open, NewKey(), NewKey()))
            {
                await AssertProblemAsync(duplicate, HttpStatusCode.Conflict,
                    IdentityAccessProblemCodesV1.DuplicatePendingRequest);
            }

            // The approval queue shows it to the approver, oldest first, without anything external.
            using (var queue = await approverClient.GetAsync($"{GrantRequestsRoute}?status=pending"))
            {
                Assert.Equal(HttpStatusCode.OK, queue.StatusCode);
                using var body = JsonDocument.Parse(await queue.Content.ReadAsStringAsync());
                var item = Assert.Single(body.RootElement.EnumerateArray().ToArray());
                Assert.Equal(pending.Id, item.GetProperty("requestId").GetGuid());
                Assert.Equal(requester, item.GetProperty("requestedBy").GetProperty("userId").GetGuid());
                Assert.Equal(target, item.GetProperty("target").GetProperty("userId").GetGuid());
                Assert.Equal("1", item.GetProperty("version").GetString());
            }

            // The requester's own approval is refused and recorded; the request stays pending.
            var approveRoute = $"{GrantRequestsRoute}/{pending.Id}/approve";
            using (var self = await PostAsync(requesterClient, approveRoute, new ApproveAccessGrantRequestV1("1"),
                       NewKey(), NewKey()))
            {
                await AssertProblemAsync(self, HttpStatusCode.Conflict, IdentityAccessProblemCodesV1.SelfApprovalRefused);
            }

            Assert.Equal(AccessGrantRequestStatus.Pending, (await ReadRequestAsync(factory, pending.Id)).Status);

            var approveCorrelation = NewKey();
            Outcome approved;
            using (var response = await PostAsync(approverClient, approveRoute, new ApproveAccessGrantRequestV1("1"),
                       NewKey(), approveCorrelation))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                approved = await ReadOutcomeAsync(response);
            }

            Assert.Equal(pending.Id, approved.Id);
            Assert.Equal("2", approved.Version);
            Assert.Equal(nameof(AccessGrantRequestStatus.Approved), approved.Status);
            var assignmentId = Assert.IsType<Guid>(approved.RoleAssignmentId);

            var decided = await ReadRequestAsync(factory, pending.Id);
            Assert.Equal(AccessGrantRequestStatus.Approved, decided.Status);
            Assert.Equal(AccessGrantApprovalMode.SecondApproval, decided.ApprovalMode);
            Assert.Equal(approver, decided.DecidedByUserId);
            Assert.Equal(assignmentId, decided.ResultingRoleAssignmentId);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var assignment = await identity.RoleAssignments.IgnoreQueryFilters().AsNoTracking()
                    .SingleAsync(x => x.Id == assignmentId);
                Assert.Equal(target, assignment.UserId);
                Assert.Equal(criticalRole, assignment.RoleId);
                Assert.Equal(TestCompanyIds.CompanyA, assignment.CompanyId);
                Assert.Equal(approver, assignment.ApprovedByUserId);
                var message = await identity.OutboxMessages.IgnoreQueryFilters().AsNoTracking()
                    .SingleAsync(x => x.AggregateId == assignmentId);
                Assert.Equal(approveCorrelation, message.CorrelationId);
                var payload = JsonSerializer.Deserialize<IdentityPermissionChangedV1>(message.PayloadJson, Json)!;
                Assert.Equal(pending.Id, payload.RequestId);
                Assert.Equal(approver, payload.ActorUserId);
                Assert.Equal(IdentityPermissionChangedV1.Granted, payload.Change);

                var audit = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
                var requestText = pending.Id.ToString("D");
                var assignmentText = assignmentId.ToString("D");
                Assert.Equal(1, await audit.AuditEvents.IgnoreQueryFilters().CountAsync(
                    x => x.EntityId == requestText && x.CorrelationId == approveCorrelation
                        && x.EventType == IdentityAccessAssignmentService.RequestApprovedEvent));
                Assert.Equal(1, await audit.AuditEvents.IgnoreQueryFilters().CountAsync(
                    x => x.EntityId == assignmentText && x.CorrelationId == approveCorrelation
                        && x.EventType == IdentityAccessAssignmentService.AssignmentGrantedEvent));
            }

            // A decided request is not decided again.
            using (var again = await PostAsync(approverClient, approveRoute, new ApproveAccessGrantRequestV1("2"),
                       NewKey(), NewKey()))
            {
                await AssertProblemAsync(again, HttpStatusCode.Conflict, IdentityAccessProblemCodesV1.RequestNotPending);
            }

            Assert.Equal(1, await CountAssignmentsAsync(factory, target, criticalRole));
        });
    }

    [SqlServerFact]
    public async Task Two_concurrent_approvals_of_one_request_leave_exactly_one_assignment()
    {
        var adminRoleCode = SyntheticCode("ADMIN");
        await WithDatabaseAsync(adminRoleCode, async factory =>
        {
            var managerRole = await CreateRoleAsync(factory, adminRoleCode, IdentityPermissionManifestV1.RolesManage);
            var approverRole = await CreateRoleAsync(factory, SyntheticCode("APPROVER"),
                IdentityPermissionManifestV1.AccessApprove);
            var criticalRole = await CreateRoleAsync(factory, SyntheticCode("KRITIK"),
                IdentityPermissionManifestV1.AssignmentsRead);
            var requester = await EnsureUserAsync(factory, TestUsers.NoAssignment);
            var target = await EnsureUserAsync(factory, TestUsers.CompanyA);
            await AssignDirectlyAsync(factory, requester, managerRole, TestCompanyIds.CompanyA);
            await AssignDirectlyAsync(factory, await EnsureUserAsync(factory, TestUsers.Unprovisioned), approverRole,
                TestCompanyIds.CompanyA);
            await AssignDirectlyAsync(factory, await EnsureUserAsync(factory, TestUsers.CompanyB), approverRole,
                TestCompanyIds.CompanyA);
            using var requesterClient = CreateClient(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA);
            using var firstApprover = CreateClient(factory, TestUsers.Unprovisioned, TestCompanyIds.CompanyA);
            using var secondApprover = CreateClient(factory, TestUsers.CompanyB, TestCompanyIds.CompanyA);

            Outcome pending;
            using (var response = await PostAsync(requesterClient, GrantRequestsRoute,
                       new CreateAccessGrantRequestRequestV1(target, criticalRole, null, null, null), NewKey(), NewKey()))
            {
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                pending = await ReadOutcomeAsync(response);
            }

            var approveRoute = $"{GrantRequestsRoute}/{pending.Id}/approve";
            var responses = await Task.WhenAll(
                PostAsync(firstApprover, approveRoute, new ApproveAccessGrantRequestV1("1"), NewKey(), NewKey()),
                PostAsync(secondApprover, approveRoute, new ApproveAccessGrantRequestV1("1"), NewKey(), NewKey()));
            try
            {
                Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
                var loser = Assert.Single(responses, response => response.StatusCode != HttpStatusCode.OK);
                Assert.Equal(HttpStatusCode.Conflict, loser.StatusCode);
                using var body = JsonDocument.Parse(await loser.Content.ReadAsStringAsync());
                Assert.Contains(body.RootElement.GetProperty("code").GetString(), new[]
                {
                    IdentityAccessProblemCodesV1.VersionConflict,
                    IdentityAccessProblemCodesV1.RequestNotPending,
                });
            }
            finally
            {
                foreach (var response in responses)
                {
                    response.Dispose();
                }
            }

            Assert.Equal(1, await CountAssignmentsAsync(factory, target, criticalRole));
            var decided = await ReadRequestAsync(factory, pending.Id);
            Assert.Equal(AccessGrantRequestStatus.Approved, decided.Status);
            Assert.Equal(2, decided.Version);
        });
    }

    [SqlServerFact]
    public async Task The_last_effective_admin_of_a_company_cannot_be_revoked_even_by_two_admins_at_once()
    {
        var adminRoleCode = SyntheticCode("ADMIN");
        await WithDatabaseAsync(adminRoleCode, async factory =>
        {
            var adminRole = await CreateRoleAsync(factory, adminRoleCode, IdentityPermissionManifestV1.RolesManage);
            var first = await EnsureUserAsync(factory, TestUsers.NoAssignment);
            var second = await EnsureUserAsync(factory, TestUsers.Unprovisioned);
            var third = await EnsureUserAsync(factory, TestUsers.CompanyB);
            var firstAssignment = await AssignDirectlyAsync(factory, first, adminRole, TestCompanyIds.CompanyA);
            var secondAssignment = await AssignDirectlyAsync(factory, second, adminRole, TestCompanyIds.CompanyA);
            var thirdAssignment = await AssignDirectlyAsync(factory, third, adminRole, TestCompanyIds.CompanyA);
            // The same person holds the Admin role in company B too; it never counts for company A.
            await AssignDirectlyAsync(factory, first, adminRole, TestCompanyIds.CompanyB);
            using var firstClient = CreateClient(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA);
            using var secondClient = CreateClient(factory, TestUsers.Unprovisioned, TestCompanyIds.CompanyA);

            // Three Admins: one revocation goes through.
            using (var response = await PostAsync(firstClient, $"{AssignmentsRoute}/{thirdAssignment}/revoke",
                       new RevokeIdentityRoleAssignmentRequestV1("1", null), NewKey(), NewKey()))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            // Two Admins revoke each other at the same moment: the company lock lets exactly one through.
            var responses = await Task.WhenAll(
                PostAsync(firstClient, $"{AssignmentsRoute}/{secondAssignment}/revoke",
                    new RevokeIdentityRoleAssignmentRequestV1("1", null), NewKey(), NewKey()),
                PostAsync(secondClient, $"{AssignmentsRoute}/{firstAssignment}/revoke",
                    new RevokeIdentityRoleAssignmentRequestV1("1", null), NewKey(), NewKey()));
            try
            {
                Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
                // The loser either lost its own authority (code-less 403) or met the last Admin rule.
                var loser = Assert.Single(responses, response => response.StatusCode != HttpStatusCode.OK);
                Assert.Contains(loser.StatusCode, new[] { HttpStatusCode.Forbidden, HttpStatusCode.Conflict });
                if (loser.StatusCode == HttpStatusCode.Conflict)
                {
                    using var body = JsonDocument.Parse(await loser.Content.ReadAsStringAsync());
                    Assert.Equal(IdentityAccessProblemCodesV1.LastAdminRequired, body.RootElement.GetProperty("code").GetString());
                }
            }
            finally
            {
                foreach (var response in responses)
                {
                    response.Dispose();
                }
            }

            Guid survivor;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var active = await identity.RoleAssignments.IgnoreQueryFilters().AsNoTracking()
                    .Where(x => x.CompanyId == TestCompanyIds.CompanyA && x.RoleId == adminRole
                        && x.Status == RoleAssignmentStatus.Active)
                    .ToListAsync();
                var only = Assert.Single(active);
                survivor = only.UserId;
            }

            // The survivor revoking their own, last Admin assignment is refused and it stays active.
            var survivorAssignment = survivor == first ? firstAssignment : secondAssignment;
            using var survivorClient = CreateClient(factory,
                survivor == first ? TestUsers.NoAssignment : TestUsers.Unprovisioned, TestCompanyIds.CompanyA);
            using (var last = await PostAsync(survivorClient, $"{AssignmentsRoute}/{survivorAssignment}/revoke",
                       new RevokeIdentityRoleAssignmentRequestV1("1", null), NewKey(), NewKey()))
            {
                await AssertProblemAsync(last, HttpStatusCode.Conflict, IdentityAccessProblemCodesV1.LastAdminRequired);
            }

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var row = await identity.RoleAssignments.IgnoreQueryFilters().AsNoTracking()
                    .SingleAsync(x => x.Id == survivorAssignment);
                Assert.Equal(RoleAssignmentStatus.Active, row.Status);
                Assert.Equal(1, row.Version);
            }
        });
    }

    [SqlServerFact]
    public async Task The_down_guard_refuses_while_workflow_data_would_be_lost_on_real_SQL()
    {
        var adminRoleCode = SyntheticCode("ADMIN");
        await WithDatabaseAsync(adminRoleCode, async factory =>
        {
            var guard = Assert.IsType<SqlOperation>(new AddAccessGrantRequestWorkflow().DownOperations[0]).Sql;
            await using var scope = factory.Services.CreateAsyncScope();
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            // Nothing of the workflow yet: the guard lets Down through.
            await identity.Database.ExecuteSqlRawAsync(guard);

            var role = await CreateRoleAsync(factory, SyntheticCode("OPERATOR"),
                ErpPermissions.OrganizationLocationsStatusUpdate);
            var holder = await EnsureUserAsync(factory, TestUsers.CompanyA);
            var actor = await EnsureUserAsync(factory, TestUsers.NoAssignment);

            // A withdrawn request is a state the old schema cannot hold.
            var requestId = Guid.NewGuid();
            await using (var writeScope = factory.Services.CreateAsyncScope())
            {
                var write = writeScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var request = new AccessGrantRequest(requestId, actor, holder, role, TestCompanyIds.CompanyA, true);
                request.Withdraw(actor, DateTimeOffset.UtcNow);
                write.AccessGrantRequests.Add(request);
                await write.SaveChangesAsync();
            }

            var withdrawn = await Assert.ThrowsAsync<SqlException>(() => identity.Database.ExecuteSqlRawAsync(guard));
            Assert.Equal(DownGuardErrorNumber, withdrawn.Number);
            Assert.Equal(1, await identity.AccessGrantRequests.IgnoreQueryFilters().CountAsync(x => x.Id == requestId));

            // Without it the guard passes again; a revoked assignment carries who and when, which Down would drop.
            await identity.AccessGrantRequests.IgnoreQueryFilters().Where(x => x.Id == requestId).ExecuteDeleteAsync();
            await identity.Database.ExecuteSqlRawAsync(guard);
            var assignmentId = await AssignDirectlyAsync(factory, holder, role, TestCompanyIds.CompanyA);
            await using (var writeScope = factory.Services.CreateAsyncScope())
            {
                var write = writeScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var assignment = await write.RoleAssignments.IgnoreQueryFilters().SingleAsync(x => x.Id == assignmentId);
                assignment.Revoke(actor, DateTimeOffset.UtcNow);
                await write.SaveChangesAsync();
            }

            var revoked = await Assert.ThrowsAsync<SqlException>(() => identity.Database.ExecuteSqlRawAsync(guard));
            Assert.Equal(DownGuardErrorNumber, revoked.Number);
            Assert.Equal(RoleAssignmentStatus.Revoked, (await identity.RoleAssignments.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(x => x.Id == assignmentId)).Status);
        });
    }

    private static string SyntheticCode(string kind) => $"SENTETIK-{kind}-{Guid.NewGuid():N}";

    private static string NewKey() => Guid.NewGuid().ToString("N");

    private static HttpClient CreateClient(SqlServerRelationalApiFactory factory, string user, Guid company)
    {
        var client = factory.CreateClientFor(user, company);
        // The shared factory's Cari handshake is not an Identity contract; key and correlation are set per request.
        client.DefaultRequestHeaders.Remove(CounterpartyClientContract.HeaderName);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Remove("X-Correlation-Id");
        return client;
    }

    private static Task<HttpResponseMessage> PostAsync<TBody>(
        HttpClient client,
        string route,
        TBody body,
        string idempotencyKey,
        string correlationId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = JsonContent.Create(body, options: Json),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Headers.Add("X-Correlation-Id", correlationId);
        return SendAsync(client, request);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request)
    {
        using (request)
        {
            return await client.SendAsync(request);
        }
    }

    private static async Task<Outcome> ReadOutcomeAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(text);
        Assert.Equal(
            ["id", "roleAssignmentId", "status", "version"],
            body.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        return JsonSerializer.Deserialize<Outcome>(text, Json)!;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.False(response.Headers.Contains(ReplayedHeader));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
    }

    private static async Task AssertCodeLessAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.TryGetProperty("code", out _));
    }

    /// <summary>The rows one assignment's commands leave: itself, its versions, outbox, audit and command records.</summary>
    private static async Task<AssignmentEffects> ReadAssignmentEffectsAsync(
        SqlServerRelationalApiFactory factory,
        Guid assignmentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.True(identity.Database.IsSqlServer());
        var audit = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var entityId = assignmentId.ToString("D");
        var assignment = await identity.RoleAssignments.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.Id == assignmentId);
        return new AssignmentEffects(
            await identity.RoleAssignments.IgnoreQueryFilters().CountAsync(x => x.Id == assignmentId),
            assignment.Version,
            await identity.OutboxMessages.IgnoreQueryFilters().CountAsync(x => x.AggregateId == assignmentId),
            await identity.AccessGrantRequests.IgnoreQueryFilters().CountAsync(),
            await audit.AuditEvents.IgnoreQueryFilters().CountAsync(x => x.EntityId == entityId
                && (x.EventType == IdentityAccessAssignmentService.AssignmentGrantedEvent
                    || x.EventType == IdentityAccessAssignmentService.AssignmentRevokedEvent)),
            await identity.CommandExecutions.IgnoreQueryFilters().CountAsync(
                x => x.CompanyId == TestCompanyIds.CompanyA && x.CommandName == IdentityAccessCommandsV1.Revoke));
    }

    private static async Task<int> CountAssignmentsAsync(SqlServerRelationalApiFactory factory, Guid userId, Guid roleId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await identity.RoleAssignments.IgnoreQueryFilters()
            .CountAsync(x => x.CompanyId == TestCompanyIds.CompanyA && x.UserId == userId && x.RoleId == roleId);
    }

    private static async Task<AccessGrantRequest> ReadRequestAsync(SqlServerRelationalApiFactory factory, Guid requestId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await identity.AccessGrantRequests.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.Id == requestId);
    }

    /// <summary>The user a configured development header resolves to, created with its link when it has none.</summary>
    private static async Task<Guid> EnsureUserAsync(SqlServerRelationalApiFactory factory, string developmentUser)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var link = await identity.ExternalIdentities.SingleOrDefaultAsync(
            x => x.Issuer == BootstrapIdentityOptions.Issuer && x.Subject == developmentUser);
        if (link is not null)
        {
            return link.UserId;
        }

        var userId = Guid.NewGuid();
        identity.Users.Add(new UserAccount(userId, "SENTETIK Kimlik yetki kullanıcısı", null));
        identity.ExternalIdentities.Add(
            new ExternalIdentityLink(Guid.NewGuid(), userId, BootstrapIdentityOptions.Issuer, developmentUser));
        await identity.SaveChangesAsync();
        return userId;
    }

    private static async Task<Guid> CreateRoleAsync(
        SqlServerRelationalApiFactory factory,
        string code,
        params string[] permissions)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var roleId = Guid.NewGuid();
        var role = new RoleDefinition(roleId, code, "SENTETIK Kimlik yetki rolü", true);
        foreach (var permission in permissions)
        {
            role.AddPermission(permission);
        }

        identity.Roles.Add(role);
        await identity.SaveChangesAsync();
        return roleId;
    }

    /// <summary>A synthetic, company-wide assignment written directly: the test's own setup, never a seeded grant.</summary>
    private static async Task<Guid> AssignDirectlyAsync(
        SqlServerRelationalApiFactory factory,
        Guid userId,
        Guid roleId,
        Guid company)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var assignment = new RoleAssignment(Guid.NewGuid(), userId, roleId, company,
            DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
        assignment.AddScope(AccessScopeType.Company, company);
        identity.RoleAssignments.Add(assignment);
        await identity.SaveChangesAsync();
        return assignment.Id;
    }

    private static async Task WithDatabaseAsync(string adminRoleCode, Func<SqlServerRelationalApiFactory, Task> test,
        Action<IServiceCollection>? configureServices = null)
    {
        var connection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var databaseName = $"GoymenERP_Identity_AccessAssignment_{Guid.NewGuid():N}";
        var overrides = new Dictionary<string, string?>
        {
            ["Identity:AdminRoleCode"] = adminRoleCode,
        };
        await using var factory = new SqlServerRelationalApiFactory(connection,
            configureServices: configureServices,
            applyTestCompanyBTopologyTemplates: false, databaseName: databaseName, configurationOverrides: overrides);
        await using var cleanupScope = factory.Services.CreateAsyncScope();
        var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        // Validate the immutable cleanup target before running assertions, not in their finally path.
        Assert.Equal(databaseName, cleanupDb.Database.GetDbConnection().Database);
        try
        {
            await test(factory);
        }
        finally
        {
            await cleanupDb.Database.EnsureDeletedAsync();
        }
    }

    private sealed record Outcome(Guid Id, string Version, string Status, Guid? RoleAssignmentId);

    private sealed record AssignmentEffects(
        int Assignments,
        long AssignmentVersion,
        int OutboxMessages,
        int GrantRequests,
        int AuditEvents,
        int RevokeExecutions);
}
