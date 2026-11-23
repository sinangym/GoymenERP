using System.Net;
using System.Net.Http.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// URT-S17 (SAT-D317) against REAL SQL Server through the REAL host: the order cost view answers under the reports grant
/// (manufacturing.reports.read, given here to a synthetic role only) with the order's stored status and version, and is
/// "pending / order_not_closed" while the actual order remains open (never a zero figure); a caller
/// without the reports grant is refused, another company's caller does not find the order, and reading it writes
/// nothing.
/// </summary>
public sealed partial class ManufacturingApiSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task The_actual_production_cost_is_pending_for_an_open_order_under_the_reports_grant_and_writes_nothing()
    {
        await using var run = await Run.StartAsync();
        var user = await run.GrantAsync(TestUsers.CompanyA, CompanyA,
            [.. AllWrites, ErpPermissions.ManufacturingReportsRead]);
        using var client = run.ClientFor(user, CompanyA);
        var (orderId, roll) = await run.OpenOrderAsync(user);
        var issued = await run.OkAsync(client, $"{Base}/orders/{orderId}/input-issues",
            Run.Issue(1, roll, "600.5", At));
        var output = await run.OkAsync(client, $"{Base}/orders/{orderId}/outputs",
            await run.OutputAsync(client, issued.Version, roll, "GoodProduct", "590", At.AddMinutes(1)));
        var path = $"{Base}/orders/{orderId}/production-cost";
        var commands = await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Commands");
        var audits = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents");

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);
        var cost = (await response.Content.ReadFromJsonAsync<ProductionCostViewV1>())!;
        Assert.Equal((1, orderId, "Open", output.Version, "pending", "order_not_closed"),
            (cost.ContractVersion, cost.OrderId, cost.OrderStatus, cost.OrderVersion, cost.Status, cost.ReasonCode));
        Assert.Equal(((string?)null, (string?)null, (string?)null, (string?)null),
            (cost.CostPerKgTl, cost.InputTotalTl, cost.DistributedTotalTl, cost.RemainderTl));
        Assert.Empty(cost.Lines);
        Assert.Empty(cost.CurrencyLines);

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
