using System.Data;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Concurrency;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Subcontract_whole_instruction_reads_every_order_and_technical_risk_failure_dominates_terminal_refusal_in_either_order()
    {
        await using var run = await StartManufacturingInventoryRunAsync();
        var first = await SeedSubcontractDeliveryHostAsync(run, "SYN-S3-RISK-A");
        var second = await SeedSubcontractDeliveryHostAsync(run, "SYN-S3-RISK-B", first.Party, first.Account);
        await using var scope = SubcontractHostScope.Create(run, first.Party, first.Account);
        var one = await scope.CommandAsync(first); var two = await scope.CommandAsync(second);
        var create = one with { Lines = [one.Lines[0], two.Lines[0]] };
        var created = await scope.Host.CreateAsync(create, new("s3-risk-create", "SENTETIK-risk-create", null, At));
        await PrepareS3LoadedInstructionAsync(scope, created, run.Actor.ToString("D"));
        var view = await scope.Host.ReadAsync(created.InstructionId);
        var ship = new ShipSubcontractDeliveryV1(view.Version, At.AddHours(2), view.Lines.Select(line => new SubcontractDeliveryShipmentLineV1(
            line.LineId, Guid.NewGuid(), Guid.NewGuid(), line.AllocationVersion!, line.LabelVersion!, line.Source.SalesOrderId, line.Source.SalesOrderLineId,
            line.Source.ManufacturingOrderId, line.Source.OutputFactId, line.Source.LabelId)).ToArray());
        var orderIds = new[] { first.SalesOrder, second.SalesOrder }.Order().ToArray();
        var metadata = new LogisticsCommandMetadata("s3-risk-same-key", "SENTETIK-risk-command", null, At);
        foreach (var reverse in new[] { false, true })
        {
            scope.Risk.OverLimitOrder = orderIds[reverse ? 1 : 0]; scope.Risk.ThrowingOrder = orderIds[reverse ? 0 : 1]; scope.Risk.Reads.Clear();
            var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => scope.Host.ShipAsync(created.InstructionId, ship, metadata));
            Assert.Equal(503, refusal.Status); Assert.Equal(orderIds, scope.Risk.Reads);
            Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingShares WHERE InstructionId=@id AND Kind=2", ("@id", created.InstructionId)));
            Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingAttempts WHERE InstructionId=@id AND CommandName=@command", ("@id", created.InstructionId), ("@command", LogisticsSubcontractDeliveryV1.ShipCommand)));
            foreach (var line in ship.Lines) Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements WHERE Id=@id", ("@id", line.MovementId)));
        }
        scope.Risk.ThrowingOrder = null; scope.Risk.OverLimitOrder = first.SalesOrder;
        var terminal = metadata with { IdempotencyKey = "s3-risk-terminal" };
        Assert.Equal(409, (await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => scope.Host.ShipAsync(created.InstructionId, ship, terminal))).Status);
        scope.Risk.Reads.Clear();
        Assert.True((await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => scope.Host.ShipAsync(created.InstructionId, ship, terminal))).IsReplay);
        Assert.Empty(scope.Risk.Reads);
        scope.Risk.OverLimitOrder = null;
        var applied = await scope.Host.ShipAsync(created.InstructionId, ship, metadata);
        Assert.All(applied.Lines, line => Assert.Equal("80", line.ShippedKg));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingShares WHERE InstructionId=@id AND Kind=2", ("@id", created.InstructionId)));
    }

    [SqlServerFact]
    public async Task Stale_subcontract_label_version_keeps_all_stock_unreserved_and_a_foreign_company_cannot_read_its_label_details()
    {
        await using var run = await StartManufacturingInventoryRunAsync(); var seed = await SeedSubcontractDeliveryHostAsync(run, "SYN-S3-STALE");
        await using var scope = SubcontractHostScope.Create(run, seed.Party, seed.Account); var command = await scope.CommandAsync(seed);
        command = command with { Lines = [command.Lines[0] with { ExpectedLabelVersion = "999999" }] };
        var failure = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => scope.Host.CreateAsync(command, new("s3-stale", "SENTETIK-s3-stale", null, At)));
        Assert.Equal(409, failure.Status);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Allocations WHERE Id=@id", ("@id", command.Lines[0].AllocationId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingInstructions WHERE Id=@id", ("@id", command.InstructionId)));
        await using var services = run.Factory.Services.CreateAsyncScope(); var provider = services.ServiceProvider;
        await using var db = run.Context(CompanyB); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await using var reader = await InventorySubcontractDeliveryParticipant.EnlistAsync(db.Database.GetDbConnection(), tx.GetDbTransaction(), CompanyB,
            ManufacturingSettings(run), provider.GetRequiredService<AuditEventCollector>(), provider.GetRequiredService<IAuditTransactionWriterV1>(), provider.GetRequiredService<ISalesOrderLineSerializationLockBinderV1>());
        Assert.Null(await reader.ReadLabelAsync(seed.Output.LabelId)); await tx.RollbackAsync();
    }

    [SqlServerFact]
    public async Task Subcontract_label_reads_are_same_transaction_dispose_only_and_do_not_accept_ended_or_weaker_transactions()
    {
        await using var run = await StartManufacturingInventoryRunAsync(); var seed = await SeedSubcontractDeliveryHostAsync(run, "SYN-S3-READ-HANDLES");
        await using var services = run.Factory.Services.CreateAsyncScope(); var provider = services.ServiceProvider;
        var collector = provider.GetRequiredService<AuditEventCollector>(); var writer = provider.GetRequiredService<IAuditTransactionWriterV1>(); var locks = provider.GetRequiredService<ISalesOrderLineSerializationLockBinderV1>();
        await using var db = run.Context(CompanyA);
        await using (var weak = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            Assert.Equal(503, (await Assert.ThrowsAsync<InventoryDefiniteShipmentException>(() => InventorySubcontractDeliveryParticipant.EnlistAsync(db.Database.GetDbConnection(), weak.GetDbTransaction(), CompanyA, ManufacturingSettings(run), collector, writer, locks))).Status);
            await weak.RollbackAsync();
        }
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var reader = await InventorySubcontractDeliveryParticipant.EnlistAsync(db.Database.GetDbConnection(), tx.GetDbTransaction(), CompanyA, ManufacturingSettings(run), collector, writer, locks);
        var value = (await reader.ReadLabelAsync(seed.Output.LabelId))!; Assert.Equal(seed.Output.LabelId, value.LabelId); Assert.Equal(CompanyA, value.CompanyId);
        await reader.DisposeAsync(); Assert.Same(tx, db.Database.CurrentTransaction); Assert.Same(db.Database.GetDbConnection(), tx.GetDbTransaction().Connection);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => reader.ReadLabelAsync(seed.Output.LabelId));
        await using var second = await InventorySubcontractDeliveryParticipant.EnlistAsync(db.Database.GetDbConnection(), tx.GetDbTransaction(), CompanyA, ManufacturingSettings(run), collector, writer, locks);
        await tx.RollbackAsync(); var ended = await Assert.ThrowsAsync<InventoryDefiniteShipmentException>(() => second.ReadLabelAsync(seed.Output.LabelId)); Assert.Equal(503, ended.Status);
    }

    private static async Task PrepareS3LoadedInstructionAsync(SubcontractHostScope scope, LogisticsSubcontractInstructionV1 created, string actor)
    {
        await scope.Store.MutateInstructionAsync(scope.Envelope("s3-risk-issue"), S3Hash("s3-risk-issue"), LogisticsCommands.IssueV1, created.InstructionId,
            (value, now) => value.Issue(value.Version, now), new("logistics.loading-instruction.issued.v1", "Issue", "SENTETIK"));
        foreach (var line in created.Lines)
        {
            var current = await scope.Host.ReadAsync(created.InstructionId);
            await scope.Host.LoadAsync(created.InstructionId, line.LineId, new(current.Version, Guid.NewGuid(), "80", "Measured"),
                new($"s3-risk-load-{line.LineId:D}", $"SENTETIK-load-{line.LineId:D}", null, At));
        }
        await scope.Store.MutateInstructionAsync(scope.Envelope("s3-risk-weigh"), S3Hash("s3-risk-weigh"), LogisticsCommands.RecordWeighingV1, created.InstructionId,
            (value, now) => value.RecordWeighing(value.Version, Guid.NewGuid(), WeighingDirection.TareFirst, S3Kg(100), S3Kg(260), actor, now), new("logistics.loading-instruction.weighed.v1", "Weigh", "SENTETIK"));
    }
}
