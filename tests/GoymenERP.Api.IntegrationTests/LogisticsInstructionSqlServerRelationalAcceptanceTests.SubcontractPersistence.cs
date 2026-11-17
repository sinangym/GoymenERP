using System.Data;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class LogisticsInstructionSqlServerRelationalAcceptanceTests
{
    // These are persistence invariants with explicitly synthetic owner snapshots. The host chain has its own real-owner facts.
    [SqlServerFact]
    public async Task A_subcontract_instruction_rebuilds_its_kind_sources_and_append_only_weighing_corrections_without_touching_the_original()
    {
        await using var run = await Run.StartAsync(); var company = new CompanyContext(); company.Set(CompanyA);
        var collector = new AuditEventCollector();
        await using var db = new LogisticsDbContext(new DbContextOptionsBuilder<LogisticsDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(collector, company)).Options, company);
        var store = new LogisticsStore(db, company, collector, TimeProvider.System);
        var (instruction, sources) = SyntheticSubcontract(); var id = instruction.Id; var line = Assert.Single(instruction.Lines);
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await store.StoreSubcontractInstructionAsync(run.Envelope("sub-create"), Hash("sub-create"), Day, () => instruction, sources);
            await tx.CommitAsync();
        }
        await store.MutateInstructionAsync(run.Envelope("sub-issue"), Hash("sub-issue"), LogisticsCommands.IssueV1, id, (value, now) => value.Issue(2, now), Issued);
        await store.MutateInstructionAsync(run.Envelope("sub-load"), Hash("sub-load"), LogisticsCommands.RecordLoadV1, id,
            (value, now) => value.RecordLoad(3, Guid.NewGuid(), line.Id, Kg(6), MeasurementKind.Measured, now), Loaded);
        var weighing = Guid.NewGuid();
        await store.MutateInstructionAsync(run.Envelope("sub-weigh"), Hash("sub-weigh"), LogisticsCommands.RecordWeighingV1, id,
            (value, now) => value.RecordWeighing(4, weighing, WeighingDirection.TareFirst, Kg(100), Kg(106), run.Actor, now), Weighed);
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var shipment = Guid.NewGuid();
            await store.ShipSubcontractInstructionAsync(run.Envelope("sub-ship"), Hash("sub-ship"), id, 5, new Dictionary<Guid, Guid> { [line.Id] = shipment },
                [new(shipment, line.Source.OrderId, id, "within", null, "none", null, SalesShipmentRiskGateContractV1.Version)], T0.AddMinutes(3));
            await tx.CommitAsync();
        }
        await store.MutateInstructionAsync(run.Envelope("sub-correct"), Hash("sub-correct"), LogisticsCommands.CorrectWeighingV1, id,
            (value, now) => value.CorrectWeighing(6, Guid.NewGuid(), WeighingDirection.TareFirst, Kg(100), Kg(105), "SENTETIK doğrulanmış düzeltme", run.Actor, now),
            new("logistics.loading-instruction.weighing-corrected.v1", "CorrectWeighing", "SENTETIK doğrulanmış düzeltme"));
        await using var fresh = run.Store(CompanyA); var restored = (await fresh.Store.FindInstructionAsync(id))!.Instruction;
        Assert.True(restored.IsContractProcessingDelivery); Assert.Equal(7, restored.Version);
        Assert.Equal(6m, restored.Weighing!.Net.Value); Assert.Equal(5m, Assert.Single(restored.WeighingCorrections).Net.Value);
        Assert.Equal(2, restored.WeighingRevision); Assert.Equal(6m, Assert.Single(restored.WeighingCorrections).PreviousNet.Value);
        Assert.Equal(6m, Assert.Single(restored.Lines).ShippedQuantity.Value);
        Assert.Equal(6m, await run.ScalarAsync<decimal>("SELECT NetKg FROM logistics.VehicleWeighings WHERE InstructionId=@id", ("@id", id)));
        Assert.Equal(sources[line.Id], (await store.ReadSubcontractSourcesAsync(id))[line.Id]);
        await using var foreign = run.Store(CompanyB); Assert.Null(await foreign.Store.FindInstructionAsync(id));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements WHERE SourceId=@id", ("@id", id)));
    }

    [SqlServerFact]
    public async Task Exact_subcontract_replay_keeps_the_original_reply_after_later_instruction_changes_and_requires_the_same_company_actor_and_payload()
    {
        await using var run = await Run.StartAsync(); var company = new CompanyContext(); company.Set(CompanyA);
        var collector = new AuditEventCollector();
        await using var db = new LogisticsDbContext(new DbContextOptionsBuilder<LogisticsDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(collector, company)).Options, company);
        var store = new LogisticsStore(db, company, collector, TimeProvider.System); var (instruction, sources) = SyntheticSubcontract();
        var line = Assert.Single(instruction.Lines); var envelope = run.Envelope("sub-reply"); var hash = Hash("sub-reply");
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await store.StoreSubcontractInstructionAsync(envelope, hash, Day, () => instruction, sources);
            var reply = new LogisticsSubcontractInstructionV1(CompanyA, instruction.Id, "2", "Draft", instruction.CustomerPartyId, instruction.CompanyAccountId,
                [new(line.Id, sources[line.Id], line.AllocationId!.Value, "6", "0", "0", "0", "1", "1")], null, [], false, envelope.CorrelationId);
            await store.CompleteSubcontractResultAsync(envelope, hash, LogisticsCommands.CreateV1, reply); await tx.CommitAsync();
        }
        await store.MutateInstructionAsync(run.Envelope("sub-reply-issue"), Hash("sub-reply-issue"), LogisticsCommands.IssueV1, instruction.Id, (value, now) => value.Issue(2, now), Issued);
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var replay = (await store.FindSubcontractResultAsync(envelope with { CorrelationId = "SENTETIK-retry" }, hash, LogisticsCommands.CreateV1))!;
            Assert.True(replay.IsReplay); Assert.Equal("Draft", replay.Status); Assert.Equal("2", replay.Version); Assert.Equal(envelope.CorrelationId, replay.OriginalCorrelationId);
            Assert.Equal(LogisticsStoreCodes.SameKeyPayloadMismatch, (await Assert.ThrowsAsync<LogisticsStoreException>(() =>
                store.FindSubcontractResultAsync(envelope, Hash("changed"), LogisticsCommands.CreateV1))).Code);
            await Assert.ThrowsAsync<LogisticsStoreException>(() => store.FindSubcontractResultAsync(envelope with { ActorId = "SENTETIK-other" }, hash, LogisticsCommands.CreateV1));
            await tx.RollbackAsync();
        }
    }

    [SqlServerFact]
    public async Task The_subcontract_Down_guard_allows_legacy_sale_but_refuses_before_any_subcontract_evidence_can_be_dropped()
    {
        await using var run = await Run.StartAsync(); var guard = Assert.IsType<SqlOperation>(new AddLogisticsSubcontractDeliveries().DownOperations[0]).Sql;
        await using (var ordinary = run.Store(CompanyA)) await ordinary.Store.StoreInstructionAsync(run.Envelope("sub-down-ordinary"), Hash("sub-down-ordinary"), Day, () => Instruction(Owners.For(CompanyA)));
        await run.ExecuteAsync(guard);
        var company = new CompanyContext(); company.Set(CompanyA); var audit = new AuditEventCollector();
        await using var db = new LogisticsDbContext(new DbContextOptionsBuilder<LogisticsDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(audit, company)).Options, company);
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var (instruction, sources) = SyntheticSubcontract();
            await new LogisticsStore(db, company, audit, TimeProvider.System).StoreSubcontractInstructionAsync(run.Envelope("sub-down-create"), Hash("sub-down-create"), Day, () => instruction, sources);
            await tx.CommitAsync();
        }
        Assert.Equal(51935, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard))).Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingInstructions WHERE ShipmentKind=2"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingInstructionLines WHERE SubcontractSourceJson IS NOT NULL"));
    }

    private static (LoadingInstruction Instruction, IReadOnlyDictionary<Guid, LogisticsSubcontractSourceV1> Sources) SyntheticSubcontract()
    {
        var owners = Owners.For(CompanyA); var instruction = LoadingInstruction.Create(Guid.NewGuid(), CompanyA, owners.Party, owners.Account, T0, LoadingInstructionShipmentKind.ContractProcessingDelivery);
        var line = Guid.NewGuid(); var orderLine = Guid.NewGuid(); var product = Guid.NewGuid(); var label = Guid.NewGuid();
        instruction.AddLine(1, line, new(owners.Order, orderLine, 0), product, KgUnit, Guid.NewGuid(), Kg(6), T0);
        var source = new LogisticsSubcontractSourceV1(CompanyA, owners.Party, owners.Account, owners.Order, orderLine, 4, 0, product, "1", Guid.NewGuid(),
            Guid.NewGuid(), 5, Guid.NewGuid(), 3, 1, Guid.NewGuid(), label, "6", new string('A', 64), "SYN-LABEL", "SYN-SALES", "SYN-SERVICE", "SENTETIK hizmet");
        return (instruction, new Dictionary<Guid, LogisticsSubcontractSourceV1> { [line] = source });
    }
}
