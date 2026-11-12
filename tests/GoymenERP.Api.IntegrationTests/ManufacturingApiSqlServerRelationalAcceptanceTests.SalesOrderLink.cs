using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Api.Errors;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Üretim U2-b / API-16 (#1085 follow-up) through the real host on a REAL SQL Server: POST /plans/{planId}/sales-order-link
/// under the plan save's grant, with the real Manufacturing host adapter of Sales's approved-line read. Only Sales's own
/// <see cref="ISalesOrderLineReferencePortV1"/> answer is fixed (the Sales owner's rows are not this module's to seed): the
/// link and unlink with replay and the stale link revision, a half link, every Sales refusal, and the grant lost while Sales
/// was being read — none of the refused requests stores a link or its audit event. Synthetic grants in this test's database.
/// </summary>
public sealed partial class ManufacturingApiSqlServerRelationalAcceptanceTests
{
    private static readonly Guid LinkSalesOrder = Guid.Parse("5a1e0000-0000-4000-8000-00000000c001");
    private static readonly Guid LinkSalesLine = Guid.Parse("5a1e0000-0000-4000-8000-00000000c002");
    private static readonly Guid LinkCustomerParty = Guid.Parse("5a1e0000-0000-4000-8000-00000000c010");
    private static readonly Guid LinkCustomerAccount = Guid.Parse("5a1e0000-0000-4000-8000-00000000c011");
    private static readonly Guid LinkProduct = Guid.Parse("5a1e0000-0000-4000-8000-00000000c012");

    [SqlServerFact]
    public async Task A_draft_plan_is_linked_to_an_approved_sales_line_through_the_host_replayed_and_unlinked()
    {
        await using var run = await Run.StartAsync();
        var user = await run.GrantAsync(TestUsers.CompanyA, CompanyA, [.. AllWrites, ErpPermissions.ManufacturingPlansRead]);
        var actor = await run.UserIdOfAsync(user);
        var planId = await SalesLinkDraftPlanAsync(run, user, "SYN-MFG-LINK-OK");
        var sales = new FixedSalesLines(_ => Task.FromResult(ApprovedSalesLine(CompanyA, revision: 3)));
        await using var host = SalesLinkHost(run, sales);
        using var client = SalesLinkClient(host, user);
        var path = $"{Base}/plans/{planId}/sales-order-link";
        var link = new ChangePlanSalesOrderLinkRequestV1(1, 0, LinkSalesOrder, LinkSalesLine);

        var linked = await client.PostWithKeyAsync(path, link, "link-1");
        Assert.Equal(HttpStatusCode.OK, linked.StatusCode);
        var view = (await linked.Content.ReadFromJsonAsync<PlanViewV1>())!;
        Assert.Equal(("Draft", 1L, 1L), (view.Status, view.Version, view.SalesOrderLinkRevision));
        var snapshot = Assert.IsType<PlanSalesOrderLinkViewV1>(view.SalesOrderLink);
        Assert.Equal((LinkSalesOrder, LinkSalesLine, LinkCustomerParty, LinkCustomerAccount, LinkProduct, 3, actor.ToString("D")),
            (snapshot.SalesOrderId, snapshot.SalesOrderLineId, snapshot.CustomerPartyId, snapshot.CustomerAccountId,
                snapshot.ProductId, snapshot.LineRevision, snapshot.LinkedBy));
        Assert.Equal(1, sales.Reads);

        // Replay: the stored answer, without asking Sales again.
        var replay = await client.PostWithKeyAsync(path, link, "link-1");
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal("true", replay.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal(client.DefaultRequestHeaders.GetValues("X-Correlation-Id").Single(),
            replay.Headers.GetValues("Idempotency-Original-Correlation-Id").Single());
        Assert.Equal(JsonSerializer.Serialize(view),
            JsonSerializer.Serialize(await replay.Content.ReadFromJsonAsync<PlanViewV1>()));
        Assert.Equal(1, sales.Reads);

        // The plan read answers the link from storage.
        var reread = (await client.GetFromJsonAsync<PlanViewV1>($"{Base}/plans/{planId}"))!;
        Assert.Equal(JsonSerializer.Serialize(view.SalesOrderLink), JsonSerializer.Serialize(reread.SalesOrderLink));

        // A stale link revision under a new key is the link conflict (not version_conflict) and changes nothing.
        var stale = await client.PostWithKeyAsync(path, link, "link-stale");
        await AssertSalesLinkProblemAsync(stale, HttpStatusCode.Conflict, ManufacturingRuleCodes.PlanSalesOrderLinkChanged);
        Assert.Equal(1L, await SalesLinkRevisionAsync(run, planId));
        // Sales is asked before the store checks the link revision.
        Assert.Equal(2, sales.Reads);

        // Unlink: neither id, at the current link revision; Sales is not asked again.
        var unlinked = await client.PostWithKeyAsync(path, new ChangePlanSalesOrderLinkRequestV1(1, 1, null, null), "unlink-1");
        Assert.Equal(HttpStatusCode.OK, unlinked.StatusCode);
        var unlinkedView = (await unlinked.Content.ReadFromJsonAsync<PlanViewV1>())!;
        Assert.Null(unlinkedView.SalesOrderLink);
        Assert.Equal((1L, 2L), (unlinkedView.Version, unlinkedView.SalesOrderLinkRevision));
        Assert.Equal(2, sales.Reads);
        // The link and the unlink each write the one link-change audit event.
        Assert.Equal(2, await SalesLinkedAuditCountAsync(run));
    }

    [SqlServerFact]
    public async Task A_half_link_and_every_sales_refusal_store_no_link_and_no_audit()
    {
        await using var run = await Run.StartAsync();
        var user = await run.GrantAsync(TestUsers.CompanyA, CompanyA, [.. AllWrites, ErpPermissions.ManufacturingPlansRead]);
        var planId = await SalesLinkDraftPlanAsync(run, user, "SYN-MFG-LINK-REFUSED");
        SalesOrderLineReferenceAnswerV1? answer = null;
        Exception? failure = null;
        var sales = new FixedSalesLines(_ => failure is null ? Task.FromResult(answer!) : throw failure);
        await using var host = SalesLinkHost(run, sales);
        using var client = SalesLinkClient(host, user);
        var path = $"{Base}/plans/{planId}/sales-order-link";

        // A half link is refused before Sales is read.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostWithKeyAsync(path,
            new ChangePlanSalesOrderLinkRequestV1(1, 0, LinkSalesOrder, null), "half-order")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostWithKeyAsync(path,
            new ChangePlanSalesOrderLinkRequestV1(1, 0, null, LinkSalesLine), "half-line")).StatusCode);
        Assert.Equal(0, sales.Reads);

        var link = new ChangePlanSalesOrderLinkRequestV1(1, 0, LinkSalesOrder, LinkSalesLine);
        answer = new SalesOrderLineReferenceAnswerV1(SalesOrderLineReferenceOutcomeV1.NotApproved, null, "Pending");
        await AssertSalesLinkProblemAsync(await client.PostWithKeyAsync(path, link, "not-approved"),
            HttpStatusCode.UnprocessableEntity, ManufacturingRuleCodes.PlanSalesOrderLineNotApproved);
        answer = new SalesOrderLineReferenceAnswerV1(SalesOrderLineReferenceOutcomeV1.NotFound, null, null);
        await AssertSalesLinkProblemAsync(await client.PostWithKeyAsync(path, link, "not-found"),
            HttpStatusCode.UnprocessableEntity, ManufacturingRuleCodes.PlanSalesOrderLineNotFound);

        // Another company's line, or a Sales failure, is the fail-closed 503 — never a link.
        answer = ApprovedSalesLine(CompanyB, revision: 1);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostWithKeyAsync(path, link, "foreign")).StatusCode);
        failure = new InvalidOperationException("synthetic Sales read failure");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostWithKeyAsync(path, link, "failed")).StatusCode);
        Assert.Equal(4, sales.Reads);

        // An unknown plan is the code-less 404 even with an approved line (Sales is read before the plan) ...
        failure = null;
        answer = ApprovedSalesLine(CompanyA, revision: 1);
        await AssertSalesLinkCodeLessAsync(await client.PostWithKeyAsync(
            $"{Base}/plans/{Guid.NewGuid()}/sales-order-link", link, "unknown-plan"), HttpStatusCode.NotFound);
        Assert.Equal(5, sales.Reads);
        // ... and so is another company's plan; an unlink does not ask Sales.
        var userB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, [.. AllWrites, ErpPermissions.ManufacturingPlansRead]);
        using (var other = SalesLinkClient(host, userB, CompanyB))
        {
            await AssertSalesLinkCodeLessAsync(await other.PostWithKeyAsync(path,
                new ChangePlanSalesOrderLinkRequestV1(1, 0, null, null), "foreign-plan"), HttpStatusCode.NotFound);
        }

        Assert.Equal(5, sales.Reads);

        var plan = (await client.GetFromJsonAsync<PlanViewV1>($"{Base}/plans/{planId}"))!;
        Assert.Equal((1L, 0L, (PlanSalesOrderLinkViewV1?)null), (plan.Version, plan.SalesOrderLinkRevision, plan.SalesOrderLink));
        Assert.Equal(0L, await SalesLinkRevisionAsync(run, planId));
        Assert.Equal(0, await SalesLinkedAuditCountAsync(run));
    }

    [SqlServerFact]
    public async Task The_grant_lost_while_sales_is_read_refuses_the_link_and_stores_nothing()
    {
        await using var run = await Run.StartAsync();
        var user = await run.GrantAsync(TestUsers.CompanyA, CompanyA, [.. AllWrites, ErpPermissions.ManufacturingPlansRead]);
        var actor = await run.UserIdOfAsync(user);
        var planId = await SalesLinkDraftPlanAsync(run, user, "SYN-MFG-LINK-REVOKED");

        // Sales answers Approved, but the user's every assignment is removed while Sales is being read.
        var sales = new FixedSalesLines(async _ =>
        {
            Assert.True(await run.ScalarAsync<int>(
                "DELETE s FROM [identity].RoleAssignmentScopes s JOIN [identity].RoleAssignments a ON a.Id = s.RoleAssignmentId "
                + "WHERE a.UserId = @user; DELETE FROM [identity].RoleAssignments WHERE UserId = @user; SELECT @@ROWCOUNT;",
                ("@user", actor)) > 0);
            return ApprovedSalesLine(CompanyA, revision: 2);
        });
        await using var host = SalesLinkHost(run, sales);
        using var client = SalesLinkClient(host, user);

        var refused = await client.PostWithKeyAsync($"{Base}/plans/{planId}/sales-order-link",
            new ChangePlanSalesOrderLinkRequestV1(1, 0, LinkSalesOrder, LinkSalesLine), "revoked");

        // The in-service permission fence answers 403 with the shared `forbidden` code (only 404 is code-less here).
        await AssertSalesLinkProblemAsync(refused, HttpStatusCode.Forbidden, ErpProblemCodes.Forbidden);
        Assert.Equal(1, sales.Reads);
        Assert.Equal(0L, await SalesLinkRevisionAsync(run, planId));
        Assert.Equal(1L, await run.ScalarAsync<long>("SELECT Version FROM manufacturing.Plans WHERE Id = @id", ("@id", planId)));
        Assert.Equal(0, await SalesLinkedAuditCountAsync(run));
    }

    private static async Task<Guid> SalesLinkDraftPlanAsync(Run run, string user, string labelCode)
    {
        var workCenter = await run.WorkCenterOfAsync(CompanyA);
        var roll = await run.StockedLabelAsync(labelCode);
        using var client = run.ClientFor(user, CompanyA);
        var created = await client.PostWithKeyAsync($"{Base}/plans", new SavePlanRequestV1(Guid.NewGuid(), workCenter,
            [new PlannedInputRequestV1(roll.Id, "1000", roll.Version)], null));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<PlanViewV1>())!.PlanId;
    }

    private static SalesOrderLineReferenceAnswerV1 ApprovedSalesLine(Guid company, int revision) =>
        new(SalesOrderLineReferenceOutcomeV1.Approved,
            new SalesOrderLineReferenceV1(company, LinkSalesOrder, LinkSalesLine, 1, LinkCustomerParty, LinkCustomerAccount,
                LinkProduct, "1", Guid.NewGuid(), "10", revision, 1), null);

    private static WebApplicationFactory<Program> SalesLinkHost(Run run, FixedSalesLines sales) =>
        run.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISalesOrderLineReferencePortV1>();
            services.AddSingleton<ISalesOrderLineReferencePortV1>(sales);
        }));

    private static HttpClient SalesLinkClient(WebApplicationFactory<Program> host, string user, Guid? company = null)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", user);
        client.DefaultRequestHeaders.Add("X-Company-Id", (company ?? CompanyA).ToString());
        client.DefaultRequestHeaders.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
        return client;
    }

    private static async Task AssertSalesLinkProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, problem.RootElement.GetProperty("code").GetString());
    }

    /// <summary>404 carries no problem code on the wire: an empty body or a problem without <c>code</c>.</summary>
    private static async Task AssertSalesLinkCodeLessAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        if (body.Length > 0)
        {
            using var problem = JsonDocument.Parse(body);
            Assert.False(problem.RootElement.TryGetProperty("code", out _));
        }
    }

    private static Task<long> SalesLinkRevisionAsync(Run run, Guid planId) => run.ScalarAsync<long>(
        "SELECT SalesOrderLinkRevision FROM manufacturing.Plans WHERE Id = @id", ("@id", planId));

    private static Task<int> SalesLinkedAuditCountAsync(Run run) => run.ScalarAsync<int>(
        "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'manufacturing.plan.sales-order-linked.v1'");

    /// <summary>Sales's approved-line read answered by the test; counts the reads. The loading list is not used here.</summary>
    private sealed class FixedSalesLines(Func<(Guid OrderId, Guid LineId), Task<SalesOrderLineReferenceAnswerV1>> answer)
        : ISalesOrderLineReferencePortV1
    {
        private int reads;

        public int Reads => Volatile.Read(ref reads);

        public Task<SalesOrderLineReferenceAnswerV1> GetApprovedLineAsync(Guid orderId, Guid lineId,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref reads);
            return answer((orderId, lineId));
        }

        public Task<SalesApprovedLinePageV1> ListApprovedLinesAsync(int pageSize, string? cursor,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
