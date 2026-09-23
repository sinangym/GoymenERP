using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Lots;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
/// real company-scoped Inventory labels with physical positions and exact label versions. A positive
/// answer here is API evidence, not a production outcome.
/// </summary>
public sealed class ManufacturingApiSqlServerRelationalAcceptanceTests
{
    private const string Base = "/api/v1/manufacturing";
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid Product = Guid.Parse("7e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a91");
    private static readonly Guid Lot = Guid.Parse("7e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a92");
    private static readonly DateTimeOffset At = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);

    private static readonly string[] AllWrites =
    [
        ErpPermissions.ManufacturingPlansWrite,
        ErpPermissions.ManufacturingPlansApprove,
        ErpPermissions.ManufacturingOrdersCreate,
        ErpPermissions.ManufacturingFactsRecord,
        ErpPermissions.ManufacturingOrdersRead,
        ErpPermissions.InventoryLabelsRead,
        ErpPermissions.InventoryBalancesRead,
    ];

    [SqlServerFact]
    public async Task An_authorized_user_plans_approves_orders_records_facts_and_reads_the_roll_balance()
    {
        await using var run = await Run.StartAsync();
        var workCenter = await run.WorkCenterOfAsync(CompanyA);
        var user = await run.GrantAsync(TestUsers.CompanyA, CompanyA, AllWrites);
        using var client = run.ClientFor(user, CompanyA);
        var correlation = client.DefaultRequestHeaders.GetValues("X-Correlation-Id").Single();
        var roll = await run.StockedLabelAsync("SYN-MFG-JOURNEY");

        // Plan: created, and the same key + body again is its replay with no second plan.
        var planRequest = new SavePlanRequestV1(Guid.NewGuid(), workCenter,
            [new PlannedInputRequestV1(roll.Id, "1000.125", roll.Version)], new TargetRequestV1("GoodOutputMass", "900"));
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
            new RecordFactRequestV1(Guid.NewGuid(), order.Version, roll.Id, "1000.125", At));
        order = await run.OkAsync(client, $"{Base}/orders/{orderId}/outputs",
            new DeclareOutputRequestV1(Guid.NewGuid(), order.Version, roll.Id, "GoodProduct", "900.000", At.AddHours(1)));
        order = await run.OkAsync(client, $"{Base}/orders/{orderId}/wip-measurements",
            new RecordFactRequestV1(Guid.NewGuid(), order.Version, roll.Id, "100.125", At.AddHours(2)));

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
        var roll = await run.StockedLabelAsync("SYN-MFG-GRANT");
        var readerOnly = await run.GrantAsync(TestUsers.NoAssignment, CompanyA, ErpPermissions.ManufacturingOrdersRead);
        var plannerOnly = await run.GrantAsync(TestUsers.Unprovisioned, CompanyA,
            ErpPermissions.ManufacturingPlansWrite, ErpPermissions.InventoryLabelsRead,
            ErpPermissions.InventoryBalancesRead);
        var request = new SavePlanRequestV1(Guid.NewGuid(), workCenter,
            [new PlannedInputRequestV1(roll.Id, "10", roll.Version)], null);

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

    /// <summary>
    /// The plan read against real SQL. Until this slice a plan could be created and approved but never
    /// read back, so every claim below is new evidence rather than a restatement: that the read returns
    /// the STORED plan (the approval is visible through it, not just in the approve response), that it
    /// carries its own narrow grant, and that another company's plan is the same code-less 404 as an
    /// unknown id — which is the company query filter on <c>PlanRow</c> doing the work, not a UI check.
    /// </summary>
    [SqlServerFact]
    public async Task A_plan_is_read_back_from_storage_under_its_own_grant_and_only_for_the_context_company()
    {
        await using var run = await Run.StartAsync();
        var workCenter = await run.WorkCenterOfAsync(CompanyA);
        var reader = await run.GrantAsync(TestUsers.CompanyA, CompanyA,
            [.. AllWrites, ErpPermissions.ManufacturingPlansRead]);
        // Every Manufacturing write, but NOT the plan read: the grant is measured, not assumed from the others.
        var writerWithoutRead = await run.GrantAsync(TestUsers.Unprovisioned, CompanyA, AllWrites);
        var userB = await run.GrantAsync(TestUsers.CompanyB, CompanyB,
            [.. AllWrites, ErpPermissions.ManufacturingPlansRead]);
        var roll = await run.StockedLabelAsync("SYN-MFG-PLAN-READ");

        var request = new SavePlanRequestV1(Guid.NewGuid(), workCenter,
            [new PlannedInputRequestV1(roll.Id, "1000.250", roll.Version)], new TargetRequestV1("GoodOutputMass", "900"));
        PlanViewV1 created;
        using (var client = run.ClientFor(reader, CompanyA))
        {
            var create = await client.PostWithKeyAsync($"{Base}/plans", request);
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            created = (await create.Content.ReadFromJsonAsync<PlanViewV1>())!;

            // Reopened == created, compared as a WHOLE view. Naming three fields would pass while a fourth
            // drifted, and a drifting fourth field is exactly what two hand-mapped projections produce.
            var reopened = await client.GetAsync($"{Base}/plans/{created.PlanId}");
            Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
            Assert.True(reopened.Headers.CacheControl is { Private: true, NoStore: true });
            var view = (await reopened.Content.ReadFromJsonAsync<PlanViewV1>())!;
            Assert.Equal(JsonSerializer.Serialize(created), JsonSerializer.Serialize(view));
            // Not vacuous: the comparison is over a view that carries the plan's content. The kilogram is
            // "1000.25", not the "1000.250" that was sent: the read projects through the SAME Text(decimal)
            // normalization the write answer uses, so a trailing zero the caller typed does not survive
            // either answer. Measured on the formatter, and on the existing journey case where a declared
            // "900.000" reads back as "900".
            Assert.Equal(("Draft", 1L, "1000.25"), (view.Status, view.Version, Assert.Single(view.Inputs).PlannedKg));

            // The read answers from STORAGE: approving through another command changes what it returns.
            Assert.Equal(HttpStatusCode.OK,
                (await client.PostWithKeyAsync($"{Base}/plans/{created.PlanId}/approval", new ApprovePlanRequestV1(1)))
                    .StatusCode);
            var afterApproval = (await (await client.GetAsync($"{Base}/plans/{created.PlanId}")).Content
                .ReadFromJsonAsync<PlanViewV1>())!;
            Assert.Equal(("Approved", 2L), (afterApproval.Status, afterApproval.Version));
            Assert.NotNull(afterApproval.ApprovedByUserId);

            // An unknown id in the caller's own company is the code-less 404 the other company gets below.
            Assert.Equal(HttpStatusCode.NotFound,
                (await client.GetAsync($"{Base}/plans/{Guid.NewGuid()}")).StatusCode);
        }

        // Every write grant in the module does not add up to the read.
        using (var client = run.ClientFor(writerWithoutRead, CompanyA))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{Base}/plans/{created.PlanId}")).StatusCode);
        }

        // Company B holds the read grant in its OWN company and still cannot see A's plan: not found, never
        // forbidden — a 403 would confirm the row exists (#257, the order read's rule).
        using (var client = run.ClientFor(userB, CompanyB))
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Base}/plans/{created.PlanId}")).StatusCode);
        }

        // B's user naming company A is refused before anything is read.
        using (var client = run.ClientFor(userB, CompanyA))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{Base}/plans/{created.PlanId}")).StatusCode);
        }

        // Without a company context nothing runs.
        using (var client = run.Factory.CreateClient())
        {
            client.DefaultRequestHeaders.Add("X-Development-User", reader);
            client.DefaultRequestHeaders.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{Base}/plans/{created.PlanId}")).StatusCode);
        }

        // The refusals stored nothing and created no second plan.
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Plans"));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.Plans WHERE CompanyId = @c", ("@c", CompanyB)));
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
    public async Task The_mass_balance_read_is_bal003_of_the_stored_facts_only_for_the_context_company_and_the_read_grant()
    {
        await using var run = await Run.StartAsync();
        var userA = await run.GrantAsync(TestUsers.CompanyA, CompanyA, AllWrites);
        var userB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, AllWrites);
        var (orderId, roll) = await run.OpenOrderAsync(userA);
        var path = $"{Base}/orders/{orderId}/mass-balance";

        using (var client = run.ClientFor(userA, CompanyA))
        {
            var issued = await run.OkAsync(client, $"{Base}/orders/{orderId}/input-issues",
                new RecordFactRequestV1(Guid.NewGuid(), 1, roll, "100", At));

            // BAL-003 of the stored facts: 100 issued, nothing declared → 100 unaccounted; BAL-004/006 the engine's Unavailable.
            using var first = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.True(first.Headers.CacheControl is { Private: true, NoStore: true });
            var view = (await first.Content.ReadFromJsonAsync<MassBalanceViewV1>())!;
            Assert.Equal((orderId, issued.Version, "GDC-BAL/1 (master v3 §28.9)"), (view.OrderId, view.Version, view.FormulaVersion));
            var line = Assert.Single(view.Rolls);
            Assert.Equal((roll, new MassDifferenceViewV1("100", "0", "100", false)), (line.ParentRollId, line.Difference));
            Assert.Equal(("Unavailable", (string?)null), (line.ScrapPercent.Outcome, line.ScrapPercent.Value));
            Assert.Equal(["fireKg", "rawMaterialKg"], line.ScrapPercent.MissingParameters);
            Assert.Equal(("Unavailable", (string?)null), (line.ActualUnitKg.Outcome, line.ActualUnitKg.Value));
            Assert.Equal(["actualKg", "actualCount"], line.ActualUnitKg.MissingParameters);

            // Reopen after a new fact: the read follows the stored facts (60 good + 40 scrap → fully accounted).
            var good = await run.OkAsync(client, $"{Base}/orders/{orderId}/outputs",
                new DeclareOutputRequestV1(Guid.NewGuid(), issued.Version, roll, "GoodProduct", "60", At.AddMinutes(1)));
            await run.OkAsync(client, $"{Base}/orders/{orderId}/outputs",
                new DeclareOutputRequestV1(Guid.NewGuid(), good.Version, roll, "Scrap", "40", At.AddMinutes(2)));
            var reopened = (await client.GetFromJsonAsync<MassBalanceViewV1>(path))!;
            Assert.Equal(new MassDifferenceViewV1("100", "100", "0", true), Assert.Single(reopened.Rolls).Difference);

            // A read replays nothing and writes nothing: the same answer again, no new fact, no command attempt for it.
            var facts = await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Facts");
            var commands = await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Commands");
            var again = (await client.GetFromJsonAsync<MassBalanceViewV1>(path))!;
            // Member by member: a record's Equals compares its list members (MissingParameters) by reference.
            var before = reopened.Rolls.Single();
            var after = again.Rolls.Single();
            Assert.Equal((reopened.Version, before.ParentRollId, before.Difference), (again.Version, after.ParentRollId, after.Difference));
            foreach (var (beforeAnswer, afterAnswer) in new[] { (before.ScrapPercent, after.ScrapPercent), (before.ActualUnitKg, after.ActualUnitKg) })
            {
                Assert.Equal((beforeAnswer.Outcome, beforeAnswer.Value, beforeAnswer.RefusalCode, beforeAnswer.RefusedParameter),
                    (afterAnswer.Outcome, afterAnswer.Value, afterAnswer.RefusalCode, afterAnswer.RefusedParameter));
                Assert.Equal(beforeAnswer.MissingParameters, afterAnswer.MissingParameters);
            }
            Assert.Equal((facts, commands), (await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Facts"),
                await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Commands")));

            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Base}/orders/{Guid.NewGuid()}/mass-balance")).StatusCode);
        }

        // Another company's order is the same code-less 404 as an unknown one.
        using (var client = run.ClientFor(userB, CompanyB))
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
        }

        // Company A's read-only user without a Manufacturing permission, and B's user naming A: refused before any read.
        using (var client = run.ClientFor(TestUsers.CompanyAViewer, CompanyA))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        }

        using (var client = run.ClientFor(userB, CompanyA))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        }

        // Without a company context nothing runs.
        using (var client = run.Factory.CreateClient())
        {
            client.DefaultRequestHeaders.Add("X-Development-User", userA);
            client.DefaultRequestHeaders.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(path)).StatusCode);
        }
    }

    [SqlServerFact]
    public async Task A_read_that_keeps_meeting_concurrent_commits_is_a_retryable_503_platform_problem()
    {
        // FİNANS #313 INFO-4 through the real host: authentication, the company context, the read grant
        // and the endpoint's problem mapping are real. Only the store's read is made to exhaust its
        // re-reads, because three torn reads in a row cannot be produced on purpose against SQL Server:
        // this is the HTTP answer's evidence, not a reproduction of the torn read.
        await using var run = await Run.StartAsync();
        var user = await run.GrantAsync(TestUsers.CompanyA, CompanyA, ErpPermissions.ManufacturingPlansWrite,
            ErpPermissions.ManufacturingPlansApprove, ErpPermissions.ManufacturingOrdersCreate,
            ErpPermissions.ManufacturingOrdersRead);
        var (orderId, _) = await run.OpenOrderAsync(user);
        await using var host = run.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IManufacturingOrderStore>();
            services.AddScoped<IManufacturingOrderStore>(provider =>
                new ReadsKeepMeetingCommits(provider.GetRequiredService<ManufacturingStore>()));
        }));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", user);
        client.DefaultRequestHeaders.Add("X-Company-Id", CompanyA.ToString());
        client.DefaultRequestHeaders.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));

        var response = await client.GetAsync($"{Base}/orders/{orderId}");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(("service_unavailable", 503), (problem.RootElement.GetProperty("code").GetString(),
            problem.RootElement.GetProperty("status").GetInt32()));
        Assert.False(problem.RootElement.TryGetProperty("orderId", out _));

        // The same order through the unchanged host reads, and the refused read changed nothing.
        using var reader = run.ClientFor(user, CompanyA);
        var read = await reader.GetAsync($"{Base}/orders/{orderId}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(1L, (await read.Content.ReadFromJsonAsync<OrderViewV1>())!.Version);
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
        var foreignRoll = await run.StockedLabelAsync("SYN-MFG-FOREIGN-WORK-CENTER");
        var foreign = await client.PostWithKeyAsync($"{Base}/plans", new SavePlanRequestV1(Guid.NewGuid(),
            await run.WorkCenterOfAsync(CompanyB),
            [new PlannedInputRequestV1(foreignRoll.Id, "10", foreignRoll.Version)], null));
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
        var roll = await run.StockedLabelAsync("SYN-MFG-IDEMPOTENCY");
        var plan = new SavePlanRequestV1(Guid.NewGuid(), await run.WorkCenterOfAsync(CompanyA),
            [new PlannedInputRequestV1(roll.Id, "1000", roll.Version)], null);
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

        // Both attempts are released together. Whatever the interleaving, the answer is the same:
        // one records, the other answers from its record (a replay before the write, or §8 after it).
        using var start = new Barrier(2);
        async Task<HttpResponseMessage> AttemptAsync()
        {
            using var client = run.ClientFor(userA, CompanyA);
            start.SignalAndWait(TimeSpan.FromSeconds(30));
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

    [SqlServerFact]
    public async Task The_close_writes_once_replays_its_key_and_refuses_an_unexplained_difference()
    {
        // MFG-4 (Business 5728619644) against real SQL: every roll accounts for its own issued mass, no
        // tolerance, and the close is one ADR-011 attempt per key.
        await using var run = await Run.StartAsync();
        var closer = await run.GrantAsync(TestUsers.CompanyA, CompanyA, [.. AllWrites, ErpPermissions.ManufacturingOrdersClose]);
        var (orderId, roll) = await run.OpenOrderAsync(closer);

        using (var client = run.ClientFor(closer, CompanyA))
        {
            var issued = await run.OkAsync(client, $"{Base}/orders/{orderId}/input-issues",
                new RecordFactRequestV1(Guid.NewGuid(), 1, roll, "1000", At));

            // 600 of 1000 declared: the difference is unexplained, so the close is refused and nothing moves.
            var partial = await run.OkAsync(client, $"{Base}/orders/{orderId}/outputs",
                new DeclareOutputRequestV1(Guid.NewGuid(), issued.Version, roll, "GoodProduct", "600", At.AddMinutes(1)));
            using var refused = await client.PostWithKeyAsync($"{Base}/orders/{orderId}/close",
                new CloseOrderRequestV1(partial.Version), "close-refused");
            Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
            Assert.Equal(ManufacturingRuleCodes.UnexplainedDifference, (await Problem(refused)).Code);
            Assert.Equal("Open", (await client.GetFromJsonAsync<OrderViewV1>($"{Base}/orders/{orderId}"))!.Status);
            // A rule refusal is not this key's answer: no command row, and the order was not touched.
            Assert.Equal(0, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM manufacturing.Commands WHERE CommandName = N'manufacturing.order.close.v1'"));

            // A stale version is a TERMINAL refusal: recorded once, and the same key answers from it.
            var balanced = await run.OkAsync(client, $"{Base}/orders/{orderId}/outputs",
                new DeclareOutputRequestV1(Guid.NewGuid(), partial.Version, roll, "Scrap", "400", At.AddMinutes(2)));
            using var stale = await client.PostWithKeyAsync($"{Base}/orders/{orderId}/close",
                new CloseOrderRequestV1(balanced.Version - 1), "close-stale");
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal(ManufacturingRuleCodes.VersionConflict, (await Problem(stale)).Code);
            using var staleAgain = await client.PostWithKeyAsync($"{Base}/orders/{orderId}/close",
                new CloseOrderRequestV1(balanced.Version - 1), "close-stale");
            Assert.Equal(HttpStatusCode.Conflict, staleAgain.StatusCode);
            Assert.Equal("true", staleAgain.Headers.GetValues("Idempotency-Replayed").Single());

            // The accepted close: one write, one audit event, one command row for this key.
            using var accepted = await client.PostWithKeyAsync($"{Base}/orders/{orderId}/close",
                new CloseOrderRequestV1(balanced.Version), "close-1");
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            var closed = (await accepted.Content.ReadFromJsonAsync<OrderViewV1>())!;
            Assert.Equal(("Closed", balanced.Version + 1), (closed.Status, closed.Version));
            Assert.NotNull(closed.ClosedByUserId);
            Assert.NotNull(closed.ClosedAtUtc);

            using var replay = await client.PostWithKeyAsync($"{Base}/orders/{orderId}/close",
                new CloseOrderRequestV1(balanced.Version), "close-1");
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal("true", replay.Headers.GetValues("Idempotency-Replayed").Single());

            // A NEW key on a closed order is the domain's own refusal, not a second close.
            using var second = await client.PostWithKeyAsync($"{Base}/orders/{orderId}/close",
                new CloseOrderRequestV1(closed.Version), "close-2");
            Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
            Assert.Equal(ManufacturingRuleCodes.OrderClosed, (await Problem(second)).Code);

            Assert.Equal(1, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM manufacturing.Orders WHERE Id = @o AND ClosedAtUtc IS NOT NULL", ("@o", orderId)));
            Assert.Equal(1, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'manufacturing.order.closed.v1'"));
            // Two attempts recorded: the terminal stale conflict and the success. The refusals are not.
            Assert.Equal(2, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM manufacturing.Commands WHERE CommandName = N'manufacturing.order.close.v1'"));
        }

        // Another company cannot see or close it: the same code-less 404 as an unknown id.
        var otherCompany = await run.GrantAsync(TestUsers.CompanyB, CompanyB, [.. AllWrites, ErpPermissions.ManufacturingOrdersClose]);
        using (var client = run.ClientFor(otherCompany, CompanyB))
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Base}/orders/{orderId}")).StatusCode);
            using var denied = await client.PostWithKeyAsync($"{Base}/orders/{orderId}/close", new CloseOrderRequestV1(1));
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        }

        // The read grant alone does not close: the close has its own permission.
        var readerOnly = await run.GrantAsync(TestUsers.NoAssignment, CompanyA, ErpPermissions.ManufacturingOrdersRead);
        using (var client = run.ClientFor(readerOnly, CompanyA))
        {
            using var forbidden = await client.PostWithKeyAsync($"{Base}/orders/{orderId}/close", new CloseOrderRequestV1(1));
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        // Nothing above closed a second order or wrote a second close.
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'manufacturing.order.closed.v1'"));
    }

    [SqlServerFact]
    public async Task The_order_list_is_this_companys_stored_orders_newest_first_under_the_read_grant()
    {
        await using var run = await Run.StartAsync();
        var userA = await run.GrantAsync(TestUsers.CompanyA, CompanyA, [.. AllWrites, ErpPermissions.ManufacturingOrdersClose]);
        var userB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, AllWrites);
        var (firstOrder, firstRoll) = await run.OpenOrderAsync(userA);
        var (secondOrder, _) = await run.OpenOrderAsync(userA);

        using (var client = run.ClientFor(userA, CompanyA))
        {
            using var response = await client.GetAsync($"{Base}/orders");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl is { Private: true, NoStore: true });
            var page = (await response.Content.ReadFromJsonAsync<OrderListViewV1>())!;
            Assert.Null(page.NextCursor);
            // Both orders of this company, and only those, in the server's own sequence: createdAtUtc DESC,
            // then id DESC. The two were created by separate API calls into a `datetimeoffset` (7 digits,
            // 100 ns) column, so their instants differ and the PRIMARY sort alone decides — newest first.
            // That is asserted, not assumed: if the instants ever tied, the next line would say so instead
            // of the sequence quietly depending on SQL Server's uniqueidentifier tie-break.
            Assert.Equal(2, page.Orders.Count);
            Assert.NotEqual(page.Orders[0].CreatedAtUtc, page.Orders[1].CreatedAtUtc);
            Assert.Equal([secondOrder, firstOrder], page.Orders.Select(row => row.OrderId));
            var summary = page.Orders.Single(row => row.OrderId == firstOrder);
            // The stored snapshot, as text: one planned roll of 1000 kg, no target, open, version 1.
            Assert.Equal((firstOrder, "Slitting", "Open", 1L, 1, "1000"),
                (summary.OrderId, summary.Process, summary.Status, summary.Version, summary.PlannedRollCount,
                    summary.PlannedInputKg));
            Assert.Null(summary.Target);
            Assert.Null(summary.ClosedAtUtc);

            // One page at a time: the cursor is the server's own and the second page is the rest.
            using var firstPage = await client.GetAsync($"{Base}/orders?pageSize=1");
            var one = (await firstPage.Content.ReadFromJsonAsync<OrderListViewV1>())!;
            var onId = Assert.Single(one.Orders).OrderId;
            Assert.NotNull(one.NextCursor);
            using var nextPage = await client.GetAsync($"{Base}/orders?pageSize=1&cursor={Uri.EscapeDataString(one.NextCursor!)}");
            var two = (await nextPage.Content.ReadFromJsonAsync<OrderListViewV1>())!;
            var twoId = Assert.Single(two.Orders).OrderId;
            // Walking one at a time answers exactly the sequence one whole page answers: the cursor
            // continues where the page stopped, repeats nothing and skips nothing. This is the check that
            // would catch an ORDER BY and a cursor predicate that did not compare the same way.
            Assert.Equal([secondOrder, firstOrder], new[] { onId, twoId });
            Assert.Null(two.NextCursor);

            // A cursor that is not this company's and this ordering's is refused on the cursor, never a first page.
            using var badCursor = await client.GetAsync($"{Base}/orders?cursor=not-a-cursor");
            Assert.Equal(HttpStatusCode.BadRequest, badCursor.StatusCode);
            Assert.Equal(ManufacturingApiCodes.RequestInvalid, (await Problem(badCursor)).Code);
            using var badPage = await client.GetAsync($"{Base}/orders?pageSize=101");
            Assert.Equal(HttpStatusCode.BadRequest, badPage.StatusCode);
            using var badStatus = await client.GetAsync($"{Base}/orders?status=open");
            Assert.Equal(HttpStatusCode.BadRequest, badStatus.StatusCode);

            // The status filter follows the stored status: the closed order leaves the open list.
            var issued = await run.OkAsync(client, $"{Base}/orders/{firstOrder}/input-issues",
                new RecordFactRequestV1(Guid.NewGuid(), 1, firstRoll, "1000", At));
            var declared = await run.OkAsync(client, $"{Base}/orders/{firstOrder}/outputs",
                new DeclareOutputRequestV1(Guid.NewGuid(), issued.Version, firstRoll, "GoodProduct", "1000", At.AddMinutes(1)));
            Assert.Equal(HttpStatusCode.OK, (await client.PostWithKeyAsync($"{Base}/orders/{firstOrder}/close",
                new CloseOrderRequestV1(declared.Version), "close-list")).StatusCode);

            var open = (await client.GetFromJsonAsync<OrderListViewV1>($"{Base}/orders?status=Open"))!;
            Assert.Equal(secondOrder, Assert.Single(open.Orders).OrderId);
            var closed = (await client.GetFromJsonAsync<OrderListViewV1>($"{Base}/orders?status=Closed"))!;
            var closedSummary = Assert.Single(closed.Orders);
            Assert.Equal((firstOrder, "Closed"), (closedSummary.OrderId, closedSummary.Status));
            Assert.NotNull(closedSummary.ClosedAtUtc);

            // A list is a read: it stores nothing.
            Assert.Equal(0, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType LIKE N'manufacturing.order.list%'"));
        }

        // Company B's list is its own: another company's order is in no page of it.
        using (var client = run.ClientFor(userB, CompanyB))
        {
            var page = (await client.GetFromJsonAsync<OrderListViewV1>($"{Base}/orders"))!;
            Assert.Empty(page.Orders);
        }

        // Without the read grant there is no list at all.
        var noGrant = await run.GrantAsync(TestUsers.Unprovisioned, CompanyA, ErpPermissions.ManufacturingPlansWrite);
        using (var client = run.ClientFor(noGrant, CompanyA))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{Base}/orders")).StatusCode);
        }
    }

    private static async Task<(string? Code, string? ExpectedVersion, string? CurrentVersion)> Problem(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string? Text(string name) => document.RootElement.TryGetProperty(name, out var value) ? value.GetString() : null;
        return (Text("code"), Text("expectedVersion"), Text("currentVersion"));
    }

    /// <summary>The real store, except that its read has already used up its re-reads (INFO-4).</summary>
    private sealed class ReadsKeepMeetingCommits(ManufacturingStore store) : IManufacturingOrderStore
    {
        public Task<ManufacturingWriteOutcome> CreateOrderAsync(Guid orderId, Guid planId,
            ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default) =>
            store.CreateOrderAsync(orderId, planId, metadata, cancellationToken);

        public Task<ManufacturingWriteOutcome> IssueInputAsync(Guid orderId, long expectedVersion, Guid issueId,
            Guid parentRollId, Kilograms mass, DateTimeOffset issuedAt, ManufacturingCommandMetadata metadata,
            CancellationToken cancellationToken = default) =>
            store.IssueInputAsync(orderId, expectedVersion, issueId, parentRollId, mass, issuedAt, metadata,
                cancellationToken);

        public Task<ManufacturingWriteOutcome> ReturnUnusedInputAsync(Guid orderId, long expectedVersion,
            Guid returnId, Guid parentRollId, Kilograms mass, DateTimeOffset returnedAt,
            ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default) =>
            store.ReturnUnusedInputAsync(orderId, expectedVersion, returnId, parentRollId, mass, returnedAt, metadata,
                cancellationToken);

        public Task<ManufacturingWriteOutcome> DeclareOutputAsync(Guid orderId, long expectedVersion, Guid outputId,
            Guid parentRollId, OutputClass outputClass, Kilograms mass, DateTimeOffset declaredAt,
            ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default) =>
            store.DeclareOutputAsync(orderId, expectedVersion, outputId, parentRollId, outputClass, mass, declaredAt,
                metadata, cancellationToken);

        public Task<ManufacturingWriteOutcome> RecordWorkInProgressAsync(Guid orderId, long expectedVersion,
            Guid measurementId, Guid parentRollId, Kilograms mass, DateTimeOffset measuredAt,
            ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default) =>
            store.RecordWorkInProgressAsync(orderId, expectedVersion, measurementId, parentRollId, mass, measuredAt,
                metadata, cancellationToken);

        public Task CloseOrderAsync(Guid orderId, long expectedVersion, Guid closedByUserId, DateTimeOffset closedAt,
            ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default) =>
            store.CloseOrderAsync(orderId, expectedVersion, closedByUserId, closedAt, metadata, cancellationToken);

        public Task<ProductionOrder?> FindOrderAsync(Guid orderId, CancellationToken cancellationToken = default) =>
            throw new ManufacturingStoreException(ManufacturingStoreCodes.ConcurrentChange, "sentetik: yeniden okumalar tükendi");

        public Task<ManufacturingOrderPage> ListOrdersAsync(int pageSize, string? cursor, OrderStatus? status,
            CancellationToken cancellationToken = default) =>
            store.ListOrdersAsync(pageSize, cursor, status, cancellationToken);
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
            // Idempotent when the host already applied Inventory's migrations.
            await using var inventory = run.InventoryContext(CompanyA);
            await inventory.Database.MigrateAsync();
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

        /// <summary>
        /// A real company-A Inventory label with a physical position. The version is read after the owner
        /// store completes its writes so each Manufacturing request carries the exact owner version.
        /// </summary>
        public async Task<ParentRollFixture> StockedLabelAsync(string code)
        {
            var location = await ScalarAsync<Guid>(
                "SELECT TOP (1) Id FROM organization.Locations WHERE CompanyId = @c ORDER BY Id", ("@c", CompanyA));
            var label = new MaterialLabel(Guid.NewGuid(), CompanyA, code, Product, Lot);
            var owner = new StockOwner(StockPartyKind.Company, CompanyA);
            var custodian = new StockCustodian(StockPartyKind.Company, CompanyA);
            var position = new StockPosition(CompanyA, owner, custodian, location, label, BlockState.Free);
            var writer = Guid.NewGuid();
            await using var scope = InventoryStore(writer);
            await scope.Store.SaveLotAsync(new MaterialLot(Lot, CompanyA, Product), [label], InventoryMetadata(writer));
            await scope.Store.RecordMovementAsync(
                InventoryMovement(new StockMovementLine(position, StockDirection.In,
                    GoymenERP.Modules.Inventory.Domain.Quantities.Kilograms.Positive(1000.125m))),
                InventoryMetadata(writer));
            var version = await ScalarAsync<long>(
                "SELECT Version FROM inventory.Labels WHERE CompanyId = @c AND Id = @id",
                ("@c", CompanyA), ("@id", label.Id));
            return new ParentRollFixture(label.Id, version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>An approved plan of one roll and its open order in company A, through the API.</summary>
        public async Task<(Guid OrderId, Guid Roll)> OpenOrderAsync(string user)
        {
            using var client = ClientFor(user, CompanyA);
            var roll = await StockedLabelAsync($"SYN-MFG-ORDER-{Guid.NewGuid():N}");
            var plan = new SavePlanRequestV1(Guid.NewGuid(), await WorkCenterOfAsync(CompanyA),
                [new PlannedInputRequestV1(roll.Id, "1000", roll.Version)], null);
            Assert.Equal(HttpStatusCode.Created, (await client.PostWithKeyAsync($"{Base}/plans", plan)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostWithKeyAsync($"{Base}/plans/{plan.PlanId}/approval",
                new ApprovePlanRequestV1(1))).StatusCode);
            var orderId = Guid.NewGuid();
            Assert.Equal(HttpStatusCode.Created, (await client.PostWithKeyAsync($"{Base}/orders",
                new CreateOrderRequestV1(orderId, plan.PlanId))).StatusCode);
            return (orderId, roll.Id);
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

        private InventoryDbContext InventoryContext(Guid company, params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var options = new DbContextOptionsBuilder<InventoryDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", InventoryDbContext.Schema))
                .AddInterceptors(interceptors)
                .Options;
            return new InventoryDbContext(options, companyContext);
        }

        private InventoryStoreScope InventoryStore(Guid writer)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(CompanyA);
            var collector = new AuditEventCollector();
            var dbContext = InventoryContext(CompanyA, new AuditSaveChangesInterceptor(collector, companyContext));
            var settings = new ApprovedSettingsPrerequisiteProvider(companyContext,
                new FixedCurrentUser(writer, CompanyA));
            return new InventoryStoreScope(dbContext, new InventoryStore(dbContext, companyContext, collector,
                new InventoryKilogramScale(settings), TimeProvider.System));
        }

        private static InventoryCommandMetadata InventoryMetadata(Guid writer) => new(
            writer.ToString("D"), "synthetic.manufacturing.parent-roll", ["SYNTHETIC-MANUFACTURING-PARENT-ROLL"],
            Guid.NewGuid().ToString("N"), CausationId: null, IdempotencyKey: Guid.NewGuid().ToString("N"),
            SourceChannel: "Web");

        private static StockMovement InventoryMovement(params StockMovementLine[] lines) =>
            new(Guid.NewGuid(), CompanyA, StockMovementKind.Receipt, At,
                new StockSourceReference("synthetic.manufacturing.parent-roll", Guid.NewGuid()), lines);

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

    private sealed record ParentRollFixture(Guid Id, string Version);

    private sealed class InventoryStoreScope(InventoryDbContext dbContext, InventoryStore store) : IAsyncDisposable
    {
        public InventoryStore Store => store;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    private sealed class FixedCurrentUser(Guid userId, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
