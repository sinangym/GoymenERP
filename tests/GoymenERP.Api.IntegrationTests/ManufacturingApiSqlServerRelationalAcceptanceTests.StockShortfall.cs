using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Manufacturing.Application;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-D318 (owner decision of 2026-11-13): no operation takes physical stock below zero, and the refusal says which
/// operation and where. Through the real host on real SQL Server the input issue's 409 keeps its status and code and
/// adds the optional <c>stockShortfall</c> member: the refused issue, its parent label with the product, the stored
/// place and the requested kilograms, never the label's stock figures. The refusal is not terminal: the same key and
/// body are decided again. Another company's caller never reaches it.
/// </summary>
public sealed partial class ManufacturingApiSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task An_input_issue_refused_for_physical_stock_names_the_issue_label_and_place_only_to_its_own_company()
    {
        await using var run = await Run.StartAsync();
        var userA = await run.GrantAsync(TestUsers.CompanyA, CompanyA, AllWrites);
        var userB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, AllWrites);
        var (orderId, roll) = await run.OpenOrderAsync(userA);
        // A quality block leaves 50 kg of the roll's 1000.125 kg Free, under the plan's 1000 kg allocation.
        await run.BlockAsync(roll, 950.125m);
        var location = await run.ScalarAsync<Guid>(
            "SELECT TOP (1) Id FROM organization.Locations WHERE CompanyId = @c ORDER BY Id", ("@c", CompanyA));
        var issueId = Guid.NewGuid();
        var issue = Run.Issue(1, roll, "100", At, issueId);
        var path = $"{Base}/orders/{orderId}/input-issues";

        using (var client = run.ClientFor(userA, CompanyA))
        {
            // The same key and body again: not a stored answer, the same refusal decided again.
            foreach (var attempt in new[] { "first", "same-key-again" })
            {
                using var refused = await client.PostWithKeyAsync(path, issue, "shortfall-issue-key");
                var body = await refused.Content.ReadAsStringAsync();
                Assert.True(refused.StatusCode == HttpStatusCode.Conflict, $"{attempt}: {body}");
                Assert.False(refused.Headers.Contains("Idempotency-Replayed"), attempt);
                using var problem = JsonDocument.Parse(body);
                Assert.Equal(InventoryManufacturingRealizationCodesV1.PhysicalStockInsufficient,
                    problem.RootElement.GetProperty("code").GetString());
                var shortfall = problem.RootElement.GetProperty("stockShortfall");
                Assert.Equal(
                    new[]
                    {
                        "labelId", "locationId", "movementKind", "productId", "requestedKg", "sourceId", "sourceKind",
                        "sourceLineId",
                    },
                    shortfall.EnumerateObject().Select(member => member.Name).Order(StringComparer.Ordinal));
                Assert.Equal(("Issue", InventoryManufacturingRealizationContractV1.InputIssueSourceKind, "100"),
                    (shortfall.GetProperty("movementKind").GetString(), shortfall.GetProperty("sourceKind").GetString(),
                        shortfall.GetProperty("requestedKg").GetString()));
                Assert.Equal((orderId, issueId, roll, run.ProductFixture.ProductId, location),
                    (shortfall.GetProperty("sourceId").GetGuid(), shortfall.GetProperty("sourceLineId").GetGuid(),
                        shortfall.GetProperty("labelId").GetGuid(), shortfall.GetProperty("productId").GetGuid(),
                        shortfall.GetProperty("locationId").GetGuid()));
            }

            var order = (await client.GetFromJsonAsync<OrderViewV1>($"{Base}/orders/{orderId}"))!;
            Assert.Equal(1L, order.Version);
        }

        // Company B's caller with A's order and label: the write stops at B's own Settings check (422, its kilogram
        // profile is not in effect at the fact's date) before any order or stock is read, and names nothing of A's.
        using (var client = run.ClientFor(userB, CompanyB))
        {
            using var foreign = await client.PostWithKeyAsync(path, issue);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, foreign.StatusCode);
            await AssertNoStockShortfallAsync(foreign, roll, location);
        }

        // B's caller naming company A is refused before anything is read.
        using (var client = run.ClientFor(userB, CompanyA))
        {
            using var forbidden = await client.PostWithKeyAsync(path, issue);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            await AssertNoStockShortfallAsync(forbidden, roll, location);
        }

        // Nothing was issued: no fact, the allocation and the Free kg as the block left them.
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Facts"));
        Assert.Equal(1000m, await run.ScalarAsync<decimal>(
            "SELECT SUM(RemainingKg) FROM inventory.Allocations WHERE LabelId = @label AND Status = 1",
            ("@label", roll)));
        Assert.Equal(50m, await run.ScalarAsync<decimal>(
            "SELECT QuantityKg FROM inventory.Balances WHERE LabelId = @label AND Block = 1", ("@label", roll)));
    }

    private static async Task AssertNoStockShortfallAsync(HttpResponseMessage response, Guid label, Guid location)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("stockShortfall", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(label.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(location.ToString(), body, StringComparison.OrdinalIgnoreCase);
    }
}
