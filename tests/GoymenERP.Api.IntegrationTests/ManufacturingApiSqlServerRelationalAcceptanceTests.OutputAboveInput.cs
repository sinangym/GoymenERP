using System.Net;
using System.Net.Http.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// URT-D41/URT-D42 (SINAN_KARARLARI_2026-11-13) against REAL SQL Server through the REAL host, on a business day of the
/// reviewed version-0 catalog (± 2 % shortfall tolerance, URT-D6/URT-D23 unchanged): a parent roll whose output exceeds
/// its input never closes — no tolerance, no reasoned exception approval — and the refusal stores nothing; the check is
/// per parent roll, so another roll's shortfall does not hide it; and the read-only report lists the company's earlier
/// closes with output above input, per roll, without reopening or changing any of them (URT-D42 (3)).
/// </summary>
public sealed partial class ManufacturingApiSqlServerRelationalAcceptanceTests
{
    private const string OutputAboveInputReportPath = $"{Base}/reports/output-above-input-closures";

    [SqlServerFact]
    public async Task Output_above_input_on_a_parent_roll_refuses_the_close_with_no_tolerance_and_no_approval_path()
    {
        await using var run = await Run.StartAsync(useSqlSettings: true, clockAt: CatalogDay);
        var closer = await run.GrantAsync(TestUsers.CompanyA, CompanyA,
            [.. AllWrites, ErpPermissions.ManufacturingOrdersClose]);
        using var client = run.ClientFor(closer, CompanyA);

        // 600 issued, 6 scrap and 603 good: 9 kg (1.5 %) more out than in. Inside the ± 2 % band, and still refused.
        var (orderId, roll) = await run.OpenOrderAsync(closer);
        var issued = await run.OkAsync(client, $"{Base}/orders/{orderId}/input-issues",
            Run.Issue(1, roll, "600", CatalogDay.AddMinutes(-30)));
        var scrap = await run.OkAsync(client, $"{Base}/orders/{orderId}/outputs",
            await run.OutputAsync(client, issued.Version, roll, "Scrap", "6", CatalogDay.AddMinutes(-20)));
        var good = await run.OkAsync(client, $"{Base}/orders/{orderId}/outputs",
            await run.OutputAsync(client, scrap.Version, roll, "GoodProduct", "603", CatalogDay.AddMinutes(-10)));
        Assert.Equal("-9", Assert.Single(good.Rolls).UnexplainedKg);
        var commandsBefore = await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Commands");
        var auditBefore = await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType LIKE N'manufacturing.%'");

        async Task AssertOutputAboveInputAsync(HttpClient caller, string? reason)
        {
            using var refused = await caller.PostWithKeyAsync($"{Base}/orders/{orderId}/close",
                new CloseOrderRequestV1(good.Version, reason));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
            Assert.Equal(ClosureAssessmentCodesV1.OutputExceedsInput, (await Problem(refused)).Code);
        }

        // The normal close, a reasoned close without the exception grant (not a 403) and the reasoned close of a caller
        // who holds the exception approval (not a close): the same refusal each time. There is no approval path.
        await AssertOutputAboveInputAsync(client, null);
        await AssertOutputAboveInputAsync(client, "SENTETIK fazla çıktı");
        var approver = await run.GrantAsync(TestUsers.CompanyA, CompanyA,
            ErpPermissions.ManufacturingOrdersCloseExceptionApprove);
        using (var approverClient = run.ClientFor(approver, CompanyA))
        {
            await AssertOutputAboveInputAsync(approverClient, "SENTETIK fazla çıktı");
        }

        // The refusal stores nothing: the order stays open at its version, with no close decision, no command, no audit
        // event, and the plan's allocation is not released (600 of the 1000 allocated kg used, 400 still held).
        var open = (await client.GetFromJsonAsync<OrderViewV1>($"{Base}/orders/{orderId}"))!;
        Assert.Equal(("Open", good.Version), (open.Status, open.Version));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.Orders WHERE CompanyId = @c AND Id = @o AND Status = 1 AND ClosedAtUtc IS NULL",
            ("@c", CompanyA), ("@o", orderId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.OrderClosureDecisions WHERE CompanyId = @c AND OrderId = @o",
            ("@c", CompanyA), ("@o", orderId)));
        Assert.Equal((commandsBefore, auditBefore), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Commands"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType LIKE N'manufacturing.%'")));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Allocations WHERE LabelId = @l AND Status = 1 AND RemainingKg = 400",
            ("@l", roll)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Allocations WHERE LabelId = @l AND Status = 3", ("@l", roll)));

        // Once the roll's facts no longer show more out than in (here a further 10 kg issue: 610 in, 609 out), the close
        // follows the unchanged shortfall rule: 1 kg short, inside the band, closed with no reason and no approval.
        var corrected = await run.OkAsync(client, $"{Base}/orders/{orderId}/input-issues",
            Run.Issue(good.Version, roll, "10", CatalogDay.AddMinutes(-5)));
        Assert.Equal("1", Assert.Single(corrected.Rolls).UnexplainedKg);
        var closed = await run.OkAsync(client, $"{Base}/orders/{orderId}/close", new CloseOrderRequestV1(corrected.Version));
        Assert.Equal("Closed", closed.Status);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Allocations WHERE LabelId = @l AND Status = 3 "
            + "AND ReleaseReason = N'manufacturing.order-closed' AND ReleasedKg = 390", ("@l", roll)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Allocations WHERE LabelId = @l AND Status = 1", ("@l", roll)));

        // Per parent roll (URT-D42 (1)): one roll 5 kg above its input, the other 15 kg short. Summed, the order would be
        // 10 kg (0.5 %) short and inside the band; the roll above its input refuses the close all the same.
        var firstRoll = await run.StockedLabelAsync($"SYN-MFG-ABOVE-1-{Guid.NewGuid():N}");
        var secondRoll = await run.StockedLabelAsync($"SYN-MFG-ABOVE-2-{Guid.NewGuid():N}");
        var plan = new SavePlanRequestV1(Guid.NewGuid(), await run.WorkCenterOfAsync(CompanyA),
            [new PlannedInputRequestV1(firstRoll.Id, "1000", firstRoll.Version),
                new PlannedInputRequestV1(secondRoll.Id, "1000", secondRoll.Version)], null);
        Assert.Equal(HttpStatusCode.Created, (await client.PostWithKeyAsync($"{Base}/plans", plan)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await run.ApproverAsync(CompanyA)).PostWithKeyAsync(
            $"{Base}/plans/{plan.PlanId}/approval", new ApprovePlanRequestV1(1))).StatusCode);
        var twoRollOrder = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await client.PostWithKeyAsync($"{Base}/orders",
            new CreateOrderRequestV1(twoRollOrder, plan.PlanId))).StatusCode);
        var firstIssued = await run.OkAsync(client, $"{Base}/orders/{twoRollOrder}/input-issues",
            Run.Issue(1, firstRoll.Id, "1000", CatalogDay.AddMinutes(-40)));
        var secondIssued = await run.OkAsync(client, $"{Base}/orders/{twoRollOrder}/input-issues",
            Run.Issue(firstIssued.Version, secondRoll.Id, "1000", CatalogDay.AddMinutes(-35)));
        var above = await run.OkAsync(client, $"{Base}/orders/{twoRollOrder}/outputs",
            await run.OutputAsync(client, secondIssued.Version, firstRoll.Id, "GoodProduct", "1005", CatalogDay.AddMinutes(-30)));
        var secondScrap = await run.OkAsync(client, $"{Base}/orders/{twoRollOrder}/outputs",
            await run.OutputAsync(client, above.Version, secondRoll.Id, "Scrap", "10", CatalogDay.AddMinutes(-25)));
        var under = await run.OkAsync(client, $"{Base}/orders/{twoRollOrder}/outputs",
            await run.OutputAsync(client, secondScrap.Version, secondRoll.Id, "GoodProduct", "975", CatalogDay.AddMinutes(-20)));
        Assert.Equal(("-5", "15"), (
            under.Rolls.Single(line => line.ParentRollId == firstRoll.Id).UnexplainedKg,
            under.Rolls.Single(line => line.ParentRollId == secondRoll.Id).UnexplainedKg));
        using (var refused = await client.PostWithKeyAsync($"{Base}/orders/{twoRollOrder}/close",
                   new CloseOrderRequestV1(under.Version)))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
            Assert.Equal(ClosureAssessmentCodesV1.OutputExceedsInput, (await Problem(refused)).Code);
        }

        var stillOpen = (await client.GetFromJsonAsync<OrderViewV1>($"{Base}/orders/{twoRollOrder}"))!;
        Assert.Equal(("Open", under.Version), (stillOpen.Status, stillOpen.Version));
    }

    [SqlServerFact]
    public async Task The_report_lists_earlier_closes_with_output_above_input_per_roll_and_changes_none_of_them()
    {
        await using var run = await Run.StartAsync(useSqlSettings: true, clockAt: CatalogDay);
        var closer = await run.GrantAsync(TestUsers.CompanyA, CompanyA,
            [.. AllWrites, ErpPermissions.ManufacturingOrdersClose, ErpPermissions.ManufacturingOrdersCloseExceptionApprove]);
        using var client = run.ClientFor(closer, CompanyA);

        // An order closed through today's API: 1000 issued, 10 scrap and the given good output.
        async Task<(Guid OrderId, Guid Roll)> ClosedAsync(string goodKg, string? reason)
        {
            var (orderId, roll) = await run.OpenOrderAsync(closer);
            var issued = await run.OkAsync(client, $"{Base}/orders/{orderId}/input-issues",
                Run.Issue(1, roll, "1000", CatalogDay.AddMinutes(-30)));
            var scrap = await run.OkAsync(client, $"{Base}/orders/{orderId}/outputs",
                await run.OutputAsync(client, issued.Version, roll, "Scrap", "10", CatalogDay.AddMinutes(-20)));
            var good = await run.OkAsync(client, $"{Base}/orders/{orderId}/outputs",
                await run.OutputAsync(client, scrap.Version, roll, "GoodProduct", goodKg, CatalogDay.AddMinutes(-10)));
            var closed = await run.OkAsync(client, $"{Base}/orders/{orderId}/close",
                new CloseOrderRequestV1(good.Version, reason));
            Assert.Equal("Closed", closed.Status);
            return (orderId, roll);
        }

        // A close stored before URT-D41 can no longer be made through the API: its stored good output is raised in place,
        // so the stored close replays as such an order would, closed with more accounted than issued on its roll.
        async Task RaiseGoodOutputAsync(Guid orderId, decimal kg) =>
            Assert.Equal(1, await run.ExecuteAsync(
                "UPDATE manufacturing.Facts SET MassKg = @kg WHERE CompanyId = @c AND OrderId = @o AND Kind = 3 AND OutputClass = 1",
                ("@kg", kg), ("@c", CompanyA), ("@o", orderId)));

        // A normal close (15 kg short), an earlier close inside the stored tolerance (15 kg above after the raise) and an
        // earlier close with the reasoned exception approval (40 kg short when approved, 40 kg above after the raise).
        var normal = await ClosedAsync("975", null);
        var earlier = await ClosedAsync("975", null);
        await RaiseGoodOutputAsync(earlier.OrderId, 1005m);
        var approved = await ClosedAsync("950", "SENTETIK hat sapması");
        await RaiseGoodOutputAsync(approved.OrderId, 1030m);

        // An open order with output above input is not a close: it is in no page of the report.
        var (openOrder, openRoll) = await run.OpenOrderAsync(closer);
        var openIssued = await run.OkAsync(client, $"{Base}/orders/{openOrder}/input-issues",
            Run.Issue(1, openRoll, "1000", CatalogDay.AddMinutes(-30)));
        await run.OkAsync(client, $"{Base}/orders/{openOrder}/outputs",
            await run.OutputAsync(client, openIssued.Version, openRoll, "GoodProduct", "1005", CatalogDay.AddMinutes(-10)));

        // Both stored closes replay unchanged: the order read rebuilds them closed, with the raised output.
        var earlierRead = (await client.GetFromJsonAsync<OrderViewV1>($"{Base}/orders/{earlier.OrderId}"))!;
        var approvedRead = (await client.GetFromJsonAsync<OrderViewV1>($"{Base}/orders/{approved.OrderId}"))!;
        Assert.Equal(("Closed", "-15"), (earlierRead.Status, Assert.Single(earlierRead.Rolls).UnexplainedKg));
        Assert.Equal(("Closed", "-40"), (approvedRead.Status, Assert.Single(approvedRead.Rolls).UnexplainedKg));
        var reads = new Dictionary<Guid, (OrderViewV1 Read, Guid Roll, string AccountedKg, string DifferenceKg, bool Approved)>
        {
            [earlier.OrderId] = (earlierRead, earlier.Roll, "1015", "-15", false),
            [approved.OrderId] = (approvedRead, approved.Roll, "1040", "-40", true),
        };
        var closedBy = await run.UserIdOfAsync(TestUsers.CompanyA);

        // What the report must leave exactly as it found it: every order row of the company (status, version, close
        // moment, row version), every stored fact, the close decisions, the module's audit events and the command log.
        async Task<(string Orders, string Facts, int Decisions, int Audit, int Commands)> SnapshotAsync() => (
            await run.ScalarAsync<string>(
                "SELECT COALESCE(STRING_AGG(CAST(CONCAT(CONVERT(nvarchar(36), Id), N'|', Status, N'|', Version, N'|', "
                + "CONVERT(nvarchar(40), ClosedAtUtc, 127), N'|', "
                + "CONVERT(nvarchar(20), CONVERT(varbinary(8), RowVersion), 1)) AS nvarchar(max)), N',') "
                + "WITHIN GROUP (ORDER BY Id), N'') FROM manufacturing.Orders WHERE CompanyId = @c", ("@c", CompanyA)),
            await run.ScalarAsync<string>(
                "SELECT COALESCE(STRING_AGG(CAST(CONCAT(Kind, N'|', CONVERT(nvarchar(36), Id), N'|', MassKg) AS nvarchar(max)), "
                + "N',') WITHIN GROUP (ORDER BY Kind, Id), N'') FROM manufacturing.Facts WHERE CompanyId = @c", ("@c", CompanyA)),
            await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM manufacturing.OrderClosureDecisions WHERE CompanyId = @c", ("@c", CompanyA)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType LIKE N'manufacturing.%'"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Commands"));
        var before = await SnapshotAsync();

        // The server's own sequence of this company's closes, latest close first. Every close here happened at the same
        // fixed instant, so SQL Server's uniqueidentifier ordering of the ids decides: read from SQL, not assumed.
        var closedSequence = (await run.ScalarAsync<string>(
                "SELECT STRING_AGG(CONVERT(nvarchar(36), Id), N',') WITHIN GROUP (ORDER BY ClosedAtUtc DESC, Id DESC) "
                + "FROM manufacturing.Orders WHERE CompanyId = @c AND Status = 2", ("@c", CompanyA)))
            .Split(',').Select(Guid.Parse).ToList();
        Assert.Equal(3, closedSequence.Count);
        Assert.Contains(normal.OrderId, closedSequence);
        var listed = closedSequence.Where(reads.ContainsKey).ToList();
        Assert.Equal(2, listed.Count);

        // One page: the three closes examined, the two earlier closes listed in that sequence, each with only its roll
        // above input, the numbers of the mass-balance read, the stored closer and moment and how the earlier rule let
        // it close. The normal close and the open order are not listed.
        using (var response = await client.GetAsync(OutputAboveInputReportPath))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl is { Private: true, NoStore: true });
            var report = (await response.Content.ReadFromJsonAsync<OutputAboveInputClosureReportViewV1>())!;
            Assert.Equal(("GDC-BAL/1 (master v3 §28.9)", 3, (string?)null),
                (report.FormulaVersion, report.ExaminedOrderCount, report.NextCursor));
            Assert.Equal(listed, report.Closures.Select(closure => closure.OrderId));
            foreach (var closure in report.Closures)
            {
                var (read, roll, accounted, difference, approvedClose) = reads[closure.OrderId];
                Assert.Equal((read.PlanId, read.WorkCenterId, "Slitting", read.Version, closedBy, CatalogDay, approvedClose),
                    (closure.PlanId, closure.WorkCenterId, closure.Process, closure.Version, closure.ClosedByUserId,
                        closure.ClosedAtUtc, closure.ClosedWithExceptionApproval));
                var line = Assert.Single(closure.Rolls);
                Assert.Equal(new OutputAboveInputRollViewV1(roll, new MassDifferenceViewV1("1000", accounted, difference, false)),
                    line);
                var balance = (await client.GetFromJsonAsync<MassBalanceViewV1>($"{Base}/orders/{closure.OrderId}/mass-balance"))!;
                Assert.Equal(Assert.Single(balance.Rolls).Difference, line.Difference);
            }
        }

        // One closed order per page: the walk examines every close once, in the server's sequence, and lists a close on
        // the page that examined it; the page that examined only the normal close lists nothing and still goes on.
        var walked = new List<OutputAboveInputClosureReportViewV1>();
        string? cursor = null;
        do
        {
            var path = cursor is null
                ? $"{OutputAboveInputReportPath}?pageSize=1"
                : $"{OutputAboveInputReportPath}?pageSize=1&cursor={Uri.EscapeDataString(cursor)}";
            var page = (await client.GetFromJsonAsync<OutputAboveInputClosureReportViewV1>(path))!;
            walked.Add(page);
            cursor = page.NextCursor;
        }
        while (cursor is not null && walked.Count < 10);
        Assert.Equal(3, walked.Count);
        Assert.Null(walked[^1].NextCursor);
        for (var index = 0; index < walked.Count; index++)
        {
            Guid[] expected = listed.Contains(closedSequence[index]) ? [closedSequence[index]] : [];
            Assert.Equal(1, walked[index].ExaminedOrderCount);
            Assert.Equal(expected, walked[index].Closures.Select(closure => closure.OrderId));
        }

        var firstCursor = walked[0].NextCursor;
        Assert.NotNull(firstCursor);

        // A cursor that is not this company's closed-order cursor is refused on the cursor, never a first page:
        // unreadable, another company's, and the order list's and the plan list's own cursors for this very company.
        foreach (var refusedCursor in new[]
                 {
                     "not-a-cursor",
                     ManufacturingStore.ListCursor.Encode(CompanyB, CatalogDay, Guid.NewGuid(),
                         ManufacturingStore.ListCursor.ClosedOrdering),
                     ManufacturingStore.ListCursor.Encode(CompanyA, CatalogDay, Guid.NewGuid()),
                     ManufacturingStore.ListCursor.Encode(CompanyA, CatalogDay, Guid.NewGuid(),
                         ManufacturingStore.ListCursor.PlanOrdering),
                 })
        {
            using var refused = await client.GetAsync(
                $"{OutputAboveInputReportPath}?cursor={Uri.EscapeDataString(refusedCursor)}");
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Equal(ManufacturingApiCodes.RequestInvalid, (await Problem(refused)).Code);
        }

        // A page size outside 1..100 is refused on the page size.
        foreach (var refusedSize in new[] { "0", "101" })
        {
            using var refused = await client.GetAsync($"{OutputAboveInputReportPath}?pageSize={refusedSize}");
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Equal(ManufacturingApiCodes.RequestInvalid, (await Problem(refused)).Code);
        }

        // And this report's cursor is not an order-list cursor either.
        using (var crossed = await client.GetAsync($"{Base}/orders?cursor={Uri.EscapeDataString(firstCursor!)}"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, crossed.StatusCode);
        }

        // The order read grant alone is enough: a company-A user holding nothing else sees the same report.
        var reader = await run.GrantAsync(TestUsers.NoAssignment, CompanyA, ErpPermissions.ManufacturingOrdersRead);
        using (var readerClient = run.ClientFor(reader, CompanyA))
        {
            var report = (await readerClient.GetFromJsonAsync<OutputAboveInputClosureReportViewV1>(OutputAboveInputReportPath))!;
            Assert.Equal(listed, report.Closures.Select(closure => closure.OrderId));
        }

        // Company B's report is its own: none of A's closes is examined, and A's cursor is not one of B's cursors.
        var userB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, ErpPermissions.ManufacturingOrdersRead);
        using (var clientB = run.ClientFor(userB, CompanyB))
        {
            var report = (await clientB.GetFromJsonAsync<OutputAboveInputClosureReportViewV1>(OutputAboveInputReportPath))!;
            Assert.Equal((0, 0, (string?)null), (report.ExaminedOrderCount, report.Closures.Count, report.NextCursor));
            using var foreignCursor = await clientB.GetAsync(
                $"{OutputAboveInputReportPath}?cursor={Uri.EscapeDataString(firstCursor!)}");
            Assert.Equal(HttpStatusCode.BadRequest, foreignCursor.StatusCode);
            Assert.Equal(ManufacturingApiCodes.RequestInvalid, (await Problem(foreignCursor)).Code);
        }

        // B's user naming company A, A's read-only user without a Manufacturing permission and a user holding another
        // Manufacturing grant only: refused before any read.
        using (var bNamingA = run.ClientFor(userB, CompanyA))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await bNamingA.GetAsync(OutputAboveInputReportPath)).StatusCode);
        }

        using (var viewer = run.ClientFor(TestUsers.CompanyAViewer, CompanyA))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync(OutputAboveInputReportPath)).StatusCode);
        }

        var plannerOnly = await run.GrantAsync(TestUsers.Unprovisioned, CompanyA, ErpPermissions.ManufacturingPlansWrite);
        using (var planner = run.ClientFor(plannerOnly, CompanyA))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await planner.GetAsync(OutputAboveInputReportPath)).StatusCode);
        }

        // Without a company context nothing runs.
        using (var noContext = run.Factory.CreateClient())
        {
            noContext.DefaultRequestHeaders.Add("X-Development-User", reader);
            noContext.DefaultRequestHeaders.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
            Assert.Equal(HttpStatusCode.BadRequest, (await noContext.GetAsync(OutputAboveInputReportPath)).StatusCode);
        }

        // The report changed nothing: no order reopened, re-versioned or re-stamped, no fact, decision, audit event or
        // command written.
        Assert.Equal(before, await SnapshotAsync());
    }
}
