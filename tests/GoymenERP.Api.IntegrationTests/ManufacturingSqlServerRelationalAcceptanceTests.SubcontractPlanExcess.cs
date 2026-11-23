using System.Data;
using System.Globalization;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task A_failure_after_actual_extra_reservation_rolls_back_both_owners_and_retries_the_same_explicit_key()
    {
        await using var run = await StartManufacturingInventoryRunAsync(); var seed = await SeedSubcontractDeliveryHostAsync(run, "SYN-S3-EXTRA-RB");
        Guid instructionId; Guid lineId; Guid exceptionId = Guid.NewGuid(); string labelVersion;
        await using (var setup = SubcontractHostScope.Create(run, seed.Party, seed.Account))
        {
            var original = await setup.CommandAsync(seed); var command = original with { Lines = [original.Lines[0] with { QuantityKg = "60" }] };
            var created = await setup.Host.CreateAsync(command, new("extra-rb-create", "SENTETIK-extra-rb-create", null, At));
            instructionId = created.InstructionId; lineId = command.Lines[0].LineId;
            await setup.Store.MutateInstructionAsync(setup.Envelope("extra-rb-issue"), S3Hash("extra-rb-issue"), LogisticsCommands.IssueV1, instructionId,
                (value, now) => value.Issue(long.Parse(created.Version, CultureInfo.InvariantCulture), now), new("logistics.loading-instruction.issued.v1", "Issue", "SENTETIK"));
            await using var tx = await setup.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await setup.Store.ProposePlanExcessAsync(setup.Envelope("extra-rb-propose") with { PermissionCode = LogisticsPermissionManifestV1.LoadsRecord },
                S3Hash("extra-rb-propose"), instructionId, lineId, new(exceptionId, "3", "20", "SENTETIK"), default);
            await setup.Store.ApprovePlanExcessAsync(setup.Envelope("extra-rb-approve") with { PermissionCode = LogisticsPlanExcessV1.ApprovePermission },
                S3Hash("extra-rb-approve"), exceptionId, new("1", "3"), default); await tx.CommitAsync();
        }
        await using (var read = SubcontractHostScope.Create(run, seed.Party, seed.Account))
            labelVersion = (await read.Host.ReadAsync(instructionId)).Lines[0].LabelVersion!;
        var allocation = Guid.NewGuid(); var request = new ReserveLogisticsPlanExcessV1("3", "2", allocation, labelVersion, "20", "SENTETIK rollback");
        var metadata = new LogisticsCommandMetadata("extra-rb-reserve", "SENTETIK-extra-rb-reserve", null, At);
        var audits = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents");
        await using (var failed = SubcontractHostScope.Create(run, seed.Party, seed.Account, new FailAfterS3LogisticsSave()))
            await Assert.ThrowsAsync<InvalidOperationException>(() => failed.Host.ReservePlanExcessAsync(instructionId, lineId, exceptionId, request, metadata));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Allocations WHERE Id=@id", ("@id", allocation)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.PlanExcessReservations WHERE AllocationId=@id", ("@id", allocation)));
        Assert.Equal(audits, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));
        await using var retry = SubcontractHostScope.Create(run, seed.Party, seed.Account);
        var result = await retry.Host.ReservePlanExcessAsync(instructionId, lineId, exceptionId, request, metadata);
        Assert.False(result.IsReplay); Assert.Equal(allocation, Assert.Single(result.Lines[0].AdditionalAllocations!).AllocationId);
        Assert.Equal(80m, await run.ScalarAsync<decimal>("SELECT SUM(QuantityKg) FROM inventory.Balances WHERE LabelId=@id", ("@id", seed.Output.LabelId)));
    }

    [SqlServerFact]
    public async Task An_explicit_approved_subcontract_extra_reserves_separately_and_ships_one_actual_movement_without_rewriting_plan()
    {
        await using var run = await StartManufacturingInventoryRunAsync(); var seed = await SeedSubcontractDeliveryHostAsync(run, "SYN-S3-EXTRA");
        await using var scope = SubcontractHostScope.Create(run, seed.Party, seed.Account);
        var initial = await scope.CommandAsync(seed);
        var command = initial with { Lines = [initial.Lines[0] with { QuantityKg = "60" }] };
        var created = await scope.Host.CreateAsync(command, new("extra-create", "SENTETIK-extra-create", null, At));
        await scope.Store.MutateInstructionAsync(scope.Envelope("extra-issue"), S3Hash("extra-issue"), LogisticsCommands.IssueV1, created.InstructionId,
            (value, now) => value.Issue(long.Parse(created.Version, CultureInfo.InvariantCulture), now), new("logistics.loading-instruction.issued.v1", "Issue", "SENTETIK"));
        var exceptionId = Guid.NewGuid(); var lineId = command.Lines[0].LineId;
        await using (var tx = await scope.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await scope.Store.ProposePlanExcessAsync(scope.Envelope("extra-propose") with { PermissionCode = LogisticsPermissionManifestV1.LoadsRecord },
                S3Hash("extra-propose"), created.InstructionId, lineId, new(exceptionId, "3", "20", "SENTETIK ayrı fazla pay"), default);
            await tx.CommitAsync();
        }
        await using (var tx = await scope.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await scope.Store.ApprovePlanExcessAsync(scope.Envelope("extra-approve") with { PermissionCode = LogisticsPlanExcessV1.ApprovePermission },
                S3Hash("extra-approve"), exceptionId, new("1", "3"), default); await tx.CommitAsync();
        }
        var approved = await scope.Host.ReadAsync(created.InstructionId); var additionalId = Guid.NewGuid();
        var request = new ReserveLogisticsPlanExcessV1(approved.Version, "2", additionalId, approved.Lines[0].LabelVersion!, "20", "SENTETIK ayrı stok ayırma");
        var reserved = await scope.Host.ReservePlanExcessAsync(created.InstructionId, lineId, exceptionId, request, new("extra-reserve", "SENTETIK-extra-reserve", null, At));
        Assert.Equal(("60", "20", approved.Version), (reserved.Lines[0].InstructedKg, reserved.Lines[0].ApprovedPlanExcess, reserved.Version));
        var extra = Assert.Single(reserved.Lines[0].AdditionalAllocations!); Assert.Equal((additionalId, exceptionId, "20"), (extra.AllocationId, extra.ExceptionId, extra.RemainingKg));
        var loaded = await scope.Host.LoadAsync(created.InstructionId, lineId, new(reserved.Version, Guid.NewGuid(), "80", "Measured", [new(exceptionId, "2", "20")]), new("extra-load", "SENTETIK-extra-load", null, At));
        await scope.Store.MutateInstructionAsync(scope.Envelope("extra-weigh"), S3Hash("extra-weigh"), LogisticsCommands.RecordWeighingV1, created.InstructionId,
            (value, now) => value.RecordWeighing(value.Version, Guid.NewGuid(), WeighingDirection.TareFirst, S3Kg(100), S3Kg(180), run.Actor.ToString("D"), now),
            new("logistics.loading-instruction.weighed.v1", "Weigh", "SENTETIK"));
        var current = await scope.Host.ReadAsync(created.InstructionId); var line = current.Lines[0]; var source = line.Source; var movement = Guid.NewGuid();
        var shipped = await scope.Host.ShipAsync(created.InstructionId, new(current.Version, At.AddHours(1), [new(lineId, Guid.NewGuid(), movement,
            line.AllocationVersion!, line.LabelVersion!, source.SalesOrderId, source.SalesOrderLineId, source.ManufacturingOrderId, source.OutputFactId, source.LabelId,
            [new(exceptionId, "2", "20")], [new(extra.AllocationId, extra.Version, "20")])]), new("extra-ship", "SENTETIK-extra-ship", null, At));
        Assert.Equal(("60", "80", "80"), (shipped.Lines[0].InstructedKg, shipped.Lines[0].LoadedKg, shipped.Lines[0].ShippedKg));
        Assert.Equal("0", Assert.Single(shipped.Lines[0].AdditionalAllocations!).RemainingKg);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements WHERE Id=@id", ("@id", movement)));
        Assert.Equal(0m, await run.ScalarAsync<decimal>("SELECT COALESCE(SUM(QuantityKg),0) FROM inventory.Balances WHERE LabelId=@id", ("@id", seed.Output.LabelId)));
        Assert.Equal(60m, await run.ScalarAsync<decimal>("SELECT Instructed FROM logistics.LoadingInstructionLines WHERE InstructionId=@id", ("@id", created.InstructionId)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.PlanExcessUsages WHERE ExceptionId=@id", ("@id", exceptionId)));
        var replay = await scope.Host.ReservePlanExcessAsync(created.InstructionId, lineId, exceptionId, request, new("extra-reserve", "SENTETIK-extra-retry", null, At));
        Assert.True(replay.IsReplay); Assert.Equal("20", Assert.Single(replay.Lines[0].AdditionalAllocations!).RemainingKg);
        Assert.Equal(reserved.Version, replay.Version); // Original reservation answer, not today's consumed allocation.
        var tamper = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => run.ScalarAsync<int>(
            "UPDATE logistics.PlanExcessReservations SET Quantity=Quantity+1 WHERE AllocationId=@id; SELECT @@ROWCOUNT", ("@id", extra.AllocationId)));
        Assert.Equal(51820, tamper.Number);
    }
}
