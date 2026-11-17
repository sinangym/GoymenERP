using System.Net;
using System.Net.Http.Json;
using GoymenERP.Api.ManufacturingRuntime;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Manufacturing.Application;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingApiSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task A_late_actual_inventory_generation_failure_rolls_back_the_HTTP_close_release_audit_and_key_then_the_same_key_succeeds()
    {
        await using var run = await Run.StartAsync();
        var user = await run.GrantAsync(TestUsers.CompanyA, CompanyA, [.. AllWrites, ErpPermissions.ManufacturingOrdersClose]);
        using var client = run.ClientFor(user, CompanyA);
        var (orderId, roll) = await run.OpenOrderAsync(user);
        var issue = await run.OkAsync(client, $"{Base}/orders/{orderId}/input-issues", Run.Issue(1, roll, "600", At));
        var balanced = await run.OkAsync(client, $"{Base}/orders/{orderId}/outputs",
            await run.OutputAsync(client, issue.Version, roll, "GoodProduct", "600", At.AddMinutes(1)));
        var labelVersion = await run.CurrentLabelVersionAsync(roll);
        var audits = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents");
        await using var broken = run.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            // Run the REAL owner and its SQL writes first. Only the final failure is injected; no successful owner is mocked.
            services.RemoveAll<ManufacturingInventoryCostGenerationFactoryV1>();
            services.AddScoped<ManufacturingInventoryCostGenerationFactoryV1>(provider => async (connection, transaction, companyId, audit, writer, token) =>
            {
                Assert.Equal(System.Data.IsolationLevel.Serializable, transaction.IsolationLevel);
                return new FailAfterGenerationStage(await InventoryManufacturingCostGenerationParticipant.EnlistAsync(
                    connection, transaction, companyId, audit, writer, token, provider.GetRequiredService<TimeProvider>()));
            });
        }));
        using (var failing = broken.CreateClient())
        {
            failing.DefaultRequestHeaders.Add("X-Development-User", user);
            failing.DefaultRequestHeaders.Add("X-Company-Id", CompanyA.ToString());
            failing.DefaultRequestHeaders.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
            using var refused = await failing.PostWithKeyAsync($"{Base}/orders/{orderId}/close",
                new CloseOrderRequestV1(balanced.Version), "actual-generation-retry");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
            Assert.False(refused.Headers.Contains("Idempotency-Replayed"));
        }
        var open = (await client.GetFromJsonAsync<OrderViewV1>($"{Base}/orders/{orderId}"))!;
        Assert.Equal(("Open", balanced.Version), (open.Status, open.Version));
        Assert.Equal(labelVersion, await run.CurrentLabelVersionAsync(roll));
        Assert.Equal(audits, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.CostGenerations"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.ManufacturingCostGenerations"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.ManufacturingCostGenerationHistory"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Commands WHERE CommandName = N'manufacturing.order.close.v1'"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Allocations WHERE LabelId = @label AND Status = 1 AND RemainingKg = 400", ("@label", roll)));
        using var accepted = await client.PostWithKeyAsync($"{Base}/orders/{orderId}/close",
            new CloseOrderRequestV1(balanced.Version), "actual-generation-retry");
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.NotEqual("true", accepted.Headers.TryGetValues("Idempotency-Replayed", out var replay) ? replay.Single() : null);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.CostGenerations"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.ManufacturingCostGenerations"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'manufacturing.order.closed.v2'"));
    }

    private sealed class FailAfterGenerationStage(IInventoryManufacturingCostGenerationParticipantV1 owner)
        : IInventoryManufacturingCostGenerationParticipantV1
    {
        public Task<InventoryManufacturingCostGenerationResultV1?> ReadActiveAsync(Guid orderId, CancellationToken cancellationToken = default) =>
            owner.ReadActiveAsync(orderId, cancellationToken);
        public async Task<InventoryManufacturingCostGenerationResultV1> StageAsync(InventoryManufacturingCostGenerationCommandV1 command,
            InventoryCommandMetadata metadata, CancellationToken cancellationToken = default)
        {
            await owner.StageAsync(command, metadata, cancellationToken);
            throw new InventoryManufacturingCostGenerationException(503, "synthetic_failure_after_actual_owner_write");
        }
        public Task<InventoryManufacturingCostGenerationResultV1> ReverseAsync(InventoryManufacturingCostGenerationReversalV1 command,
            InventoryCommandMetadata metadata, CancellationToken cancellationToken = default) => owner.ReverseAsync(command, metadata, cancellationToken);
        public ValueTask DisposeAsync() => owner.DisposeAsync();
    }
}
