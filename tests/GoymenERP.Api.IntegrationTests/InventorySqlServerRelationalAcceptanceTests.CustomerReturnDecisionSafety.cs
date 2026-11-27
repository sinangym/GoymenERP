using GoymenERP.Api.LogisticsRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    // Actual existing Inventory shipment/return participant and shared SQL transaction. Logistics lineage
    // identifiers below are controlled boundary inputs, not a complete native delivery -> return approval E2E.
    [SqlServerFact]
    public async Task Customer_return_runtime_owned_approval_preserves_actual_inventory_effect_audit_and_exact_replay()
    {
        await using var run = await Run.StartAsync();
        var shipment = await RecordShipmentAsync(run, "SYN-RETURN-OWNED-RUNTIME", "40");
        var request = ReturnRequest(shipment, "10"); var key = "owned-return-" + Guid.NewGuid();
        await using var frame = ReturnStockFrame(run);
        var envelope = new LogisticsWriteEnvelope(frame.User.Actor, "synthetic.inventory.test-write", [], "SYN-return-owned", null, key, "Test");
        var stockRequest = new LogisticsCustomerReturnStockRequestV1(CompanyA, request.CustomerReturnId, Guid.NewGuid(),
            request.LoadingInstructionLineId, Guid.NewGuid(), request.SourceShipmentId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            10m, request.InventoryMovementId, request.OccurredAtUtc);
        var first = await frame.Runner.ExecuteApprovalAsync(CompanyA,
            (stock, token) => stock.StageAsync(stockRequest, envelope, token), (stock, token) => stock.RequireStillAsync(token));
        var effects = await ReturnEffectsAsync(run, first.OriginalLabelId);
        Assert.False(first.IsReplay); Assert.Equal((1, 1, 1, 1), (effects.ReturnMovements, effects.Commands, effects.OutboxMessages, effects.AuditEvents));
        Assert.Equal(shipment.ShipmentRequest.MovementId, first.OriginalShipmentMovementId);
        var replay = await frame.Runner.ExecuteApprovalAsync(CompanyA,
            (stock, token) => stock.StageAsync(stockRequest, envelope with { CorrelationId = "SYN-later-retry" }, token),
            (stock, token) => stock.RequireStillAsync(token));
        Assert.True(replay.IsReplay); Assert.Equal(first, replay with { IsReplay = false });
        Assert.Equal(effects, await ReturnEffectsAsync(run, first.OriginalLabelId)); Assert.False(frame.Writer.HasPendingDrafts);
        Assert.Equal(409, (await Assert.ThrowsAsync<LogisticsCustomerReturnServiceException>(() => frame.Runner.ExecuteApprovalAsync(CompanyA,
            (stock, token) => stock.StageAsync(stockRequest with { Quantity = 11m }, envelope, token),
            (stock, token) => stock.RequireStillAsync(token)))).Status);
        Assert.Equal(effects, await ReturnEffectsAsync(run, first.OriginalLabelId));
    }

    [SqlServerFact]
    public async Task Customer_return_runtime_final_delegation_fence_rolls_back_inventory_and_both_owners_actual_audit_batches()
    {
        await using var run = await Run.StartAsync();
        var shipment = await RecordShipmentAsync(run, "SYN-RETURN-OWNED-ABORT", "40");
        var request = ReturnRequest(shipment, "10"); await using var frame = ReturnStockFrame(run);
        var before = await ReturnEffectsAsync(run, shipment.ShipmentRequest.LabelId);
        var envelope = new LogisticsWriteEnvelope(frame.User.Actor, "synthetic.inventory.test-write", [], "SYN-return-abort", null, "abort-return-" + Guid.NewGuid(), "Test");
        var stockRequest = new LogisticsCustomerReturnStockRequestV1(CompanyA, request.CustomerReturnId, Guid.NewGuid(),
            request.LoadingInstructionLineId, Guid.NewGuid(), request.SourceShipmentId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            10m, request.InventoryMovementId, request.OccurredAtUtc);
        var fences = 0; var staged = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<LogisticsCustomerReturnServiceException>(() => frame.Runner.ExecuteApprovalAsync(CompanyA,
            async (stock, token) =>
            {
                var answer = await stock.StageAsync(stockRequest, envelope, token); staged = true;
                // A controlled Logistics audit draft tests the real scoped writer after Inventory has
                // independently flushed its own real audit via the borrowed participant context.
                frame.Collector.Enqueue(new(CompanyA, "logistics.synthetic.customer-return.v1", "Logistics.CustomerReturn", request.CustomerReturnId.ToString("D"),
                    "ApprovalBoundary", 1, 2, AuditActorTypes.User, frame.User.Actor, "synthetic.inventory.test-write", [], "{}", "{}", "SYNTHETIC Logistics boundary audit",
                    null, AuditAiInvolvement.None, null, "Test", null, null, envelope.CorrelationId, null, envelope.IdempotencyKey));
                return answer;
            }, async (stock, token) =>
            {
                await stock.RequireStillAsync(token); fences++;
                if (fences == 3)
                {
                    Assert.True(staged); Assert.False(frame.Writer.HasPendingDrafts);
                    // Controlled live delegation refusal, not the real Work delegation reader.
                    throw new LogisticsCustomerReturnServiceException(403, "forbidden");
                }
            }))).Status);
        Assert.Equal(3, fences); Assert.Equal(before, await ReturnEffectsAsync(run, shipment.ShipmentRequest.LabelId));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'logistics.synthetic.customer-return.v1'"));
        Assert.Empty(frame.Db.ChangeTracker.Entries()); Assert.False(frame.Writer.HasPendingDrafts);
    }

    private sealed record ReturnStockRuntimeFrame(LogisticsDbContext Db, LogisticsCustomerReturnTransactionRunner Runner,
        AuditEventCollector Collector, IAuditTransactionWriterV1 Writer, LogisticsCustomerReturnDecisionSafetyTests.SafetyUser User,
        ServiceProvider Provider, IServiceScope Scope) : IAsyncDisposable
    { public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Provider.DisposeAsync(); Scope.Dispose(); } }

    private static ReturnStockRuntimeFrame ReturnStockFrame(Run run)
    {
        var scope = run.Factory.Services.CreateScope(); var company = scope.ServiceProvider.GetRequiredService<ICompanyContext>(); company.Set(CompanyA);
        var collector = scope.ServiceProvider.GetRequiredService<AuditEventCollector>(); var writer = scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
        var db = new LogisticsDbContext(new DbContextOptionsBuilder<LogisticsDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString).Options, company);
        var services = new ServiceCollection().AddSingleton(writer).AddSingleton<ISettingsPrerequisiteProvider>(Settings(run, CompanyA)).BuildServiceProvider();
        var user = new LogisticsCustomerReturnDecisionSafetyTests.SafetyUser();
        return new(db, new LogisticsCustomerReturnTransactionRunner(db, services, company, user,
            new LogisticsCustomerReturnDecisionSafetyTests.SafetyAuthority(), TimeProvider.System), collector, writer, user, services, scope);
    }
}
