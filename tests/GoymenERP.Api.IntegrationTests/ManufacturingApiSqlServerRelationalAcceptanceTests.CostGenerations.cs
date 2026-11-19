using System.Net;
using System.Net.Http.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Domain.Periods;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Manufacturing.Application;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingApiSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task The_actual_host_recognizes_the_original_TL_USD_issue_stamps_and_keeps_non_kg_consumption_out_of_the_price_formula()
    {
        await using var run = await Run.StartAsync();
        var user = await run.GrantAsync(TestUsers.CompanyA, CompanyA,
            [.. AllWrites, ErpPermissions.ManufacturingOrdersClose, ErpPermissions.ManufacturingReportsRead]);
        using var client = run.ClientFor(user, CompanyA);
        var (orderId, roll) = await run.OpenOrderAsync(user, costedInput: true);
        var issued = await run.OkAsync(client, $"{Base}/orders/{orderId}/input-issues", Run.Issue(1, roll, "600", At));
        var balanced = await run.OkAsync(client, $"{Base}/orders/{orderId}/outputs",
            await run.OutputAsync(client, issued.Version, roll, "GoodProduct", "600", At.AddMinutes(1)));
        using (var measured = await client.PostWithKeyAsync($"{Base}/orders/{orderId}/non-kg-inputs",
            new RecordManufacturingNonKgInputRequestV1(Guid.NewGuid(), balanced.Version, 0, "Paint",
                "Synthetic measured paint", "Litre", "2.25", At, null, "Synthetic actual use")))
            Assert.Equal(HttpStatusCode.OK, measured.StatusCode);
        var closed = await run.OkAsync(client, $"{Base}/orders/{orderId}/close", new CloseOrderRequestV1(balanced.Version));
        var generation = Assert.Single((await client.GetFromJsonAsync<ManufacturingCostGenerationHistoryV1>($"{Base}/orders/{orderId}/cost-generations"))!.Generations);
        Assert.Equal(("Recognized", closed.Version), (generation.State, generation.ClosedOrderVersion));
        var output = Assert.Single(generation.Outputs);
        Assert.Equal(("6000", "6000", "0", "2"), (output.OriginalTlAmount, output.RemainingTlValue, output.ConsumedTlDifference, output.UsdUnitCostPerKg));
        var cost = (await client.GetFromJsonAsync<ProductionCostViewV1>($"{Base}/orders/{orderId}/production-cost"))!;
        Assert.Equal(("available", "10", "6000"), (cost.Status, cost.CostPerKgTl, cost.InputTotalTl));
        Assert.Contains("paint_cost_not_included", cost.NoteCodes);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.ManufacturingCostGenerationHistory"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Journals"));
    }

    [SqlServerFact]
    public async Task The_actual_host_closes_with_non_kg_facts_and_unknown_prices_then_reopens_both_owners_and_closes_a_new_generation()
    {
        await using var run = await Run.StartAsync();
        var user = await run.GrantAsync(TestUsers.CompanyA, CompanyA,
            [.. AllWrites, ErpPermissions.ManufacturingOrdersClose, ErpPermissions.ManufacturingOrdersReopen,
                ErpPermissions.ManufacturingReportsRead, ErpPermissions.AccountingPeriodsRead]);
        using var client = run.ClientFor(user, CompanyA);
        var (orderId, roll) = await run.OpenOrderAsync(user);
        var issued = await run.OkAsync(client, $"{Base}/orders/{orderId}/input-issues", Run.Issue(1, roll, "600", At));
        var balanced = await run.OkAsync(client, $"{Base}/orders/{orderId}/outputs",
            await run.OutputAsync(client, issued.Version, roll, "GoodProduct", "600", At.AddMinutes(1)));
        // This is a real actual-consumption owner fact. Its unit is never turned into estimated kg or a guessed price.
        using (var measured = await client.PostWithKeyAsync($"{Base}/orders/{orderId}/non-kg-inputs",
            new RecordManufacturingNonKgInputRequestV1(Guid.NewGuid(), balanced.Version, 0, "Other",
                "Synthetic actual packaging", "Piece", "3", At, null, "Synthetic measured consumption")))
            Assert.Equal(HttpStatusCode.OK, measured.StatusCode);
        var before = (await client.GetFromJsonAsync<OrderViewV1>($"{Base}/orders/{orderId}"))!;
        var closed = await run.OkAsync(client, $"{Base}/orders/{orderId}/close", new CloseOrderRequestV1(before.Version), "actual-cost-close");
        Assert.Equal("Closed", closed.Status);
        var path = $"{Base}/orders/{orderId}/cost-generations";
        var first = Assert.Single((await client.GetFromJsonAsync<ManufacturingCostGenerationHistoryV1>(path))!.Generations);
        Assert.Equal(("Unreadable", closed.Version), (first.State, first.ClosedOrderVersion));
        Assert.All(first.Outputs, output => Assert.Null(output.OriginalTlAmount));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.ManufacturingCostGenerations"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.NonKgInputFacts"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Allocations WHERE LabelId = @label AND ReleasedKg = 400 AND Status = 3", ("@label", roll)));
        using (var replay = await client.PostWithKeyAsync($"{Base}/orders/{orderId}/close", new CloseOrderRequestV1(before.Version), "actual-cost-close"))
        {
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal("true", replay.Headers.GetValues("Idempotency-Replayed").Single());
        }
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.ManufacturingCostGenerationHistory"));
        // A disposable real Accounting period is the owner evidence required by URT-D36, not an always-open fake port.
        var company = new CompanyContext(); company.Set(CompanyA);
        await using (var accounting = new AccountingDbContext(new DbContextOptionsBuilder<AccountingDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString).Options, company))
        {
            var now = DateTimeOffset.UtcNow;
            accounting.Periods.Add(new PeriodRow { Id = Guid.NewGuid(), CompanyId = CompanyA,
                StartsOn = new DateOnly(now.Year, 1, 1), EndsOn = new DateOnly(now.Year, 12, 31),
                Status = (int)AccountingPeriodStatus.Open, PreparedByUserId = Guid.NewGuid(), PreparedAtUtc = now,
                ApprovedByUserId = Guid.NewGuid(), ApprovedAtUtc = now, Version = 1, CreatedAtUtc = now, UpdatedAtUtc = now });
            await accounting.SaveChangesAsync();
        }
        var reopened = await run.OkAsync(client, $"{Base}/orders/{orderId}/reopening",
            new ReopenManufacturingOrderRequestV1(closed.Version, "Synthetic actual reopening"));
        Assert.Equal("Open", reopened.Status);
        var history = (await client.GetFromJsonAsync<ManufacturingCostGenerationHistoryV1>(path))!.Generations;
        Assert.Equal(["Unreadable", "Reversed"], history.Select(item => item.State));
        Assert.Equal(first.GenerationId, history[1].GenerationId);
        var reclosed = await run.OkAsync(client, $"{Base}/orders/{orderId}/close", new CloseOrderRequestV1(reopened.Version));
        history = (await client.GetFromJsonAsync<ManufacturingCostGenerationHistoryV1>(path))!.Generations;
        Assert.Equal(["Unreadable", "Reversed", "Unreadable"], history.Select(item => item.State));
        Assert.NotEqual(first.GenerationId, history[2].GenerationId);
        Assert.Equal(reclosed.Version, history[2].ClosedOrderVersion);
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.ManufacturingCostGenerations"));
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.ManufacturingCostGenerationHistory"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.NonKgInputFacts"));
    }

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
