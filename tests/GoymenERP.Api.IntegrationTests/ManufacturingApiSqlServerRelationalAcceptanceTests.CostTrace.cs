using System.Net;
using System.Net.Http.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// URT-S19 (B-27, DEC-PRDCT-009, API-42) against REAL SQL Server through the REAL host: the order's cost trace answers
/// the stored raw data under its own registered grant (manufacturing.reports.read, given here to a synthetic role only),
/// the cost part is "pending / order_not_closed" while the actual order remains open (never a zero), a
/// caller without the reports grant is refused, another company's caller does not find the order, and reading it writes
/// nothing.
/// </summary>
public sealed partial class ManufacturingApiSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task The_actual_cost_trace_answers_the_stored_raw_data_and_a_pending_cost_for_an_open_order()
    {
        await using var run = await Run.StartAsync();
        var user = await run.GrantAsync(TestUsers.CompanyA, CompanyA,
            [.. AllWrites, ErpPermissions.ManufacturingReportsRead]);
        using var client = run.ClientFor(user, CompanyA);
        var (orderId, roll) = await run.OpenOrderAsync(user);
        var issued = await run.OkAsync(client, $"{Base}/orders/{orderId}/input-issues",
            Run.Issue(1, roll, "600.5", At));
        await run.OkAsync(client, $"{Base}/orders/{orderId}/outputs",
            await run.OutputAsync(client, issued.Version, roll, "GoodProduct", "590", At.AddMinutes(1)));
        var path = $"{Base}/orders/{orderId}/cost-trace";
        var commands = await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Commands");
        var audits = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents");

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);
        var trace = (await response.Content.ReadFromJsonAsync<CostTraceViewV1>())!;
        Assert.Equal((1, "weighted-average-raw-material-kg", orderId, "Open"),
            (trace.ContractVersion, trace.MethodCode, trace.OrderId, trace.Status));
        var lot = Assert.Single(trace.Lots);
        Assert.Equal((roll, "1000", "600.5", "0", "600.5"),
            (lot.ParentRollId, lot.PlannedKg, lot.IssuedKg, lot.ReturnedKg, lot.ConsumedKg));
        Assert.Equal([new CostTraceOutputViewV1("GoodProduct", "590", 1, 0)], trace.Outputs);
        Assert.Equal(("pending", "order_not_closed", (string?)null),
            (trace.Cost.Status, trace.Cost.ReasonCode, trace.Cost.CostPerKgTl));

        // The order read grant without the reports grant is refused; another company's caller does not find the order.
        var reader = await run.GrantAsync(TestUsers.NoAssignment, CompanyA, ErpPermissions.ManufacturingOrdersRead);
        using (var readerClient = run.ClientFor(reader, CompanyA))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await readerClient.GetAsync(path)).StatusCode);
        }

        var userB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, ErpPermissions.ManufacturingOrdersRead,
            ErpPermissions.ManufacturingReportsRead);
        using (var clientB = run.ClientFor(userB, CompanyB))
        {
            Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync(path)).StatusCode);
        }

        Assert.Equal((commands, audits), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Commands"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents")));
    }
}
