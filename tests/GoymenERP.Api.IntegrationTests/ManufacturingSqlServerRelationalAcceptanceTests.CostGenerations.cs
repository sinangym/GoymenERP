using GoymenERP.Modules.Manufacturing.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Data.SqlClient;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task The_cost_generation_Down_guard_passes_with_legacy_orders_and_refuses_before_losing_even_unreadable_history()
    {
        await using var run = await Run.StartAsync(); var (_, orderId) = await run.ApprovedOrderAsync(CompanyA);
        var guard = Assert.IsType<SqlOperation>(new AddManufacturingCostGenerations().DownOperations[0]).Sql;
        Assert.Equal(1, await run.ScalarAsync<int>(guard + "; SELECT 1"));
        await using (var scope = run.Store(CompanyA))
        {
            await using var tx = await scope.Context.Database.BeginTransactionAsync();
            await scope.Orders.CloseOrderAsync(orderId, 1, run.Actor, At, Metadata(run));
            await scope.Corrections.AppendCostGenerationAsync(new(CompanyA, orderId, Guid.NewGuid(), 2, 1, "1", ManufacturingCostGenerationStateV1.Unreadable,
                new string('a', 64), "synthetic:v1", "{}", "{}", At, "Synthetic retained unreadable source", Metadata(run).CorrelationId), 0, Metadata(run));
            await tx.CommitAsync();
        }
        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(guard + "; SELECT 1"));
        Assert.Equal(51960, refused.Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.CostGenerations"));
    }
    [SqlServerFact]
    public async Task An_unreadable_cost_generation_joins_the_close_transaction_and_rollback_keeps_the_original_audit_and_history()
    {
        await using var run = await Run.StartAsync(); var (_, orderId) = await run.ApprovedOrderAsync(CompanyA);
        var auditBefore = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents");
        var generationId = Guid.NewGuid();
        await using (var scope = run.Store(CompanyA))
        {
            await using var tx = await scope.Context.Database.BeginTransactionAsync();
            await scope.Orders.CloseOrderAsync(orderId, 1, run.Actor, At, Metadata(run));
            await scope.Corrections.AppendCostGenerationAsync(new(CompanyA, orderId, generationId, 2, 1, "1",
                ManufacturingCostGenerationStateV1.Unreadable, new string('a', 64), "synthetic:v1", "{}", "{}", At,
                "Synthetic missing stamped cost", Metadata(run).CorrelationId), 0, Metadata(run));
            Assert.Single(await scope.Corrections.ReadCostGenerationsAsync(orderId));
            await tx.RollbackAsync();
        }
        await using var fresh = run.Store(CompanyA);
        Assert.Empty(await fresh.Corrections.ReadCostGenerationsAsync(orderId));
        Assert.Equal(1, (await fresh.Orders.FindOrderAsync(orderId))!.Version);
        Assert.Equal(auditBefore, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));
    }

    [SqlServerFact]
    public async Task Late_recognition_appends_the_same_generation_and_a_second_close_retains_the_first_immutable_result()
    {
        await using var run = await Run.StartAsync(); var (_, orderId) = await run.ApprovedOrderAsync(CompanyA);
        var id = Guid.NewGuid(); var metadata = Metadata(run);
        await using (var scope = run.Store(CompanyA))
        {
            await using var tx = await scope.Context.Database.BeginTransactionAsync();
            await scope.Orders.CloseOrderAsync(orderId, 1, run.Actor, At, metadata);
            var pending = new ManufacturingCostGenerationV1(CompanyA, orderId, id, 2, 1, "1", ManufacturingCostGenerationStateV1.Unreadable,
                new string('a', 64), "synthetic:v1", "{\"source\":\"unreadable\"}", "{}", At, "Synthetic unavailable stamp", metadata.CorrelationId);
            await scope.Corrections.AppendCostGenerationAsync(pending, 0, metadata);
            var recognized = pending with { Revision = 2, InventoryVersion = "2", State = ManufacturingCostGenerationStateV1.Recognized,
                InputFingerprint = new string('b', 64), EvidenceJson = "{\"source\":\"immutable-stamp\"}" };
            await scope.Corrections.AppendCostGenerationAsync(recognized, 1, metadata);
            await scope.Corrections.AppendCostGenerationAsync(recognized with { Revision = 3, InventoryVersion = "3", State = ManufacturingCostGenerationStateV1.Reversed }, 2, metadata);
            await scope.Corrections.ReopenOrderAsync(orderId, 2, run.Actor, At.AddMinutes(1), "Synthetic correction", metadata,
                (_, _) => Task.FromResult<ManufacturingApiResult<object>?>(null));
            await scope.Orders.CloseOrderAsync(orderId, 3, run.Actor, At.AddMinutes(2), metadata);
            await scope.Corrections.AppendCostGenerationAsync(pending with { GenerationId = Guid.NewGuid(), ClosedOrderVersion = 4 }, 0, metadata);
            await tx.CommitAsync();
        }
        await using var fresh = run.Store(CompanyA); var history = await fresh.Corrections.ReadCostGenerationsAsync(orderId);
        Assert.Equal(4, history.Count); Assert.Equal(id, history[0].GenerationId); Assert.Equal(id, history[1].GenerationId);
        Assert.Equal("{\"source\":\"unreadable\"}", history[0].EvidenceJson);
        Assert.Equal(ManufacturingCostGenerationStateV1.Reversed, history[2].State);
        Assert.NotEqual(id, history[3].GenerationId);
        await using var other = run.Store(CompanyB); Assert.Empty(await other.Corrections.ReadCostGenerationsAsync(orderId));
    }
}
