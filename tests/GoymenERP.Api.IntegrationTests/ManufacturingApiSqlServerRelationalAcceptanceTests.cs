using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

internal static class ManufacturingHttpClientExtensions
{
    /// <summary>A JSON POST with its own <c>Idempotency-Key</c> (a new one unless the test names one).</summary>
    public static Task<HttpResponseMessage> PostWithKeyAsync<T>(this HttpClient client, string path, T body, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString("N"));
        return client.SendAsync(request);
    }
}

/// <summary>
/// MFG-API (#313) against a REAL SQL Server, through the real host: authentication, company
/// context, the permission policies, the fresh authority fence, Settings' approved kilogram profile
/// (company A = GDC) and Manufacturing's store. The callers are the factory's configured development
/// users, linked to an ERP account in this test's own database when they have none; grants are
/// synthetic test roles on the registered names, created there and never seeded. Parent rolls are
/// synthetic label identities (no Inventory link in this slice). A positive answer here is API
/// evidence, not a production outcome.
/// </summary>
public sealed class ManufacturingApiSqlServerRelationalAcceptanceTests
{
    private const string Base = "/api/v1/manufacturing";
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly DateTimeOffset At = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);

    private static readonly string[] AllWrites =
    [
        ErpPermissions.ManufacturingPlansWrite,
        ErpPermissions.ManufacturingPlansApprove,
        ErpPermissions.ManufacturingOrdersCreate,
        ErpPermissions.ManufacturingFactsRecord,
        ErpPermissions.ManufacturingOrdersRead,
    ];

    [SqlServerFact]
    public async Task An_authorized_user_plans_approves_orders_records_facts_and_reads_the_roll_balance()
    {
        await using var run = await Run.StartAsync();
        var workCenter = await run.WorkCenterOfAsync(CompanyA);
        var user = await run.GrantAsync(TestUsers.CompanyA, CompanyA, AllWrites);
        using var client = run.ClientFor(user, CompanyA);
        var correlation = client.DefaultRequestHeaders.GetValues("X-Correlation-Id").Single();
        var roll = Guid.NewGuid();

        // Plan: created, and the same key + body again is its replay with no second plan.
        var planRequest = new SavePlanRequestV1(Guid.NewGuid(), workCenter,
            [new PlannedInputRequestV1(roll, "1000.125")], new TargetRequestV1("GoodOutputMass", "900"));
        var created = await client.PostWithKeyAsync($"{Base}/plans", planRequest, "plan-key-1");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        // The typed header re-orders its text; the directives are what matter.
        Assert.True(created.Headers.CacheControl is { Private: true, NoStore: true });
        var plan = (await created.Content.ReadFromJsonAsync<PlanViewV1>())!;
        Assert.Equal((CompanyA, workCenter, "Draft", 1L, "1000.125"),
            (plan.CompanyId, plan.WorkCenterId, plan.Status, plan.Version, Assert.Single(plan.Inputs).PlannedKg));
        var replay = await client.PostWithKeyAsync($"{Base}/plans", planRequest, "plan-key-1");
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal("true", replay.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal(correlation, replay.Headers.GetValues("Idempotency-Original-Correlation-Id").Single());

        var approved = await client.PostWithKeyAsync($"{Base}/plans/{plan.PlanId}/approval", new ApprovePlanRequestV1(1));
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var approvedPlan = (await approved.Content.ReadFromJsonAsync<PlanViewV1>())!;
        Assert.Equal(("Approved", 2L), (approvedPlan.Status, approvedPlan.Version));
        Assert.NotNull(approvedPlan.ApprovedByUserId);

        var orderId = Guid.NewGuid();
        var orderResponse = await client.PostWithKeyAsync($"{Base}/orders", new CreateOrderRequestV1(orderId, plan.PlanId));
        Assert.Equal(HttpStatusCode.Created, orderResponse.StatusCode);
        Assert.Equal($"{Base}/orders/{orderId:D}", orderResponse.Headers.Location?.ToString());
        var order = (await orderResponse.Content.ReadFromJsonAsync<OrderViewV1>())!;

        order = await run.OkAsync(client, $"{Base}/orders/{orderId}/input-issues",
            new RecordFactRequestV1(Guid.NewGuid(), order.Version, roll, "1000.125", At));
        order = await run.OkAsync(client, $"{Base}/orders/{orderId}/outputs",
            new DeclareOutputRequestV1(Guid.NewGuid(), order.Version, roll, "GoodProduct", "900.000", At.AddHours(1)));
        order = await run.OkAsync(client, $"{Base}/orders/{orderId}/wip-measurements",
            new RecordFactRequestV1(Guid.NewGuid(), order.Version, roll, "100.125", At.AddHours(2)));

        // The read rebuilds the order from its stored facts: issued − returned = outputs + WIP + 0.
        var read = await client.GetAsync($"{Base}/orders/{orderId}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var detail = (await read.Content.ReadFromJsonAsync<OrderViewV1>())!;
        var balance = Assert.Single(detail.Rolls);
        Assert.Equal(("1000.125", "900", "100.125", "0", true),
            (balance.IssuedKg, balance.OutputsKg["GoodProduct"], balance.WorkInProgressKg, balance.UnexplainedKg,
                balance.IsFullyAccounted));
        Assert.Equal(("Open", 4L, "NotCalculated"), (detail.Status, detail.Version, detail.CostStatus));
        Assert.Equal(("GoodOutputMass", "900", "900", "0"), (detail.RemainingTarget.Basis, detail.RemainingTarget.TargetKg,
            detail.RemainingTarget.AchievedKg, detail.RemainingTarget.RemainingKg));

        // One plan, one order, three facts; six audit events under the caller's correlation (no replay event).
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Plans"));
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Facts"));
        Assert.Equal(6, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType LIKE N'Manufacturing.%' AND CorrelationId = @c",
            ("@c", correlation)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType LIKE N'Manufacturing.%' AND PermissionCode NOT LIKE N'manufacturing.%'"));
    }

    [SqlServerFact]
    public async Task Each_endpoint_needs_its_own_grant_and_a_refused_write_stores_nothing()
    {
        await using var run = await Run.StartAsync();
        var workCenter = await run.WorkCenterOfAsync(CompanyA);
        var readerOnly = await run.GrantAsync(TestUsers.NoAssignment, CompanyA, ErpPermissions.ManufacturingOrdersRead);
        var plannerOnly = await run.GrantAsync(TestUsers.Unprovisioned, CompanyA, ErpPermissions.ManufacturingPlansWrite);
        var request = new SavePlanRequestV1(Guid.NewGuid(), workCenter, [new PlannedInputRequestV1(Guid.NewGuid(), "10")], null);

        using (var client = run.ClientFor(readerOnly, CompanyA))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostWithKeyAsync($"{Base}/plans", request)).StatusCode);
        }

        // Company A's read-only user: access to the company, no Manufacturing permission (none is seeded).
        using (var client = run.ClientFor(TestUsers.CompanyAViewer, CompanyA))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostWithKeyAsync($"{Base}/plans", request)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{Base}/orders/{Guid.NewGuid()}")).StatusCode);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Plans"));

        using (var client = run.ClientFor(plannerOnly, CompanyA))
        {
            Assert.Equal(HttpStatusCode.Created, (await client.PostWithKeyAsync($"{Base}/plans", request)).StatusCode);
            // Saving a plan is not approving it.
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostWithKeyAsync(
                $"{Base}/plans/{request.PlanId}/approval", new ApprovePlanRequestV1(1))).StatusCode);
        }

        Assert.Equal("Draft", await run.ScalarAsync<string>(
            "SELECT CASE [Status] WHEN 1 THEN N'Draft' ELSE N'Approved' END FROM manufacturing.Plans"));
    }

    [SqlServerFact]
    public async Task Another_companys_order_is_not_found_and_the_company_comes_only_from_the_context()
    {
        await using var run = await Run.StartAsync();
        var userA = await run.GrantAsync(TestUsers.CompanyA, CompanyA, AllWrites);
        var userB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, AllWrites);
        var (orderId, roll) = await run.OpenOrderAsync(userA);

        using (var client = run.ClientFor(userB, CompanyB))
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Base}/orders/{orderId}")).StatusCode);
            // Company B has no approved kilogram source: the write stops at Settings (503), before the order is looked up.
            var fact = await client.PostWithKeyAsync($"{Base}/orders/{orderId}/input-issues",
                new RecordFactRequestV1(Guid.NewGuid(), 1, roll, "10", At));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, fact.StatusCode);
        }

        // B's user naming company A is refused before anything is read.
        using (var client = run.ClientFor(userB, CompanyA))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{Base}/orders/{orderId}")).StatusCode);
        }

        // Without a company context nothing runs.
        using (var client = run.Factory.CreateClient())
        {
            client.DefaultRequestHeaders.Add("X-Development-User", userA);
            client.DefaultRequestHeaders.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{Base}/orders/{orderId}")).StatusCode);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Facts"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Orders WHERE CompanyId = @c", ("@c", CompanyB)));
    }

    [SqlServerFact]
    public async Task Stale_versions_reused_ids_masses_outside_the_profile_and_foreign_work_centers_are_refused()
    {
        await using var run = await Run.StartAsync();
        var userA = await run.GrantAsync(TestUsers.CompanyA, CompanyA, AllWrites);
        var (orderId, roll) = await run.OpenOrderAsync(userA);
        using var client = run.ClientFor(userA, CompanyA);
        var factId = Guid.NewGuid();

        var first = await run.OkAsync(client, $"{Base}/orders/{orderId}/input-issues",
            new RecordFactRequestV1(factId, 1, roll, "500", At), "issue-key-1");

        // The version the caller read is gone: 409 with both versions.
        var stale = await client.PostWithKeyAsync($"{Base}/orders/{orderId}/input-returns",
            new RecordFactRequestV1(Guid.NewGuid(), 1, roll, "1", At.AddHours(1)));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var staleProblem = await Problem(stale);
        Assert.Equal((ManufacturingRuleCodes.VersionConflict, "1", "2"),
            (staleProblem.Code, staleProblem.ExpectedVersion, staleProblem.CurrentVersion));

        // The same fact id with other content under a new key: 409 (R3, the ledger's own identity).
        var reused = await client.PostWithKeyAsync($"{Base}/orders/{orderId}/input-issues",
            new RecordFactRequestV1(factId, first.Version, roll, "501", At));
        Assert.Equal((HttpStatusCode.Conflict, ManufacturingStoreCodes.FactIdConflict),
            (reused.StatusCode, (await Problem(reused)).Code));
        // The first key and request again (500.000 is 500): its stored answer (ADR-011).
        var again = await client.PostWithKeyAsync($"{Base}/orders/{orderId}/input-issues",
            new RecordFactRequestV1(factId, 1, roll, "500.000", At), "issue-key-1");
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal("true", again.Headers.GetValues("Idempotency-Replayed").Single());

        // More decimals than the kilogram profile allows: refused, never rounded.
        var scale = await client.PostWithKeyAsync($"{Base}/orders/{orderId}/input-returns",
            new RecordFactRequestV1(Guid.NewGuid(), first.Version, roll, "1.0001", At.AddHours(1)));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, ManufacturingMassCodes.ScaleExceeded),
            (scale.StatusCode, (await Problem(scale)).Code));

        // A plan at company B's work center under company A: the composite key refuses it, named.
        var foreign = await client.PostWithKeyAsync($"{Base}/plans", new SavePlanRequestV1(Guid.NewGuid(),
            await run.WorkCenterOfAsync(CompanyB), [new PlannedInputRequestV1(Guid.NewGuid(), "10")], null));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, ManufacturingStoreCodes.WorkCenterNotFound),
            (foreign.StatusCode, (await Problem(foreign)).Code));

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Facts"));
        Assert.Equal("500.000", (await run.ScalarAsync<decimal>("SELECT MassKg FROM manufacturing.Facts"))
            .ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Plans"));
    }

    [SqlServerFact]
    public async Task The_idempotency_key_replays_one_attempt_and_refuses_another_request_or_actor()
    {
        // ADR-011 §2–§4, §7 over the module's own command log.
        await using var run = await Run.StartAsync();
        var userA = await run.GrantAsync(TestUsers.CompanyA, CompanyA, AllWrites);
        var otherA = await run.GrantAsync(TestUsers.NoAssignment, CompanyA, AllWrites);
        using var client = run.ClientFor(userA, CompanyA);
        var correlation = client.DefaultRequestHeaders.GetValues("X-Correlation-Id").Single();
        var plan = new SavePlanRequestV1(Guid.NewGuid(), await run.WorkCenterOfAsync(CompanyA),
            [new PlannedInputRequestV1(Guid.NewGuid(), "1000")], null);
        Assert.Equal(HttpStatusCode.Created, (await client.PostWithKeyAsync($"{Base}/plans", plan, "save-1")).StatusCode);

        // The same key with another body: 409, and nothing of the first answer.
        var otherBody = await client.PostWithKeyAsync($"{Base}/plans", plan with { PlanId = Guid.NewGuid() }, "save-1");
        Assert.Equal((HttpStatusCode.Conflict, ManufacturingApiCodes.IdempotencyConflict),
            (otherBody.StatusCode, (await Problem(otherBody)).Code));
        Assert.False(otherBody.Headers.Contains("Idempotency-Replayed"));
        Assert.DoesNotContain(plan.PlanId.ToString(), await otherBody.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        // The same key and body from another actor of the company: 409 as well.
        using (var other = run.ClientFor(otherA, CompanyA))
        {
            var otherActor = await other.PostWithKeyAsync($"{Base}/plans", plan, "save-1");
            Assert.Equal((HttpStatusCode.Conflict, ManufacturingApiCodes.IdempotencyConflict),
                (otherActor.StatusCode, (await Problem(otherActor)).Code));
        }

        // The approval replays from the log: a lost answer retried is 200, not a version conflict.
        var approved = await client.PostWithKeyAsync($"{Base}/plans/{plan.PlanId}/approval", new ApprovePlanRequestV1(1), "approve-1");
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var retried = await client.PostWithKeyAsync($"{Base}/plans/{plan.PlanId}/approval", new ApprovePlanRequestV1(1), "approve-1");
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        Assert.Equal(("true", correlation), (retried.Headers.GetValues("Idempotency-Replayed").Single(),
            retried.Headers.GetValues("Idempotency-Original-Correlation-Id").Single()));
        Assert.Equal(("Approved", 2L), ((await retried.Content.ReadFromJsonAsync<PlanViewV1>())!.Status,
            (await approved.Content.ReadFromJsonAsync<PlanViewV1>())!.Version));

        // A terminal version conflict (§7) is its key's answer too, and a terminal not-found.
        var stale = await client.PostWithKeyAsync($"{Base}/plans/{plan.PlanId}/approval", new ApprovePlanRequestV1(1), "approve-2");
        var staleAgain = await client.PostWithKeyAsync($"{Base}/plans/{plan.PlanId}/approval", new ApprovePlanRequestV1(1), "approve-2");
        Assert.Equal((HttpStatusCode.Conflict, HttpStatusCode.Conflict), (stale.StatusCode, staleAgain.StatusCode));
        Assert.Equal((ManufacturingRuleCodes.VersionConflict, "1", "2"), await Problem(staleAgain));
        Assert.Equal("true", staleAgain.Headers.GetValues("Idempotency-Replayed").Single());
        var unknown = Guid.NewGuid();
        var missing = await client.PostWithKeyAsync($"{Base}/plans/{unknown}/approval", new ApprovePlanRequestV1(1), "approve-3");
        var missingAgain = await client.PostWithKeyAsync($"{Base}/plans/{unknown}/approval", new ApprovePlanRequestV1(1), "approve-3");
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (missing.StatusCode, missingAgain.StatusCode));
        Assert.Equal("true", missingAgain.Headers.GetValues("Idempotency-Replayed").Single());

        // One plan, one approval; the log holds one row per (command, key) and no raw key.
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Plans"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'manufacturing.plan.approved.v1'"));
        Assert.Equal(4, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Commands"));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.Commands WHERE ResultJson LIKE N'%approve-1%' OR ResultJson LIKE N'%save-1%'"));
        Assert.Equal(4, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.Commands WHERE LEN(IdempotencyKeyHash) = 64 AND IdempotencyKeyHash = UPPER(IdempotencyKeyHash)"));
    }

    [SqlServerFact]
    public async Task Two_attempts_racing_on_one_key_store_one_command_and_both_answer_it()
    {
        // ADR-011 §8: the unique key leaves one winner; the loser answers from the winner's record.
        await using var run = await Run.StartAsync();
        var userA = await run.GrantAsync(TestUsers.CompanyA, CompanyA, AllWrites);
        var (orderId, roll) = await run.OpenOrderAsync(userA);
        var fact = new RecordFactRequestV1(Guid.NewGuid(), 1, roll, "250", At);

        async Task<HttpResponseMessage> AttemptAsync()
        {
            using var client = run.ClientFor(userA, CompanyA);
            return await client.PostWithKeyAsync($"{Base}/orders/{orderId}/input-issues", fact, "race-1");
        }

        var answers = await Task.WhenAll(Task.Run(AttemptAsync), Task.Run(AttemptAsync));

        Assert.All(answers, answer => Assert.Equal(HttpStatusCode.OK, answer.StatusCode));
        Assert.Single(answers, answer => answer.Headers.Contains("Idempotency-Replayed"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Facts"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.Commands WHERE CommandName = N'manufacturing.input.issue.v1'"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'manufacturing.input.issued.v1'"));
    }

    private static async Task<(string? Code, string? ExpectedVersion, string? CurrentVersion)> Problem(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string? Text(string name) => document.RootElement.TryGetProperty(name, out var value) ? value.GetString() : null;
        return (Text("code"), Text("expectedVersion"), Text("currentVersion"));
    }

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            // The host migrates Organization first and Manufacturing after it.
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            return run;
        }

        /// <summary>A client of the configured development user; every write names its own Idempotency-Key.</summary>
        public HttpClient ClientFor(string user, Guid company)
        {
            var client = Factory.CreateClientFor(user, company);
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            return client;
        }

        public Task<Guid> WorkCenterOfAsync(Guid company) =>
            ScalarAsync<Guid>("SELECT TOP (1) Id FROM organization.WorkCenters WHERE CompanyId = @c ORDER BY Id", ("@c", company));

        /// <summary>An approved plan of one roll and its open order in company A, through the API.</summary>
        public async Task<(Guid OrderId, Guid Roll)> OpenOrderAsync(string user)
        {
            using var client = ClientFor(user, CompanyA);
            var roll = Guid.NewGuid();
            var plan = new SavePlanRequestV1(Guid.NewGuid(), await WorkCenterOfAsync(CompanyA),
                [new PlannedInputRequestV1(roll, "1000")], null);
            Assert.Equal(HttpStatusCode.Created, (await client.PostWithKeyAsync($"{Base}/plans", plan)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostWithKeyAsync($"{Base}/plans/{plan.PlanId}/approval",
                new ApprovePlanRequestV1(1))).StatusCode);
            var orderId = Guid.NewGuid();
            Assert.Equal(HttpStatusCode.Created, (await client.PostWithKeyAsync($"{Base}/orders",
                new CreateOrderRequestV1(orderId, plan.PlanId))).StatusCode);
            return (orderId, roll);
        }

        public async Task<OrderViewV1> OkAsync<T>(HttpClient client, string path, T body, string? key = null)
        {
            var response = await client.PostWithKeyAsync(path, body, key);
            Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<OrderViewV1>())!;
        }

        /// <summary>
        /// A synthetic role with exactly these permissions in one company, assigned to a configured
        /// development user (linked to a new ERP account first if it has none). Returns the header value.
        /// </summary>
        public async Task<string> GrantAsync(string developmentUser, Guid company, params string[] permissions)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var link = await dbContext.ExternalIdentities.SingleOrDefaultAsync(
                x => x.Issuer == BootstrapIdentityOptions.Issuer && x.Subject == developmentUser);
            Guid actor;
            if (link is null)
            {
                actor = Guid.NewGuid();
                dbContext.Users.Add(new UserAccount(actor, "Synthetic Manufacturing user", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor,
                    BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"MFGAPI-{roleId:N}", "Synthetic Manufacturing test role", true);
            foreach (var permission in permissions)
            {
                role.AddPermission(permission);
            }

            var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company,
                DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
            assignment.AddScope(AccessScopeType.Company, company);
            dbContext.Roles.Add(role);
            dbContext.RoleAssignments.Add(assignment);
            await dbContext.SaveChangesAsync();
            return developmentUser;
        }

        public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(baseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{Factory.DatabaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{Factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{Factory.DatabaseName}];
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }
    }
}
