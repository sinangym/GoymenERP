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
        using var client = run.Factory.CreateClientFor(user, CompanyA);
        var correlation = client.DefaultRequestHeaders.GetValues("X-Correlation-Id").Single();
        var roll = Guid.NewGuid();

        // Plan: created, and the same key + body again is its replay with no second plan.
        var planRequest = new SavePlanRequestV1(Guid.NewGuid(), workCenter,
            [new PlannedInputRequestV1(roll, "1000.125")], new TargetRequestV1("GoodOutputMass", "900"));
        var created = await client.PostAsJsonAsync($"{Base}/plans", planRequest);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        // The typed header re-orders its text; the directives are what matter.
        Assert.True(created.Headers.CacheControl is { Private: true, NoStore: true });
        var plan = (await created.Content.ReadFromJsonAsync<PlanViewV1>())!;
        Assert.Equal((CompanyA, workCenter, "Draft", 1L, "1000.125"),
            (plan.CompanyId, plan.WorkCenterId, plan.Status, plan.Version, Assert.Single(plan.Inputs).PlannedKg));
        var replay = await client.PostAsJsonAsync($"{Base}/plans", planRequest);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal("true", replay.Headers.GetValues("Idempotency-Replayed").Single());

        var approved = await client.PostAsJsonAsync($"{Base}/plans/{plan.PlanId}/approval", new ApprovePlanRequestV1(1));
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var approvedPlan = (await approved.Content.ReadFromJsonAsync<PlanViewV1>())!;
        Assert.Equal(("Approved", 2L), (approvedPlan.Status, approvedPlan.Version));
        Assert.NotNull(approvedPlan.ApprovedByUserId);

        var orderId = Guid.NewGuid();
        var orderResponse = await client.PostAsJsonAsync($"{Base}/orders", new CreateOrderRequestV1(orderId, plan.PlanId));
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

        using (var client = run.Factory.CreateClientFor(readerOnly, CompanyA))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"{Base}/plans", request)).StatusCode);
        }

        // Company A's read-only user: access to the company, no Manufacturing permission (none is seeded).
        using (var client = run.Factory.CreateClientFor(TestUsers.CompanyAViewer, CompanyA))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"{Base}/plans", request)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{Base}/orders/{Guid.NewGuid()}")).StatusCode);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Plans"));

        using (var client = run.Factory.CreateClientFor(plannerOnly, CompanyA))
        {
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"{Base}/plans", request)).StatusCode);
            // Saving a plan is not approving it.
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(
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

        using (var client = run.Factory.CreateClientFor(userB, CompanyB))
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Base}/orders/{orderId}")).StatusCode);
            // Company B has no approved kilogram source: the write stops at Settings (503), before the order is looked up.
            var fact = await client.PostAsJsonAsync($"{Base}/orders/{orderId}/input-issues",
                new RecordFactRequestV1(Guid.NewGuid(), 1, roll, "10", At));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, fact.StatusCode);
        }

        // B's user naming company A is refused before anything is read.
        using (var client = run.Factory.CreateClientFor(userB, CompanyA))
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
        using var client = run.Factory.CreateClientFor(userA, CompanyA);
        var factId = Guid.NewGuid();

        var first = await run.OkAsync(client, $"{Base}/orders/{orderId}/input-issues",
            new RecordFactRequestV1(factId, 1, roll, "500", At));

        // The version the caller read is gone: 409 with both versions.
        var stale = await client.PostAsJsonAsync($"{Base}/orders/{orderId}/input-returns",
            new RecordFactRequestV1(Guid.NewGuid(), 1, roll, "1", At.AddHours(1)));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var staleProblem = await Problem(stale);
        Assert.Equal((ManufacturingRuleCodes.VersionConflict, "1", "2"),
            (staleProblem.Code, staleProblem.ExpectedVersion, staleProblem.CurrentVersion));

        // The same fact id with other content: 409; with the same content: a replay.
        var reused = await client.PostAsJsonAsync($"{Base}/orders/{orderId}/input-issues",
            new RecordFactRequestV1(factId, first.Version, roll, "501", At));
        Assert.Equal((HttpStatusCode.Conflict, ManufacturingStoreCodes.FactIdConflict),
            (reused.StatusCode, (await Problem(reused)).Code));
        var again = await client.PostAsJsonAsync($"{Base}/orders/{orderId}/input-issues",
            new RecordFactRequestV1(factId, 1, roll, "500.000", At));
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal("true", again.Headers.GetValues("Idempotency-Replayed").Single());

        // More decimals than the kilogram profile allows: refused, never rounded.
        var scale = await client.PostAsJsonAsync($"{Base}/orders/{orderId}/input-returns",
            new RecordFactRequestV1(Guid.NewGuid(), first.Version, roll, "1.0001", At.AddHours(1)));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, ManufacturingMassCodes.ScaleExceeded),
            (scale.StatusCode, (await Problem(scale)).Code));

        // A plan at company B's work center under company A: the composite key refuses it, named.
        var foreign = await client.PostAsJsonAsync($"{Base}/plans", new SavePlanRequestV1(Guid.NewGuid(),
            await run.WorkCenterOfAsync(CompanyB), [new PlannedInputRequestV1(Guid.NewGuid(), "10")], null));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, ManufacturingStoreCodes.WorkCenterNotFound),
            (foreign.StatusCode, (await Problem(foreign)).Code));

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Facts"));
        Assert.Equal("500.000", (await run.ScalarAsync<decimal>("SELECT MassKg FROM manufacturing.Facts"))
            .ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Plans"));
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

        public Task<Guid> WorkCenterOfAsync(Guid company) =>
            ScalarAsync<Guid>("SELECT TOP (1) Id FROM organization.WorkCenters WHERE CompanyId = @c ORDER BY Id", ("@c", company));

        /// <summary>An approved plan of one roll and its open order in company A, through the API.</summary>
        public async Task<(Guid OrderId, Guid Roll)> OpenOrderAsync(string user)
        {
            using var client = Factory.CreateClientFor(user, CompanyA);
            var roll = Guid.NewGuid();
            var plan = new SavePlanRequestV1(Guid.NewGuid(), await WorkCenterOfAsync(CompanyA),
                [new PlannedInputRequestV1(roll, "1000")], null);
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"{Base}/plans", plan)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"{Base}/plans/{plan.PlanId}/approval",
                new ApprovePlanRequestV1(1))).StatusCode);
            var orderId = Guid.NewGuid();
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"{Base}/orders",
                new CreateOrderRequestV1(orderId, plan.PlanId))).StatusCode);
            return (orderId, roll);
        }

        public async Task<OrderViewV1> OkAsync<T>(HttpClient client, string path, T body)
        {
            var response = await client.PostAsJsonAsync(path, body);
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
