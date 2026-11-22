using System.Net;
using System.Net.Http.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// WP11 (DALGA_PLANI_v2 1.12): the explanations on the actual host and SQL Server — the production cost explanation's last
/// step is the production cost read's figure, the mass balance explanation's last step is the mass balance read's difference,
/// and a caller without the existing grant sees neither (403), another company's order is 404. Written, NOT_RUN locally.
/// </summary>
public sealed partial class ManufacturingApiSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task WP11_the_explanations_end_on_the_shown_figures_and_are_withheld_without_the_existing_grants()
    {
        await using var run = await Run.StartAsync();
        var user = await run.GrantAsync(TestUsers.CompanyA, CompanyA,
            [.. AllWrites, ErpPermissions.ManufacturingOrdersClose, ErpPermissions.ManufacturingReportsRead]);
        var other = await run.GrantAsync(TestUsers.CompanyB, CompanyB, [.. AllWrites, ErpPermissions.ManufacturingReportsRead]);
        var (orderId, roll) = await run.OpenOrderAsync(user, costedInput: true);
        using (var client = run.ClientFor(user, CompanyA))
        {
            var issued = await run.OkAsync(client, $"{Base}/orders/{orderId}/input-issues", Run.Issue(1, roll, "600", At));
            var balanced = await run.OkAsync(client, $"{Base}/orders/{orderId}/outputs",
                await run.OutputAsync(client, issued.Version, roll, "GoodProduct", "600", At.AddMinutes(1)));

            var balance = Assert.Single((await client.GetFromJsonAsync<MassBalanceViewV1>($"{Base}/orders/{orderId}/mass-balance"))!.Rolls);
            using var balanceAnswer = await client.GetAsync($"{Base}/orders/{orderId}/mass-balance/explanation");
            Assert.Equal(HttpStatusCode.OK, balanceAnswer.StatusCode);
            Assert.True(balanceAnswer.Headers.CacheControl is { Private: true, NoStore: true });
            var balanceExplained = Assert.Single((await balanceAnswer.Content.ReadFromJsonAsync<MassBalanceExplanationViewV1>())!.Rolls);
            Assert.Equal((balance.Difference.DifferenceKg, balance.Difference.DifferenceKg),
                (balanceExplained.DisplayedDifferenceKg, balanceExplained.Steps[^1].Value));

            await run.OkAsync(client, $"{Base}/orders/{orderId}/close", new CloseOrderRequestV1(balanced.Version));
            var cost = (await client.GetFromJsonAsync<ProductionCostViewV1>($"{Base}/orders/{orderId}/production-cost"))!;
            var explained = (await client.GetFromJsonAsync<ProductionCostExplanationViewV1>(
                $"{Base}/orders/{orderId}/production-cost/explanation"))!;
            Assert.Equal(("available", cost.CostPerKgTl, cost.CostPerKgTl),
                (explained.Status, explained.DisplayedCostPerKgTl, explained.Steps[^1].Value));
            Assert.Equal(ManufacturingExplanationCodesV1.CostPerKgRecorded, explained.Steps[^1].Code);
            Assert.NotEmpty(explained.Issues);
        }

        // Without the reports grant the cost explanation is withheld; another company's order is the code-less 404.
        var reader = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, [ErpPermissions.ManufacturingOrdersRead]);
        using (var client = run.ClientFor(reader, CompanyA))
        {
            Assert.Equal(HttpStatusCode.Forbidden,
                (await client.GetAsync($"{Base}/orders/{orderId}/production-cost/explanation")).StatusCode);
        }

        using (var client = run.ClientFor(other, CompanyB))
        {
            Assert.Equal(HttpStatusCode.NotFound,
                (await client.GetAsync($"{Base}/orders/{orderId}/production-cost/explanation")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await client.GetAsync($"{Base}/orders/{orderId}/mass-balance/explanation")).StatusCode);
        }
    }
}
