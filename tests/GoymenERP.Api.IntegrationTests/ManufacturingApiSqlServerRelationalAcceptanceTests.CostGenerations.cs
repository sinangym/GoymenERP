using System.Net;
using System.Net.Http.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingApiSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Cost_generation_history_is_a_private_company_scoped_read_and_a_close_grant_does_not_disclose_financial_results()
    {
        await using var run = await Run.StartAsync();
        var user = await run.GrantAsync(TestUsers.CompanyA, CompanyA, [.. AllWrites, ErpPermissions.ManufacturingReportsRead]);
        var (orderId, _) = await run.OpenOrderAsync(user);
        var commandsBefore = await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Commands");
        var auditsBefore = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents");
        using (var client = run.ClientFor(user, CompanyA))
        {
            using var response = await client.GetAsync($"{Base}/orders/{orderId}/cost-generations");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl is { Private: true, NoStore: true });
            var body = (await response.Content.ReadFromJsonAsync<ManufacturingCostGenerationHistoryV1>())!;
            Assert.Equal(orderId, body.OrderId); Assert.Empty(body.Generations);
        }
        var readerB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, ErpPermissions.ManufacturingReportsRead);
        using (var client = run.ClientFor(readerB, CompanyB))
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Base}/orders/{orderId}/cost-generations")).StatusCode);
        var closer = await run.GrantAsync(TestUsers.NoAssignment, CompanyA, ErpPermissions.ManufacturingOrdersClose);
        using (var client = run.ClientFor(closer, CompanyA))
        {
            using var denied = await client.PostWithKeyAsync($"{Base}/orders/{orderId}/cost-recognition",
                new RecognizeManufacturingOrderCostRequestV1(2, 0, "Synthetic disclosure fence"));
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        Assert.Equal(commandsBefore, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Commands"));
        Assert.Equal(auditsBefore, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));
    }
}
